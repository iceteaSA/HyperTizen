using Newtonsoft.Json;
using System.Threading.Tasks;
using Tizen.Applications;
using System.Net.WebSockets;
using System.Text;
using System;
using System.Threading;
using System.Runtime.InteropServices;
using SkiaSharp;
using Tizen.NUI;
using System.Diagnostics;
using System.Linq;
using Tizen.Uix.Tts;
using System.Net.Sockets;
using Tizen.Applications.RPCPort;
using System.IO;
using Tizen.Messaging.Messages;
using System.Linq.Expressions;
using Tizen.Applications.Notifications;

namespace HyperTizen
{
    // Service state enum for lifecycle management
    public enum ServiceState
    {
        Idle,           // Not capturing
        Starting,       // Initializing
        Capturing,      // Active capture
        Paused,         // Temporarily suspended
        Stopping,       // Shutting down
        Error           // Failed state
    }

    // Detailed status information
    public class ServiceStatus
    {
        public ServiceState State { get; set; }
        public long FramesCaptured { get; set; }
        public double AverageFPS { get; set; }
        public int ErrorCount { get; set; }
        public bool IsConnected { get; set; }
        public string LastError { get; set; }
        public DateTime StartTime { get; set; }
    }

    internal class HyperionClient
    {
        // Lifecycle management fields
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning = false;
        private bool _isPaused = false;
        private readonly object _pauseLock = new object();
        private ServiceState _serviceState = ServiceState.Idle;
        private readonly object _stateLock = new object();

        // Capture statistics
        private long _framesCaptured = 0;
        private int _errorCount = 0;
        private string _lastError = null;
        private DateTime _startTime;
        private List<double> _fpsHistory = new List<double>();

        public HyperionClient()
        {
            Task.Run(() => Start());
        }

        // Get current service state
        public ServiceState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _serviceState;
                }
            }
            private set
            {
                lock (_stateLock)
                {
                    _serviceState = value;
                }
            }
        }

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
                const bool DIAGNOSTIC_MODE = true;

                Globals.Instance.SetConfig();

                // Log the discovered configuration
                Helper.Log.Write(Helper.eLogType.Info,
                    $"Config: {Globals.Instance.ServerIp ?? "null"}:{Globals.Instance.ServerPort}");

                // In diagnostic mode, skip config validation to allow initialization
                if (!DIAGNOSTIC_MODE)
                {
                    // Validate configuration before starting loop
                    if (string.IsNullOrEmpty(Globals.Instance.ServerIp) || Globals.Instance.ServerPort <= 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Error,
                            "STARTUP FAILED: Invalid config from SSDP");
                        return;
                    }
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Warning,
                        "DIAGNOSTIC MODE ACTIVE - Will pause after initialization");
                }

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

                    // Show notification
                    Notification diagNotif = new Notification
                    {
                        Title = "🔬 DIAGNOSTIC MODE",
                        Content = "Paused for 10 min - Connect WebSocket to see logs!",
                        Count = 999
                    };
                    NotificationManager.Post(diagNotif);

                    // Wait for 10 minutes (with cancellation support)
                    for (int i = 10; i > 0; i--)
                    {
                        // Check for cancellation
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            Helper.Log.Write(Helper.eLogType.Warning, "DIAGNOSTIC MODE: Cancelled by user");
                            break;
                        }

                        // Get WebSocket diagnostics
                        string wsDiag = Helper.Log.GetWebSocketDiagnostics();

                        Helper.Log.Write(Helper.eLogType.Info,
                            $"Diagnostic mode: {i} minute(s) remaining...");
                        Helper.Log.Write(Helper.eLogType.Info, wsDiag);

                        // Show notification with WebSocket status
                        Notification countdownNotif = new Notification
                        {
                            Title = $"⏱ {i} min remaining",
                            Content = wsDiag,
                            Count = 100 + i
                        };
                        NotificationManager.Post(countdownNotif);

                        try
                        {
                            await Task.Delay(60000, _cancellationTokenSource.Token); // 1 minute
                        }
                        catch (OperationCanceledException)
                        {
                            Helper.Log.Write(Helper.eLogType.Info, "DIAGNOSTIC MODE: Cancelled during delay");
                            break;
                        }
                    }

                    Helper.Log.Write(Helper.eLogType.Warning,
                        "DIAGNOSTIC MODE: 10 minutes elapsed. Exiting gracefully.");
                    Globals.Instance.Enabled = false;
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, "Starting main capture loop...");
                State = ServiceState.Capturing;

                int consecutiveErrors = 0;
                const int maxConsecutiveErrors = 10;

                while (!_cancellationTokenSource.Token.IsCancellationRequested && Globals.Instance.Enabled)
                {
                    try
                    {
                        // Handle pause state
                        if (_isPaused)
                        {
                            if (State != ServiceState.Paused)
                            {
                                State = ServiceState.Paused;
                                Helper.Log.Write(Helper.eLogType.Info, "Capture loop paused");
                            }

                            await Task.Delay(100, _cancellationTokenSource.Token);
                            continue;
                        }
                        else
                        {
                            // Resume from paused state
                            if (State == ServiceState.Paused)
                            {
                                State = ServiceState.Capturing;
                                Helper.Log.Write(Helper.eLogType.Info, "Capture loop resumed");
                            }
                        }

                        if(Networking.client != null && Networking.client.Client.Connected)
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

                            // Add delay between retry attempts to prevent tight loop
                            if (Networking.client == null || !Networking.client.Connected)
                            {
                                Helper.Log.Write(Helper.eLogType.Warning, "Register failed, retry in 2s");
                                await Task.Delay(2000, _cancellationTokenSource.Token);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, "Capture loop cancelled gracefully");
                        break;
                    }
                    catch (Exception loopEx)
                    {
                        consecutiveErrors++;
                        _errorCount++;
                        _lastError = $"{loopEx.GetType().Name}: {loopEx.Message}";
                        Helper.Log.Write(Helper.eLogType.Error,
                            $"LOOP ERROR #{consecutiveErrors}: {_lastError}");

                        if (consecutiveErrors >= maxConsecutiveErrors)
                        {
                            Helper.Log.Write(Helper.eLogType.Error,
                                $"TOO MANY ERRORS ({consecutiveErrors}). Stopping capture to prevent crash.");
                            State = ServiceState.Error;
                            Globals.Instance.Enabled = false;
                            break;
                        }

                        // Wait before retrying to prevent tight error loop
                        try
                        {
                            await Task.Delay(2000, _cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            Helper.Log.Write(Helper.eLogType.Info, "Cancelled during error recovery delay");
                            break;
                        }
                    }
                }

                Helper.Log.Write(Helper.eLogType.Info, "Capture loop exited");
                State = ServiceState.Idle;
            }
            catch (Exception ex)
            {
                _errorCount++;
                _lastError = $"FATAL: {ex.GetType().Name}: {ex.Message}";
                Helper.Log.Write(Helper.eLogType.Error,
                    $"FATAL ERROR in Start(): {_lastError}\nStack: {ex.StackTrace}");
                State = ServiceState.Error;
            }
            finally
            {
                _isRunning = false;
                Helper.Log.Write(Helper.eLogType.Info, "HyperionClient Start() method completed");
            }
        }

        public async Task Stop()
        {
            Helper.Log.Write(Helper.eLogType.Info, "Stopping HyperionClient...");
            State = ServiceState.Stopping;

            // Set global enabled flag to false
            Globals.Instance.Enabled = false;

            // Cancel the cancellation token source
            if (_cancellationTokenSource != null)
            {
                try
                {
                    _cancellationTokenSource.Cancel();
                    Helper.Log.Write(Helper.eLogType.Info, "Cancellation token signaled");
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Warning,
                        $"Error cancelling token: {ex.Message}");
                }

                // Wait for graceful shutdown with timeout
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (_isRunning && stopwatch.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(100);
                }

                if (_isRunning)
                {
                    Helper.Log.Write(Helper.eLogType.Warning,
                        "Force stopped after timeout - capture loop may still be running");
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Info,
                        $"Graceful shutdown completed in {stopwatch.ElapsedMilliseconds}ms");
                }

                // Dispose of cancellation token source
                try
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Warning,
                        $"Error disposing cancellation token: {ex.Message}");
                }
            }

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
        }

        public void Pause()
        {
            lock (_pauseLock)
            {
                if (_isPaused)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "Capture already paused");
                    return;
                }

                _isPaused = true;
                Helper.Log.Write(Helper.eLogType.Info, "Capture paused");
            }
        }

        public void Resume()
        {
            lock (_pauseLock)
            {
                if (!_isPaused)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "Capture not paused");
                    return;
                }

                _isPaused = false;
                Helper.Log.Write(Helper.eLogType.Info, "Capture resumed");
            }
        }

        public ServiceStatus GetStatus()
        {
            double avgFPS = 0;
            if (_fpsHistory.Count > 0)
            {
                avgFPS = _fpsHistory.Average();
            }

            bool isConnected = Networking.client != null && Networking.client.Connected;

            var status = new ServiceStatus
            {
                State = State,
                FramesCaptured = _framesCaptured,
                AverageFPS = avgFPS,
                ErrorCount = _errorCount,
                IsConnected = isConnected,
                LastError = _lastError,
                StartTime = _startTime
            };

            return status;
        }
    }
}