using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tizen.Applications;

namespace HyperTizen
{
    public sealed class Globals
    {
        private static readonly Globals instance = new Globals();

        // Explicit static constructor to tell C# compiler
        // not to mark type as beforefieldinit
        static Globals()
        {
        }

        private Globals()
        {
        }

        public static Globals Instance
        {
            get
            {
                return instance;
            }
        }
        public void SetConfig()
        {
            // First, try to use the stored rpcServer preference (user's selection from UI)
            if (Preference.Contains("rpcServer"))
            {
                string rpcServer = Preference.Get<string>("rpcServer");
                if (!string.IsNullOrEmpty(rpcServer))
                {
                    // Parse ws://ip:port format
                    try
                    {
                        var uri = new Uri(rpcServer.Replace("ws://", "http://").Replace("wss://", "https://"));
                        ServerIp = uri.Host;
                        ServerPort = uri.Port;
                        Helper.Log.Write(Helper.eLogType.Info,
                            $"Config: Using saved server {ServerIp}:{ServerPort} from preferences");

                        // Safe parsing of enabled preference with fallback to false
                        string enabledPref = Preference.Contains("enabled") ? Preference.Get<string>("enabled") : "false";
                        Enabled = bool.TryParse(enabledPref, out bool enabledResult) && enabledResult;
                        Helper.Log.Write(Helper.eLogType.Info, $"Loaded enabled setting: {Enabled}");

                        Width = 3840/8;
                        Height = 2160/8;
                        return; // Successfully loaded from preferences
                    }
                    catch (Exception ex)
                    {
                        Helper.Log.Write(Helper.eLogType.Warning,
                            $"Failed to parse saved rpcServer '{rpcServer}': {ex.Message}. Falling back to SSDP discovery.");
                    }
                }
            }

            // Fallback to SSDP discovery if no saved preference or parsing failed
            Helper.Log.Write(Helper.eLogType.Info, "SSDP: Starting discovery...");

            (string ip, int port) = Helper.SsdpDiscovery.GetHyperIpAndPort();

            if (string.IsNullOrEmpty(ip) || port <= 0)
            {
                Helper.Log.Write(Helper.eLogType.Error,
                    "SSDP FAILED: No HyperHDR found. Check network!");
                // Set defaults or leave as null for validation later
                ServerIp = null;
                ServerPort = 0;
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Info,
                    $"SSDP OK: Found {ip}:{port}");
                ServerIp = ip;
                ServerPort = port;
            }

            // Safe parsing of enabled preference with fallback to false
            string enabledPref = Preference.Contains("enabled") ? Preference.Get<string>("enabled") : "false";
            Enabled = bool.TryParse(enabledPref, out bool enabledResult) && enabledResult;
            Helper.Log.Write(Helper.eLogType.Info, $"Loaded enabled setting: {Enabled}");

            Width = 3840/8;
            Height = 2160/8;
        }

        public string ServerIp; //IP of hyperhdr server
        public int ServerPort; //Port of hyperhdr server
        public int Width; //Capture Width
        public int Height; //Capture Height
        public bool Enabled; //Is the service enabled
        public bool ShowNotifications = false; //Show notification popups on TV (default: false, use new UI instead)
    }
}
