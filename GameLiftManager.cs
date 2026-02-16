
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
            err = GameLiftServerAPI.ProcessReady(new ProcessParameters(
                OnStartGameSession, 
                OnUpdateGameSession, 
                OnProcessTerminate, 
                OnHealthCheck, 
                IndividualServer.peerManager?.port ?? throw new Exception("null peerManager"), new LogParameters()));
            if (!err.Success) throw new Exception("AWS GameLift: " + err.Error.ErrorMessage);

            IndividualServer.ready = false;
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
            IndividualServer.lobbyParameters = new LobbyParameters(gameSession.GameProperties); 
            IndividualServer.lobbyParameters.MaxPlayers = gameSession.MaximumPlayerSessionCount;
            IndividualServer.ready = true;
            GenericOutcome err = GameLiftServerAPI.ActivateGameSession();
            if (!err.Success)
            {
                throw new Exception("AWS GameLift: " + err.Error.ErrorMessage);
            }
            gameSessionID = gameSession.GameSessionId;
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
            IndividualServer.ready = false;
        }

        public bool OnHealthCheck()
        {
            return true;
        }
    }

}
