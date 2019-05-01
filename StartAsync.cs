using Ninja.WebSockets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebsocketCore
{
    public class StartAsync
    {
        // Create Log4Net ILog Object (Logfile)
        private static log4net.ILog AsyncLog = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static IWebSocketServerFactory _webSocketServerFactory;

        public static void Run()
        {
            try
            {
                // Start Websocket Task
                _webSocketServerFactory = new WebSocketServerFactory();
                Task task = Task.Run(() => StartWebServer());
                task.Wait();
            }
            catch (Exception ex)
            {
                AsyncLog.Error(ex.ToString());
                AsyncLog.Error("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static async Task StartWebServer()
        {
            try
            {
                // List of supported websocket protocols
                IList<string> supportedSubProtocols = new string[] { "tccs" };

                using (WebsocketCore server = new WebsocketCore(_webSocketServerFactory, supportedSubProtocols))
                {
                    // Start Websocket listener
                    await server.Listen();
                    AsyncLog.Info("Press any key to quit");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                AsyncLog.Error(ex.ToString());
                AsyncLog.Error("Press any key to quit");
                Console.ReadKey();
            }
        }
    }
}