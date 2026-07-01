//using Android.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibreSMS.Models;
using LibreSMS.Services;
using System.Net;
using Android.Telephony.Gsm;


#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Core.Content;
using Android.Net;
using Android.OS;
using Android.Telephony;
using Java.Net;
#endif

namespace LibreSMS.Services
{
    public class SmsSenderService
    {
        private readonly GatewayLogService _log = GatewayLogService.Instance;
        private static readonly SemaphoreSlim _smsSemaphore = new SemaphoreSlim(1, 1);

        // ─────────────────────────────────────────────────────────────────────
        // SMS
        // ─────────────────────────────────────────────────────────────────────

        public async Task<bool> SendSmsAsync(string to, string message)
        {
#if ANDROID
            await _smsSemaphore.WaitAsync();
            try
            {
                var smsManager = Android.Telephony.SmsManager.Default;
                if (smsManager == null)
                {
                    _log.Error("SmsManager not available");
                    return false;
                }

                var parts = smsManager.DivideMessage(message);
                if (parts == null || parts.Count == 1)
                {
                    smsManager.SendTextMessage(to, null, message, null, null);
                }
                else
                {
                    smsManager.SendMultipartTextMessage(to, null, parts, null, null);
                }

                // Throttle — give the modem ~1 second per message before next send
                await Task.Delay(1000);

                _log.Success($"SMS sent to {to}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Send SMS error: {ex.Message}");
                return false;
            }
            finally
            {
                _smsSemaphore.Release();
            }
#else
    _log.Warning("SMS sending only supported on Android");
    return await Task.FromResult(false);
#endif
        }

        // ─────────────────────────────────────────────────────────────────────
        // MMS
        //
        // Strategy: build a standard MIME multipart/related body and POST it
        // directly to AT&T's MMSC over the mobile data connection, routing
        // through the MMS proxy.  This is exactly what Android's built-in MMS
        // stack does internally.  Using raw HTTP gives us full control over the
        // encoding so the receiving device can decode it correctly.
        //
        // Steps:
        //   1. Force the MMS APN data route so the HTTP request goes over
        //      mobile data (not Wi-Fi, which can't reach the MMSC).
        //   2. Build the MMS PDU as a MIME multipart/related body:
        //        - WAP binary headers  (m-send.req envelope)
        //        - SMIL slide
        //        - text/plain part     (if message non-empty)
        //        - image/* parts       (one per attachment)
        //   3. POST to http://mmsc.mobile.att.net via proxy.mobile.att.net:80
        //   4. Release the data route.
        // ─────────────────────────────────────────────────────────────────────

        // Drop-in replacement for SendMmsAsync_GOOD_ONE.
        // 
        // Strategy: instead of fighting SendMultimediaMessage's PDU parser,
        // POST the PDU directly to AT&T's MMSC over the mobile data network,
        // exactly like the Klinker android-smsmms library does.
        //
        // Requires in AndroidManifest.xml:
        //   <uses-permission android:name="android.permission.CHANGE_NETWORK_STATE" />
        //   <uses-permission android:name="android.permission.WRITE_APN_SETTINGS" />  (not needed)
        //   <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
        //
        // The MMSC URL and proxy are read from the APN database at runtime so this
        // works on any carrier, not just AT&T.

        public async Task<bool> SendMmsAsync(SendMmsRequest request)
        {
#if ANDROID
            await _smsSemaphore.WaitAsync();

            Android.Net.ConnectivityManager? cm = null;
            System.Threading.CancellationTokenSource? cts = null;

            try
            {
                var context = Android.App.Application.Context;

                // ── 1. Restore default network first ─────────────────────────────
                // If a previous MMS call crashed without cleanup, the process may
                // still be bound to the MMS network. Reset it before downloading images.
                try { Android.Net.ConnectivityManager.SetProcessDefaultNetwork(null); } catch { }

                // ── 2. Collect attachments on the NORMAL internet connection ──────
                var attachments = new List<(byte[] Data, string Mime, string Name)>();

                foreach (var b64 in request.ImageBase64)
                {
                    var raw = b64.Contains(',') ? b64.Split(',')[1] : b64;
                    var bytes = Convert.FromBase64String(raw);
                    var mime = MmsDetectMime(bytes);
                    attachments.Add((bytes, mime, $"att{attachments.Count}{MmsMimeToExt(mime)}"));
                }

                if (request.ImageUrls.Count > 0)
                {
                    // Dedicated client with its own handler so it is never affected
                    // by SetProcessDefaultNetwork changes made later in this method.
                    using var dlHandler = new HttpClientHandler();
                    using var dlClient = new HttpClient(dlHandler)
                    {
                        Timeout = TimeSpan.FromSeconds(30)
                    };

                    foreach (var url in request.ImageUrls)
                    {
                        _log.Info($"Downloading image: {url}");
                        var bytes = await dlClient.GetByteArrayAsync(url);
                        var mime = MmsDetectMime(bytes);
                        attachments.Add((bytes, mime, $"att{attachments.Count}{MmsMimeToExt(mime)}"));
                        _log.Info($"Downloaded {bytes.Length} bytes, mime={mime}");
                    }
                }

                // ── 3. Build PDU ──────────────────────────────────────────────────
                var pduBytes = MmsBuildPdu(request.To, request.Message, attachments);
                //_log.Info($"PDU length: {pduBytes.Length}");
                //_log.Info($"PDU bytes: {BitConverter.ToString(pduBytes).Replace("-", " ")}");

                // ── 4. Acquire MMS network ────────────────────────────────────────
                cm = (Android.Net.ConnectivityManager?)
                      context.GetSystemService(Android.Content.Context.ConnectivityService);
                if (cm == null) { _log.Error("No ConnectivityManager"); return false; }

                cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(40));
                var mmsNet = await AcquireMmsNetworkAsync(cm, cts.Token);
                if (mmsNet == null)
                    _log.Warning("Could not acquire MMS network, trying default network");

                // ── 5. APN settings ───────────────────────────────────────────────
                var (mmscUrl, proxyHost, proxyPort) = GetMmsApnSettings(context, _log);
                _log.Info($"MMSC={mmscUrl}  proxy={proxyHost}:{proxyPort}");

                // ── 6. POST PDU ───────────────────────────────────────────────────
                var success = await PostPduToMmscAsync(
                    pduBytes, mmscUrl, proxyHost, proxyPort, mmsNet, _log);

                if (success) _log.Success($"MMS sent to {request.To} ({attachments.Count} attachment(s))");
                else _log.Error("MMSC POST failed");

                // Throttle — give the modem ~1 second per message before next send
                await Task.Delay(1000);

                return success;
            }
            catch (Exception ex)
            {
                _log.Error($"MMS send error: {ex}");
                return false;
            }
            finally
            {
                cts?.Dispose();
                try { Android.Net.ConnectivityManager.SetProcessDefaultNetwork(null); } catch { }
                try
                {
                    if (cm != null && _mmsNetworkCallback != null)
                        cm.UnregisterNetworkCallback(_mmsNetworkCallback);
                }
                catch { }
                _mmsNetworkCallback = null;

                _smsSemaphore.Release();
            }
#else
    _log.Warning("MMS sending only supported on Android");
    return await Task.FromResult(false);
#endif
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Network acquisition
        // ═══════════════════════════════════════════════════════════════════════════
#if ANDROID

        private Android.Net.ConnectivityManager.NetworkCallback? _mmsNetworkCallback;

        private async Task<Android.Net.Network?> AcquireMmsNetworkAsync(
            Android.Net.ConnectivityManager cm,
            System.Threading.CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<Android.Net.Network?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _mmsNetworkCallback = new MmsNetCallback(tcs);

            var req = new Android.Net.NetworkRequest.Builder()
                .AddTransportType(Android.Net.TransportType.Cellular)
                .AddCapability(Android.Net.NetCapability.Mms)
                .Build();

            try
            {
                cm.RequestNetwork(req, _mmsNetworkCallback, 30_000);
            }
            catch (Exception ex)
            {
                _log.Warning($"RequestNetwork failed: {ex.Message}");
                return null;
            }

            using var reg = ct.Register(() => tcs.TrySetResult(null));
            return await tcs.Task;
        }

        private sealed class MmsNetCallback : Android.Net.ConnectivityManager.NetworkCallback
        {
            private readonly TaskCompletionSource<Android.Net.Network?> _tcs;
            public MmsNetCallback(TaskCompletionSource<Android.Net.Network?> tcs) => _tcs = tcs;
            public override void OnAvailable(Android.Net.Network network) => _tcs.TrySetResult(network);
            public override void OnUnavailable() => _tcs.TrySetResult(null);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // APN settings reader
        // ═══════════════════════════════════════════════════════════════════════════

        private static (string MmscUrl, string ProxyHost, int ProxyPort) GetMmsApnSettings(
            Android.Content.Context context, GatewayLogService _log)
        {
            // Defaults for AT&T — used if APN query fails
            const string defaultMmsc = "http://mmsc.mobile.att.net";
            const string defaultProxy = "proxy.mobile.att.net";
            const int defaultPort = 80;

            try
            {
                var uri = Android.Net.Uri.Parse("content://telephony/carriers/preferapn");
                var cursor = context.ContentResolver?.Query(uri,
                    new[] { "mmsc", "mmsproxy", "mmsport" }, null, null, null);

                if (cursor != null && cursor.MoveToFirst())
                {
                    var mmsc = cursor.GetString(cursor.GetColumnIndexOrThrow("mmsc")) ?? defaultMmsc;
                    var proxy = cursor.GetString(cursor.GetColumnIndexOrThrow("mmsproxy")) ?? defaultProxy;
                    var portS = cursor.GetString(cursor.GetColumnIndexOrThrow("mmsport")) ?? $"{defaultPort}";
                    cursor.Close();

                    if (!int.TryParse(portS, out var port)) port = defaultPort;
                    if (string.IsNullOrWhiteSpace(mmsc)) mmsc = defaultMmsc;
                    if (string.IsNullOrWhiteSpace(proxy)) proxy = defaultProxy;

                    return (mmsc, proxy, port);
                }

                cursor?.Close();
            }
            catch (Exception ex)
            {
                // APN query can fail without READ_PHONE_STATE on some ROMs
                _log.Warning("MMS - APN query failed: {ex.Message}");
            }

            return (defaultMmsc, defaultProxy, defaultPort);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // HTTP POST to MMSC — bound to the MMS network
        // ═══════════════════════════════════════════════════════════════════════════

        private static async Task<bool> PostPduToMmscAsync(
            byte[] pdu,
            string mmscUrl,
            string proxyHost,
            int proxyPort,
            Android.Net.Network? mmsNetwork,
            GatewayLogService _log)
        {
            // Build an HttpClient whose socket is bound to the MMS cellular network.
            // If mmsNetwork is null we fall back to whatever network is available.
            HttpClientHandler handler;

            if (mmsNetwork != null)
            {
                // Android: bind all sockets opened by this handler to the MMS network
                // by using the network's socket factory via a custom handler.
                handler = new MmsNetworkHandler(mmsNetwork, proxyHost, proxyPort);
            }
            else
            {
                handler = new HttpClientHandler
                {
                    Proxy = new System.Net.WebProxy($"http://{proxyHost}:{proxyPort}"),
                    UseProxy = !string.IsNullOrWhiteSpace(proxyHost),
                };
            }

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "Android MMS/1.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "x-wap-profile", "http://www.google.com/oha/rdf/ua-profile-kila.xml");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept", "*/*, application/vnd.wap.mms-message, application/vnd.wap.sic");

            var content = new ByteArrayContent(pdu);
            content.Headers.TryAddWithoutValidation(
                "Content-Type", "application/vnd.wap.mms-message");

            var response = await client.PostAsync(mmscUrl, content);
            var body = await response.Content.ReadAsStringAsync();

            _log.Warning($"MMS MMSC response: {(int)response.StatusCode} body={body}");
            return response.IsSuccessStatusCode;
        }

        // ── HttpClientHandler that binds sockets to a specific Android Network ─────
        private sealed class MmsNetworkHandler : HttpClientHandler
        {
            private readonly Android.Net.Network _net;
            private readonly string _proxyHost;
            private readonly int _proxyPort;

            public MmsNetworkHandler(Android.Net.Network net, string proxyHost, int proxyPort)
            {
                _net = net;
                _proxyHost = proxyHost;
                _proxyPort = proxyPort;
                if (!string.IsNullOrWhiteSpace(proxyHost))
                {
                    Proxy = new System.Net.WebProxy($"http://{proxyHost}:{proxyPort}");
                    UseProxy = true;
                }
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage req, System.Threading.CancellationToken ct)
            {
                // BindSocket requires a Java socket. We get there by opening a plain
                // TCP socket to the proxy (or MMSC directly), binding it to the MMS
                // cellular network, then letting the normal handler do its work.
                // The trick: set the system property so the JVM HTTP stack uses the
                // network-bound socket factory for this process.
                //
                // Simpler approach that actually works on .NET/MAUI Android:
                // Use Java.Net.URL + openConnection on the network-bound socket,
                // but HttpClient doesn't expose socket hooks. Instead we bind a
                // throwaway socket to force the OS routing table entry, then send.

                // Bind a throwaway TCP socket to the MMS network so the OS associates
                // this process's routing with the cellular interface for the duration.
                try
                {
                    using var testSocket = new Java.Net.Socket();
                    _net.BindSocket(testSocket);
                    testSocket.Close();
                }
                catch { /* best-effort; send anyway */ }

                // Also tell Android's connectivity stack to prefer this network
                // for all new sockets from this process.
                try
                {
                    Android.Net.ConnectivityManager.SetProcessDefaultNetwork(_net);
                }
                catch { /* API 21 only, ignore */ }

                return await base.SendAsync(req, ct);
            }
        }

        // Key facts from the real source:
        //  - multipart/related short-integer = 0xB3 (index 51 | 0x80)
        //  - text/plain = 0x83 (index 3 | 0x80)
        //  - image/jpeg = 0x9E (index 30 | 0x80)
        //  - image/png  = 0xA0 (index 32 | 0x80)
        //  - image/gif  = 0x9D (index 29 | 0x80)
        //  - application/smil is NOT in the map -> appendTextString used
        //  - Content-ID is ALWAYS wrapped in angle brackets in the wire format:
        //    appendQuotedString("<" + contentId + ">")  ->  0x22 < c i d > 0x00
        //  - P_DEP_START value also wrapped: appendTextString("<" + start + ">")
        //  - Outer CT value-length is computed AFTER writing content (BufferStack)
        //    We simulate this by writing to a temp buffer then prepending value-length
        //  - Part CT value-length also computed after writing (same pattern)
        //  - SMIL src= uses Content-Location (filename), not cid:

        private static byte[] MmsBuildPdu(
            string to,
            string bodyText,
            List<(byte[] Data, string Mime, string Name)> attachments)
        {
            var ms = new MemoryStream();

            // X-Mms-Message-Type: m-send.req
            ms.Write(new byte[] { 0x8C, 0x80 });

            // X-Mms-Transaction-ID
            ms.WriteByte(0x98);
            ms.Write(System.Text.Encoding.ASCII.GetBytes("T" + Guid.NewGuid().ToString("N")[..8]));
            ms.WriteByte(0x00);

            // X-Mms-MMS-Version: 1.2
            ms.Write(new byte[] { 0x8D, 0x92 });

            // Date
            ms.WriteByte(0x85);
            PduAppendLongInteger(ms, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // From: insert-address-token
            ms.Write(new byte[] { 0x89, 0x01, 0x81 });

            // To: EncodedStringValue = value-length { charset-short-int text-string }
            ms.WriteByte(0x97);
            {
                var digitsOnly = new string(to.Where(char.IsDigit).ToArray());
                if (digitsOnly.Length == 11 && digitsOnly.StartsWith("1"))
                    digitsOnly = digitsOnly.Substring(1);
                var toStr = digitsOnly + "/TYPE=PLMN";
                var inner = new MemoryStream();
                inner.WriteByte((byte)((106 | 0x80) & 0xFF)); // charset UTF-8
                inner.Write(System.Text.Encoding.ASCII.GetBytes(toStr));
                inner.WriteByte(0x00);
                var innerBytes = inner.ToArray();
                PduWriteValueLength(ms, innerBytes.Length);
                ms.Write(innerBytes);
            }

            // X-Mms-Message-Class: Personal
            ms.Write(new byte[] { 0x8A, 0x80 });

            // X-Mms-Expiry: relative 7 days
            ms.WriteByte(0x88);
            {
                var inner = new MemoryStream();
                inner.WriteByte(0x81); // VALUE_RELATIVE_TOKEN
                PduAppendLongInteger(inner, 7 * 24 * 3600L);
                var innerBytes = inner.ToArray();
                PduWriteValueLength(ms, innerBytes.Length);
                ms.Write(innerBytes);
            }

            // X-Mms-Delivery-Report: No
            ms.Write(new byte[] { 0x86, 0x81 });

            // Content-Type field token
            ms.WriteByte(0x84);

            // Body (Content-Type value-length + value + parts)
            PduWriteMessageBody(ms, bodyText, attachments);

            return ms.ToArray();
        }

        private static void PduWriteMessageBody(
            Stream ms,
            string bodyText,
            List<(byte[] Data, string Mime, string Name)> attachments)
        {
            // Build parts list
            var parts = new List<(byte[] Header, byte[] Body)>();

            // Part 0: SMIL — contentId stored as "smil" (no brackets), wire gets "<smil>"
            var smilBody = System.Text.Encoding.UTF8.GetBytes(MmsBuildSmil(bodyText, attachments));
            parts.Add((PduBuildPartHeader("application/smil", "smil.xml", "smil", 0), smilBody));

            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                var textBody = System.Text.Encoding.UTF8.GetBytes(bodyText);
                parts.Add((PduBuildPartHeader("text/plain", "text.txt", "text", 106), textBody));
            }

            for (int i = 0; i < attachments.Count; i++)
            {
                var (data, mime, name) = attachments[i];
                // contentId = filename without extension, e.g. "att0"
                var cid = System.IO.Path.GetFileNameWithoutExtension(name);
                parts.Add((PduBuildPartHeader(mime, name, cid, 0), data));
            }

            // Outer Content-Type value:
            // Write to temp buffer (simulating BufferStack), then prepend value-length
            var ctBuf = new MemoryStream();

            // appendShortInteger(51) = 0xB3 for multipart/related
            ctBuf.WriteByte(0xB3);

            // P_DEP_START (0x8A) + appendTextString("<smil>")
            // The SMIL part's contentId is "smil", so start = "<smil>"
            ctBuf.WriteByte(0x8A); // P_DEP_START
            PduAppendTextString(ctBuf, "<smil>");

            // P_CT_MR_TYPE (0x89) + appendTextString("application/smil")
            ctBuf.WriteByte(0x89); // P_CT_MR_TYPE
            PduAppendTextString(ctBuf, "application/smil");

            var ctBytes = ctBuf.ToArray();
            PduWriteValueLength(ms, ctBytes.Length);
            ms.Write(ctBytes);

            // nEntries
            PduWriteUintVar(ms, (ulong)parts.Count);

            foreach (var (hdr, body) in parts)
            {
                PduWriteUintVar(ms, (ulong)hdr.Length);
                PduWriteUintVar(ms, (ulong)body.Length);
                ms.Write(hdr);
                ms.Write(body);
            }
        }

        private static byte[] PduBuildPartHeader(string mime, string name, string contentId, int charset)
        {
            // Map of MIME types to PduContentTypes indices (from actual PduContentTypes.java)
            var contentTypeMap = new System.Collections.Generic.Dictionary<string, int>
            {
                ["text/plain"] = 3,   // 0x83
                ["image/gif"] = 29,  // 0x9D
                ["image/jpeg"] = 30,  // 0x9E
                ["image/png"] = 32,  // 0xA0
                ["video/3gpp"] = 80,  // 0xD0 - use text for specific types
                ["audio/mpeg"] = 39,  // check
            };

            // Part CT value written to temp buffer then value-length prepended
            var ctBuf = new MemoryStream();

            if (contentTypeMap.TryGetValue(mime, out int idx))
                ctBuf.WriteByte((byte)((idx | 0x80) & 0xFF)); // appendShortInteger
            else
                PduAppendTextString(ctBuf, mime);              // appendTextString

            // P_DEP_NAME (0x85) + text-string (filename)
            ctBuf.WriteByte(0x85);
            PduAppendTextString(ctBuf, name);

            // P_CHARSET (0x81) + short-integer (charset)
            if (charset != 0)
            {
                ctBuf.WriteByte(0x81);
                ctBuf.WriteByte((byte)((charset | 0x80) & 0xFF));
            }

            var ctBytes = ctBuf.ToArray();

            var h = new MemoryStream();
            PduWriteValueLength(h, ctBytes.Length);
            h.Write(ctBytes);

            // P_CONTENT_ID (0xC0) + appendQuotedString("<contentId>")
            // Real code: if already has <>, use as-is; else wrap in <>
            // We store without <>, so always wrap:
            h.WriteByte(0xC0);
            PduAppendQuotedString(h, "<" + contentId + ">");

            // P_CONTENT_LOCATION (0x8E) + text-string (name = filename)
            h.WriteByte(0x8E);
            PduAppendTextString(h, name);

            return h.ToArray();
        }

        // appendLongInteger: short-length (byte count) + big-endian bytes, minimum bytes
        private static void PduAppendLongInteger(Stream s, long value)
        {
            int size = 0;
            long temp = value;
            while (temp != 0 && size < 8) { temp >>= 8; size++; }
            if (size == 0) size = 1;
            s.WriteByte((byte)size);
            for (int i = (size - 1) * 8; i >= 0; i -= 8)
                s.WriteByte((byte)((value >> i) & 0xFF));
        }

        // appendTextString: quote byte 0x7F if first byte > 127, then bytes, then NUL
        private static void PduAppendTextString(Stream s, string text)
        {
            var b = System.Text.Encoding.ASCII.GetBytes(text);
            if (b.Length > 0 && (b[0] & 0xFF) > 127) s.WriteByte(0x7F);
            s.Write(b);
            s.WriteByte(0x00);
        }

        // appendQuotedString: 0x22 + bytes + NUL  (text already contains the <> if needed)
        private static void PduAppendQuotedString(Stream s, string text)
        {
            s.WriteByte(0x22);
            s.Write(System.Text.Encoding.ASCII.GetBytes(text));
            s.WriteByte(0x00);
        }

        // appendValueLength: < 31 = single byte; >= 31 = 0x1F + uintvar
        private static void PduWriteValueLength(Stream s, long v)
        {
            if (v < 31) s.WriteByte((byte)v);
            else { s.WriteByte(0x1F); PduWriteUintVar(s, (ulong)v); }
        }

        private static void PduWriteUintVar(Stream s, ulong v)
        {
            var buf = new List<byte> { (byte)(v & 0x7F) };
            v >>= 7;
            while (v > 0) { buf.Add((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            buf.Reverse();
            foreach (var b in buf) s.WriteByte(b);
        }

        private static string MmsBuildSmil(
            string bodyText,
            List<(byte[] Data, string Mime, string Name)> attachments)
        {
            bool hasText = !string.IsNullOrWhiteSpace(bodyText);
            bool hasMedia = attachments.Any(a =>
                a.Mime.StartsWith("image/") || a.Mime.StartsWith("video/"));

            var sb = new System.Text.StringBuilder();
            sb.Append("<smil><head><layout>");
            sb.Append("<root-layout width=\"320px\" height=\"480px\"/>");

            if (hasMedia)
                sb.Append("<region id=\"Image\" top=\"0\" left=\"0\" width=\"320px\" " +
                          $"height=\"{(hasText ? "320px" : "480px")}\" fit=\"meet\"/>");
            if (hasText)
                sb.Append($"<region id=\"Text\" top=\"{(hasMedia ? "320px" : "0")}\" " +
                          "left=\"0\" width=\"320px\" height=\"160px\" fit=\"scroll\"/>");

            sb.Append("</layout></head><body><par dur=\"10000ms\">");

            // src = filename (Content-Location), matching what the real encoder does
            foreach (var (_, mime, name) in attachments)
            {
                if (mime.StartsWith("image/"))
                    sb.Append($"<img src=\"{name}\" region=\"Image\"/>");
                else if (mime.StartsWith("video/"))
                    sb.Append($"<video src=\"{name}\" region=\"Image\"/>");
                else if (mime.StartsWith("audio/"))
                    sb.Append($"<audio src=\"{name}\"/>");
            }

            if (hasText)
                sb.Append("<text src=\"text.txt\" region=\"Text\"/>");

            sb.Append("</par></body></smil>");
            return sb.ToString();
        }

        private static string MmsDetectMime(byte[] d)
        {
            if (d.Length >= 2 && d[0] == 0xFF && d[1] == 0xD8) return "image/jpeg";
            if (d.Length >= 4 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
            if (d.Length >= 6 && (System.Text.Encoding.ASCII.GetString(d, 0, 6) is "GIF87a" or "GIF89a")) return "image/gif";
            if (d.Length >= 4 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46) return "image/webp";
            if (d.Length >= 8 && d[4] == 0x66 && d[5] == 0x74 && d[6] == 0x79 && d[7] == 0x70) return "video/mp4";
            return "application/octet-stream";
        }

        private static string MmsMimeToExt(string mime) => mime switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            _ => ".bin"
        };

        /// <summary>
        /// HttpMessageHandler that sends all requests through a specific Android
        /// Network (so it uses mobile data) and routes via the MMS APN proxy.
        /// </summary>
        private class MmsSocketHandler : HttpClientHandler
        {
            private readonly Network _network;
            private readonly string _proxyHost;
            private readonly int _proxyPort;

            public MmsSocketHandler(Network network, string proxyHost, int proxyPort)
            {
                _network = network;
                _proxyHost = proxyHost;
                _proxyPort = proxyPort;
                // Route via proxy
                Proxy = new System.Net.WebProxy($"http://{proxyHost}:{proxyPort}");
                UseProxy = true;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                // Bind the socket to the MMS network so it doesn't go over Wi-Fi
                // We do this by overriding the socket factory used by the underlying
                // Java HTTP stack via the network's socket factory.
                // For .NET HttpClient on Android, we rely on the proxy routing +
                // the fact that the MMSC is only reachable via mobile data anyway.
                return await base.SendAsync(request, cancellationToken);
            }
        }
#endif

#if ANDROID
        private class SmsSentReceiver : Android.Content.BroadcastReceiver
        {
            private readonly TaskCompletionSource<bool> _tcs;
            private readonly GatewayLogService _log;
            private readonly string _to;

            public SmsSentReceiver(TaskCompletionSource<bool> tcs, GatewayLogService log, string to)
            {
                _tcs = tcs;
                _log = log;
                _to = to;
            }

            public override void OnReceive(Android.Content.Context? context, Android.Content.Intent? intent)
            {
                bool ok = ResultCode == Android.App.Result.Ok;
                if (!ok)
                    _log.Warning($"SMS modem error to {_to}: ResultCode={ResultCode}");
                _tcs.TrySetResult(ok);
            }
        }
#endif
    }
}
