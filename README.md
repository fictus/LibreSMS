# LibreSMS

**Turn your Android phone into a self-hosted SMS/MMS gateway.**

LibreSMS runs a local HTTP server on your device. Any application on your network can send and receive SMS/MMS messages through a simple REST API — no third-party services, no SIM-card proxies, no subscriptions.

Built with .NET MAUI, targeting Android.

---

## Features

- **Send SMS** via HTTP GET or POST from any device on your network
- **Send MMS** with images (base64 or URL)
- **Receive SMS/MMS** forwarded in real-time to a webhook URL of your choice
- **Read unread messages** from the device inbox via API
- **Foreground service** keeps the gateway alive with screen off
- **Wake lock** prevents CPU sleep during active sessions
- **Auto-start on boot** option
- **Webhook secret** support for authenticated delivery
- **Built-in test console** to send messages and fire test webhooks from within the app
- **Live dashboard** with message stats, uptime, and endpoint reference
- Dark industrial UI theme

---

## Requirements

- Android device with an active SIM card
- .NET 10 SDK
- Android SDK (API 21+)
- Visual Studio 2026 with MAUI workload installed

---

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/yourname/LibreSMS.git
cd LibreSMS
```

### 2. Open in Visual Studio

Open `LibreSMS.sln`. Make sure the MAUI workload is installed (`Tools → Get Tools and Features → Mobile development with .NET`).

### 3. Build and deploy to your Android device

Connect your Android device via USB with developer mode and USB debugging enabled, then run:

```bash
dotnet build LibreSMS/LibreSMS.csproj -f net10.0-android
dotnet run --project LibreSMS/LibreSMS.csproj -f net10.0-android
```

Or press **Run** in Visual Studio with your device selected as the target.

### 4. Set LibreSMS as the default SMS app

The app will display a warning banner if it is not the default SMS app. Tap **SET AS DEFAULT** on the Dashboard or Controls tab. This is required to receive incoming SMS and MMS — without it, only outbound sending works.

### 5. Configure and start

1. Go to the **Controls** tab
2. Set your **Webhook URL** (the server that will receive incoming messages)
3. Set the **Listen Port** (default: `8686`)
4. Toggle **Forward SMS** and/or **Forward MMS** as needed
5. Optionally enable **Auto-Start on Boot**
6. Tap **SAVE CONFIGURATION**
7. Return to the **Dashboard** tab and tap **START GATEWAY**

The gateway is running when the status dot turns green and the LAN URL appears in the endpoint list.

---

## HTTP API

The gateway listens on all network interfaces. Replace `<device-ip>` with your phone's local IP address (shown on the Dashboard).

### Send SMS

```
GET http://<device-ip>:8686/sendsms?to=+15551234567&message=Hello
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `to` | Yes | Destination phone number (E.164 format recommended) |
| `message` | Yes | Text content of the SMS |

Also accepts `number` or `phone` as aliases for `to`, and `text` or `body` as aliases for `message`.

**Example response:**
```json
{
  "success": true,
  "message": "SMS sent to +15551234567"
}
```

---

### Send MMS

```
GET http://<device-ip>:8686/sendmms?to=+15551234567&message=Look+at+this&image_url=https://example.com/photo.jpg
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `to` | Yes | Destination phone number |
| `message` | No | Optional text body |
| `image` | No | Single image as base64 string |
| `image_url` | No | Single image URL (downloaded by the device) |
| `image_0` … `image_9` | No | Multiple images as base64 |
| `image_url_0` … `image_url_9` | No | Multiple image URLs |

---

### Get unread messages

Returns all unread SMS messages from the device inbox.

```
GET http://<device-ip>:8686/getmessages
```

**Example response:**
```json
{
  "success": true,
  "message": "Retrieved 2 unread message(s).",
  "data": {
    "isSuccessful": true,
    "description": "Retrieved 2 unread message(s).",
    "requestMethod": "GET",
    "messages": [
      {
        "from": "+15559876543",
        "body": "Hey, are you there?",
        "timestamp": "2026-05-24T10:32:00"
      }
    ]
  }
}
```

---

### Gateway status

```
GET http://<device-ip>:8686/status
```

Returns current running state, message counts, and uptime.

---

### Health check

```
GET http://<device-ip>:8686/health
```

Returns `{ "success": true, "message": "OK" }`. Useful for monitoring.

---

### POST support

All endpoints also accept POST requests with parameters as:
- URL-encoded form body (`Content-Type: application/x-www-form-urlencoded`)
- JSON body (`Content-Type: application/json`)

---

## Webhook

When an SMS or MMS arrives, LibreSMS fires a GET request to your configured webhook URL with the following query parameters:

| Parameter | Description |
|-----------|-------------|
| `id` | Unique message ID |
| `from` | Sender phone number |
| `to` | Recipient (your device number) |
| `message` | Message body text |
| `type` | `SMS` or `MMS` |
| `timestamp` | ISO 8601 timestamp |
| `attachment_count` | Number of attachments (MMS) |
| `attachment_0_name` | Filename of first attachment |
| `attachment_0_type` | MIME type of first attachment |
| `attachment_0_data` | Base64-encoded attachment data |
| `attachment_0_size` | Size in bytes |

Additional attachments follow the same pattern (`attachment_1_*`, `attachment_2_*`, etc.).

**Example webhook call:**
```
GET https://your-server.com/webhook
  ?id=abc123
  &from=%2B15559876543
  &to=self
  &message=Hello+there
  &type=SMS
  &timestamp=2026-05-24T10%3A32%3A00.000Z
```

If a **Webhook Secret** is configured in Controls, it is appended as `?secret=<your-secret>` — validate this on your server to reject unauthorized deliveries.

---

## App Tabs

### Dashboard
Live view of the gateway state. Shows the start/stop button, received/sent message counters, webhook delivery stats, all HTTP endpoint URLs, and session uptime.

### Controls
Configuration panel. Set the webhook URL, webhook secret, HTTP listen port, SMS/MMS forwarding toggles, and auto-start behavior. The default SMS app status is shown here with a one-tap shortcut to change it.

### Logs
Scrolling activity log with color-coded entries (info, success, warning, error) for every gateway event — incoming messages, webhook deliveries, HTTP requests, and errors.

### Test
In-app test console. Send a real SMS or MMS directly from the device without an external API call. Also includes a **Fire Test Webhook** button that sends a simulated incoming message to your configured webhook URL.

---

## Configuration Reference

| Setting | Default | Description |
|---------|---------|-------------|
| Webhook URL | _(empty)_ | URL to POST incoming messages to |
| Webhook Secret | _(empty)_ | Optional token appended to webhook calls |
| Listen Port | `8686` | Port the HTTP server binds to (1024–65535) |
| Forward SMS | `true` | Forward incoming SMS to the webhook |
| Forward MMS | `true` | Forward incoming MMS to the webhook |
| Auto-Start on Boot | `false` | Automatically start the gateway when the device boots |

Configuration is persisted to local storage and loaded on startup.

---

## Project Structure

```
LibreSMS/
├── App.xaml / App.xaml.cs          # Application entry point and global styles
├── AppShell.xaml                   # Tab bar navigation shell
├── MauiProgram.cs                  # MAUI builder and font registration
├── Models/                         # Data models (config, messages, requests)
├── Pages/
│   ├── DashboardPage               # Live status and endpoint info
│   ├── ControlsPage                # Configuration UI
│   ├── LogsPage                    # Activity log viewer
│   └── TestPage                    # Manual send and webhook test console
├── Services/
│   ├── GatewayService              # Singleton orchestrator
│   ├── HttpGatewayServer           # HTTP listener and route handler
│   ├── SmsSenderService            # Android SMS/MMS dispatch
│   ├── WebhookService              # Outbound webhook delivery
│   ├── ConfigService               # Config persistence
│   └── GatewayLogService           # In-memory log store
└── Platforms/
    └── Android/
        ├── AndroidManifest.xml
        ├── SmsReceiver             # BroadcastReceiver for incoming SMS
        ├── MmsReceiver             # BroadcastReceiver for incoming MMS
        ├── SmsInboxReader          # Reads unread messages from content provider
        ├── GatewayForegroundService # Keeps process alive in background
        └── RespondService          # Handles reply-via-message intents
```

---

## Permissions

LibreSMS requests the following Android permissions:

| Permission | Purpose |
|------------|---------|
| `SEND_SMS` | Send outbound SMS |
| `RECEIVE_SMS` | Receive incoming SMS |
| `READ_SMS` | Read message inbox |
| `WRITE_SMS` | Required by some OEMs for default SMS app behavior |
| `RECEIVE_MMS` | Receive incoming MMS |
| `INTERNET` | HTTP server and webhook delivery |
| `FOREGROUND_SERVICE` | Keep gateway alive with screen off |
| `RECEIVE_BOOT_COMPLETED` | Auto-start on device boot |
| `WAKE_LOCK` | Prevent CPU sleep during active sessions |
| `READ_PHONE_STATE` | Detect SIM and device number |

---

## License

MIT
