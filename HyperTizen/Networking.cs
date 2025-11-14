using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Google.FlatBuffers;
using hyperhdrnet;
using Tizen.Messaging.Messages;

namespace HyperTizen
{
    public static class Networking
    {
        public static TcpClient client;
        public static NetworkStream stream;

        public static void DisconnectClient()
        {
            try
            {
                if (stream != null)
                {
                    stream.Flush();
                    stream.Close(500);
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Warning, $"DisconnectClient: Stream close error: {ex.Message}");
            }

            try
            {
                if (client != null)
                {
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Warning, $"DisconnectClient: Client close error: {ex.Message}");
            }

            // CRITICAL FIX: Null out references to prevent race conditions
            stream = null;
            client = null;
            Helper.Log.Write(Helper.eLogType.Info, "DisconnectClient: Client and stream nulled");
        }

        public static void SendRegister()
        {
            try
            {
                // Validate before connecting
                if (string.IsNullOrEmpty(Globals.Instance.ServerIp) || Globals.Instance.ServerPort <= 0)
                {
                    Helper.Log.Write(Helper.eLogType.Error, 
                        $"TCP FAILED: Bad config {Globals.Instance.ServerIp ?? "null"}:{Globals.Instance.ServerPort}");
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, 
                    $"TCP: Connecting to {Globals.Instance.ServerIp}:{Globals.Instance.ServerPort}");

                client = new TcpClient(Globals.Instance.ServerIp, Globals.Instance.ServerPort);

                // Disable Nagle's algorithm to prevent buffering delays
                client.NoDelay = true;

                Helper.Log.Write(Helper.eLogType.Info, "TCP: Socket created (NoDelay=true)");
                
                if (client == null || !client.Connected)
                {
                    Helper.Log.Write(Helper.eLogType.Error, "TCP FAILED: Client null/not connected");
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, "TCP: Connected! Getting stream...");
                
                stream = Networking.client.GetStream();
                if (stream == null)
                {
                    Helper.Log.Write(Helper.eLogType.Error, "TCP FAILED: No stream");
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, "TCP: Stream OK, creating FlatBuffer msg...");

                byte[] registrationMessage = Networking.CreateRegistrationMessage();
                if (registrationMessage == null)
                {
                    Helper.Log.Write(Helper.eLogType.Error, "TCP FAILED: No FlatBuffer message");
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, $"TCP: Sending {registrationMessage.Length} bytes...");

                // Message already includes 4-byte little-endian size prefix from FinishSizePrefixed
                // Log the exact bytes being sent for debugging
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"TCP: Message bytes (with size prefix): {BitConverter.ToString(registrationMessage)}");

                stream.Write(registrationMessage, 0, registrationMessage.Length);

                // CRITICAL FIX: Flush the stream to ensure data is actually sent!
                // Without this, data stays in buffer and HyperHDR never receives it
                stream.Flush();

                Helper.Log.Write(Helper.eLogType.Info, "TCP: Data sent and flushed, waiting for reply...");
                
                ReadRegisterReply();
                
                Helper.Log.Write(Helper.eLogType.Info, "TCP OK: Fully registered!");
            }
            catch (SocketException ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, 
                    $"SOCKET ERROR: {ex.Message} (Code:{ex.ErrorCode})");
                DisconnectClient();
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, 
                    $"ERROR: {ex.GetType().Name}: {ex.Message}");
                DisconnectClient();
            }
        }

        public static async Task SendImageAsync(byte[] yData, byte[] uvData, int width, int height)
        {
            // ENHANCED NULL SAFETY: Check client validity before proceeding
            try
            {
                if (client == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "SendImageAsync: client is null");
                    return;
                }

                if (client.Client == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "SendImageAsync: client.Client is null");
                    return;
                }

                if (!client.Connected)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "SendImageAsync: client not connected");
                    return;
                }

                if (stream == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "SendImageAsync: stream is null");
                    return;
                }
            }
            catch (NullReferenceException ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"SendImageAsync: NullRef during validation: {ex.Message}");
                return;
            }
            catch (ObjectDisposedException ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"SendImageAsync: Object disposed during validation: {ex.Message}");
                return;
            }

            byte[] message = CreateFlatBufferMessage(yData, uvData, width, height);
            if (message == null)
            {
                Helper.Log.Write(Helper.eLogType.Warning, "SendImageAsync: CreateFlatBufferMessage returned null");
                return;
            }

            var watchFPS = System.Diagnostics.Stopwatch.StartNew();
            _ = SendMessageAndReceiveReplyAsync(message);
            watchFPS.Stop();
            Helper.Log.Write(Helper.eLogType.Performance, "SendImageAsync elapsed ms: " + watchFPS.ElapsedMilliseconds);
        }
        static byte[] CreateFlatBufferMessage(byte[] yData, byte[] uvData, int width, int height)
        {
            // ENHANCED NULL SAFETY: Detailed checks with logging
            try
            {
                if (client == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateFlatBufferMessage: client is null");
                    return null;
                }

                if (client.Client == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateFlatBufferMessage: client.Client is null");
                    return null;
                }

                if (!client.Connected)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateFlatBufferMessage: client not connected");
                    return null;
                }

                if (stream == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateFlatBufferMessage: stream is null");
                    return null;
                }
            }
            catch (NullReferenceException ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"CreateFlatBufferMessage: NullRef during validation: {ex.Message}");
                return null;
            }
            catch (ObjectDisposedException ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"CreateFlatBufferMessage: Object disposed: {ex.Message}");
                return null;
            }

            var builder = new FlatBufferBuilder(yData.Length + uvData.Length + 100);

            var yVector = NV12Image.CreateDataYVector(builder, yData);
            var uvVector = NV12Image.CreateDataUvVector(builder, uvData);

            NV12Image.StartNV12Image(builder);
            NV12Image.AddDataY(builder, yVector);
            NV12Image.AddDataUv(builder, uvVector);
            NV12Image.AddWidth(builder, width);
            NV12Image.AddHeight(builder, height);
            NV12Image.AddStrideY(builder, width);  //TODO: Check if this is correct
            NV12Image.AddStrideUv(builder, width);
            var nv12Image = NV12Image.EndNV12Image(builder);

            Image.StartImage(builder);
            Image.AddDataType(builder, ImageType.NV12Image);
            Image.AddData(builder, nv12Image.Value);
            Image.AddDuration(builder, -1);
            var imageOffset = Image.EndImage(builder);

            Request.StartRequest(builder);
            Request.AddCommandType(builder, Command.Image);
            Request.AddCommand(builder, imageOffset.Value);
            var requestOffset = Request.EndRequest(builder);

            // Use FinishSizePrefixed to include the 4-byte little-endian size prefix
            Request.FinishSizePrefixedRequestBuffer(builder, requestOffset);
            return builder.SizedByteArray();
        }

        static Reply ParseReply(byte[] receivedData)
        {
            var byteBuffer = new ByteBuffer(receivedData, 4); //shift for header
            return Reply.GetRootAsReply(byteBuffer);
        }

        public static byte[] CreateRegistrationMessage()
        {
            // ENHANCED NULL SAFETY: Detailed checks with logging
            try
            {
                if (client == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateRegistrationMessage: client is null");
                    return null;
                }

                if (client.Client == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateRegistrationMessage: client.Client is null");
                    return null;
                }

                if (!client.Connected)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateRegistrationMessage: client not connected");
                    return null;
                }

                if (stream == null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "CreateRegistrationMessage: stream is null");
                    return null;
                }
            }
            catch (NullReferenceException ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"CreateRegistrationMessage: NullRef during validation: {ex.Message}");
                return null;
            }
            catch (ObjectDisposedException ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"CreateRegistrationMessage: Object disposed: {ex.Message}");
                return null;
            }

            var builder = new FlatBufferBuilder(256); //TODO:Check how to calculate correctly

            var originOffset = builder.CreateString("HyperTizen");

            Helper.Log.Write(Helper.eLogType.Debug,
                $"CreateRegistrationMessage: Building Register with origin='HyperTizen', priority=123");

            Register.StartRegister(builder);
            Register.AddPriority(builder, 123);
            Register.AddOrigin(builder, originOffset);
            var registerOffset = Register.EndRegister(builder);

            Helper.Log.Write(Helper.eLogType.Debug,
                $"CreateRegistrationMessage: Register offset={registerOffset.Value}");

            Request.StartRequest(builder);
            Request.AddCommandType(builder, Command.Register);
            Request.AddCommand(builder, registerOffset.Value);
            var requestOffset = Request.EndRequest(builder);

            Helper.Log.Write(Helper.eLogType.Debug,
                $"CreateRegistrationMessage: Request offset={requestOffset.Value}");

            // Use FinishSizePrefixed to include the 4-byte little-endian size prefix
            // This is the correct FlatBuffers protocol format
            Request.FinishSizePrefixedRequestBuffer(builder, requestOffset);
            byte[] message = builder.SizedByteArray();

            Helper.Log.Write(Helper.eLogType.Debug,
                $"CreateRegistrationMessage: Generated {message.Length} byte message (with size prefix)");

            return message;
        }

        public static void ReadRegisterReply()
        {
            try
            {
                if (client == null || !client.Connected || stream == null)
                {
                    Helper.Log.Write(Helper.eLogType.Error, "ReadRegisterReply: No client/stream");
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, "ReadRegisterReply: Waiting for server reply...");

                // Log stream status for debugging
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"ReadRegisterReply: Stream readable={stream.CanRead}, writable={stream.CanWrite}, dataAvailable={stream.DataAvailable}");

                // Set read timeout to prevent infinite blocking
                stream.ReadTimeout = 5000; // 5 second timeout

                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"ReadRegisterReply: Got {bytesRead} bytes");

                    byte[] replyData = new byte[bytesRead];
                    Array.Copy(buffer, replyData, bytesRead);

                    // Log raw reply bytes for debugging
                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"ReadRegisterReply: Raw bytes: {BitConverter.ToString(replyData)}");

                    Reply reply = ParseReply(replyData);

                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"ReadRegisterReply: Parsed - Registered={reply.Registered}, Video={reply.Video}");

                    if (reply.Registered > 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, "ReadRegisterReply: REGISTERED OK!");
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Error, $"ReadRegisterReply: NOT registered (code: {reply.Registered})");
                    }
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Error, "ReadRegisterReply: No data received");
                }
            }
            catch (System.IO.IOException ex)
            {
                // Log stream state at timeout
                string streamState = stream != null ?
                    $"CanRead={stream.CanRead}, CanWrite={stream.CanWrite}, DataAvail={stream.DataAvailable}" :
                    "stream is null";

                Helper.Log.Write(Helper.eLogType.Error,
                    $"ReadRegisterReply TIMEOUT: {ex.Message}");
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"ReadRegisterReply TIMEOUT: Stream state at timeout: {streamState}");
                DisconnectClient();
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error,
                    $"ReadRegisterReply ERROR: {ex.GetType().Name}: {ex.Message}");
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"ReadRegisterReply ERROR: Stack trace: {ex.StackTrace}");
                DisconnectClient();
            }
        }

        public static async Task ReadImageReply()
        {
            if (client == null || !client.Connected || stream == null)
                return;

            byte[] buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {

                byte[] replyData = new byte[bytesRead];
                Array.Copy(buffer, replyData, bytesRead);
                Reply reply = ParseReply(replyData);


                Helper.Log.Write(Helper.eLogType.Info, $"SendMessageAndReceiveReply: Reply_Video: {reply.Video}");
                Helper.Log.Write(Helper.eLogType.Info, $"SendMessageAndReceiveReply: Reply_Registered: {reply.Registered}");
                if (!string.IsNullOrEmpty(reply.Error))
                {
                    Helper.Log.Write(Helper.eLogType.Error, "SendMessageAndReceiveReply: (closing tcp client now) Reply_Error: " + reply.Error);
                    //Debug.WriteLine("SendMessageAndReceiveReply: Faulty msg(size:" + message.Length + "): " + BitConverter.ToString(message));
                    DisconnectClient();
                    return;
                }
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Error, "SendMessageAndReceiveReply: (closing tcp client now) No Answer from Server.");
                DisconnectClient();
                return;
            }
        }

        static async Task SendMessageAndReceiveReplyAsync(byte[] message)
        {
            try
            {
                if (client == null || !client.Connected || stream == null)
                    return;

                // Message already includes 4-byte little-endian size prefix from FinishSizePrefixed
                Helper.Log.Write(Helper.eLogType.Info, "SendMessageAndReceiveReply: message.Length; " + message.Length);
                await stream.WriteAsync(message, 0, message.Length);
                await stream.FlushAsync();
                Helper.Log.Write(Helper.eLogType.Info, "SendMessageAndReceiveReply: Data sent");
                _ = ReadImageReply();
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, "SendMessageAndReceiveReply: Exception (closing tcp client now) Sending/Receiving: " + ex.Message);
                DisconnectClient();
                return;
            }
        }

    }
}
