
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
        public static string Name = "Router Lobby";
        public static GameLiftManager? gameLift;
        public static bool ready = false;
        public static SecuredPeerManager? peerManager = null;
        public static LobbyParameters? lobbyParameters;

        static void Main(string[] args)
        {
            

            SharedCodeLogger.DebugInner += RainMeadow.Debug;
            SharedCodeLogger.DebugMeInner += RainMeadow.DebugMe;
            SharedCodeLogger.ErrorInner += RainMeadow.Error;

            RainMeadow.Debug("Hello world!");
            try
            {
                CommandLineArgumentAttribute.InitializeCommandLine();
                peerManager = new SecuredPeerManager(CommandLineArguments.port, 10000);
                RainMeadow.Debug($"Direct connect address is {peerManager.Me.ToString(false)}");
                SetupClientEvents();
            }
            catch (Exception except)
            {
                RainMeadow.Error(except);
                throw;
            }

            RainMeadow.Debug(Stopwatch.Frequency);

            if (CommandLineArguments.gameLift)
            {
                gameLift = new GameLiftManager();
                gameLift.ProcessSetup();
            }
            else
            {
                lobbyParameters = new LobbyParameters() { 
                    MaxPlayers = CommandLineArguments.maxplayers, 
                    PasswordProtected = CommandLineArguments.passwordprotected,
                    Mode = CommandLineArguments.mode,
                    BannedMods = CommandLineArguments.bannedMods, 
                    Mods = CommandLineArguments.mods};
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
