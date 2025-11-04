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
                Globals.Instance.SetConfig();
                
                // Log the discovered configuration
                Helper.Log.Write(Helper.eLogType.Info, 
                    $"Config: {Globals.Instance.ServerIp ?? "null"}:{Globals.Instance.ServerPort}");
                
                // Validate configuration before starting loop
                if (string.IsNullOrEmpty(Globals.Instance.ServerIp) || Globals.Instance.ServerPort <= 0)
                {
                    Helper.Log.Write(Helper.eLogType.Error, 
                        "STARTUP FAILED: Invalid config from SSDP");
                    return;
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
                    return;
                }
                catch (NullReferenceException nullEx)
                {
                    Helper.Log.Write(Helper.eLogType.Error, 
                        $"NULL POINTER: SecVideoCapture SDK initialization failed - {nullEx.Message}");
                    return;
                }
                catch (OutOfMemoryException memEx)
                {
                    Helper.Log.Write(Helper.eLogType.Error, 
                        $"OUT OF MEMORY: Cannot allocate capture buffers - {memEx.Message}");
                    return;
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, 
                        $"INIT FAILED: {ex.GetType().Name}: {ex.Message}");
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