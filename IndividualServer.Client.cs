using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Aws.GameLift.Server;
using RainMeadow.Shared;
using RainMeadow.Shared.Models;

namespace RainMeadow.IndividualServer
{
    static partial class IndividualServer
    {
        static public List<Client> clients = new();
        public class Client
        {
            public readonly ushort RouterID;
            public readonly PlayerInfo Info;
            public readonly SecuredPeerManager.RemotePeer Peer;

            private bool prospective;
            public bool Prospective => prospective;
            public readonly bool ExposeIPAddress;


            public bool LobbyOwner => clients.FirstOrDefault() == this;    
            public bool Proxied => Prospective || ExposeIPAddress;
            public SecuredPeerId PeerID => Peer.id;
            public string gameLiftSessionID;

            public Client(SecuredPeerId endPoint, ushort RouterID, bool exposeIPAddress, bool prospective, PlayerInfo info, string gameLiftSessionID)
            {
                this.prospective = prospective;
                this.RouterID = RouterID;
                this.Info = info;
                this.ExposeIPAddress = exposeIPAddress;
                this.gameLiftSessionID = gameLiftSessionID;

                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                Peer = peerManager.GetRemotePeer(endPoint, true) ?? throw new Exception("Failed to create remote peer"); // create remotePeer
                peerManager.OnPeerForgotten += OnPeerForgotten;

                clients.Add(this);
                RainMeadow.Debug($"New client: {this}");
            }


            private void OnPeerForgotten(SecuredPeerManager.RemotePeer forgottenPeer, string reason)
            {
                if (this.Peer == forgottenPeer) RemoveClient();
                RainMeadow.Debug($"Forgot {this}, Reason: {reason}");
            }

            public void RemoveClient(string reason = "")
            {
                if (gameLift != null)
                {
                    gameLift.RemovePlayerSession(gameLiftSessionID);
                }

                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                peerManager.OnPeerForgotten -= OnPeerForgotten;

                if (clients.Contains(this))
                {
                    RainMeadow.Debug($"Removing client: {this}");
                    clients.Remove(this);

                    var removalPacket = new RouterModifyPlayerListPacket(RouterModifyPlayerListPacket.Operation.Remove, new List<ushort> { RouterID });
                    foreach (Client client in clients) client.Send(removalPacket, true);
                    peerManager.TerminatePeer(Peer, reason);
                    PrintRemainingClients();
                }
            }

            public void Send(Packet packet, bool reliable)
            {
                if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
                using (MemoryStream stream = new())
                using (BinaryWriter writer = new(stream))
                {
                    Packet.Encode(packet, writer, PeerID, peerManager.Me);
                    peerManager.Send(stream.GetBuffer(), PeerID, reliable? SecuredPeerManager.PacketFlags.Reliable : SecuredPeerManager.PacketFlags.Unreliable, packet.boxed);
                }
            }

            public override string ToString()
            {
                StringBuilder str = new($"Client #{RouterID} ({PeerID.ToString(false)})");
                if (prospective) str.Insert(0, "Prospective");
                return str.ToString();
            }

            public void FulfillProspective()
            {
                if (gameLift != null)
                {
                    gameLift.AcceptPlayerSession(gameLiftSessionID);
                }

                RainMeadow.Debug(PeerID.ToString(false));
                if (!prospective) return;
                prospective = false;
                if (!Proxied)
                {
                    var notifyPacketForUnproxied = new RouterModifyPlayerListPacket(
                        RouterModifyPlayerListPacket.Operation.Update,
                        [ RouterID ],
                        [ PeerID ],
                        [ Info ]
                    );

                    var mutualUnproxiedClients =  clients.Where(x => !x.Proxied && x != this).ToArray();
                    
                    foreach (Client client in mutualUnproxiedClients) 
                        client.Send(notifyPacketForUnproxied, true);

                    Send(new RouterModifyPlayerListPacket(
                        RouterModifyPlayerListPacket.Operation.Update,
                        mutualUnproxiedClients.Select(x => x.RouterID).ToList(),
                        mutualUnproxiedClients.Select(x => (SecuredPeerId?)x.PeerID).ToList(),
                        mutualUnproxiedClients.Select(x => x.Info).ToList()
                    ), true);
                }
            }
        }


        static void SetupClientEvents()
        {
            Packet.packetFactory += Packet.RouterFactory;
            BeginRouterSession.ProcessAction += BeginRouterSession_ProcessAction;
            RouteSessionData.ProcessAction += RouteSessionData_ProcessAction;
            // PublishRouterLobby.ProcessAction += PublishRouterLobby_ProcessAction;
            RouterChatMessage.ProcessAction += ChatMessage_ProcessAction;
            RouterCustomPacket.ProcessAction += RouterCustomPacket_ProcessAction;
            PlayerJoiningDecision.ProcessAction += PlayerJoiningDecision_ProcessAction;
        }


        static bool CheckSender([NotNullWhen(true)] out Client? source, SecuredPeerId processingEndpoint, ushort fromRouterID, bool allowProsectiveClients = false)
        {
            if (!clients.Any()) 
            {
                RainMeadow.Error("Prospective client attempted to communicate illegally");
                source = null;
                return false;
            }

            source = clients.First(x => x.PeerID.CompareAndUpdate(processingEndpoint));
            if (source.Prospective && !allowProsectiveClients)
            {
                RainMeadow.Error("Prospective client attempted to communicate illegally");
                return false;
            }

            if (source is null) 
            {
                RainMeadow.Error("Impersonation attempt! unknown sender disguised as " + fromRouterID.ToString());
                return false;
            } 
            else if (fromRouterID != source.RouterID) 
            {
                RainMeadow.Error("Impersonation attempt! probably-player-"+ source.RouterID.ToString() + " disguised as " + fromRouterID.ToString());
                return false;
            }
            return true;
        }

        static void RouteSessionData_ProcessAction(RouteSessionData packet) => ProcessRoutePacket(packet);
        static void RouterCustomPacket_ProcessAction(RouterCustomPacket packet) => ProcessRoutePacket(packet);
        static void ProcessRoutePacket(RoutePacket packet)
        {
            // reminder: prospective clients can only communicate with the lobby host
            var allowProsectiveClients = packet.toRouterID == clients.FirstOrDefault()?.RouterID || packet.toRouterID == packet.fromRouterID;
            if (!CheckSender(out var sourceClient, packet.processingPeer ?? throw new InvalidProgrammerException("No processing endpoint"), packet.fromRouterID, allowProsectiveClients)) return;

            // this isn't really an error.
            // if (packet.fromRouterID == packet.toRouterID) 
            // {
            //     RainMeadow.Error("Client "+ packet.toRouterID.ToString() + " send a packet to themself");
            // }

            var destinationClient = clients.FirstOrDefault(x => x.RouterID == packet.toRouterID);
            if (destinationClient == null) 
            {
                RainMeadow.Error($"Received packet for unknown client #{packet.toRouterID}");
                return;
            }

            if (destinationClient.Prospective && !sourceClient.LobbyOwner)
            {
                RainMeadow.Error("Only host can communicate with prospective clients " + packet.toRouterID.ToString());
            }

            destinationClient.Send(packet, false);
        }

        const ushort nullRouterID = 0;
        static void BeginRouterSession_ProcessAction(BeginRouterSession packet)
        {
            if (!packet.boxed)
            {
                // proves that they actually own the public key they're using
                RainMeadow.Error($"Unboxed BeginRouterSession packet from {packet.processingPeer}");
                return;
            }

            PlayerInfo info;
            if (gameLift != null)
            {
                info = gameLift.GetPlayerSession(packet.gameliftID ?? throw new Exception("No gamelift ID???"));
                byte[] publicKey = Sodium.LibSodium.HexToBin(info.publicKey!);
                if (!publicKey.SequenceEqual(packet.processingPeer.publicKey))
                {
                    RainMeadow.Error($"{packet.processingPeer} has the incorrect public key for {packet.gameliftID}");
                    return;
                }
            }
            else
            {
                info = new PlayerInfo() { username = packet.name };
            }




            if (clients.Any(x => x.PeerID == packet.processingPeer)) return;
            var usedIDs = clients.Select(x => x.RouterID);
            ushort id = 0;
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
            else
            {
                id = 1;
            } 

            if (peerManager is null) throw new InvalidProgrammerException("peerManager is null");
            if (id == nullRouterID)
            {
                peerManager.TerminatePeer(packet.processingPeer, "No more available router ids");
                return;
            }

            var newClient = new Client(packet.processingPeer, id, packet.exposeIPAddress, clients.Any(), info, packet.name);
            var notifyPacketForProxied = new RouterModifyPlayerListPacket(
                RouterModifyPlayerListPacket.Operation.Add,
                [ newClient.RouterID ],
                [ null ],
                [ info ]
            );

            var notifyPacketForUnproxied = newClient.Proxied? notifyPacketForProxied : new RouterModifyPlayerListPacket(
                RouterModifyPlayerListPacket.Operation.Add,
                [ newClient.RouterID ],
                [ newClient.PeerID ],
                [ info ]
            );

            foreach (Client client in clients.Where(x => !x.Proxied && x != newClient).ToArray())
            {
                client.Send(client.Proxied? notifyPacketForProxied : notifyPacketForUnproxied, true);
            }


            newClient.Send(new RouterModifyPlayerListPacket(
                RouterModifyPlayerListPacket.Operation.Add,
                clients.Select(x => x.RouterID).ToList(),
                clients.Select(x => x.Proxied? null : x.PeerID).ToList(),
                clients.Select(x => x.Info).ToList()
            ), true);

     
            
            newClient.Send(new JoinRouterLobby(id, Name, lobbyParameters!), true);
            PrintRemainingClients();
        }


        static void PlayerJoiningDecision_ProcessAction(PlayerJoiningDecision packet) 
        {
            if (!clients.Any())
            {
                RainMeadow.Error($"Recieved PlayerJoiningDecision from {packet.processingPeer} in an empty lobby");
                return;
            }

            if (!CheckSender(out var source, packet.processingPeer ?? throw new InvalidProgrammerException("No processing endpoint"), clients.First().RouterID, false)) return;
            if (!source.LobbyOwner)
            {
                RainMeadow.Error($"Recieved PlayerJoiningDecision from non owner source {source}");
            }

            var newClient = clients.FirstOrDefault(x => x.RouterID == packet.player);
            if (newClient == null) return;

            switch (packet.decision)
            {
                case PlayerJoiningDecision.Decision.Reject:
                    newClient.RemoveClient();
                    break;

                case PlayerJoiningDecision.Decision.Accept:
                    if (newClient.Prospective) newClient.FulfillProspective();
                    break;
            }

            PrintRemainingClients();
        }

        static void ChatMessage_ProcessAction(RouterChatMessage packet)
        {
            if (!CheckSender(out var sender, packet.processingPeer, packet.fromRouterID)) { return; }
            foreach (Client client in clients)
            {
                if (client == sender) continue; 
                client.Send(packet, true);
            }
        }
    }

}
