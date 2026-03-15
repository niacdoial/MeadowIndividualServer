
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using RainMeadow.Shared;
using RainMeadow.Shared.Models;
using Sodium;

namespace RainMeadow.IndividualServer
{
    
    static partial class IndividualServer
    {
        
        public static string Name = "Router Lobby";
        public static GameLiftManager? gameLift;
        public static SecuredPeerManager? peerManager = null;
        public static LobbyParameters? lobbyParameters;
        public static readonly EventWaitHandle readyLock = new EventWaitHandle(false, EventResetMode.ManualReset);

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
                readyLock.Set();
            }

            Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs args) => 
            {
                ShutDown();
                args.Cancel = true;
            };

            // Main Lobby
            try
            {
                UpdateLoop();
            }
            catch (Exception except)
            {
                RainMeadow.Error(except);
            }
            OnExit.Invoke();
        }

        // Shuts Down Connections, Exit the program manually. 
        public static void ShutDown()
        {
            RainMeadow.Debug("Shutting down server");
            if (peerManager is not null)
            {
                peerManager.AcceptNewConnections = false;
                peerManager.TerminateAllPeers("Server shutting down...");
            }
        }

        public static event Action OnExit = delegate { };
        

        static void UpdateLoop()
        {
            if (peerManager is null) throw new Exception("peerManager is null");
            while (peerManager.AcceptNewConnections || peerManager.AnyConnection)
            {
                readyLock.WaitOne();
                peerManager.Update();
                if (peerManager.Receive(out SecuredPeerId? sender, out bool boxed, true) is byte[] data)
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
