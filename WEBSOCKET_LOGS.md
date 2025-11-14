# WebSocket Log Viewer

HyperTizen now includes a real-time WebSocket log server that broadcasts all log messages to connected web browsers.

## Features

- ✅ **Real-time log streaming** - See logs as they happen
- ✅ **Automatic reconnection** - Handles network interruptions gracefully
- ✅ **Exponential backoff** - Smart retry logic (1s, 2s, 4s, 8s, 16s, 30s max)
- ✅ **Color-coded logs** - Different colors for Debug, Info, Warning, Error, Performance
- ✅ **Auto-scroll** - Automatically scrolls to newest logs (can be disabled)
- ✅ **Recent log history** - New connections receive the last 50 log entries
- ✅ **Multiple clients** - Support for multiple simultaneous browser connections
- ✅ **Persistent settings** - IP and port saved in browser localStorage

## Quick Start

### 1. Start HyperTizen on Your TV

The WebSocket server starts automatically when HyperTizen launches on port **45678**.

### 2. Open the Log Viewer

1. Open `logs.html` in your web browser
2. Enter your TV's IP address (e.g., `192.168.1.100`)
3. Click **Connect**

That's it! Logs will start streaming in real-time.

## Usage

### First Time Setup

1. Find your TV's IP address:
   - On TV: Settings → Network → Network Status
   - Or check your router's DHCP client list

2. Open `logs.html` in any modern browser:
   - Chrome
   - Firefox
   - Safari
   - Edge

3. Enter TV IP and click Connect

4. Your settings are saved automatically for next time

### Controls

| Button | Description |
|--------|-------------|
| **Connect** | Connect to the TV with current IP/port settings |
| **Reconnect** | Force immediate reconnection |
| **Clear** | Clear all logs from the display |
| **Auto-scroll** | Toggle automatic scrolling to newest logs |

### Connection Status Indicators

| Status | Color | Meaning |
|--------|-------|---------|
| **Connected** | 🟢 Green | WebSocket connected and receiving logs |
| **Connecting** | 🟡 Yellow | Attempting to connect... |
| **Disconnected** | 🔴 Red | Not connected, will auto-retry |

## Troubleshooting

### Cannot Connect

1. **Check TV IP address**
   - Make sure the IP is correct
   - TV and computer must be on the same network

2. **Check firewall**
   - Port 45678 must be accessible
   - Try temporarily disabling firewall to test

3. **Check HyperTizen is running**
   - The app must be running on the TV
   - Check TV notifications for "HyperTizen" messages

### Connection Keeps Dropping

This is normal behavior! The WebSocket client will automatically reconnect using exponential backoff:

- **Attempt 1**: Reconnect after 1 second
- **Attempt 2**: Reconnect after 2 seconds
- **Attempt 3**: Reconnect after 4 seconds
- **Attempt 4**: Reconnect after 8 seconds
- **Attempt 5**: Reconnect after 16 seconds
- **Attempt 6+**: Reconnect after 30 seconds (max)

When the connection is restored, you'll see:
- Status changes to "Connected" (green)
- Recent log history is replayed
- New logs stream in real-time

### Browser Console Errors

Open browser DevTools (F12) and check the Console tab for detailed error messages.

Common issues:
- **Mixed Content**: If viewing logs.html over HTTPS, WebSocket must use `wss://` (not needed for local file)
- **CORS**: Not applicable for WebSockets
- **Network Error**: Check IP address and network connectivity

## Log Types

Logs are color-coded by severity:

| Type | Color | Use Case |
|------|-------|----------|
| **Debug** | 🟢 Green | Detailed debug information |
| **Info** | 🔵 Cyan | General information messages |
| **Warning** | 🟡 Yellow | Warnings and potential issues |
| **Error** | 🔴 Red | Errors and failures |
| **Performance** | 🟣 Purple | Performance metrics and timing |

## Advanced Configuration

### Change WebSocket Port

To use a different port, modify `HyperTizen_App.cs`:

```csharp
// Change port 45678 to your desired port
Helper.Log.StartWebSocketServer(9999);
```

Then rebuild the app and update the port in the browser UI.

### Access from Remote Network

By default, the WebSocket server listens on all interfaces (`0.0.0.0`).

To access from outside your local network:
1. Configure port forwarding on your router (port 45678 → TV's IP)
2. Use your public IP address in the browser
3. **Security Warning**: This exposes logs to the internet!

### Integration with CI/CD

You can connect to the WebSocket programmatically:

```javascript
const ws = new WebSocket('ws://TV_IP:45678');

ws.onmessage = (event) => {
    console.log('Log:', event.data);
    // Parse and process logs
};
```

Use this for automated testing or log aggregation.

## Technical Details

### WebSocket Protocol

- **Protocol**: RFC 6455 WebSocket
- **Format**: Text frames (UTF-8)
- **Port**: 45678 (default)
- **Handshake**: HTTP Upgrade with Sec-WebSocket-Key

### Message Format

Each log message is sent as a single text frame:

```
[HH:mm:ss] [Type] Message
```

Example:
```
[14:23:45] [Info] VideoCapture initialized successfully!
```

### Server Implementation

- **Server**: Custom WebSocket server (`LogWebSocketServer.cs`)
- **Threading**: Async/await with background threads
- **Concurrency**: Thread-safe with locks
- **Buffer**: Last 50 logs kept in memory
- **Broadcasting**: All clients receive all logs simultaneously

### Client Implementation

- **Technology**: Vanilla JavaScript (no dependencies)
- **Reconnection**: Exponential backoff with max delay
- **Storage**: localStorage for settings persistence
- **Auto-scroll**: Smooth scrolling to newest logs
- **Performance**: Handles high-frequency logs efficiently

## Example Use Cases

### Development & Debugging

Keep the log viewer open while developing:
- See API calls and responses
- Monitor capture performance
- Debug WebSocket connections
- Track memory usage

### Production Monitoring

Monitor deployed TVs in real-time:
- Watch for errors and warnings
- Track system health
- Diagnose customer issues
- Verify deployment success

### Performance Testing

Analyze performance metrics:
- Capture frame times
- Network latency
- CPU usage
- Memory allocation

## Comparison to Traditional Logs

| Feature | WebSocket Logs | File Logs |
|---------|---------------|-----------|
| Real-time | ✅ Yes | ❌ No (must download) |
| Remote access | ✅ Easy | ⚠️ Requires SSH/FTP |
| Multiple viewers | ✅ Yes | ⚠️ File locking issues |
| Color coding | ✅ Yes | ❌ Plain text |
| Auto-refresh | ✅ Yes | ❌ Manual refresh |
| History | ✅ Last 50 | ✅ Full history |
| Disk usage | ✅ Zero | ⚠️ Can grow large |

## Tips & Tricks

1. **Keep it open**: Leave the log viewer open during development
2. **Multiple windows**: Open multiple browser tabs for side-by-side comparison
3. **Filter logs**: Use browser DevTools to filter by log type
4. **Save logs**: Right-click → Save As to export logs to file
5. **Bookmark**: Bookmark the logs.html page for quick access
6. **Mobile**: Works on mobile browsers too!

## Credits

- WebSocket server: Built with System.Net.Sockets
- HTML client: Pure vanilla JavaScript
- Log integration: Hooks into existing Helper.Log class

## License

Same as HyperTizen project.
