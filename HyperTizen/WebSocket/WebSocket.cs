using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using HyperTizen.WebSocket.DataTypes;
using Rssdp;
using Tizen.Applications;
using static HyperTizen.WebSocket.DataTypes.SSDPScanResultEvent;

namespace HyperTizen.WebSocket
{
    public class WSServer
    {
        private HttpListener _httpListener;
        private List<System.Net.WebSockets.WebSocket> _connectedClients = new List<System.Net.WebSockets.WebSocket>();
        private readonly object _clientsLock = new object();
        private List<string> usnList = new List<string>()
        {
            "urn:hyperion-project.org:device:basic:1",
            "urn:hyperhdr.eu:device:basic:1"
        };

        public WSServer(string uriPrefix)
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(uriPrefix);
        }

        public async Task StartAsync()
        {
            _httpListener.Start();
            while (true)
            {
                var httpContext = await _httpListener.GetContextAsync();
                if (httpContext.Request.IsWebSocketRequest)
                {
                    var wsContext = await httpContext.AcceptWebSocketAsync(null);
                    _ = HandleWebSocketAsync(wsContext.WebSocket);
                }
                else
                {
                    httpContext.Response.StatusCode = 400;
                    httpContext.Response.Close();
                }
            }
        }

        private async Task HandleWebSocketAsync(System.Net.WebSockets.WebSocket webSocket)
        {
            // Add client to connected list
            lock (_clientsLock)
            {
                _connectedClients.Add(webSocket);
            }

            // Send initial status to newly connected client
            try
            {
                bool isCapturing = App.Configuration.Enabled;
                string initialStatus = isCapturing ? "capturing" : "stopped";
                string initialMessage = isCapturing ? "Currently capturing" : "Capture stopped";

                string statusEvent = JsonConvert.SerializeObject(new StatusUpdateEvent(initialStatus, initialMessage));
                await SendAsync(webSocket, statusEvent);

                Helper.Log.Write(Helper.eLogType.Info,
                    $"Sent initial status to client: {initialStatus}");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Warning,
                    $"Failed to send initial status to client: {ex.Message}");
            }

            try
            {
                var buffer = new byte[1024 * 4];
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                while (result.MessageType != WebSocketMessageType.Close)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await OnMessageAsync(webSocket, message);
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }

                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            finally
            {
                // Remove client from connected list
                lock (_clientsLock)
                {
                    _connectedClients.Remove(webSocket);
                }
            }
        }

        protected async Task OnMessageAsync(System.Net.WebSockets.WebSocket webSocket, string message)
        {
            try
            {
                BasicEvent data = JsonConvert.DeserializeObject<BasicEvent>(message);

                switch (data.Event)
                {
                    case Event.ScanSSDP:
                        {
                            var devices = await ScanSSDPAsync();
                            string resultEvent = JsonConvert.SerializeObject(new SSDPScanResultEvent(devices));
                            await SendAsync(webSocket, resultEvent);
                            break;
                        }

                    case Event.GetLogs:
                        {
                            var logs = Helper.Log.GetRecentLogs();
                            string logPath = Helper.Log.GetWorkingLogPath();
                            string resultEvent = JsonConvert.SerializeObject(new LogsResultEvent(logs, logPath));
                            await SendAsync(webSocket, resultEvent);
                            break;
                        }

                    case Event.ReadConfig:
                        {
                            ReadConfigEvent readConfigEvent = JsonConvert.DeserializeObject<ReadConfigEvent>(message);
                            string result = await ReadConfigAsync(readConfigEvent);
                            await SendAsync(webSocket, result);
                            break;
                        }

                    case Event.SetConfig:
                        {
                            SetConfigEvent setConfigEvent = JsonConvert.DeserializeObject<SetConfigEvent>(message);
                            await SetConfiguration(setConfigEvent);
                            break;
                        }
                }
            }
            catch (JsonException jsonEx)
            {
                Helper.Log.Write(Helper.eLogType.Error,
                    $"Invalid JSON received from WebSocket client: {jsonEx.Message}");
                Helper.Log.Write(Helper.eLogType.Debug, $"Message: {message}");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error,
                    $"Error processing WebSocket message: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task<List<SSDPDevice>> ScanSSDPAsync()
        {
            var devices = new List<SSDPDevice>();
            using (var deviceLocator = new SsdpDeviceLocator())
            {
                var foundDevices = await deviceLocator.SearchAsync();
                foreach (var foundDevice in foundDevices)
                {
                    if (!usnList.Contains(foundDevice.NotificationType)) continue;

                    var fullDevice = await foundDevice.GetDeviceInfo();
                    Uri descLocation = foundDevice.DescriptionLocation;
                    devices.Add(new SSDPDevice(fullDevice.FriendlyName, descLocation.OriginalString.Replace(descLocation.PathAndQuery, "")));
                }
            }
            return devices;
        }

        private async Task<string> ReadConfigAsync(ReadConfigEvent readConfigEvent)
        {
            string result;
            if (!Preference.Contains(readConfigEvent.key))
            {
                result = JsonConvert.SerializeObject(new ReadConfigResultEvent(true, readConfigEvent.key, "Key doesn't exist."));
            }
            else
            {
                string value = Preference.Get<string>(readConfigEvent.key);
                result = JsonConvert.SerializeObject(new ReadConfigResultEvent(false, readConfigEvent.key, value));
            }
            return result;
        }

        private async Task SendAsync(System.Net.WebSockets.WebSocket webSocket, string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task BroadcastStatusUpdate(string status, string message)
        {
            string statusEvent = JsonConvert.SerializeObject(new StatusUpdateEvent(status, message));
            var buffer = Encoding.UTF8.GetBytes(statusEvent);

            List<System.Net.WebSockets.WebSocket> clients;
            lock (_clientsLock)
            {
                clients = new List<System.Net.WebSockets.WebSocket>(_connectedClients);
            }

            foreach (var client in clients)
            {
                try
                {
                    if (client.State == WebSocketState.Open)
                    {
                        await client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, $"Failed to send status update to client: {ex.Message}");
                }
            }
        }

        async Task SetConfiguration(SetConfigEvent setConfigEvent)
        {
            switch (setConfigEvent.key)
            {
                case "rpcServer":
                    {
                        App.Configuration.RPCServer = setConfigEvent.value;
                        Helper.Log.Write(Helper.eLogType.Info, $"RPC server set to: {setConfigEvent.value}");
                        //App.client.UpdateURI(setConfigEvent.value);
                        break;
                    }
                case "enabled":
                    {
                        // Validate input to prevent crashes
                        if (!bool.TryParse(setConfigEvent.value, out bool value))
                        {
                            Helper.Log.Write(Helper.eLogType.Error,
                                $"Invalid boolean value for 'enabled': {setConfigEvent.value}");
                            return;
                        }

                        // Synchronize state changes with lock to prevent race conditions
                        bool stateChanged = false;
                        bool newState = false;

                        lock (_clientsLock)
                        {
                            bool wasEnabled = App.Configuration.Enabled;
                            App.Configuration.Enabled = value;
                            Globals.Instance.Enabled = value;

                            if (wasEnabled != value)
                            {
                                stateChanged = true;
                                newState = value;
                                Helper.Log.Write(Helper.eLogType.Info,
                                    $"Capture state changed: {wasEnabled} -> {value}");
                            }
                        }

                        // Perform state-dependent actions outside the lock
                        if (stateChanged)
                        {
                            if (newState)
                            {
                                Helper.Log.Write(Helper.eLogType.Info, "Starting screen capture");
                                Task.Run(() => App.client.Start());
                            }
                            else
                            {
                                Helper.Log.Write(Helper.eLogType.Info, "Stopping screen capture");
                                // TODO: Implement proper Stop() method in HyperionClient
                            }

                            // Broadcast status update with error handling
                            try
                            {
                                if (newState)
                                {
                                    await BroadcastStatusUpdate("capturing", "Screen capture started");
                                }
                                else
                                {
                                    await BroadcastStatusUpdate("stopped", "Screen capture stopped");
                                }
                            }
                            catch (Exception ex)
                            {
                                Helper.Log.Write(Helper.eLogType.Warning,
                                    $"Failed to broadcast status update: {ex.Message}");
                            }
                        }
                        break;
                    }
            }

            Preference.Set(setConfigEvent.key, setConfigEvent.value);
        }
    }

    public static class WebSocketServer
    {
        public static async Task StartServerAsync()
        {
            var wsServer = new WSServer("http://+:45677/");
            await wsServer.StartAsync();
        }
    }

    public class WebSocketClient
    {
        private string uri;
        public ClientWebSocket client;
        private byte errorTimes = 0;

        public WebSocketClient(string uri)
        {
            this.uri = uri;
            client = new ClientWebSocket();
        }

        public async Task ConnectAsync()
        {
            await client.ConnectAsync(new Uri(uri), CancellationToken.None);
            _ = ReceiveMessagesAsync();
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[1024 * 4];
            while (client.State == WebSocketState.Open)
            {
                var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
                else
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    OnMessage(message);
                }
            }
        }

        private void OnMessage(string message)
        {

        }
    }
}