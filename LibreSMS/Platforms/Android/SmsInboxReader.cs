using LibreSMS.Models;
using Android.Content;




#if ANDROID
using AndroidX.Core.Content;
using Android.Net;
using Android.OS;
using Android.Telephony;
using Java.Net;
#endif

namespace LibreSMS.Platforms.Android
{
    public static class SmsInboxReader
    {
        /// <summary>
        /// Returns all unread SMS messages from the device inbox and marks them as read.
        /// </summary>
        public static List<SmsUnreadMessageDataItem> GetUnreadMessages()
        {
            var results = new List<SmsUnreadMessageDataItem>();
            var readIds = new List<string>();

            try
            {
                var context = global::Android.App.Application.Context;
                var uri = global::Android.Net.Uri.Parse("content://sms/inbox");

                string[] projection = {
                    "_id", "address", "body", "date", "read",
                    "type", "service_center", "thread_id"
                };

                using var cursor = context.ContentResolver?.Query(
                    uri,
                    projection,
                    selection: "read = 0",
                    selectionArgs: null,
                    sortOrder: "date DESC");

                if (cursor == null) return results;

                int idxId = cursor.GetColumnIndex("_id");
                int idxAddress = cursor.GetColumnIndex("address");
                int idxBody = cursor.GetColumnIndex("body");
                int idxDate = cursor.GetColumnIndex("date");
                int idxType = cursor.GetColumnIndex("type");
                int idxServiceCenter = cursor.GetColumnIndex("service_center");
                int idxThreadId = cursor.GetColumnIndex("thread_id");

                string ownNumber = GetOwnNumber(context);

                while (cursor.MoveToNext())
                {
                    string id = idxId >= 0 ? (cursor.GetString(idxId) ?? string.Empty) : string.Empty;

                    long dateMs = idxDate >= 0 ? cursor.GetLong(idxDate) : 0;
                    var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(dateMs).UtcDateTime;

                    int smsType = idxType >= 0 ? cursor.GetInt(idxType) : 1;
                    string messageType = smsType switch
                    {
                        1 => "inbox",
                        2 => "sent",
                        3 => "draft",
                        4 => "outbox",
                        5 => "failed",
                        6 => "queued",
                        _ => smsType.ToString()
                    };

                    string sender = idxAddress >= 0 ? (cursor.GetString(idxAddress) ?? string.Empty) : string.Empty;

                    int? threadId = null;
                    if (idxThreadId >= 0 && !cursor.IsNull(idxThreadId))
                        threadId = cursor.GetInt(idxThreadId);

                    results.Add(new SmsUnreadMessageDataItem
                    {
                        id = id,
                        sender = sender,
                        number = sender,
                        receiver = ownNumber,
                        message = idxBody >= 0 ? (cursor.GetString(idxBody) ?? string.Empty) : string.Empty,
                        date = dateTime.ToString("o"),
                        read = false,
                        messageType = messageType,
                        serviceCenter = idxServiceCenter >= 0 ? (cursor.GetString(idxServiceCenter) ?? string.Empty) : string.Empty,
                        threadID = threadId
                    });

                    if (!string.IsNullOrEmpty(id))
                        readIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                Services.GatewayLogService.Instance.Error($"SmsInboxReader query error: {ex.Message}");
            }

            // Mark all fetched messages as read
            if (readIds.Count > 0)
                MarkAsRead(readIds);

            return results;
        }

        /// <summary>
        /// Updates read = 1 for every message id in the list.
        /// </summary>
        private static void MarkAsRead(List<string> ids)
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var uri = global::Android.Net.Uri.Parse("content://sms/inbox");
                var values = new ContentValues();
                values.Put("read", 1);

                // Build "WHERE _id IN (?,?,?)" to do it in one call
                var placeholders = string.Join(",", System.Linq.Enumerable.Repeat("?", ids.Count));
                int updated = context.ContentResolver?.Update(
                    uri,
                    values,
                    $"_id IN ({placeholders})",
                    ids.ToArray()) ?? 0;

                Services.GatewayLogService.Instance.Info($"Marked {updated} message(s) as read.");
            }
            catch (Exception ex)
            {
                Services.GatewayLogService.Instance.Error($"SmsInboxReader mark-read error: {ex.Message}");
            }
        }

        private static string GetOwnNumber(Context context)
        {
            try
            {
                var tm = context.GetSystemService(Context.TelephonyService)
                    as global::Android.Telephony.TelephonyManager;
                return tm?.Line1Number ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
