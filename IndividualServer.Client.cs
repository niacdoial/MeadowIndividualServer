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
            public readonly string name;
            public readonly bool exposeIPAddress;
            public Client(IPEndPoint endPoint, ushort routerID, bool exposeIPAddress, string name="")
            {
                RainMeadow.Debug($"New client: {endPoint}, {routerID}, {name}");
                this.routerID = routerID;
                this.endPoint = endPoint;
                this.name = name;
                this.exposeIPAddress = exposeIPAddress;

                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                clients.Add(this);
                peerManager.GetRemotePeer(endPoint, true); // create remotePeer
                peerManager.OnPeerForgotten += OnPeerForgotten;
            }

            private void OnPeerForgotten(IPEndPoint forgottenEndPoint)
            {
                if (this.endPoint == forgottenEndPoint) RemoveClient();
            }

            private void RemoveClient()
            {
                RainMeadow.Debug($"Removing client: {endPoint}, {routerID}");
                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                peerManager.OnPeerForgotten -= OnPeerForgotten;

                if (clients.Contains(this))
                {
                    clients.Remove(this);
                    Send(new RouterModifyPlayerListPacket(RouterModifyPlayerListPacket.Operation.Remove, new List<ushort> { routerID }),
                            UDPPeerManager.PacketType.Reliable);
                    foreach (Client client in clients)
                    {
                        client.Send(new RouterModifyPlayerListPacket(RouterModifyPlayerListPacket.Operation.Remove, new List<ushort> { routerID }),
                            UDPPeerManager.PacketType.Reliable);
                    }
                    peerManager.ForgetPeer(endPoint);
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
        }

        static void RouteSessionData_ProcessAction(RouteSessionData packet)
        {
            var actualSrcRouterID = clients.First(x => UDPPeerManager.CompareIPEndpoints(packet.processingEndpoint, x.endPoint)).routerID;
            if (packet.fromRouterID != actualSrcRouterID) {
                RainMeadow.Error("Impersonation attempt! assumed "+ actualSrcRouterID.ToString() + " disguised as " + packet.fromRouterID.ToString());
                return;
            }
            var destinationClient = clients.First(x => x.routerID == packet.toRouterID);
            destinationClient.Send(
                new RouteSessionData(destinationClient.routerID, actualSrcRouterID, packet.data, packet.size),
                UDPPeerManager.PacketType.Unreliable
            );
        }

        const ushort nullrouterID = 0;
        static void BeginRouterSession_ProcessAction(BeginRouterSession packet)
        {
            if (clients.Any(x => x.endPoint == packet.processingEndpoint)) return;

            var usedIDs = clients.Select(x => x.routerID);
            ushort id = 1;

            if (usedIDs.Any())
            {
                try
                {
                    var possibleIDs = usedIDs.Select(x => (ushort)(x + 1));
                    id = possibleIDs.Except(usedIDs).Where(x => x != 0).First();
                }
                catch (InvalidOperationException exception)
                {
                    RainMeadow.Error(exception);
                    return;
                }
            }

            if (id == nullrouterID)
            {
                RainMeadow.Error("No available RouterIDs");
                return;
            }

            var newClient = new Client(packet.processingEndpoint, id, packet.exposeIPAddress, packet.name);
            var useEndpoint = SharedPlatform.BlackHole;
            if (packet.exposeIPAddress) {
                useEndpoint = packet.processingEndpoint;
            }

            foreach (Client client in clients)
            {
                if (newClient == client)
                {
                    client.Send(new RouterModifyPlayerListPacket(
                        RouterModifyPlayerListPacket.Operation.Add,
                        clients.Select(x => x.routerID).ToList(),
                        clients.Select(x => x.endPoint).ToList(),
                        clients.Select(x => x.name).ToList()
                        ),
                        UDPPeerManager.PacketType.Reliable);
                    continue;
                }

                client.Send(new RouterModifyPlayerListPacket(
                    RouterModifyPlayerListPacket.Operation.Add,
                    new List<ushort> { id },
                    new List<IPEndPoint> { useEndpoint },
                    new List<string> { packet.name }
                    ),
                    UDPPeerManager.PacketType.Reliable);
            }

            newClient.Send(new JoinRouterLobby(id, maxplayers, name, passwordprotected, mode, mods, bannedMods), UDPPeerManager.PacketType.Reliable);
        }
    }

}
