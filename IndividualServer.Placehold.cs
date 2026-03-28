using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using RainMeadow.Shared;
using RainMeadow.Shared.Models;

namespace RainMeadow.IndividualServer
{
    static partial class IndividualServer
    {
        static void PublishRouterLobby_ProcessAction(PublishRouterLobby packet)
        {
            if (clients.Any(x => x.PeerID == packet.processingPeer)) return;

            if (clients.Count() != 0) {
                RainMeadow.Debug("Cannot submit a lobby if the server isn't empty!");
                return;
            }

            PlayerInfo plInfo = new PlayerInfo()
            {
                username = packet.userName,
                sub=null, publicKey=null,
                IsDev=false, IsTrustedCommunity=false,
                CapeEntry=null
            };

            clients.Add(new Client(packet.processingPeer, 1, packet.exposeIPAddress, false, plInfo, packet.userName));

            // TODO: protection against double-sending? or do we trust the host not to jank this up?
            RainMeadow.Debug("Received new lobby");
            if (packet.lobbyParameters.MaxPlayers <= 0) {
                RainMeadow.Error("published lobby: bad maxplayers");
            }
            Name = packet.lobbyName;
            lobbyParameters = packet.lobbyParameters;
        }

        static void PrintRemainingClients()
        {
            RainMeadow.Debug("Client list:");
            foreach (Client client in clients)
            {
                RainMeadow.Debug(client);
            }
            // RainMeadow.Debug("Prospective client list:");
            // foreach (Client client in prospectiveClients) {
            //     RainMeadow.Debug(String.Format("  ID: {0}, endPoint: {1}, name: {2}", client.routerID, SharedPlatform.PlatformPeerManager.describePeerId(client.endPoint), client.name));
            // }
        }
    }
}
