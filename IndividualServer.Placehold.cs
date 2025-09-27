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
            var thisClient = clients.FirstOrDefault(x => UDPPeerManager.CompareIPEndpoints(x.endPoint, packet.processingEndpoint));
            if (thisClient == null) {
                RainMeadow.Debug("PublishLobby packet sent from an unauthorized party");
                return;
            }
            if (clients.Count() ==1 && thisClient.routerID ==1)
            {
                // TODO: protection against double-sending? or do we trust the host not to jank this up?
                RainMeadow.Debug("Received new lobby");
                if (packet.maxplayers > 0) {
                    maxplayers = packet.maxplayers;
                } else {
                    RainMeadow.Error("published lobby: bad maxplayers");
                }
                name = packet.name;
                mode = packet.mode;
                passwordprotected = packet.passwordprotected;
                mods = packet.mods;
                bannedMods = packet.bannedMods;
            }
        }

        static void PrintRemainingClients()
        {
            RainMeadow.Debug("Client list:");
            foreach (Client client in clients) {
                RainMeadow.Debug(String.Format("  ID: {0}, endPoint: {1}, name: {2}", client.routerID, client.endPoint, client.name));
            }
        }
    }
}
