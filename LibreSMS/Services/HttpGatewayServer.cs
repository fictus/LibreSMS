using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using LibreSMS.Models;
using LibreSMS.Services;

namespace LibreSMS.Services
{
    public class HttpGatewayServer
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly GatewayLogService _log = GatewayLogService.Instance;
        private readonly SmsSenderService _smsSender;
        private readonly GatewayStatus _status;

        public bool IsRunning => _listener?.IsListening ?? false;

        public HttpGatewayServer(SmsSenderService smsSender, GatewayStatus status)
        {
            _smsSender = smsSender;
            _status = status;
        }

        public async Task<bool> StartAsync(int port)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{port}/");  // listens on ALL interfaces
                _listener.Start();

                _cts = new CancellationTokenSource();

                // Get the actual LAN IP to show in the log
                string lanIp = GetLanIp();
                _status.ListenUrl = $"http://{lanIp}:{port}";
                _status.StartedAt = DateTime.Now;

                _log.Success($"HTTP server listening on port {port}");
                _log.Info($"LAN URL: http://{lanIp}:{port}");
                _log.Info($"Endpoints: /sendsms  /sendmms  /status  /getmessages  /health");

                _ = Task.Run(() => ListenLoopAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to start HTTP server: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
                _listener = null;
                _log.Info("HTTP server stopped");
            }
            catch { }
        }

        private static string GetLanIp()
        {
            try
            {
                foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (iface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return addr.Address.ToString();
                    }
                }
            }
            catch { }
            return "0.0.0.0";
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && (_listener?.IsListening ?? false))
            {
                try
                {
                    var context = await _listener!.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _log.Error($"HTTP listener error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;
            var path = req.Url?.AbsolutePath.ToLower().TrimEnd('/') ?? "/";

            _log.Info($"← {req.HttpMethod} {req.Url?.PathAndQuery}");

            res.ContentType = "application/json; charset=utf-8";
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            try
            {
                ApiResponse apiResponse;

                switch (path)
                {
                    case "/sendsms":
                        apiResponse = await HandleSendSmsAsync(req);
                        break;
                    case "/sendmms":
                        apiResponse = await HandleSendMmsAsync(req);
                        break;
                    case "/status":
                        apiResponse = new ApiResponse
                        {
                            Success = true,
                            Message = "Gateway status",
                            Data = _status
                        };
                        break;
                    case "/health":
                        apiResponse = new ApiResponse { Success = true, Message = "OK" };
                        break;
                    case "/getmessages":
                        apiResponse = HandleGetMessages(req);
                        break;
                    default:
                        res.StatusCode = 404;
                        apiResponse = new ApiResponse { Success = false, Message = $"Unknown endpoint: {path}" };
                        break;
                }

                var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions { WriteIndented = true });
                var bytes = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes);
            }
            catch (Exception ex)
            {
                _log.Error($"Request handling error: {ex.Message}");
                res.StatusCode = 500;
                var errJson = JsonSerializer.Serialize(new ApiResponse { Success = false, Message = ex.Message });
                var errBytes = Encoding.UTF8.GetBytes(errJson);
                await res.OutputStream.WriteAsync(errBytes);
            }
            finally
            {
                res.Close();
            }
        }

        private async Task<ApiResponse> HandleSendSmsAsync(HttpListenerRequest req)
        {
            // Accept both GET query params and POST body
            var query = GetQueryParams(req);

            var to = query.Get("to") ?? query.Get("number") ?? query.Get("phone");
            var message = query.Get("message") ?? query.Get("text") ?? query.Get("body");

            if (string.IsNullOrWhiteSpace(to))
                return new ApiResponse { Success = false, Message = "Missing required parameter: 'to'" };
            if (string.IsNullOrWhiteSpace(message))
                return new ApiResponse { Success = false, Message = "Missing required parameter: 'message'" };

            _log.Info($"Send SMS → {to}: {message[..Math.Min(50, message.Length)]}...");

            var success = await _smsSender.SendSmsAsync(to, message);
            if (success)
            {
                _status.MessagesSent++;
                return new ApiResponse { Success = true, Message = $"SMS sent to {to}" };
            }
            return new ApiResponse { Success = false, Message = "Failed to send SMS" };
        }

        private async Task<ApiResponse> HandleSendMmsAsync(HttpListenerRequest req)
        {
            var query = GetQueryParams(req);

            var to = query.Get("to") ?? query.Get("number") ?? query.Get("phone");
            var message = query.Get("message") ?? query.Get("text") ?? query.Get("body") ?? string.Empty;

            // Accept image as base64 or URL
            var imageBase64List = new List<string>();
            var imageUrlList = new List<string>();

            var imageBase64 = query.Get("image") ?? query.Get("image_base64") ?? query.Get("attachment");
            if (!string.IsNullOrEmpty(imageBase64)) imageBase64List.Add(imageBase64);

            // Multiple images: image_0, image_1, ...
            for (int i = 0; i < 10; i++)
            {
                var img = query.Get($"image_{i}");
                if (img != null) imageBase64List.Add(img);
                var imgUrl = query.Get($"image_url_{i}");
                if (imgUrl != null) imageUrlList.Add(imgUrl);
            }

            var singleUrl = query.Get("image_url");
            if (!string.IsNullOrEmpty(singleUrl)) imageUrlList.Add(singleUrl);

            if (string.IsNullOrWhiteSpace(to))
                return new ApiResponse { Success = false, Message = "Missing required parameter: 'to'" };

            _log.Info($"Send MMS → {to} [{imageBase64List.Count + imageUrlList.Count} attachments]");

            var mmsReq = new SendMmsRequest
            {
                To = to,
                Message = message,
                ImageBase64 = imageBase64List,
                ImageUrls = imageUrlList
            };

            var success = await _smsSender.SendMmsAsync(mmsReq);
            if (success)
            {
                _status.MessagesSent++;
                return new ApiResponse { Success = true, Message = $"MMS sent to {to}" };
            }
            return new ApiResponse { Success = false, Message = "Failed to send MMS" };
        }

        private ApiResponse HandleGetMessages(HttpListenerRequest req)
        {
            if (req.HttpMethod != "GET")
            {
                return new ApiResponse
                {
                    Success = false,
                    Message = "Method not allowed. Use GET.",
                    Data = new SmsGetMessagesDataItem
                    {
                        messages = new System.Collections.Generic.List<SmsUnreadMessageDataItem>(),
                        description = "Method not allowed.",
                        isSuccessful = false,
                        requestMethod = req.HttpMethod
                    }
                };
            }

            try
            {
#if ANDROID
                var messages = LibreSMS.Platforms.Android.SmsInboxReader.GetUnreadMessages();
#else
                var messages = new System.Collections.Generic.List<SmsUnreadMessageDataItem>();
#endif
                var data = new SmsGetMessagesDataItem
                {
                    messages = messages,
                    description = $"Retrieved {messages.Count} unread message(s).",
                    isSuccessful = true,
                    requestMethod = req.HttpMethod
                };

                _log.Info($"GET /getmessages → {messages.Count} unread message(s) returned");

                return new ApiResponse
                {
                    Success = true,
                    Message = $"Retrieved {messages.Count} unread message(s).",
                    Data = data
                };
            }
            catch (Exception ex)
            {
                _log.Error($"GetMessages error: {ex.Message}");

                return new ApiResponse
                {
                    Success = false,
                    Message = ex.Message,
                    Data = new SmsGetMessagesDataItem
                    {
                        messages = new System.Collections.Generic.List<SmsUnreadMessageDataItem>(),
                        description = ex.Message,
                        isSuccessful = false,
                        requestMethod = req.HttpMethod
                    }
                };
            }
        }

        private System.Collections.Specialized.NameValueCollection GetQueryParams(HttpListenerRequest req)
        {
            // Try GET query string first
            if (req.Url?.Query.Length > 1)
                return HttpUtility.ParseQueryString(req.Url.Query);

            // Try POST body
            if (req.HttpMethod == "POST" && req.HasEntityBody)
            {
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                var body = reader.ReadToEnd();

                if (req.ContentType?.Contains("application/json") == true)
                {
                    // Parse JSON into NameValueCollection
                    var nvc = HttpUtility.ParseQueryString(string.Empty);
                    try
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                        if (dict != null)
                            foreach (var kv in dict)
                                nvc[kv.Key] = kv.Value?.ToString();
                    }
                    catch { }
                    return nvc;
                }

                return HttpUtility.ParseQueryString(body);
            }

            return HttpUtility.ParseQueryString(string.Empty);
        }
    }
}
