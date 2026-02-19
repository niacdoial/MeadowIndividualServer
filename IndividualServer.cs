
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using RainMeadow.Shared;
using RainMeadow.Shared.Models;
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
        public static LobbyParameters lobbyParams = new();

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
                lobbyParams.MaxPlayers = maxplayers;
                lobbyParams.PasswordProtected = passwordprotected;
                lobbyParams.Mode = mode;
                lobbyParams.Mods = mods;
                lobbyParams.BannedMods = bannedMods;


                peerManager = new SecuredPeerManager(port, 10000);
                // TODO: redo this
                //peerManager.allowPeerCreationWithoutKey = false;  // status-unknown peers are only allowed for LAN
                if (peerManager is SecuredPeerManager sPMan) {
                    RainMeadow.Debug($"Direct connect address is {sPMan.ToString()} (note the IP may need to be edited)");
                } else {
                    RainMeadow.Debug($"Direct connect address is X.X.X.X:{peerManager.port} where the IP has to be completed");
                }

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

            RainMeadow.Debug(Stopwatch.Frequency);

            // Todo: Alert matchmaking server that we've successfully started listening on a new port

            // Main Lobby
            while (true)
            {

                peerManager.Update();
                if (peerManager.Receive(out var sender, out var boxed, true) is byte[] data)
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
