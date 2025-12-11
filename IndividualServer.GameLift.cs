
using Aws.GameLift;
using Aws.GameLift.Server;
using Aws.GameLift.Server.Model;

namespace RainMeadow.IndividualServer
{
    class GameLiftManager
    {
        public void ProcessSetup()
        {
            GenericOutcome err = GameLiftServerAPI.InitSDK(new ServerParameters());
            RainMeadow.Debug(GameLiftServerAPI.GetSdkVersion().Result);
            if (!err.Success) throw new System.Exception("AWS GameLift: " + err.Error.ErrorMessage);
            err = GameLiftServerAPI.ProcessReady(new ProcessParameters(
                OnStartGameSession, 
                OnUpdateGameSession, 
                OnProcessTerminate, 
                OnHealthCheck, 
                IndividualServer.peerManager.port, new LogParameters()));
            if (!err.Success) throw new System.Exception("AWS GameLift: " + err.Error.ErrorMessage);

            IndividualServer.ready = false;
        }

        private void OnStartGameSession(GameSession gameSession)
        {
            IndividualServer.maxplayers = gameSession.MaximumPlayerSessionCount;
            gameSession.GameProperties.TryGetValue("mods", out IndividualServer.mods);
            gameSession.GameProperties.TryGetValue("banned_mods", out IndividualServer.bannedMods);
            gameSession.GameProperties.TryGetValue("name", out IndividualServer.name);
            gameSession.GameProperties.TryGetValue("mode", out IndividualServer.mode);
            if (gameSession.GameProperties.ContainsKey("password_protected"))
            {
                bool.TryParse(gameSession.GameProperties["password_protected"], out IndividualServer.passwordprotected);
            }
            IndividualServer.ready = true;
            GenericOutcome err = GameLiftServerAPI.ActivateGameSession();
            if (!err.Success)
            {
                throw new System.Exception("AWS GameLift: " + err.Error.ErrorMessage);
            }
        }

        public void OnUpdateGameSession(UpdateGameSession updateGameSession)
        {
            
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
