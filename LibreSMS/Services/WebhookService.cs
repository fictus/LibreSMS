using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using LibreSMS.Models;

namespace LibreSMS.Services
{
    public class WebhookService
    {
        private readonly HttpClient _http;
        private readonly GatewayLogService _log = GatewayLogService.Instance;

        public int DeliveryCount { get; private set; }
        public int FailureCount { get; private set; }

        public WebhookService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<bool> SendWebhookAsync(string webhookUrl, IncomingMessage message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                _log.Warning("Webhook URL not configured, skipping delivery");
                return false;
            }

            try
            {
                // Build GET query string
                var uriBuilder = new UriBuilder(webhookUrl);
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);

                query["id"] = message.Id;
                query["from"] = message.From;
                query["to"] = message.To;
                query["message"] = message.Body;
                query["type"] = message.Type.ToString();
                query["timestamp"] = message.ReceivedAt.ToString("o");

                // Add attachment info
                if (message.Attachments.Count > 0)
                {
                    query["attachment_count"] = message.Attachments.Count.ToString();
                    for (int i = 0; i < message.Attachments.Count; i++)
                    {
                        var att = message.Attachments[i];
                        query[$"attachment_{i}_name"] = att.FileName;
                        query[$"attachment_{i}_type"] = att.MimeType;
                        query[$"attachment_{i}_data"] = att.Base64Data;
                        query[$"attachment_{i}_size"] = att.SizeBytes.ToString();
                    }
                }

                uriBuilder.Query = query.ToString();
                var finalUrl = uriBuilder.ToString();

                _log.Info($"Webhook → {finalUrl[..Math.Min(80, finalUrl.Length)]}...");

                var response = await _http.GetAsync(finalUrl);

                if (response.IsSuccessStatusCode)
                {
                    DeliveryCount++;
                    _log.Success($"Webhook delivered (HTTP {(int)response.StatusCode})");
                    return true;
                }
                else
                {
                    FailureCount++;
                    _log.Error($"Webhook failed: HTTP {(int)response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                FailureCount++;
                _log.Error($"Webhook error: {ex.Message}");
                return false;
            }
        }

        public void Reset()
        {
            DeliveryCount = 0;
            FailureCount = 0;
        }
    }
}
