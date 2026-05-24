using LibreSMS.Services;

namespace LibreSMS
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();

            // Initialize gateway service on startup
            _ = Task.Run(async () =>
            {
                await GatewayService.Instance.InitializeAsync();
            });
        }
    }
}
