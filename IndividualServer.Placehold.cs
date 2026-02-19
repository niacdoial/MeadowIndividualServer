using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using RainMeadow.Shared;

namespace RainMeadow.IndividualServer
{
    static partial class IndividualServer
    {
        static void PublishRouterLobby_ProcessAction(PublishRouterLobby packet)
        {
            if (clients.Any(x => x.endPoint == packet.processingPeer)) return;
            if (prospectiveClients.Any(x => x.endPoint == packet.processingPeer)) return;

            if (clients.Count() != 0) {
                RainMeadow.Debug("Cannot submit a lobby if the server isn't empty!");
                return;
            }

            clients.Add(new Client(packet.processingPeer, 1, packet.exposeIPAddress, packet.name));

            // TODO: protection against double-sending? or do we trust the host not to jank this up?
            RainMeadow.Debug("Received new lobby");
            if (packet.lobbyParameters.MaxPlayers <= 0) {
                RainMeadow.Error("published lobby: bad maxplayers");
            }
            name = packet.name;
            lobbyParams = packet.lobbyParameters;
        }

        static void PrintRemainingClients()
        {
            RainMeadow.Debug("Client list:");
            foreach (Client client in clients) {
                // TODO: undoxx
                RainMeadow.Debug(String.Format("  ID: {0}, endPoint: {1}, name: {2}", client.routerID, client.endPoint.ToString(), client.info.username));
            }
            RainMeadow.Debug("Prospective client list:");
            foreach (Client client in prospectiveClients) {
                RainMeadow.Debug(String.Format("  ID: {0}, endPoint: {1}, name: {2}", client.routerID, client.endPoint.ToString(), client.info.username));
            }
        }
    }
}
