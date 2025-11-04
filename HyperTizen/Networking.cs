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
            if (stream != null)
            {
                stream.Flush();
                stream.Close(500);
            }

            client.Close();
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
                
                Helper.Log.Write(Helper.eLogType.Info, "TCP: Socket created");
                
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

                var header = new byte[4];
                header[0] = (byte)((registrationMessage.Length >> 24) & 0xFF);
                header[1] = (byte)((registrationMessage.Length >> 16) & 0xFF);
                header[2] = (byte)((registrationMessage.Length >> 8) & 0xFF);
                header[3] = (byte)((registrationMessage.Length) & 0xFF);
                
                stream.Write(header, 0, header.Length);
                stream.Write(registrationMessage, 0, registrationMessage.Length);
                
                Helper.Log.Write(Helper.eLogType.Info, "TCP: Data sent, waiting for reply...");
                
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
            if (client == null || !client.Connected || stream == null)
                return;
            byte[] message = CreateFlatBufferMessage(yData, uvData, width, height);
            if (message == null)
                return;

            var watchFPS = System.Diagnostics.Stopwatch.StartNew();
            _ = SendMessageAndReceiveReplyAsync(message);
            watchFPS.Stop();
            Helper.Log.Write(Helper.eLogType.Performance, "SendImageAsync elapsed ms: " + watchFPS.ElapsedMilliseconds);
        }
        static byte[] CreateFlatBufferMessage(byte[] yData, byte[] uvData, int width, int height)
        {
            if (client == null || !client.Connected || stream == null)
                return null;
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

            builder.Finish(requestOffset.Value);
            return builder.SizedByteArray();
        }

        static Reply ParseReply(byte[] receivedData)
        {
            var byteBuffer = new ByteBuffer(receivedData, 4); //shift for header
            return Reply.GetRootAsReply(byteBuffer);
        }

        public static byte[] CreateRegistrationMessage()
        {
            if (client == null || !client.Connected || stream == null)
                return null;

            var builder = new FlatBufferBuilder(256); //TODO:Check how to calculate correctly

            var originOffset = builder.CreateString("HyperTizen");

            Register.StartRegister(builder);
            Register.AddPriority(builder, 123);
            Register.AddOrigin(builder, originOffset);
            var registerOffset = Register.EndRegister(builder);

            Request.StartRequest(builder);
            Request.AddCommandType(builder, Command.Register);
            Request.AddCommand(builder, registerOffset.Value);
            var requestOffset = Request.EndRequest(builder);

            builder.Finish(requestOffset.Value);
            return builder.SizedByteArray();
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

                // Set read timeout to prevent infinite blocking
                stream.ReadTimeout = 5000; // 5 second timeout

                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                
                if (bytesRead > 0)
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"ReadRegisterReply: Got {bytesRead} bytes");
                    
                    byte[] replyData = new byte[bytesRead];
                    Array.Copy(buffer, replyData, bytesRead);

                    Reply reply = ParseReply(replyData);
                    
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
                Helper.Log.Write(Helper.eLogType.Error, $"ReadRegisterReply TIMEOUT: {ex.Message}");
                DisconnectClient();
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"ReadRegisterReply ERROR: {ex.Message}");
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
                {

                    var header = new byte[4];
                    header[0] = (byte)((message.Length >> 24) & 0xFF);
                    header[1] = (byte)((message.Length >> 16) & 0xFF);
                    header[2] = (byte)((message.Length >> 8) & 0xFF);
                    header[3] = (byte)((message.Length) & 0xFF);
                    await stream.WriteAsync(header, 0, header.Length);

                    Helper.Log.Write(Helper.eLogType.Info, "SendMessageAndReceiveReply: message.Length; " + message.Length);
                    await stream.WriteAsync(message, 0, message.Length);
                    await stream.FlushAsync();
                    Helper.Log.Write(Helper.eLogType.Info, "SendMessageAndReceiveReply: Data sent");
                    _ = ReadImageReply();

                }
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
