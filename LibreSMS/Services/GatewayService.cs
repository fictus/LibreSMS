using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibreSMS.Models;
using LibreSMS.Services;

namespace LibreSMS.Services
{
    public class GatewayService
    {
        private static GatewayService? _instance;
        public static GatewayService Instance => _instance ??= new GatewayService();
        // Completes once InitializeAsync() finishes, regardless of who awaits it first.
        private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Awaits until InitializeAsync has fully completed.</summary>
        public Task WaitForInitAsync() => _initTcs.Task;
        public ConfigService Config { get; } = new();
        public WebhookService Webhook { get; } = new();
        public SmsSenderService SmsSender { get; } = new();
        public GatewayStatus Status { get; } = new();

        private HttpGatewayServer? _httpServer;
        private readonly GatewayLogService _log = GatewayLogService.Instance;

#if ANDROID
        // PARTIAL_WAKE_LOCK keeps the CPU running without forcing the screen on.
        // The foreground service notification keeps Android from killing the process;
        // the wake lock ensures the CPU is not suspended mid-request.
        private Android.OS.PowerManager.WakeLock? _wakeLock;
#endif

        public bool IsRunning => Status.IsRunning;

        public event EventHandler<bool>? StatusChanged;

        public async Task InitializeAsync()
        {
            await Config.LoadAsync();
            CheckDefaultSmsApp();
            _log.Info("Gateway service initialized");

            if (Config.Config.AutoStart)
            {
                _log.Info("Auto-start enabled, starting gateway...");
                await StartAsync();
            }

            // Signal that initialisation is complete — unblocks WaitForInitAsync() callers.
            _initTcs.TrySetResult();
        }

        public async Task<bool> StartAsync()
        {
            if (IsRunning)
            {
                _log.Warning("Gateway already running");
                return true;
            }

            try
            {
                _httpServer = new HttpGatewayServer(SmsSender, Status);
                var started = await _httpServer.StartAsync(Config.Config.HttpPort);

                if (started)
                {
                    Status.IsRunning = true;
                    Status.MessagesSent = 0;
                    Status.MessagesReceived = 0;
                    Webhook.Reset();
                    StatusChanged?.Invoke(this, true);

                    // Start foreground service on Android
                    StartForegroundService();

                    // Acquire wake lock so CPU stays alive when screen turns off
                    AcquireWakeLock();

                    _log.Success($"Gateway started on port {Config.Config.HttpPort}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to start gateway: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _httpServer?.Stop();
            _httpServer = null;
            Status.IsRunning = false;
            Status.StartedAt = null;
            StatusChanged?.Invoke(this, false);

            StopForegroundService();

            // Release wake lock so the device can sleep normally again
            ReleaseWakeLock();

            _log.Info("Gateway stopped");
        }

        public async Task HandleIncomingMessageAsync(IncomingMessage message)
        {
            // Ensure InitializeAsync() has fully completed before reading Config or Webhook.
            // This matters when the SmsReceiver broadcast wakes the app cold — the singleton
            // exists but LoadAsync() may not have finished yet.
            await WaitForInitAsync();

            // Null-guard Body: some carrier PDUs deliver a null body (e.g. flash/class-0 SMS).
            message.Body ??= string.Empty;

            Status.MessagesReceived++;

            var preview = message.Body.Length > 0
                ? message.Body[..Math.Min(60, message.Body.Length)]
                : "(empty)";
            _log.Info($"Incoming {message.Type} from {message.From}: {preview}");

            if (!string.IsNullOrWhiteSpace(Config.Config.WebhookUrl))
            {
                var delivered = await Webhook.SendWebhookAsync(Config.Config.WebhookUrl, message);
                Status.WebhookDeliveries = Webhook.DeliveryCount;
                Status.WebhookFailures = Webhook.FailureCount;
            }
        }

        public void CheckDefaultSmsApp()
        {
#if ANDROID
            try
            {
                var defaultPackage = Android.Provider.Telephony.Sms.GetDefaultSmsPackage(Android.App.Application.Context);
                Status.IsDefaultSmsApp = defaultPackage == "com.libresms.app";
            }
            catch
            {
                Status.IsDefaultSmsApp = false;
            }
#endif
        }

        public void RequestDefaultSmsApp()
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;
                var intent = new Android.Content.Intent(Android.Provider.Telephony.Sms.Intents.ActionChangeDefault);
                intent.PutExtra(Android.Provider.Telephony.Sms.Intents.ExtraPackageName, context.PackageName);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch (Exception ex)
            {
                _log.Error($"Cannot request default SMS app: {ex.Message}");
            }
#endif
        }

        private void AcquireWakeLock()
        {
#if ANDROID
            try
            {
                if (_wakeLock?.IsHeld == true) return;

                var pm = Android.App.Application.Context
                    .GetSystemService(Android.Content.Context.PowerService)
                    as Android.OS.PowerManager;

                // PARTIAL_WAKE_LOCK: CPU on, screen allowed to sleep — correct for a background server
                _wakeLock = pm?.NewWakeLock(
                    Android.OS.WakeLockFlags.Partial,
                    "LibreSMS::HttpServerWakeLock");

                // No timeout — held until ReleaseWakeLock() is explicitly called
                _wakeLock?.Acquire();
                _log.Info("Wake lock acquired — CPU will stay active with screen off");
            }
            catch (Exception ex)
            {
                _log.Warning($"Wake lock acquire failed: {ex.Message}");
            }
#endif
        }

        private void ReleaseWakeLock()
        {
#if ANDROID
            try
            {
                if (_wakeLock?.IsHeld == true)
                {
                    _wakeLock.Release();
                    _log.Info("Wake lock released");
                }
                _wakeLock = null;
            }
            catch (Exception ex)
            {
                _log.Warning($"Wake lock release failed: {ex.Message}");
            }
#endif
        }

        private void StartForegroundService()
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;
                var intent = new Android.Content.Intent(context, typeof(Platforms.Android.GatewayForegroundService));
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                    context.StartForegroundService(intent);
                else
                    context.StartService(intent);
            }
            catch (Exception ex)
            {
                _log.Warning($"Foreground service start failed: {ex.Message}");
            }
#endif
        }

        private void StopForegroundService()
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;
                var intent = new Android.Content.Intent(context, typeof(Platforms.Android.GatewayForegroundService));
                context.StopService(intent);
            }
            catch { }
#endif
        }
    }
}
