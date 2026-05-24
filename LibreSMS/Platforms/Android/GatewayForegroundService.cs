using Android.App;
using Android.Content;
using Android.OS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibreSMS.Models;
using LibreSMS.Services;

namespace LibreSMS.Platforms.Android
{
    [Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
    public class GatewayForegroundService : Service
    {
        private const string ChannelId = "sms_gateway_channel";
        private const int NotificationId = 1001;

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            CreateNotificationChannel();

            // Build a safe placeholder notification immediately — Config may not be loaded yet.
            // We update the notification text once init is done.
            StartForeground(NotificationId, BuildNotification(port: null));
            GatewayLogService.Instance.Info("Foreground service started");

            // Update the notification with the real port once InitializeAsync() has finished.
            _ = Task.Run(async () =>
            {
                try
                {
                    await GatewayService.Instance.WaitForInitAsync();
                    var nm = (NotificationManager?)GetSystemService(NotificationService);
                    nm?.Notify(NotificationId, BuildNotification(GatewayService.Instance.Config.Config.HttpPort));
                }
                catch (Exception ex)
                {
                    GatewayLogService.Instance.Warning($"Notification update failed: {ex.Message}");
                }
            });

            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            GatewayLogService.Instance.Info("Foreground service destroyed");
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId,
                    "LibreSMS Service",
                    NotificationImportance.Low)
                {
                    Description = "SMS/MMS Gateway is running"
                };
                var nm = (NotificationManager?)GetSystemService(NotificationService);
                nm?.CreateNotificationChannel(channel);
            }
        }

        private Notification BuildNotification(int? port)
        {
            var activityIntent = new Intent(this, typeof(MainActivity));
            var pendingIntent = PendingIntent.GetActivity(this, 0, activityIntent,
                PendingIntentFlags.Immutable);

            string text = port.HasValue
                ? $"Listening on port {port.Value}"
                : "Starting...";

            var builder = new Notification.Builder(this, ChannelId)
                .SetContentTitle("LibreSMS Active")
                .SetContentText(text)
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogEmail)
                .SetOngoing(true)
                .SetContentIntent(pendingIntent);

            return builder.Build()!;
        }
    }

    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted })]
    public class BootReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != Intent.ActionBootCompleted || context == null) return;

            GatewayLogService.Instance.Info("Boot completed, checking auto-start...");

            // OnReceive is synchronous — we cannot await here.
            // Spin up a background task so we can wait for init before reading Config.
            var ctx = context.ApplicationContext ?? context;
            _ = Task.Run(async () =>
            {
                try
                {
                    await GatewayService.Instance.WaitForInitAsync();

                    if (GatewayService.Instance.Config.Config.AutoStart)
                    {
                        var serviceIntent = new Intent(ctx, typeof(GatewayForegroundService));
                        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                            ctx.StartForegroundService(serviceIntent);
                        else
                            ctx.StartService(serviceIntent);
                    }
                }
                catch (Exception ex)
                {
                    GatewayLogService.Instance.Error($"BootReceiver auto-start failed: {ex.Message}");
                }
            });
        }
    }
}
