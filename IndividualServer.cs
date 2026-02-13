
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using RainMeadow.Shared;
using Sodium;

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

        static SecuredPeerManager? peerManager = null;
        static void Main(string[] args)
        {

            SharedCodeLogger.DebugInner += RainMeadow.Debug;
            SharedCodeLogger.DebugMeInner += RainMeadow.DebugMe;
            SharedCodeLogger.ErrorInner += RainMeadow.Error;

            RainMeadow.Debug("Hello world!");
            try
            {
                CommandLineArgumentAttribute.InitializeCommandLine();


                peerManager = new SecuredPeerManager(port, 10000);
                RainMeadow.Debug($"Direct connect address is {peerManager.Me.ToString(false)}");
                SetupClientEvents();
            }
            catch (Exception except)
            {
                RainMeadow.Error(except);
                throw;
            }

            // Todo: Alert matchmaking server that we've successfully started listening on a new port

            // Main Lobby
            while (true)
            {

                peerManager.Update();
                if (peerManager.Receive(out SecuredPeerId? sender, out bool boxed, false) is byte[] data)
                {
                    try
                    {
                        if (sender is null) throw new InvalidProgrammerException("sender is null");
                        using (MemoryStream stream = new(data))
                        using (BinaryReader reader = new(stream))
                        {
                            Packet.Decode(reader, sender, peerManager.Me, boxed);
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
