using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibreSMS.Models;
using LibreSMS.Services;

namespace LibreSMS.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
    [IntentFilter(new[] {
        "android.provider.Telephony.SMS_DELIVER"
    }, Priority = 999)]
    public class SmsReceiver : BroadcastReceiver
    {

        private readonly GatewayLogService _log = GatewayLogService.Instance;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != Telephony.Sms.Intents.SmsDeliverAction)
                return;

            if (context == null) return;

            try
            {
                var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
                if (messages == null || messages.Length == 0) return;

                // Group messages by sender (for multi-part SMS)
                var grouped = new Dictionary<string, (string from, List<string> parts, long timestamp)>();

                foreach (var smsMessage in messages)
                {
                    var from = smsMessage.OriginatingAddress ?? "Unknown";
                    if (!grouped.ContainsKey(from))
                        grouped[from] = (from, new List<string>(), smsMessage.TimestampMillis);
                    grouped[from].parts.Add(smsMessage.MessageBody ?? string.Empty);
                }

                foreach (var (sender, data) in grouped)
                {
                    var body = string.Join("", data.parts);

                    // Write to inbox so it appears when querying content://sms/inbox
                    WriteToInbox(context, data.from, body, data.timestamp);

                    var incomingMessage = new IncomingMessage
                    {
                        From = data.from,
                        To = GetOwnNumber(),
                        Body = body,
                        Type = MessageType.SMS,
                        ReceivedAt = DateTime.UtcNow
                    };

                    _ = GatewayService.Instance.HandleIncomingMessageAsync(incomingMessage);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"SMS receive error: {ex.Message}");
            }
        }

        private void WriteToInbox(Context context, string sender, string body, long timestampMs)
        {
            try
            {
                var values = new ContentValues();
                values.Put("address", sender);
                values.Put("body", body);
                values.Put("date", timestampMs);
                values.Put("read", 0);
                values.Put("type", 1); // 1 = inbox

                var insertedUri = context.ContentResolver?.Insert(
                    global::Android.Net.Uri.Parse("content://sms/inbox"),
                    values);

                _log.Info($"WriteToInbox: uri={insertedUri} from={sender} body={body?[..Math.Min(30, body?.Length ?? 0)]}");
            }
            catch (Exception ex)
            {
                _log.Error($"WriteToInbox failed: {ex.Message}");
            }
        }

        private string GetOwnNumber()
        {
            try
            {
                var ctx = global::Android.App.Application.Context;
                var tm = ctx.GetSystemService(Context.TelephonyService) as TelephonyManager;
                return tm?.Line1Number ?? "self";
            }
            catch
            {
                return "self";
            }
        }
    }
}
