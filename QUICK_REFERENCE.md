# Service Lifecycle Quick Reference

## WebSocket Events (Port 45677)

### Control Events

| Event | Value | Description | Parameters | Response |
|-------|-------|-------------|------------|----------|
| PauseCapture | 8 | Pause screen capture | None | StatusUpdate with "paused" |
| ResumeCapture | 9 | Resume screen capture | None | StatusUpdate with "capturing" |
| GetStatus | 10 | Get detailed service status | None | StatusResult event |
| SetConfig (enabled=false) | 0 | Stop capture | key: "enabled", value: "false" | StatusUpdate with "stopped" |

### Example Requests

```javascript
// Pause
{ "Event": 8 }

// Resume
{ "Event": 9 }

// Get Status
{ "Event": 10 }

// Stop (via SetConfig)
{ "Event": 0, "key": "enabled", "value": "false" }
```

### Example Response (StatusResult)

```json
{
  "Event": 11,
  "state": "Capturing",
  "framesCaptured": 15234,
  "averageFPS": 28.5,
  "errorCount": 2,
  "isConnected": true,
  "lastError": "None",
  "uptime": "01:23:45"
}
```

## Service States

| State | Description | UI Indicator |
|-------|-------------|--------------|
| Idle | Not capturing | No rainbow border |
| Starting | Initializing | No rainbow border |
| Capturing | Active capture | Rainbow border |
| Paused | Temporarily suspended | No rainbow border |
| Stopping | Shutting down | No rainbow border |
| Error | Failed state | Check error count |

## UI Controls

### Buttons

1. **▶ Start Capture** (Tab 1)
   - Starts screen capture
   - Enables connection to Hyperion/HyperHDR

2. **⏹ Stop Capture** (Tab 2)
   - Stops capture gracefully (5s timeout)
   - Closes network connections

3. **⏸ Pause** (Tab 3)
   - Pauses frame capture
   - Keeps connections alive

4. **▶️ Resume** (Tab 4)
   - Resumes from pause
   - Uses existing connections

5. **🔄 Restart Service** (Tab 5)
   - Kills and restarts the Tizen service

### Status Display

- **State**: Current service state (Idle, Capturing, Paused, etc.)
- **Connection**: Connected/Disconnected to Hyperion server
- **Frames**: Total frames captured since start
- **Avg FPS**: Rolling average of last 100 frames
- **Uptime**: Time since service started (HH:MM:SS)
- **Errors**: Total errors encountered

## Code Examples

### C# Backend

```csharp
// Stop capture
await App.client.Stop();

// Pause capture
App.client.Pause();

// Resume capture
App.client.Resume();

// Get status
var status = App.client.GetStatus();
Console.WriteLine($"State: {status.State}");
Console.WriteLine($"FPS: {status.AverageFPS}");
Console.WriteLine($"Frames: {status.FramesCaptured}");
```

### JavaScript Frontend

```javascript
// Connect to WebSocket
const ws = new WebSocket('ws://192.168.1.100:45677');

// Pause
ws.send(JSON.stringify({ Event: 8 }));

// Resume
ws.send(JSON.stringify({ Event: 9 }));

// Get Status
ws.send(JSON.stringify({ Event: 10 }));

// Handle status response
ws.onmessage = (event) => {
    const data = JSON.parse(event.data);
    if (data.Event === 11) {  // StatusResult
        console.log('State:', data.state);
        console.log('FPS:', data.averageFPS);
        console.log('Frames:', data.framesCaptured);
    }
};
```

## Common Scenarios

### Temporarily Disable Capture

**Before**: Stop capture, wait, start again (loses connection)
```javascript
ws.send(JSON.stringify({ Event: 0, key: 'enabled', value: 'false' }));
// ... wait ...
ws.send(JSON.stringify({ Event: 0, key: 'enabled', value: 'true' }));
```

**After**: Use pause/resume (keeps connection)
```javascript
ws.send(JSON.stringify({ Event: 8 }));  // Pause
// ... wait ...
ws.send(JSON.stringify({ Event: 9 }));  // Resume
```

### Monitor Performance

```javascript
// Request status every 5 seconds
setInterval(() => {
    ws.send(JSON.stringify({ Event: 10 }));
}, 5000);

// Handle response
ws.onmessage = (event) => {
    const data = JSON.parse(event.data);
    if (data.Event === 11) {
        if (data.averageFPS < 20) {
            console.warn('Low FPS detected!');
        }
        if (data.errorCount > 10) {
            console.error('High error count!');
        }
    }
};
```

### Graceful Shutdown

```javascript
// Old way (immediate, may leave connections)
ws.send(JSON.stringify({ Event: 0, key: 'enabled', value: 'false' }));

// New way (graceful, cleans up properly)
// Same message, but now calls Stop() internally which:
// - Signals cancellation
// - Waits up to 5 seconds
// - Closes connections
// - Logs shutdown process
ws.send(JSON.stringify({ Event: 0, key: 'enabled', value: 'false' }));
```

## Troubleshooting

### Service Won't Stop

**Check**: Logs for "Force stopped after timeout"
**Action**: Reduce network timeout or check for blocking operations

### Pause Not Working

**Check**: WebSocket connection status
**Action**: Verify control WebSocket is connected to port 45677

### High Error Count

**Check**: Last error message in status
**Action**: Verify Hyperion server is running and accessible

### FPS Dropping

**Check**: Average FPS in status
**Action**: Check TV CPU usage, network latency, Hyperion server load

## File Locations

- **Backend**: `/home/user/HyperTizen/HyperTizen/HyperionClient.cs`
- **WebSocket Handler**: `/home/user/HyperTizen/HyperTizen/WebSocket/WebSocket.cs`
- **Data Types**: `/home/user/HyperTizen/HyperTizen/WebSocket/DataTypes.cs`
- **UI**: `/home/user/HyperTizen/HyperTizenUI/index.html`
- **JavaScript**: `/home/user/HyperTizen/HyperTizenUI/js/wsClient.js`

## Log Locations

- **WebSocket Logs**: Port 45678 (ws://TV_IP:45678)
- **File Logs**: Check Helper.Log.GetWorkingLogPath() via UI

## Key Improvements

1. ✅ Empty Stop() method → Fully implemented with 5s graceful shutdown
2. ✅ CancellationToken.None everywhere → Proper cancellation tokens
3. ✅ No way to exit loop → Clean cancellation support
4. ✅ No pause/resume → Full pause/resume implementation
5. ✅ No status query → Detailed status with metrics
6. ✅ Hard shutdown → Graceful with timeout and cleanup
