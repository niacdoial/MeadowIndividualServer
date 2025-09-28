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
        static List<Client> clients = new();
        class Client
        {
            public readonly ushort routerID;
            public readonly IPEndPoint endPoint;
            public IPEndPoint publicEndPoint { get { if (exposeIPAddress) return endPoint; else return SharedPlatform.BlackHole; } }
            public readonly string name;
            public readonly bool exposeIPAddress;
            public Client(IPEndPoint endPoint, ushort routerID, bool exposeIPAddress, string name="")
            {
                RainMeadow.Debug($"New client: {routerID}, {name}");
                this.routerID = routerID;
                this.endPoint = endPoint;
                this.name = name;
                this.exposeIPAddress = exposeIPAddress;

                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                clients.Add(this);
                peerManager.GetRemotePeer(endPoint, true); // create remotePeer
                peerManager.OnPeerForgotten += OnPeerForgotten;  // TODO doesn't this accumulate?
            }

            private void OnPeerForgotten(IPEndPoint forgottenEndPoint)
            {
                if (this.endPoint == forgottenEndPoint) RemoveClient();
            }

            public void RemoveClient()
            {
                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                peerManager.OnPeerForgotten -= OnPeerForgotten;

                if (clients.Contains(this))
                {
                    RainMeadow.Debug($"Removing client: {routerID}");
                    clients.Remove(this);

                    var removalPacket = new RouterModifyPlayerListPacket(RouterModifyPlayerListPacket.Operation.Remove, new List<ushort> { routerID });
                    Send(removalPacket, UDPPeerManager.PacketType.Reliable); // TODO: why is this needed?
                    foreach (Client client in clients)
                    {
                        client.Send(removalPacket, UDPPeerManager.PacketType.Reliable);
                    }
                    peerManager.ForgetPeer(endPoint);
                    PrintRemainingClients();
                }
            }

            public void Send(Packet packet, UDPPeerManager.PacketType type, bool start_conversation = false)
            {
                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                using (MemoryStream stream = new())
                using (BinaryWriter writer = new(stream))
                {
                    Packet.Encode(packet, writer, endPoint);
                    peerManager.Send(stream.GetBuffer(), endPoint, type, start_conversation);
                }
            }
        }


        static void SetupClientEvents()
        {
            Packet.packetFactory += Packet.RouterFactory;
            BeginRouterSession.ProcessAction += BeginRouterSession_ProcessAction;
            RouteSessionData.ProcessAction += RouteSessionData_ProcessAction;
            PublishRouterLobby.ProcessAction += PublishRouterLobby_ProcessAction;
            EndRouterSession.ProcessAction += EndRouterSession_ProcessAction;
            RouterChatMessage.ProcessAction += ChatMessage_ProcessAction;
            RouterCustomPacket.ProcessAction += RouterCustomPacket_ProcessAction;
        }

        static bool CheckSender(IPEndPoint processingEndpoint, ushort fromRouterID)
        {
            var actualSrcRouterID = clients.FirstOrDefault(x => UDPPeerManager.CompareIPEndpoints(processingEndpoint, x.endPoint)).routerID;
            if (actualSrcRouterID == 0) {
                RainMeadow.Error("Impersonation attempt! unknown sender disguised as " + fromRouterID.ToString());
                return false;
            } else if (fromRouterID != actualSrcRouterID) {
                RainMeadow.Error("Impersonation attempt! probably-player-"+ actualSrcRouterID.ToString() + " disguised as " + fromRouterID.ToString());
                return false;
            }
            return true;
        }

        static void RouteSessionData_ProcessAction(RouteSessionData packet)
        {
            if (!CheckSender(packet.processingEndpoint, packet.fromRouterID)) { return; }
            var destinationClient = clients.FirstOrDefault(x => x.routerID == packet.toRouterID);
            if (destinationClient == null) {
                RainMeadow.Error("received packet for departed client " + packet.toRouterID.ToString());
                return;
            }
            destinationClient.Send(packet, UDPPeerManager.PacketType.Unreliable);
        }

        static void RouterCustomPacket_ProcessAction(RouterCustomPacket packet)
        {
            if (!CheckSender(packet.processingEndpoint, packet.fromRouterID)) { return; }
            var destinationClient = clients.FirstOrDefault(x => x.routerID == packet.toRouterID);
            if (destinationClient == null) {
                RainMeadow.Error("received packet for departed client " + packet.toRouterID.ToString());
                return;
            }
            destinationClient.Send(packet, UDPPeerManager.PacketType.Unreliable);
        }

        static void EndRouterSession_ProcessAction(EndRouterSession packet)
        {
            var self = clients.FirstOrDefault(x => UDPPeerManager.CompareIPEndpoints(x.endPoint, packet.processingEndpoint));
            if (self == null) {
                RainMeadow.Error("Client that's not there wishes to leave...");
                return;
            }
            RainMeadow.Debug("Client leaving...");
            self.RemoveClient();
        }

        const ushort nullrouterID = 0;
        static void BeginRouterSession_ProcessAction(BeginRouterSession packet)
        {
            if (clients.Any(x => x.endPoint == packet.processingEndpoint)) return;

            if (clients.Count() == 0) {
                RainMeadow.Debug("first player! let them be host");
                var hostClient = new Client(packet.processingEndpoint, 1, packet.exposeIPAddress, packet.name);
                hostClient.Send(new LobbyIsEmpty(), UDPPeerManager.PacketType.Reliable);
                hostClient.Send(new RouterModifyPlayerListPacket(
                    RouterModifyPlayerListPacket.Operation.Add,
                    clients.Select(x => x.routerID).ToList(),
                    clients.Select(x => x.publicEndPoint).ToList(),
                    clients.Select(x => x.name).ToList()
                    ),
                    UDPPeerManager.PacketType.Reliable
                );
                return;
            }

            var usedIDs = clients.Select(x => x.routerID);
            ushort id = 1;

            if (usedIDs.Any())
            {
                // let's make things more straightforward in case people leave and others join:
                // let's avoid clients mixing different clients because of a re-used ID
                // (maybe reintroduce this allocation optimisation later)
                id = (ushort)(usedIDs.Max() + 1);
                // try
                // {
                //     var possibleIDs = usedIDs.Select(x => (ushort)(x + 1));
                //     id = possibleIDs.Except(usedIDs).Where(x => x != 0).First();
                // }
                // catch (InvalidOperationException exception)
                // {
                //     RainMeadow.Error(exception);
                //     return;
                // }
            }

            if (id == nullrouterID)
            {
                RainMeadow.Error("No available RouterIDs");
                return;
            }

            var newClient = new Client(packet.processingEndpoint, id, packet.exposeIPAddress, packet.name);

            var notifyPacket = new RouterModifyPlayerListPacket(
                RouterModifyPlayerListPacket.Operation.Add,
                new List<ushort> { id },
                new List<IPEndPoint> { newClient.publicEndPoint },
                new List<string> { packet.name }
            );
            foreach (Client client in clients)
            {
                if (newClient == client)
                {
                    client.Send(new RouterModifyPlayerListPacket(
                        RouterModifyPlayerListPacket.Operation.Add,
                        clients.Select(x => x.routerID).ToList(),
                        clients.Select(x => x.publicEndPoint).ToList(),
                        clients.Select(x => x.name).ToList()
                        ),
                        UDPPeerManager.PacketType.Reliable);
                }
                else
                {
                    client.Send(notifyPacket, UDPPeerManager.PacketType.Reliable);
                }
            }

            newClient.Send(new JoinRouterLobby(id, maxplayers, name, passwordprotected, mode, mods, bannedMods), UDPPeerManager.PacketType.Reliable);
            PrintRemainingClients();
        }

        static void ChatMessage_ProcessAction(RouterChatMessage packet)
        {
            if (!CheckSender(packet.processingEndpoint, packet.fromRouterID)) { return; }

            foreach (Client client in clients)
            {
                if (client.exposeIPAddress || client.routerID == packet.fromRouterID) { continue; }
                client.Send(packet, UDPPeerManager.PacketType.Reliable);
            }
        }
    }

}
