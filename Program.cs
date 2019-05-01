﻿using System;
using System.IO;
using System.Xml;
using System.Reflection;
using System.Threading.Tasks;

namespace WebsocketCore
{
    //   _    _      _                    _        _     _____                
    //  | |  | |    | |                  | |      | |   /  __ \               
    //  | |  | | ___| |__  ___  ___   ___| | _____| |_  | /  \/ ___  _ __ ___ 
    //  | |/\| |/ _ \ '_ \/ __|/ _ \ / __| |/ / _ \ __| | |    / _ \| '__/ _ \
    //  \  /\  /  __/ |_) \__ \ (_) | (__|   <  __/ |_  | \__/\ (_) | | |  __/
    //   \/  \/ \___|_.__/|___/\___/ \___|_|\_\___|\__|  \____/\___/|_|  \___|
    //   ---------------------------------------------   --------------------    
    class Program
    {
        public static int WebsocketPort { get; set; }
        public static int BufferSize { get; set; }

        private static log4net.ILog ProgLog = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {
            // Load log4net.config file with logging settings
            var logRepository = log4net.LogManager.GetRepository(Assembly.GetEntryAssembly());
            log4net.Config.XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            ProgLog.Info("Starting Websocket Core ...");

            // Get command line parameters
            var command = Args.Configuration.Configure<ConsoleArgs>().CreateAndBind(args);
            WebsocketPort = command.Port;
            BufferSize = command.BufferSize;

            // if HELP show options
            if (command.Help)
            {
                ProgLog.Info("Websocket Server CommandLine Help");
                ProgLog.Info("-----------------------------------------------------");
                ProgLog.Info("\t/h\tShow this Help Info");
                ProgLog.Info("\t/p\tWebsocket Port");
                ProgLog.Info("\t/b\tBuffer Size (optional)");
                ProgLog.Info("\t/d\tDebug Mode (optional)");
                ProgLog.Info("-----------------------------------------------------");
                ProgLog.Info("\texample: webrocket.exe /a 192.168.0.1 /p 9000 /b 512");
                ProgLog.Info("-----------------------------------------------------");
            }            

            try
            {
                // Start WebSocket Server
                Task task = Task.Run(() => StartAsync.Run());
                task.Wait();
            }
            catch (Exception e)
            {
                ProgLog.Error(e.Message);
            }
        }
    }
}
