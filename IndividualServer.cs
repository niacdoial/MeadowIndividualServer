
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using RainMeadow.Shared;
namespace RainMeadow.IndividualServer
{
    static partial class IndividualServer
    {
        // networking
        [CommandLineArgument]
        public static ushort port = 8720;

        [CommandLineArgument]
        public static ulong heartbeatTime = 50;

        [CommandLineArgument]
        public static ulong timeoutTime = 3000;

        // lobby info
        [CommandLineArgument]
        public static int maxplayers = 4;

        [CommandLineArgument]
        public static bool passwordprotected = false;

        [CommandLineArgument]
        public static string name = "Router Lobby";

        [CommandLineArgument]
        public static string mode = "Meadow";

        [CommandLineArgument]
        public static string mods = "";

        [CommandLineArgument]
        public static string bannedMods = "";

        [CommandLineArgument]
        public static bool gameLift = false;


        public static bool ready = false;
        public static UDPPeerManager? peerManager = null;
        public static GameLiftManager? gameLiftManager = null;
        static void Main(string[] args)
        {

            SharedCodeLogger.DebugInner += RainMeadow.Debug;
            SharedCodeLogger.DebugMeInner += RainMeadow.DebugMe;
            SharedCodeLogger.ErrorInner += RainMeadow.Error;

            RainMeadow.Debug("Hello world!");
            try
            {
                CommandLineArgumentAttribute.InitializeCommandLine();


                peerManager = new(port, 10000);
                RainMeadow.Debug($"Hosting on port {peerManager.port}");
                // peerManager.OnPeerForgotten += x =>
                // {
                //     RainMeadow.Debug($"{x} was forgotten");
                // };

                SetupClientEvents();
            }
            catch (Exception except)
            {
                RainMeadow.Error(except);
                throw;
            }
            
            if (gameLift)
            {
                gameLiftManager = new GameLiftManager();
                gameLiftManager.ProcessSetup();
            }
            else 
            {
                ready = true;
            }



            // Main Lobby
            UpdateLoop();
        }

        

        static void UpdateLoop()
        {
            while (true)
            {
                if (!ready) continue;
                if (peerManager is null) throw new Exception("peerManager is null");
                peerManager.Update();
                if (peerManager.IsPacketAvailable() && peerManager.Recieve(out var sender) is byte[] data)
                {
                    try
                    {
                        if (sender is null) throw new InvalidProgrammerException("sender is null");
                        using (MemoryStream stream = new(data))
                        using (BinaryReader reader = new(stream))
                        {
                            Packet.Decode(reader, (IPEndPoint)sender);
                        }
                    }
                    catch (Exception except)
                    {
                        RainMeadow.Error(except);
                        RainMeadow.Stacktrace();
                    }
                }
            }
        }
    }
}
