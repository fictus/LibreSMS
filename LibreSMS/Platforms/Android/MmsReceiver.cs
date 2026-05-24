using Android.App;
using Android.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibreSMS.Models;
using LibreSMS.Services;

namespace LibreSMS.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_WAP_PUSH")]
    [IntentFilter(new[] { "android.provider.Telephony.WAP_PUSH_DELIVER" },
        DataMimeType = "application/vnd.wap.mms-message",
        Priority = 999)]
    public class MmsReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null) return;

            try
            {
                // Read MMS from content provider
                var incomingMessage = new IncomingMessage
                {
                    From = "MMS Sender",
                    To = "self",
                    Body = string.Empty,
                    Type = MessageType.MMS,
                    ReceivedAt = DateTime.UtcNow
                };

                // Try to get from content provider
                ReadFromContentProvider(context, incomingMessage);

                _ = GatewayService.Instance.HandleIncomingMessageAsync(incomingMessage);
            }
            catch (Exception ex)
            {
                GatewayLogService.Instance.Error($"MMS receive error: {ex.Message}");
            }
        }

        private void ReadFromContentProvider(Context context, IncomingMessage message)
        {
            try
            {
                // Query MMS inbox for the latest message
                var mmsUri = global::Android.Net.Uri.Parse("content://mms");
                var projection = new[] { "_id", "date", "m_type" };
                var sortOrder = "date DESC";

                using var cursor = context.ContentResolver?.Query(mmsUri, projection, null, null, sortOrder);
                if (cursor == null || !cursor.MoveToFirst()) return;

                var idIdx = cursor.GetColumnIndex("_id");
                var mmsId = cursor.GetLong(idIdx);

                // Get sender
                var addrUri = global::Android.Net.Uri.Parse($"content://mms/{mmsId}/addr");
                using var addrCursor = context.ContentResolver?.Query(addrUri,
                    new[] { "address", "type" }, null, null, null);

                if (addrCursor != null && addrCursor.MoveToFirst())
                {
                    do
                    {
                        var typeIdx = addrCursor.GetColumnIndex("type");
                        var addrIdx = addrCursor.GetColumnIndex("address");
                        var type = addrCursor.GetInt(typeIdx);
                        if (type == 137) // FROM
                        {
                            message.From = addrCursor.GetString(addrIdx) ?? "Unknown";
                            break;
                        }
                    } while (addrCursor.MoveToNext());
                }

                // Get parts (text + attachments)
                var partUri = global::Android.Net.Uri.Parse($"content://mms/{mmsId}/part");
                using var partCursor = context.ContentResolver?.Query(partUri,
                    new[] { "_id", "ct", "_data", "text" }, null, null, null);

                if (partCursor != null)
                {
                    while (partCursor.MoveToNext())
                    {
                        var ctIdx = partCursor.GetColumnIndex("ct");
                        var contentType = partCursor.GetString(ctIdx) ?? string.Empty;

                        if (contentType == "text/plain")
                        {
                            var textIdx = partCursor.GetColumnIndex("text");
                            message.Body += partCursor.GetString(textIdx) ?? string.Empty;
                        }
                        else if (contentType.StartsWith("image/") || contentType.StartsWith("video/"))
                        {
                            var partIdIdx = partCursor.GetColumnIndex("_id");
                            var partId = partCursor.GetLong(partIdIdx);

                            var attachment = ReadMmsPart(context, partId, contentType);
                            if (attachment != null)
                                message.Attachments.Add(attachment);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GatewayLogService.Instance.Error($"MMS content provider read error: {ex.Message}");
            }
        }

        private MessageAttachment? ReadMmsPart(Context context, long partId, string contentType)
        {
            try
            {
                var partUri = global::Android.Net.Uri.Parse($"content://mms/part/{partId}");
                using var inputStream = context.ContentResolver?.OpenInputStream(partUri);
                if (inputStream == null) return null;

                using var ms = new MemoryStream();
                inputStream.CopyTo(ms);
                var data = ms.ToArray();

                var ext = contentType.Replace("/", ".");
                return new MessageAttachment
                {
                    FileName = $"attachment_{partId}.{ext.Split('.')[^1]}",
                    MimeType = contentType,
                    Base64Data = Convert.ToBase64String(data),
                    SizeBytes = data.Length
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
