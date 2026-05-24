using LibreSMS.Models;
using LibreSMS.Services;

namespace LibreSMS.Pages;

public partial class TestPage : ContentPage
{
    private readonly GatewayService _gateway = GatewayService.Instance;

    public TestPage()
    {
        InitializeComponent();
    }

    private async void OnSendSmsClicked(object sender, EventArgs e)
    {
        var to = SmsToEntry.Text?.Trim();
        var message = SmsMessageEntry.Text?.Trim();

        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(message))
        {
            ShowResult(false, "Please enter both a phone number and message.");
            return;
        }

        var success = await _gateway.SmsSender.SendSmsAsync(to, message);
        ShowResult(success, success ? $"SMS sent to {to}" : "Failed to send SMS. Check logs.");

        if (success)
        {
            _gateway.Status.MessagesSent++;
            SmsMessageEntry.Text = string.Empty;
        }
    }

    private async void OnSendMmsClicked(object sender, EventArgs e)
    {
        var to = MmsToEntry.Text?.Trim();
        var message = MmsMessageEntry.Text?.Trim() ?? string.Empty;
        var imageUrl = MmsImageUrlEntry.Text?.Trim();

        if (string.IsNullOrEmpty(to))
        {
            ShowResult(false, "Please enter a phone number.");
            return;
        }

        var request = new SendMmsRequest
        {
            To = to,
            Message = message,
            ImageUrls = string.IsNullOrEmpty(imageUrl) ? new() : new() { imageUrl }
        };

        var success = await _gateway.SmsSender.SendMmsAsync(request);
        ShowResult(success, success ? $"MMS sent to {to}" : "Failed to send MMS. Check logs.");

        if (success)
        {
            _gateway.Status.MessagesSent++;
            MmsMessageEntry.Text = string.Empty;
            MmsImageUrlEntry.Text = string.Empty;
        }
    }

    private async void OnTestWebhookClicked(object sender, EventArgs e)
    {
        var webhookUrl = _gateway.Config.Config.WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            ShowResult(false, "No webhook URL configured. Go to Controls tab to set one.");
            return;
        }

        var testMessage = new IncomingMessage
        {
            From = "+15550000001",
            To = "self",
            Body = "Test message from LibreSMS at " + DateTime.Now.ToString("HH:mm:ss"),
            Type = MessageType.SMS
        };

        var success = await _gateway.Webhook.SendWebhookAsync(webhookUrl, testMessage);
        ShowResult(success,
            success ? $"Webhook delivered to {webhookUrl}" : "Webhook delivery failed. Check logs.");
    }

    private async void ShowResult(bool success, string message)
    {
        ResultBanner.IsVisible = true;
        ResultBanner.BackgroundColor = success
            ? Color.FromArgb("#002210")
            : Color.FromArgb("#3D0010");
        ResultBanner.Stroke = new SolidColorBrush(
            success ? Color.FromArgb("#00FF88") : Color.FromArgb("#FF3B5C"));

        ResultIcon.Text = success ? "✔" : "✘";
        ResultIcon.TextColor = success
            ? Color.FromArgb("#00FF88")
            : Color.FromArgb("#FF3B5C");

        ResultLabel.Text = message;
        ResultLabel.TextColor = Color.FromArgb("#F0F0F0");

        await Task.Delay(5000);
        ResultBanner.IsVisible = false;
    }
}
