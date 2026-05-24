using System.Net;
using System.Net.Sockets;
using LibreSMS.Services;
using LibreSMS.Models;

namespace LibreSMS.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly GatewayService _gateway = GatewayService.Instance;
    private System.Timers.Timer? _refreshTimer;

    private static string GetLocalIpAddress()
    {
        try
        {
            // Connect to an external address (no data sent) to determine the outbound interface IP
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "localhost";
        }
        catch
        {
            // Fallback: scan all host entries for a non-loopback IPv4
            try
            {
                return Dns.GetHostEntry(Dns.GetHostName())
                    .AddressList
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                        && !IPAddress.IsLoopback(a))
                    ?.ToString() ?? "localhost";
            }
            catch
            {
                return "localhost";
            }
        }
    }

    public DashboardPage()
    {
        InitializeComponent();
        _gateway.StatusChanged += OnGatewayStatusChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshUI();
        _refreshTimer = new System.Timers.Timer(2000);
        _refreshTimer.Elapsed += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshStats);
        _refreshTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    private void OnGatewayStatusChanged(object? sender, bool running)
        => MainThread.BeginInvokeOnMainThread(RefreshUI);

    private void RefreshUI()
    {
        var running = _gateway.IsRunning;
        var port = _gateway.Config.Config.HttpPort;
        var ip = GetLocalIpAddress();
        var baseUrl = $"http://{ip}:{port}";

        StatusDot.TextColor = running
            ? Color.FromArgb("#00FF88")
            : Color.FromArgb("#FF3B5C");

        SubtitleLabel.Text = running
            ? $"Running · {ip}:{port}"
            : "Stopped";

        StartStopButton.Text = running ? "STOP GATEWAY" : "START GATEWAY";
        StartStopButton.BackgroundColor = running
            ? Color.FromArgb("#FF3B5C")
            : Color.FromArgb("#00FF88");
        StartStopButton.TextColor = running ? Colors.White : Colors.Black;

        SendSmsEndpoint.Text = $"{baseUrl}/sendsms";
        SendMmsEndpoint.Text = $"{baseUrl}/sendmms";
        StatusEndpoint.Text = $"{baseUrl}/status";
        HealthEndpoint.Text = $"{baseUrl}/health";
        GetMessagesEndpoint.Text = $"{baseUrl}/getmessages";

        _gateway.CheckDefaultSmsApp();
        DefaultAppBanner.IsVisible = !_gateway.Status.IsDefaultSmsApp;

        RefreshStats();
    }

    private void RefreshStats()
    {
        var s = _gateway.Status;
        ReceivedCount.Text = s.MessagesReceived.ToString();
        SentCount.Text = s.MessagesSent.ToString();
        WebhookOkCount.Text = s.WebhookDeliveries.ToString();
        WebhookFailCount.Text = s.WebhookFailures.ToString();

        if (s.StartedAt.HasValue)
        {
            var uptime = DateTime.Now - s.StartedAt.Value;
            UptimeLabel.Text = uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s"
                : $"{uptime.Minutes}m {uptime.Seconds}s";
        }
        else
        {
            UptimeLabel.Text = "—";
        }
    }

    private async void OnStartStopClicked(object sender, EventArgs e)
    {
        StartStopButton.IsEnabled = false;

        if (_gateway.IsRunning)
        {
            _gateway.Stop();
        }
        else
        {
            var started = await _gateway.StartAsync();
            if (!started)
                await DisplayAlert("Error", "Failed to start the gateway. Check logs for details.", "OK");
        }

        StartStopButton.IsEnabled = true;
    }

    private void OnSetDefaultClicked(object sender, EventArgs e)
    {
        _gateway.RequestDefaultSmsApp();
    }
}