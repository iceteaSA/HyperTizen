# HyperionClient.cs Refactoring - Edit Commands Reference

This document lists all the specific edits made to `/home/user/HyperTizen/HyperTizen/HyperionClient.cs` for the capture architecture refactoring.

## Edit 1: Add Using Statement

**Location**: After line 20
**Purpose**: Import the new capture architecture namespace

```csharp
using HyperTizen.Capture;
```

---

## Edit 2: Add Capture Architecture Fields

**Location**: After line 56 (after _stateLock field)
**Purpose**: Add fields for the new capture architecture

```csharp
// Capture architecture fields
private ICaptureMethod _selectedCaptureMethod;
private CaptureMethodSelector _captureSelector;
```

---

## Edit 3: Add Step 1 Label and Step 2-3 Labels

**Location**: Start() method, lines 93-125
**Purpose**: Document the 10-step flow

**Old Code:**
```csharp
public async Task Start()
{
    try
    {
        // Prevent multiple simultaneous starts
        if (_isRunning)
        {
            Helper.Log.Write(Helper.eLogType.Warning, "HyperionClient already running");
            return;
        }

        State = ServiceState.Starting;
        _isRunning = true;
        _startTime = DateTime.Now;
        _framesCaptured = 0;
        _errorCount = 0;
        _fpsHistory.Clear();

        // Create new cancellation token source
        _cancellationTokenSource = new CancellationTokenSource();

        Helper.Log.Write(Helper.eLogType.Info, "HyperionClient starting...");

        // DIAGNOSTIC MODE: Set to true to pause for 10 minutes after initialization
        // Changed to false for normal operation - set to true only for debugging
        const bool DIAGNOSTIC_MODE = false;

        Globals.Instance.SetConfig();
```

**New Code:**
```csharp
public async Task Start()
{
    try
    {
        // Prevent multiple simultaneous starts
        if (_isRunning)
        {
            Helper.Log.Write(Helper.eLogType.Warning, "HyperionClient already running");
            return;
        }

        // STEP 1: Service startup
        Helper.Log.Write(Helper.eLogType.Info, "=== STEP 1: Service startup ===");
        State = ServiceState.Starting;
        _isRunning = true;
        _startTime = DateTime.Now;
        _framesCaptured = 0;
        _errorCount = 0;
        _fpsHistory.Clear();

        // Create new cancellation token source
        _cancellationTokenSource = new CancellationTokenSource();

        Helper.Log.Write(Helper.eLogType.Info, "HyperionClient starting...");

        // DIAGNOSTIC MODE: Set to true to pause for 10 minutes after initialization
        // Changed to false for normal operation - set to true only for debugging
        const bool DIAGNOSTIC_MODE = false;

        // STEP 2: Logging/control WebSocket startup
        // STEP 3: SSDP scans
        Helper.Log.Write(Helper.eLogType.Info, "=== STEP 2 & 3: WebSocket + SSDP scans ===");
        Globals.Instance.SetConfig();
```

---

## Edit 4: Replace VideoCapture.InitCapture() with CaptureMethodSelector

**Location**: Lines 138-204 (after config validation)
**Purpose**: Replace old initialization with new capture method selection

**Old Code:**
```csharp
Helper.Log.Write(Helper.eLogType.Info, "Initializing VideoCapture...");

try
{
    VideoCapture.InitCapture();
    Helper.Log.Write(Helper.eLogType.Info, "VideoCapture initialized successfully!");
}
catch (DllNotFoundException dllEx)
{
    Helper.Log.Write(Helper.eLogType.Error,
        $"DLL NOT FOUND: {dllEx.Message} - Check Tizen version compatibility");

    if (DIAGNOSTIC_MODE)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            "DIAGNOSTIC MODE: Continuing despite error...");
    }
    else
    {
        return;
    }
}
catch (NullReferenceException nullEx)
{
    Helper.Log.Write(Helper.eLogType.Error,
        $"NULL POINTER: SecVideoCapture SDK initialization failed - {nullEx.Message}");

    if (DIAGNOSTIC_MODE)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            "DIAGNOSTIC MODE: Continuing despite error...");
    }
    else
    {
        return;
    }
}
catch (OutOfMemoryException memEx)
{
    Helper.Log.Write(Helper.eLogType.Error,
        $"OUT OF MEMORY: Cannot allocate capture buffers - {memEx.Message}");

    if (DIAGNOSTIC_MODE)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            "DIAGNOSTIC MODE: Continuing despite error...");
    }
    else
    {
        return;
    }
}
catch (Exception ex)
{
    Helper.Log.Write(Helper.eLogType.Error,
        $"INIT FAILED: {ex.GetType().Name}: {ex.Message}");

    if (DIAGNOSTIC_MODE)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            "DIAGNOSTIC MODE: Continuing despite error...");
    }
    else
    {
        return;
    }
}
```

**New Code:**
```csharp
// STEP 5: Test each capture method to find best one
Helper.Log.Write(Helper.eLogType.Info, "=== STEP 5: Testing capture methods ===");

try
{
    _captureSelector = new CaptureMethodSelector();
    _selectedCaptureMethod = _captureSelector.SelectBestMethod();

    if (_selectedCaptureMethod == null)
    {
        Helper.Log.Write(Helper.eLogType.Error,
            "CAPTURE METHOD SELECTION FAILED: No working capture methods found!");

        if (!DIAGNOSTIC_MODE)
        {
            Helper.Log.Write(Helper.eLogType.Error,
                "STARTUP ABORTED: Cannot proceed without a working capture method");
            return;
        }
        else
        {
            Helper.Log.Write(Helper.eLogType.Warning,
                "DIAGNOSTIC MODE: Continuing despite no capture method...");
        }
    }
    else
    {
        Helper.Log.Write(Helper.eLogType.Info,
            $"CAPTURE METHOD SELECTED: {_selectedCaptureMethod.Name}");
    }
}
catch (Exception ex)
{
    Helper.Log.Write(Helper.eLogType.Error,
        $"CAPTURE METHOD SELECTION ERROR: {ex.GetType().Name}: {ex.Message}");

    if (!DIAGNOSTIC_MODE)
    {
        Helper.Log.Write(Helper.eLogType.Error,
            "STARTUP ABORTED: Exception during capture method selection");
        return;
    }
    else
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            "DIAGNOSTIC MODE: Continuing despite error...");
    }
}

Helper.Log.Write(Helper.eLogType.Info, "=== STEP 6: Cleanup tests (automatic) ===");
Helper.Log.Write(Helper.eLogType.Info, "=== STEP 7: Initialize best method (complete) ===");
```

---

## Edit 5: Update Diagnostic Mode Section

**Location**: Lines 206-254 (DIAGNOSTIC_MODE block)
**Purpose**: Remove old VideoCapture test, add capture method selection diagnostics

**Old Code:**
```csharp
// DIAGNOSTIC MODE: Pause for 10 minutes to allow log inspection
if (DIAGNOSTIC_MODE)
{
    Helper.Log.Write(Helper.eLogType.Warning,
        "=== DIAGNOSTIC MODE ===");
    Helper.Log.Write(Helper.eLogType.Warning,
        "Initialization complete. Pausing for 10 minutes...");
    Helper.Log.Write(Helper.eLogType.Warning,
        "Connect to WebSocket at http://<TV_IP>:45678 to view ALL logs");
    Helper.Log.Write(Helper.eLogType.Warning,
        "App will stay alive for 10 minutes, then exit gracefully");
    Helper.Log.Write(Helper.eLogType.Warning,
        "======================");

    // TEST SCREEN CAPTURE (if Tizen 8+)
    if (SDK.SystemInfo.TizenVersionMajor >= 8)
    {
        Helper.Log.Write(Helper.eLogType.Info, "");
        Helper.Log.Write(Helper.eLogType.Info, "Running screen capture test...");
        try
        {
            bool testPassed = SDK.SecVideoCaptureT8.TestCapture();
            if (testPassed)
            {
                Helper.Log.Write(Helper.eLogType.Info, "🎉 SCREEN CAPTURE TEST PASSED!");
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Warning, "⚠️ Screen capture test did not pass - check logs above");
            }
        }
        catch (Exception testEx)
        {
            Helper.Log.Write(Helper.eLogType.Error, $"Screen capture test exception: {testEx.Message}");
        }
        Helper.Log.Write(Helper.eLogType.Info, "");

        // SCAN FOR ALTERNATIVES - Look for workarounds and alternative libraries
        Helper.Log.Write(Helper.eLogType.Info, "Scanning for alternative capture methods...");
        try
        {
            SDK.LibraryScanner.ScanForAlternatives();
        }
        catch (Exception scanEx)
        {
            Helper.Log.Write(Helper.eLogType.Error, $"Library scan exception: {scanEx.Message}");
        }
    }
```

**New Code:**
```csharp
// STEP 4: DIAGNOSTIC MODE - Gather diagnostic info
if (DIAGNOSTIC_MODE)
{
    Helper.Log.Write(Helper.eLogType.Warning,
        "=== DIAGNOSTIC MODE ===");
    Helper.Log.Write(Helper.eLogType.Warning,
        "Initialization complete. Pausing for 10 minutes...");
    Helper.Log.Write(Helper.eLogType.Warning,
        "Connect to WebSocket at http://<TV_IP>:45678 to view ALL logs");
    Helper.Log.Write(Helper.eLogType.Warning,
        "App will stay alive for 10 minutes, then exit gracefully");
    Helper.Log.Write(Helper.eLogType.Warning,
        "======================");

    // Log capture method selection results
    Helper.Log.Write(Helper.eLogType.Info, "");
    Helper.Log.Write(Helper.eLogType.Info, "DIAGNOSTIC: Capture Method Selection Results:");
    if (_selectedCaptureMethod != null)
    {
        Helper.Log.Write(Helper.eLogType.Info,
            $"Selected Method: {_selectedCaptureMethod.Name} (Type: {_selectedCaptureMethod.Type})");
    }
    else
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            "No capture method was selected - all methods failed");
    }
    Helper.Log.Write(Helper.eLogType.Info, "");

    // SCAN FOR ALTERNATIVES - Look for workarounds and alternative libraries
    if (SDK.SystemInfo.TizenVersionMajor >= 8)
    {
        Helper.Log.Write(Helper.eLogType.Info, "Scanning for alternative capture methods...");
        try
        {
            SDK.LibraryScanner.ScanForAlternatives();
        }
        catch (Exception scanEx)
        {
            Helper.Log.Write(Helper.eLogType.Error, $"Library scan exception: {scanEx.Message}");
        }
    }
```

---

## Edit 6: Add Step 8 Label

**Location**: Before main capture loop start
**Purpose**: Document step 8 of the flow

**Old Code:**
```csharp
Helper.Log.Write(Helper.eLogType.Info, "Starting main capture loop...");
State = ServiceState.Capturing;
```

**New Code:**
```csharp
// STEP 8: Start capture loop
Helper.Log.Write(Helper.eLogType.Info, "=== STEP 8: Starting main capture loop ===");
State = ServiceState.Capturing;
```

---

## Edit 7: Add Step 10 Label

**Location**: Before while loop
**Purpose**: Document step 10 of the flow

**Old Code:**
```csharp
int consecutiveErrors = 0;
const int maxConsecutiveErrors = 10;

while (!_cancellationTokenSource.Token.IsCancellationRequested && Globals.Instance.Enabled)
```

**New Code:**
```csharp
int consecutiveErrors = 0;
const int maxConsecutiveErrors = 10;

// STEP 10: Continue until stopped
while (!_cancellationTokenSource.Token.IsCancellationRequested && Globals.Instance.Enabled)
```

---

## Edit 8: Replace VideoCapture.DoCapture() with ICaptureMethod.Capture()

**Location**: Inside the main capture loop (connected block)
**Purpose**: Use new capture architecture instead of old VideoCapture static methods

**Old Code:**
```csharp
if(isConnected)
{
    var watchFPS = System.Diagnostics.Stopwatch.StartNew();
    await Task.Run(() =>VideoCapture.DoCapture()); //VideoCapture.DoDummyCapture();
    watchFPS.Stop();
    var elapsedFPS = 1 / watchFPS.Elapsed.TotalSeconds;
    Helper.Log.Write(Helper.eLogType.Performance, "Capture FPS: " + elapsedFPS.ToString("F1"));

    // Update statistics
    _framesCaptured++;
    _fpsHistory.Add(elapsedFPS);
    if (_fpsHistory.Count > 100) // Keep last 100 samples
    {
        _fpsHistory.RemoveAt(0);
    }

    // Reset error counter on success
    consecutiveErrors = 0;
}
else
{
    Helper.Log.Write(Helper.eLogType.Info, "Not connected, registering...");
    await Task.Run(() => Networking.SendRegister());
```

**New Code:**
```csharp
if(isConnected)
{
    // Validate capture method is available
    if (_selectedCaptureMethod == null)
    {
        Helper.Log.Write(Helper.eLogType.Error,
            "Cannot capture: No capture method selected!");
        await Task.Delay(2000, _cancellationTokenSource.Token);
        continue;
    }

    var watchFPS = System.Diagnostics.Stopwatch.StartNew();

    // Capture using selected method
    CaptureResult captureResult = null;
    await Task.Run(() =>
    {
        captureResult = _selectedCaptureMethod.Capture(
            Globals.Instance.Width,
            Globals.Instance.Height);
    });

    watchFPS.Stop();

    // Process capture result
    if (captureResult != null && captureResult.Success)
    {
        var elapsedFPS = 1 / watchFPS.Elapsed.TotalSeconds;
        Helper.Log.Write(Helper.eLogType.Performance, "Capture FPS: " + elapsedFPS.ToString("F1"));

        // STEP 9: Initiate FlatBuffers connection (send frame)
        try
        {
            _ = Networking.SendImageAsync(
                captureResult.YData,
                captureResult.UVData,
                captureResult.Width,
                captureResult.Height);
        }
        catch (NullReferenceException ex)
        {
            Helper.Log.Write(Helper.eLogType.Error,
                $"Capture loop: NullRef in SendImageAsync: {ex.Message}");
            throw; // Re-throw to be caught by outer catch
        }
        catch (Exception ex)
        {
            Helper.Log.Write(Helper.eLogType.Error,
                $"Capture loop: Error in SendImageAsync: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        // Update statistics
        _framesCaptured++;
        _fpsHistory.Add(elapsedFPS);
        if (_fpsHistory.Count > 100) // Keep last 100 samples
        {
            _fpsHistory.RemoveAt(0);
        }

        // Reset error counter on success
        consecutiveErrors = 0;
    }
    else
    {
        // Capture failed
        string errorMsg = captureResult?.ErrorMessage ?? "Unknown capture error";
        Helper.Log.Write(Helper.eLogType.Warning,
            $"Capture failed: {errorMsg}");
        consecutiveErrors++;

        // Brief delay before retry
        await Task.Delay(500, _cancellationTokenSource.Token);
    }
}
else
{
    Helper.Log.Write(Helper.eLogType.Info, "Not connected, registering...");
    await Task.Run(() => Networking.SendRegister());
```

---

## Edit 9: Add Cleanup in Stop() Method

**Location**: Stop() method, after network connection cleanup
**Purpose**: Clean up capture method resources on shutdown

**Old Code:**
```csharp
// Close network connections
if (Networking.client != null && Networking.client.Connected)
{
    try
    {
        Helper.Log.Write(Helper.eLogType.Info, "Closing network connection...");
        Networking.DisconnectClient();
        Helper.Log.Write(Helper.eLogType.Info, "Network connection closed");
    }
    catch (Exception ex)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            $"Error closing network connection: {ex.Message}");
    }
}

State = ServiceState.Idle;
Helper.Log.Write(Helper.eLogType.Info, "HyperionClient stopped");
```

**New Code:**
```csharp
// Close network connections
if (Networking.client != null && Networking.client.Connected)
{
    try
    {
        Helper.Log.Write(Helper.eLogType.Info, "Closing network connection...");
        Networking.DisconnectClient();
        Helper.Log.Write(Helper.eLogType.Info, "Network connection closed");
    }
    catch (Exception ex)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            $"Error closing network connection: {ex.Message}");
    }
}

// Cleanup capture method resources
if (_selectedCaptureMethod != null)
{
    try
    {
        Helper.Log.Write(Helper.eLogType.Info, "Cleaning up capture method...");
        _selectedCaptureMethod.Cleanup();
        _selectedCaptureMethod = null;
        Helper.Log.Write(Helper.eLogType.Info, "Capture method cleaned up");
    }
    catch (Exception ex)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            $"Error cleaning up capture method: {ex.Message}");
    }
}

// Cleanup capture selector
if (_captureSelector != null)
{
    try
    {
        Helper.Log.Write(Helper.eLogType.Info, "Resetting capture selector...");
        _captureSelector.Reset();
        _captureSelector = null;
        Helper.Log.Write(Helper.eLogType.Info, "Capture selector reset");
    }
    catch (Exception ex)
    {
        Helper.Log.Write(Helper.eLogType.Warning,
            $"Error resetting capture selector: {ex.Message}");
    }
}

State = ServiceState.Idle;
Helper.Log.Write(Helper.eLogType.Info, "HyperionClient stopped");
```

---

## Summary of Changes

### Lines Modified
- **Line 21**: Added `using HyperTizen.Capture;`
- **Lines 58-60**: Added capture architecture fields
- **Lines 104-105**: Added STEP 1 label
- **Lines 122-125**: Added STEP 2 & 3 labels
- **Lines 148-198**: Replaced VideoCapture.InitCapture() with CaptureMethodSelector (STEP 5, 6, 7)
- **Lines 195-236**: Updated diagnostic mode (STEP 4)
- **Lines 296-298**: Added STEP 8 label
- **Lines 303-304**: Added STEP 10 label
- **Lines 351-430**: Replaced VideoCapture.DoCapture() with ICaptureMethod.Capture() (STEP 9)
- **Lines 577-609**: Added cleanup in Stop() method

### Total Lines Changed
- **Additions**: ~100 lines
- **Deletions**: ~70 lines
- **Net Change**: +30 lines

### Files Affected
- `/home/user/HyperTizen/HyperTizen/HyperionClient.cs` (refactored)

### Dependencies Added
- `HyperTizen.Capture` namespace
- `ICaptureMethod` interface
- `CaptureMethodSelector` class
- `CaptureResult` class

### Removed Dependencies
- Direct calls to `VideoCapture.InitCapture()`
- Direct calls to `VideoCapture.DoCapture()`
- Manual exception handling for specific capture methods
