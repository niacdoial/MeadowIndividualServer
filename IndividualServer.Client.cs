using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using RainMeadow.Shared;
using RainMeadow.Shared.Models;
using Sodium;

namespace RainMeadow.IndividualServer
{
    static partial class IndividualServer
    {
        static List<Client> clients = new();
        static List<Client> prospectiveClients = new();

        public enum PacketReliability
        {
            Unreliable = 0,
            Reliable
        }

        class Client
        {
            public readonly ushort routerID;
            public readonly SecuredPeerId endPoint;
            public SecuredPeerId publicEndPoint { get { if (exposeIPAddress) return endPoint; else return null; } }
            public readonly PlayerInfo info;
            public readonly bool exposeIPAddress;
            public Client(SecuredPeerId endPoint, ushort routerID, bool exposeIPAddress, string name="")
            {
                RainMeadow.Debug($"New client: {routerID}, {name}");
                this.routerID = routerID;
                this.endPoint = endPoint;
                this.info = new PlayerInfo()
                {
                    username = name,
                    publicKey = LibSodium.BinToHex(endPoint.publicKey!),
                    sub = "// TODO understand this bullshit",
                };
                this.exposeIPAddress = exposeIPAddress;

                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                peerManager.GetRemotePeer(endPoint, true); // create remotePeer
                peerManager.OnPeerForgotten += OnPeerForgotten;
            }

            private void OnPeerForgotten(SecuredPeerManager.RemotePeer forgottenPeer)
            {
                if (this.endPoint == forgottenPeer.id) RemoveClient();
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
                    foreach (Client client in clients)
                    {
                        client.Send(removalPacket, PacketReliability.Reliable);
                    }
                    peerManager.ForgetPeer(endPoint);
                    PrintRemainingClients();
                }
                if (prospectiveClients.Contains(this))
                {
                    RainMeadow.Debug($"Removing prospective client: {routerID}");
                    prospectiveClients.Remove(this);

                    var removalPacket = new RouterModifyPlayerListPacket(RouterModifyPlayerListPacket.Operation.Remove, new List<ushort> { routerID });
                    clients[0].Send(removalPacket, PacketReliability.Reliable);
                    peerManager.ForgetPeer(endPoint);
                    PrintRemainingClients();
                }
            }

            public void Send(Packet packet, PacketReliability sendType, bool boxed = false)
            {
                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                SecuredPeerManager.PacketFlags reliable = sendType switch
                {
                    PacketReliability.Reliable => SecuredPeerManager.PacketFlags.Reliable,
                    PacketReliability.Unreliable => SecuredPeerManager.PacketFlags.Unreliable,
                };
                using (MemoryStream stream = new())
                using (BinaryWriter writer = new(stream))
                {
                    Packet.Encode(packet, writer, endPoint, IndividualServer.peerManager.Me);
                    peerManager.Send(stream.GetBuffer(), endPoint, reliable, packet.boxed);
                }
            }
        }


        static void SetupClientEvents()
        {
            Packet.packetFactory += Packet.RouterFactory;
            BeginRouterSession.ProcessAction += BeginRouterSession_ProcessAction;
            RouteSessionData.ProcessAction += RouteSessionData_ProcessAction;
            PublishRouterLobby.ProcessAction += PublishRouterLobby_ProcessAction;
            RouterChatMessage.ProcessAction += ChatMessage_ProcessAction;
            RouterCustomPacket.ProcessAction += RouterCustomPacket_ProcessAction;
            PlayerJoiningDecision.ProcessAction += PlayerJoiningDecision_ProcessAction;
        }

        static bool CheckSender(SecuredPeerId processingPeer, ushort fromRouterID, bool allowProsectiveClients = false)
        {
            var actualSrc = clients.FirstOrDefault(x => x.endPoint.CompareAndUpdate(processingPeer));
            if (actualSrc is null && allowProsectiveClients) {
                actualSrc = prospectiveClients.FirstOrDefault(x => x.endPoint.CompareAndUpdate(processingPeer));
            }

            if (actualSrc is null) {
                RainMeadow.Error("Impersonation attempt! unknown sender disguised as " + fromRouterID.ToString());
                return false;
            } else if (fromRouterID != actualSrc.routerID) {
                RainMeadow.Error("Impersonation attempt! probably-player-"+ actualSrc.routerID.ToString() + " disguised as " + fromRouterID.ToString());
                return false;
            }
            return true;
        }

        static void RouteSessionData_ProcessAction(RouteSessionData packet)
        {
            // reminder: prospective clients can only communicate with the lobby host
            var allowProsectiveClients = (packet.toRouterID == clients[0].routerID);
            if (!CheckSender(packet.processingPeer, packet.fromRouterID, allowProsectiveClients)) { return; }

            if (packet.fromRouterID == packet.toRouterID) {
                RainMeadow.Error("Client "+ packet.toRouterID.ToString() + " send a packet to themself");
            }
            var destinationClient = clients.FirstOrDefault(x => x.routerID == packet.toRouterID);
            if (destinationClient == null && packet.fromRouterID == clients[0].routerID) {
                destinationClient = prospectiveClients.FirstOrDefault(x => x.routerID == packet.toRouterID);
            }
            if (destinationClient == null) {
                RainMeadow.Error("received packet for departed client " + packet.toRouterID.ToString());
                return;
            }
            destinationClient.Send(packet, PacketReliability.Unreliable);
        }

        static void RouterCustomPacket_ProcessAction(RouterCustomPacket packet)
        {
            if (!CheckSender(packet.processingPeer, packet.fromRouterID)) { return; }

            if (packet.fromRouterID == packet.toRouterID) {
                RainMeadow.Error("Client "+ packet.toRouterID.ToString() + " send a custom packet to themself");
            }
            var destinationClient = clients.FirstOrDefault(x => x.routerID == packet.toRouterID);
            if (destinationClient == null) {
                RainMeadow.Error("received custom packet for departed client " + packet.toRouterID.ToString());
                return;
            }
            destinationClient.Send(packet, PacketReliability.Unreliable);
        }

        const ushort nullrouterID = 0;
        static void BeginRouterSession_ProcessAction(BeginRouterSession packet)
        {
            if (clients.Any(x => x.endPoint == packet.processingPeer)) return;
            if (prospectiveClients.Any(x => x.endPoint == packet.processingPeer)) return;

            if (clients.Count() == 0) {
                RainMeadow.Debug("first player must submit a lobby, not try to join one!");
                return;
            }

            var usedIDs = Enumerable.Concat(
                clients.Select(x => x.routerID),
                prospectiveClients.Select(x => x.routerID)
            );
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

            var newClient = new Client(packet.processingPeer, id, packet.exposeIPAddress, packet.name);
            prospectiveClients.Add(newClient);
            Client hostClient = clients[0];

            hostClient.Send(
                new RouterModifyPlayerListPacket(
                    RouterModifyPlayerListPacket.Operation.Add,
                    new List<ushort> { id },
                    new List<SecuredPeerId> { null },
                    new List<PlayerInfo> { new PlayerInfo(){username=packet.name,} }
                ),
                PacketReliability.Reliable
            );

            newClient.Send(
                new RouterModifyPlayerListPacket(
                    RouterModifyPlayerListPacket.Operation.Add,
                    new List<ushort> { hostClient.routerID, id },
                    new List<SecuredPeerId> { null, null },
                    new List<PlayerInfo> { new PlayerInfo(){username="HOST",}, new PlayerInfo(){username=packet.name,} }
                ),
                PacketReliability.Reliable
            );
            newClient.Send(new JoinRouterLobby(id, IndividualServer.name, IndividualServer.lobbyParams), PacketReliability.Reliable);
            PrintRemainingClients();
        }


        static void PlayerJoiningDecision_ProcessAction(PlayerJoiningDecision packet) {
            var newClient = prospectiveClients.FirstOrDefault(x => x.routerID == packet.player);
            if (newClient == null) {return;}

            // FIXME: TOCTOU race here: if somebody joins while the host is leaving
            var hostClient = clients[0];
            switch (packet.decision) {
            case PlayerJoiningDecision.Decision.Reject:
                newClient.Send(
                    new RouterModifyPlayerListPacket(
                        RouterModifyPlayerListPacket.Operation.Remove,
                        new List<ushort> { hostClient.routerID, newClient.routerID }
                    ),
                    PacketReliability.Reliable
                );
                newClient.RemoveClient();  // TODO: delay;
                return;
                break;

            case PlayerJoiningDecision.Decision.Accept:
                var pkt = new RouterModifyPlayerListPacket(
                    RouterModifyPlayerListPacket.Operation.Update,
                    new List<ushort> { hostClient.routerID, newClient.routerID },
                    new List<SecuredPeerId> { null, null },
                    new List<PlayerInfo> { hostClient.info, newClient.info }
                );
                if (newClient.exposeIPAddress) {
                    pkt.endPoints[0] = hostClient.publicEndPoint;
                }
                newClient.Send(pkt, PacketReliability.Reliable);

                pkt = new RouterModifyPlayerListPacket(
                    RouterModifyPlayerListPacket.Operation.Update,
                    new List<ushort> { newClient.routerID },
                    new List<SecuredPeerId> { null },
                    new List<PlayerInfo> { newClient.info }
                );

                if (hostClient.exposeIPAddress) {
                    pkt.endPoints[0] = newClient.publicEndPoint;
                }
                hostClient.Send(pkt, PacketReliability.Reliable);

                prospectiveClients.Remove(newClient);
                clients.Add(newClient);
                break;
            }


            var notifyPacketForUnproxied = new RouterModifyPlayerListPacket(
                RouterModifyPlayerListPacket.Operation.Add,
                new List<ushort> { newClient.routerID },
                new List<SecuredPeerId> { newClient.publicEndPoint },
                new List<PlayerInfo> { newClient.info }
            );
            var notifyPacketForProxied = new RouterModifyPlayerListPacket(
                RouterModifyPlayerListPacket.Operation.Add,
                new List<ushort> { newClient.routerID },
                new List<SecuredPeerId> { null },
                new List<PlayerInfo> { newClient.info }
            );

            foreach (Client client in clients)
            {
                if (hostClient == client) {
                    // nothing to do
                }
                else if (newClient == client)
                {
                    // if somebody masks their IP, they don't get to learn any one else's
                    var endPointList = clients.Select<IndividualServer.Client,SecuredPeerId>(x => null).ToList();
                    if (client.exposeIPAddress) {
                        endPointList = clients.Select(x => x.publicEndPoint).ToList();
                    }

                    client.Send(new RouterModifyPlayerListPacket(
                        RouterModifyPlayerListPacket.Operation.Add,
                        clients.Select(x => x.routerID).ToList(),
                        endPointList,
                        clients.Select(x => x.info).ToList()
                        ),
                        PacketReliability.Reliable);
                }
                else
                {
                    if (client.exposeIPAddress)
                        client.Send(notifyPacketForUnproxied, PacketReliability.Reliable);
                    else
                        client.Send(notifyPacketForProxied, PacketReliability.Reliable);
                }
            }

            PrintRemainingClients();
        }

        static void ChatMessage_ProcessAction(RouterChatMessage packet)
        {
            if (!CheckSender(packet.processingPeer, packet.fromRouterID)) { return; }
            var senderClient = clients.FirstOrDefault(x => x.routerID == packet.fromRouterID);
            foreach (Client client in clients)
            {
                //if ((senderClient.exposeIPAddress && client.exposeIPAddress) || client.routerID == packet.fromRouterID) { continue; }
                if (client.routerID == packet.fromRouterID) { continue; }
                client.Send(packet, PacketReliability.Reliable);
            }
        }
    }

}
