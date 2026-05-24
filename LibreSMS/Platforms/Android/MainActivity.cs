using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using LibreSMS.Services;

namespace LibreSMS
{
    [Activity(
        Theme = "@style/Maui.MainTheme.NoActionBar",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                               ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                               ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(new[] { "android.intent.action.SENDTO" },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "sms", "smsto", "mms", "mmsto" })]
    public class MainActivity : MauiAppCompatActivity
    {
        private static readonly string[] RequiredPermissions = new[]
        {
            global::Android.Manifest.Permission.SendSms,
            global::Android.Manifest.Permission.ReceiveSms,
            global::Android.Manifest.Permission.ReadSms,
            global::Android.Manifest.Permission.WriteSms,
            global::Android.Manifest.Permission.ReceiveMms,
            global::Android.Manifest.Permission.ReadPhoneState,
            global::Android.Manifest.Permission.ReadContacts,
            global::Android.Manifest.Permission.ReadExternalStorage,
            global::Android.Manifest.Permission.Internet,
        };

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestAllPermissions();
            RequestSmsRole();
        }

        private void RequestAllPermissions()
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var perm in RequiredPermissions)
            {
                if (CheckSelfPermission(perm) != Permission.Granted)
                    missing.Add(perm);
            }

            if (missing.Count > 0)
                RequestPermissions(missing.ToArray(), 1001);
        }

        private void RequestSmsRole()
        {
            var intent = new Intent(
        global::Android.Provider.Telephony.Sms.Intents.ActionChangeDefault);

            intent.PutExtra(
                global::Android.Provider.Telephony.Sms.Intents.ExtraPackageName,
                PackageName);

            StartActivity(intent);
        }

        public override void OnRequestPermissionsResult(int requestCode,
            string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            for (int i = 0; i < permissions.Length; i++)
            {
                var granted = grantResults[i] == Permission.Granted;
                GatewayLogService.Instance.Info(
                    $"Permission {permissions[i].Split('.')[^1]}: {(granted ? "GRANTED" : "DENIED")}");
            }

            // Bug 6 fix: GatewayService.InitializeAsync() is fire-and-forgot in App.xaml.cs,
            // so it may not have finished by the time the permission dialog closes.
            // We post CheckDefaultSmsApp() onto the main thread after a short yield so the
            // initialisation task always gets a head-start before we touch its state.
            _ = Task.Run(async () =>
            {
                await GatewayService.Instance.WaitForInitAsync();
                MainThread.BeginInvokeOnMainThread(() =>
                    GatewayService.Instance.CheckDefaultSmsApp());
            });
        }
    }
}
