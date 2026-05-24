using LibreSMS.Models;
using LibreSMS.Services;

namespace LibreSMS.Pages;

public partial class ControlsPage : ContentPage
{
    private readonly GatewayService _gateway = GatewayService.Instance;

    public ControlsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadConfig();
        UpdateDefaultAppStatus();
    }

    private void LoadConfig()
    {
        var cfg = _gateway.Config.Config;
        WebhookUrlEntry.Text = cfg.WebhookUrl;
        WebhookSecretEntry.Text = cfg.WebhookSecret;
        PortEntry.Text = cfg.HttpPort.ToString();
        ForwardSmsSwitch.IsToggled = cfg.ForwardSms;
        ForwardMmsSwitch.IsToggled = cfg.ForwardMms;
        AutoStartSwitch.IsToggled = cfg.AutoStart;
    }

    private void UpdateDefaultAppStatus()
    {
        _gateway.CheckDefaultSmsApp();
        DefaultAppStatus.Text = _gateway.Status.IsDefaultSmsApp
            ? "✔ This is the default SMS app"
            : "✘ Not the default SMS app";
        DefaultAppStatus.TextColor = _gateway.Status.IsDefaultSmsApp
            ? Color.FromArgb("#00FF88")
            : Color.FromArgb("#FF3B5C");
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (!int.TryParse(PortEntry.Text, out int port) || port < 1024 || port > 65535)
        {
            await DisplayAlert("Invalid Port", "Port must be a number between 1024 and 65535.", "OK");
            return;
        }

        var newConfig = new GatewayConfig
        {
            WebhookUrl = WebhookUrlEntry.Text?.Trim() ?? string.Empty,
            WebhookSecret = WebhookSecretEntry.Text?.Trim() ?? string.Empty,
            HttpPort = port,
            ForwardSms = ForwardSmsSwitch.IsToggled,
            ForwardMms = ForwardMmsSwitch.IsToggled,
            AutoStart = AutoStartSwitch.IsToggled
        };

        _gateway.Config.UpdateConfig(newConfig);
        await _gateway.Config.SaveAsync();

        SaveStatusLabel.Text = "✔ Configuration saved";
        SaveStatusLabel.TextColor = Color.FromArgb("#00FF88");

        await Task.Delay(3000);
        SaveStatusLabel.Text = string.Empty;

        if (_gateway.IsRunning)
        {
            bool restart = await DisplayAlert("Restart Required",
                "The gateway is running. Restart it to apply changes?",
                "Restart Now", "Later");

            if (restart)
            {
                _gateway.Stop();
                await _gateway.StartAsync();
            }
        }
    }

    private void OnSetDefaultClicked(object sender, EventArgs e)
    {
        _gateway.RequestDefaultSmsApp();
    }
}
