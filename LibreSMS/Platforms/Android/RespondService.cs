using Android.App;
using Android.Content;
using Android.OS;

namespace LibreSMS.Platforms.Android
{
    [Service(
        Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE",
        Exported = true)]

    [IntentFilter(
        new[] { "android.intent.action.RESPOND_VIA_MESSAGE" },
        Categories = new[] { Intent.CategoryDefault },
        DataSchemes = new[] { "sms", "smsto", "mms", "mmsto" })]

    public class RespondService : Service
    {
        public override IBinder? OnBind(Intent? intent)
        {
            return null;
        }

        public override StartCommandResult OnStartCommand(
            Intent? intent,
            StartCommandFlags flags,
            int startId)
        {
            // Optional:
            // Handle quick-response SMS here.

            return StartCommandResult.NotSticky;
        }
    }
}
