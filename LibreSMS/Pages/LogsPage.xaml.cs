using LibreSMS.Services;

namespace LibreSMS.Pages;

public partial class LogsPage : ContentPage
{
    private readonly GatewayLogService _log = GatewayLogService.Instance;

    public LogsPage()
    {
        InitializeComponent();
        LogList.ItemsSource = _log.Logs;
        _log.LogAdded += OnLogAdded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateCount();
    }

    private void OnLogAdded(object? sender, Models.LogEntry e)
    {
        MainThread.BeginInvokeOnMainThread(UpdateCount);
    }

    private void UpdateCount()
    {
        LogCountLabel.Text = $"{_log.Logs.Count} entries";
    }

    private void OnClearClicked(object sender, EventArgs e)
    {
        _log.Clear();
        UpdateCount();
    }
}
