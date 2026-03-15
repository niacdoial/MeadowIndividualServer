
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aws.GameLift;
using Aws.GameLift.Server;
using Aws.GameLift.Server.Model;
using RainMeadow.Shared.Models;

namespace RainMeadow.IndividualServer
{
    class GameLiftManager
    {
        public string? gameSessionID = null;
        public void ProcessSetup()
        {
            GenericOutcome err = GameLiftServerAPI.InitSDK(new ServerParameters());
            RainMeadow.Debug(GameLiftServerAPI.GetSdkVersion().Result);
            if (!err.Success) throw new Exception("AWS GameLift: " + err.Error.ErrorMessage);
            IndividualServer.readyLock.Reset();
            err = GameLiftServerAPI.ProcessReady(new ProcessParameters(
                OnStartGameSession, 
                OnUpdateGameSession, 
                OnProcessTerminate, 
                OnHealthCheck, 
                IndividualServer.peerManager?.port ?? throw new Exception("null peerManager"), new LogParameters()));
            if (!err.Success) throw new Exception("AWS GameLift: " + err.Error.ErrorMessage);
            IndividualServer.OnExit += () => GameLiftServerAPI.ProcessEnding();
        }

        public bool AcceptPlayerSession(string session)
        {
            return GameLiftServerAPI.AcceptPlayerSession(session).Success;
        }

        public void RemovePlayerSession(string session)
        {
            GameLiftServerAPI.RemovePlayerSession(session);
        }

        private void OnUpdateGameSession(UpdateGameSession updateGameSession)
        {
            gameSessionID = updateGameSession.GameSession.GameSessionId;
        }

        private void OnStartGameSession(GameSession gameSession)
        {
            IndividualServer.peerManager!.AcceptNewConnections = true;
            IndividualServer.lobbyParameters = new LobbyParameters(gameSession.GameProperties); 
            IndividualServer.lobbyParameters.MaxPlayers = gameSession.MaximumPlayerSessionCount;
            GenericOutcome err = GameLiftServerAPI.ActivateGameSession();
            if (!err.Success)
            {
                throw new Exception("AWS GameLift: " + err.Error.ErrorMessage);
            }
            gameSessionID = gameSession.GameSessionId;
            IndividualServer.readyLock.Set();
        }

        public PlayerInfo GetPlayerSession(string playerSessionID)
        {
            var describePlayerSessionsRequest = new DescribePlayerSessionsRequest(){
                GameSessionId = gameSessionID,  
                PlayerSessionId = playerSessionID
            };

            DescribePlayerSessionsOutcome outcome = GameLiftServerAPI.DescribePlayerSessions(describePlayerSessionsRequest);
            if (!outcome.Success) throw new Exception("AWS GameLift: " + outcome.Error);
            return JsonSerializer.Deserialize<PlayerInfo>(playerSessionID) ?? throw new Exception($"Couldn't Deserialize Player Data for {playerSessionID}");
        }

        public void OnProcessTerminate()
        {
            AwsDateTimeOutcome outcome = GameLiftServerAPI.GetTerminationTime();
            if (outcome.Success)
            {
                TimeSpan time = DateTime.Now - outcome.Result;
                Task.Run(async () =>
                {
                    await Task.Delay(time.Subtract(TimeSpan.FromSeconds(10)));
                    if (IndividualServer.peerManager is not null)
                    {
                        lock (IndividualServer.peerManager)
                        {
                            IndividualServer.ShutDown();
                        }
                    }
                });
                
            }
        }

        public bool OnHealthCheck()
        {
            return true;
        }
    }

}
