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

    internal class HyperionClient
    {
        public HyperionClient()
        {
            Task.Run(() => Start());
        }

        public async Task Start()
        {
            try
            {
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
                    if (SystemInfo.TizenVersionMajor >= 8)
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
                    }

                    // Show notification
                    Notification diagNotif = new Notification
                    {
                        Title = "🔬 DIAGNOSTIC MODE",
                        Content = "Paused for 10 min - Connect WebSocket to see logs!",
                        Count = 999
                    };
                    NotificationManager.Post(diagNotif);

                    // Wait for 10 minutes
                    for (int i = 10; i > 0; i--)
                    {
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

                        await Task.Delay(60000); // 1 minute
                    }

                    Helper.Log.Write(Helper.eLogType.Warning,
                        "DIAGNOSTIC MODE: 10 minutes elapsed. Exiting gracefully.");
                    Globals.Instance.Enabled = false;
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, "Starting main capture loop...");

                int consecutiveErrors = 0;
                const int maxConsecutiveErrors = 10;

                while (Globals.Instance.Enabled)
                {
                    try
                    {
                        if(Networking.client != null && Networking.client.Client.Connected)
                        {
                            var watchFPS = System.Diagnostics.Stopwatch.StartNew();
                            await Task.Run(() =>VideoCapture.DoCapture()); //VideoCapture.DoDummyCapture();
                            watchFPS.Stop();
                            var elapsedFPS = 1 / watchFPS.Elapsed.TotalSeconds;
                            Helper.Log.Write(Helper.eLogType.Performance, "Capture FPS: " + elapsedFPS.ToString("F1"));
                            
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
                                await Task.Delay(2000);
                            }
                        }
                    }
                    catch (Exception loopEx)
                    {
                        consecutiveErrors++;
                        Helper.Log.Write(Helper.eLogType.Error, 
                            $"LOOP ERROR #{consecutiveErrors}: {loopEx.GetType().Name}: {loopEx.Message}");
                        
                        if (consecutiveErrors >= maxConsecutiveErrors)
                        {
                            Helper.Log.Write(Helper.eLogType.Error, 
                                $"TOO MANY ERRORS ({consecutiveErrors}). Stopping capture to prevent crash.");
                            Globals.Instance.Enabled = false;
                            break;
                        }
                        
                        // Wait before retrying to prevent tight error loop
                        await Task.Delay(2000);
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, 
                    $"FATAL ERROR in Start(): {ex.GetType().Name}: {ex.Message}\nStack: {ex.StackTrace}");
            }
                
        }

        public async Task Stop()
        {

        }
    }
}