﻿using Ninja.WebSockets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebsocketCore
{
    public class WebsocketServer : IDisposable
    {
        private TcpListener _listener;
        private bool _isDisposed = false;
        private readonly IWebSocketServerFactory _webSocketServerFactory;
        private readonly HashSet<string> _supportedSubProtocols;
        private static readonly string WebSocket_Directory = Environment.CurrentDirectory;
        private static string Socket_IP { get; set; }
        private string DownloadFilename { get; set; }

        public enum Command : int
        {
            ReceiveFile, SendMessage, undefined = 9
        };

        public struct ClientData
        {
            public Command ClientCommand { get; set; }
            public string ClientTextData { get; set; }
        }

        // Create Log4Net ILog Object (Logfile)
        public static log4net.ILog WSLog = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public WebsocketServer(IWebSocketServerFactory webSocketServerFactory, IList<string> supportedSubProtocols = null)
        {
            // Get Host IP Adress
            Socket_IP = GetLocalIPAddress();

            // WebSocket
            _webSocketServerFactory = webSocketServerFactory;
            _supportedSubProtocols = new HashSet<string>(supportedSubProtocols ?? new string[0]);
        }

        private void ProcessTcpClient(TcpClient tcpClient)
        {
            Task.Run(() => ProcessTcpClientAsync(tcpClient));
        }

        private string GetSubProtocol(IList<string> requestedSubProtocols)
        {
            foreach (string subProtocol in requestedSubProtocols)
            {
                // match the first sub protocol that we support
                // (the client should pass the most preferable sub protocols first)
                if (_supportedSubProtocols.Contains(subProtocol))
                {
                    WSLog.Info($"<Message>: Http header has requested sub protocol {subProtocol} which is supported");

                    return subProtocol;
                }
            }
            if (requestedSubProtocols.Count > 0)
            {
                WSLog.Warn($"<Message>: Http header has requested the following sub protocols: " +
                    $"{string.Join(", ", requestedSubProtocols)}. There are no supported protocols configured that match.");
            }
            return null;
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            try
            {
                if (_isDisposed)
                {
                    return;
                }
                // this worker thread stays alive until either of the following happens:
                // Client sends a close conection request OR
                // An unhandled exception is thrown OR
                // The server is disposed
                WSLog.Info("<Server>: Connection opened. Reading Http header from stream");
                WSLog.Info("-----------------------------------------------------------");

                // get a secure or insecure stream
                Stream stream = tcpClient.GetStream();
                WebSocketHttpContext context = await _webSocketServerFactory.ReadHttpHeaderFromStreamAsync(stream);
                if (context.IsWebSocketRequest)
                {
                    string subProtocol = GetSubProtocol(context.WebSocketRequestedProtocols);
                    var options = new WebSocketServerOptions() { KeepAliveInterval = TimeSpan.FromSeconds(30), SubProtocol = subProtocol };
                    WSLog.Info("<Message>: Http header has requested an upgrade to Web Socket protocol. Negotiating Web Socket handshake");

                    WebSocket webSocket = await _webSocketServerFactory.AcceptWebSocketAsync(context, options);

                    WSLog.Info("<Message>: Web Socket handshake response sent. Stream ready.");
                    await RespondToWebSocketRequestAsync(webSocket, source.Token);
                }
                else
                {
                    WSLog.Info("<Message>: Http header contains no web socket upgrade request. Ignoring");
                }

                // closed connection
                WSLog.Info("-----------------------------------------------------------");
                WSLog.Info("<Server>: Connection closed.");
            }
            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                WSLog.Error(ex.ToString());
            }
            finally
            {
                try
                {
                    tcpClient.Client.Close();
                    tcpClient.Close();
                    source.Cancel();
                }
                catch (Exception ex)
                {
                    WSLog.Error($"<Error>: failed to close TCP connection: {ex}");
                }
            }
        }

        public async Task RespondToWebSocketRequestAsync(WebSocket webSocket, CancellationToken token)
        {
            // prepare Buffer - Length 512MB
            int bufferLen = 512 * 1024 * 1024;
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[bufferLen]);

            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    // reading data from stream
                    result = await webSocket.ReceiveAsync(buffer, token);
                } catch(Exception e)
                {
                    WSLog.Warn(e.Message);
                    break;
                }
                // print message type
                //WSLog.Info("<CloseStatus>: " + result.CloseStatus);
                //WSLog.Info("<Description>: " + result.CloseStatusDescription);

                // client connection close
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    WSLog.Info($"<Client>: initiated close. Status: {result.CloseStatus}"
                        + " Description: {result.CloseStatusDescription}");
                    break;
                }

                // message exceeded buffer size (512MB)
                if (result.Count > bufferLen)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig,
                        $"<Error>: Web socket frame cannot exceed buffer size "
                        + "of {bufferLen:#,##0} bytes. Send multiple frames instead.", token);
                    break;
                }

                // message type text
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    WSLog.Info("<TextData>: start processing...");
                    string msg = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, result.Count);
                    await ProcessTextMessage(msg, webSocket, token);
                }

                // message type binary
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    WSLog.Info("<BinaryData>: start processing...");
                    await ProcessBinaryData(buffer, result, webSocket, token);
                }
            }
        }

        private async Task ProcessTextMessage(string msg,
            WebSocket webSocket, CancellationToken token)
        {
            WSLog.Info("<TextData>: {" + msg + "}");
            try
            {
                // Split message and analyse
                string[] msgarray = msg.Split(';');
                int msglength = msgarray.Length;
                int msgcom = int.Parse(msgarray[0]);
                ClientData cData = new ClientData
                {
                    ClientCommand = Command.undefined,
                    ClientTextData = msgarray[1]
                };

                // Check command type
                switch (msgcom)
                {
                    case 0:
                        cData.ClientCommand = Command.ReceiveFile;
                        break;

                    case 1:
                        cData.ClientCommand = Command.SendMessage;
                        break;

                    default:
                        cData.ClientCommand = Command.undefined;
                        break;
                }
                // processing client command
                await ProcessCommand(cData, webSocket, token);
            }
            catch (Exception ex)
            {
                WSLog.Error(ex.Message);
            }
        }

        /// <summary>
        /// process client command
        /// </summary>
        /// <param name="command"></param>
        /// <param name="webSocket"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task ProcessCommand(ClientData cd, WebSocket webSocket, CancellationToken token)
        {
            if (cd.ClientCommand.Equals(Command.undefined))
            {
                // Unknown command ...
                await SendMessage("9;undefined command", webSocket, token);
                return;
            }

            switch (cd.ClientCommand)
            {
                case Command.ReceiveFile:
                    WSLog.Info("Processing... [ " + cd.ClientTextData + " ]");
                    DownloadFilename = Path.GetFileName(cd.ClientTextData).Trim();
                    await SendMessage("0;received binary data command", webSocket, token);
                    break;
                    
                case Command.SendMessage:
                    WSLog.Info("Received Message: [ " + cd.ClientTextData + " ]");
                    await SendMessage("1;received send message command", webSocket, token);
                    break;
            }
        }

        private async Task ProcessBinaryData(ArraySegment<byte> buffer,
            WebSocketReceiveResult webResult, WebSocket webSocket, CancellationToken token)
        {
            var allBytes = new List<byte>();
            for (int i = 0; i < webResult.Count; i++)
            {
                allBytes.Add(buffer.Array[i]);
            }
            if (DownloadFilename != "")
            {
                WSLog.Info("Receiving and saving: " + DownloadFilename);
                // write file to disk
                SaveBinaryFile(Environment.CurrentDirectory + "\\downloads\\" + DownloadFilename, allBytes);
                // Send transaction finished
                await SendTransactionFinished(webSocket, token);
            }
        }

        /// <summary>
        /// Send File Transaction finished
        /// </summary>
        /// <param name="webSocket"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task SendTransactionFinished(WebSocket webSocket, CancellationToken token)
        {
            string fin_msg = "0;File transfer successfully completed";
            await SendMessage(fin_msg, webSocket, token);
        }

        /// <summary>
        /// Send Answer to Client
        /// </summary>
        /// <param name="message"></param>
        /// <param name="webSocket"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task SendMessage(string message, WebSocket webSocket, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            ArraySegment<byte> toSend = new ArraySegment<byte>(bytes, 0, bytes.Length);
            await webSocket.SendAsync(toSend, WebSocketMessageType.Text, true, token);
        }

        /// <summary>
        /// writes a List<byte> to filesystem
        /// </summary>
        /// <param name="FullFileName"></param>
        /// <param name="binary"></param>
        /// <param name="overwrite"></param>
        /// <returns></returns>
        private void SaveBinaryFile(string FullFileName, List<byte> binary)
        {
            if (Directory.Exists(Path.GetDirectoryName(FullFileName)).Equals(false))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FullFileName));
            }
            if (File.Exists(FullFileName).Equals(true))
            {
                if (true) { return; }
            }
            using (BinaryWriter writer = new BinaryWriter(File.Open(FullFileName, FileMode.Create)))
            {
                writer.Write(binary.ToArray());
                writer.Flush();
                writer.Close();
            }
        }

        /// <summary>
        /// Get local IP from host system
        /// </summary>
        /// <returns></returns>
        public static string GetLocalIPAddress()
        {
            string local = "0.0.0.0";
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    local = ip.ToString();
                }
            }
            if ((local != "0.0.0.0") && (local != ""))
            {
                return local;
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        /// <summary>
        /// Checks if directory exists and creates if not...
        /// </summary>
        /// <param name="directory"></param>
        private static void CheckDirectory(string directory)
        {
            if (Directory.Exists(directory).Equals(true))
            {
                Directory.Delete(directory, true);
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Starts asyncronous webSocket
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public async Task Listen()
        {
            try
            {
                //IPAddress ipaddress = IPAddress.Parse(ip);  //127.0.0.1 as an example
                IPAddress ipaddress = IPAddress.Any;
                _listener = new TcpListener(ipaddress, Program.WebsocketPort);
                _listener.Start();
                WSLog.Info($"<Server>: listening on Port {Program.WebsocketPort}");
                while (true)
                {
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync();
                    ProcessTcpClient(tcpClient);
                }
            }
            catch (SocketException ex)
            {
                string message = string.Format("<Error>: listening on port {0}. Make sure IIS "
                    + "or another application is not running and consuming your port.", Program.WebsocketPort);
                throw new Exception(message, ex);
            }
        }

        /// <summary>
        /// Shutdown webSocket listener
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                // safely attempt to shut down the listener
                try
                {
                    if (_listener != null)
                    {
                        if (_listener.Server != null)
                        {
                            _listener.Server.Close();
                        }

                        _listener.Stop();
                    }
                }
                catch (Exception ex)
                {
                    WSLog.Error(ex.ToString());
                }

                WSLog.Info("Web Server shutdown...");
            }
        }
    }
}