# HyperionClient 10-Step Startup Flow Diagram

## Visual Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ STEP 1: Service Startup                                         │
│ - Initialize lifecycle fields (_isRunning, _framesCaptured)    │
│ - Create CancellationTokenSource                               │
│ - Set State = ServiceState.Starting                            │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 2 & 3: WebSocket + SSDP Scans                             │
│ - Globals.Instance.SetConfig()                                 │
│   ├── Start WebSocket logging server (port 45678)              │
│   └── Perform SSDP discovery for Hyperion server               │
│ - Validate ServerIp and ServerPort (unless DIAGNOSTIC_MODE)    │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 5: Test Each Capture Method                               │
│ - Create CaptureMethodSelector                                 │
│ - SelectBestMethod() - Tests in priority order:                │
│   ├── T8SDK (Priority 3) - Tizen 8+ optimized                 │
│   ├── T7SDK (Priority 2) - Tizen 7 compatible                 │
│   └── PixelSampling (Priority 1) - Universal fallback         │
│ - Returns first working method or null                         │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 6: Cleanup Tests (Automatic)                              │
│ - CaptureMethodSelector.CleanupFailedMethods()                 │
│ - Disposes all non-selected capture methods                    │
│ - Releases resources from failed tests                         │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 7: Initialize Best Method (Complete)                      │
│ - _selectedCaptureMethod now holds working method              │
│ - Method is initialized and ready for capture                  │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 4: Diagnostic Mode (if DIAGNOSTIC_MODE = true)            │
│ - Log capture method selection results                         │
│ - Run LibraryScanner for alternatives                          │
│ - Show notification                                            │
│ - Pause for 10 minutes with WebSocket diagnostics              │
│ - EXIT (does not proceed to capture loop)                      │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       │ (DIAGNOSTIC_MODE = false, continue)
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 8: Start Capture Loop                                     │
│ - Set State = ServiceState.Capturing                           │
│ - Initialize error tracking (consecutiveErrors = 0)            │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 10: Continue Until Stopped                                │
│ ┌─────────────────────────────────────────────────────────────┐│
│ │ MAIN CAPTURE LOOP                                           ││
│ │                                                             ││
│ │ ┌─── Handle Pause State                                    ││
│ │ │    - Check _isPaused flag                                ││
│ │ │    - Delay 100ms if paused                               ││
│ │ └──────────────────────────────────────────┐               ││
│ │                                             │               ││
│ │ ┌─── Check Connection ◄─────────────────────┘              ││
│ │ │    clientSnapshot = Networking.client                    ││
│ │ │    isConnected = client?.Client?.Connected               ││
│ │ └──────────┬───────────────────────────────┐               ││
│ │            │                                │               ││
│ │         [Connected]                   [Not Connected]      ││
│ │            │                                │               ││
│ │            ▼                                ▼               ││
│ │ ┌──────────────────────┐      ┌─────────────────────────┐  ││
│ │ │ Validate Method      │      │ Register with Server    │  ││
│ │ │ != null              │      │ Networking.SendRegister()│ ││
│ │ └────┬─────────────────┘      │ Delay 2s if failed      │  ││
│ │      │                        └─────────────────────────┘  ││
│ │      ▼                                                     ││
│ │ ┌──────────────────────────────────────────────────┐      ││
│ │ │ CAPTURE FRAME                                    │      ││
│ │ │ captureResult = _selectedCaptureMethod.Capture(  │      ││
│ │ │     Globals.Instance.Width,                      │      ││
│ │ │     Globals.Instance.Height)                     │      ││
│ │ └────┬─────────────────────────────────────────────┘      ││
│ │      │                                                     ││
│ │      ▼                                                     ││
│ │ ┌──────────────────────────────────────────────────┐      ││
│ │ │ Check Result                                     │      ││
│ │ └─┬──────────────────────────────────────────────┬─┘      ││
│ │   │                                              │         ││
│ │ [Success]                                   [Failed]       ││
│ │   │                                              │         ││
│ │   ▼                                              ▼         ││
│ │ ┌─────────────────────────────────┐  ┌──────────────────┐ ││
│ │ │ STEP 9: Send Frame              │  │ Log Error        │ ││
│ │ │ Networking.SendImageAsync(      │  │ Increment errors │ ││
│ │ │   captureResult.YData,          │  │ Delay 500ms      │ ││
│ │ │   captureResult.UVData,         │  └──────────────────┘ ││
│ │ │   captureResult.Width,          │                       ││
│ │ │   captureResult.Height)         │                       ││
│ │ │ - Update stats (_framesCaptured)│                       ││
│ │ │ - Track FPS (_fpsHistory)       │                       ││
│ │ │ - Reset error counter           │                       ││
│ │ └─────────────────────────────────┘                       ││
│ │                                                            ││
│ │ ┌─── Error Handling                                       ││
│ │ │    - Catch OperationCanceledException → Break loop     ││
│ │ │    - Catch other exceptions → Increment consecutiveErr ││
│ │ │    - If consecutiveErrors >= 10 → STOP                ││
│ │ └────────────────────────────────────────────────────────││
│ │                                                            ││
│ │ ┌─── Loop Condition                                       ││
│ │ │    while (!cancelled && Globals.Instance.Enabled)      ││
│ │ └────────────────────────────────────────────────────────││
│ └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
                       │
                       │ (Cancelled or Disabled)
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│ SHUTDOWN SEQUENCE (Stop() method)                              │
│ 1. Signal cancellation token                                   │
│ 2. Wait for graceful shutdown (max 5s)                         │
│ 3. Close network connection (Networking.DisconnectClient)      │
│ 4. Cleanup capture method (_selectedCaptureMethod.Cleanup())   │
│ 5. Reset capture selector (_captureSelector.Reset())           │
│ 6. Set State = ServiceState.Idle                               │
└─────────────────────────────────────────────────────────────────┘
```

## Capture Method Selection (STEP 5 Detail)

```
CaptureMethodSelector.SelectBestMethod()
│
├── Initialize methods list:
│   ├── T8SdkCaptureMethod (Priority 3)
│   ├── T7SdkCaptureMethod (Priority 2)
│   └── PixelSamplingCaptureMethod (Priority 1)
│
├── Sort by priority (highest to lowest)
│
└── For each method (in order):
    │
    ├─► IsAvailable() check
    │   ├── [false] → Mark as failed, try next
    │   └── [true] → Continue testing
    │
    ├─► Test() - Real capture test
    │   ├── [false] → Mark as failed, try next
    │   └── [true] → SELECT THIS METHOD ✓
    │
    ├─► Mark remaining methods as failed
    │
    └─► CleanupFailedMethods()
        - Dispose all non-selected methods
        - Free resources

Result: Returns selected method or null if all fail
```

## Key Decision Points

### 1. Diagnostic Mode Check
```
if (DIAGNOSTIC_MODE)
├── [true]  → Run diagnostics, pause 10 min, EXIT
└── [false] → Continue to capture loop
```

### 2. Capture Method Validation
```
if (_selectedCaptureMethod == null)
├── [true]  → Log error, delay 2s, skip frame
└── [false] → Proceed with capture
```

### 3. Connection Check
```
if (isConnected)
├── [true]  → Capture and send frame
└── [false] → Register with server
```

### 4. Capture Result Check
```
if (captureResult?.Success)
├── [true]  → Send frame, update stats
└── [false] → Log error, increment error count
```

### 5. Error Recovery
```
if (consecutiveErrors >= maxConsecutiveErrors)
├── [true]  → STOP capture loop (fatal)
└── [false] → Delay and retry
```

## Data Flow

```
Capture Method (ICaptureMethod)
         │
         │ Capture(width, height)
         ▼
    CaptureResult
    ├── Success: true/false
    ├── YData: byte[]
    ├── UVData: byte[]
    ├── Width: int
    ├── Height: int
    └── ErrorMessage: string
         │
         │ (if Success)
         ▼
Networking.SendImageAsync(YData, UVData, Width, Height)
         │
         │ FlatBuffers protocol
         ▼
    Hyperion Server
    (ServerIp:ServerPort)
```

## State Transitions

```
ServiceState Flow:

Idle ──► Starting ──► Capturing ──► Idle
         │            │       │
         │            ▼       │
         │          Paused ◄──┘
         │            │
         ▼            ▼
       Error ────► Stopping ──► Idle
```

## Thread Safety

- **_selectedCaptureMethod**: Set once during startup, read-only in loop
- **_captureSelector**: Created during startup, reset during shutdown
- **_serviceState**: Protected by _stateLock
- **_isPaused**: Protected by _pauseLock
- **Networking.client**: Snapshot captured before use to prevent race conditions
