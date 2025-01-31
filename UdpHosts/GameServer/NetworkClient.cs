﻿using GameServer.Controllers;
using GameServer.Controllers.Character;
using GameServer.Enums;
using GameServer.Packets;
using GameServer.Packets.Control;
using GameServer.Packets.Matrix;
using Shared.Udp;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GameServer;

public class NetworkClient : INetworkClient
{
    public NetworkClient(IPEndPoint ep, uint socketId)
    {
        SocketId = socketId;
        RemoteEndpoint = ep;
        NetClientStatus = Status.Unknown;
        NetLastActive = DateTime.Now;
    }

    protected IPacketSender Sender { get; set; }
    protected IPlayer Player { get; private set; }
    public Status NetClientStatus { get; protected set; }
    public uint SocketId { get; protected set; }
    public IPEndPoint RemoteEndpoint { get; protected set; }
    public DateTime NetLastActive { get; protected set; }
    public ImmutableDictionary<ChannelType, Channel> NetChannels { get; protected set; }
    public IShard AssignedShard { get; protected set; }

    public void Init(IPlayer player, IShard shard, IPacketSender sender)
    {
        Player = player;
        Sender = sender;
        NetClientStatus = Status.Connecting;
        AssignedShard = shard;

        NetChannels = Channel.GetChannels(this).ToImmutableDictionary();
        NetChannels[ChannelType.Control].PacketAvailable += Control_PacketAvailable;
        NetChannels[ChannelType.Matrix].PacketAvailable += Matrix_PacketAvailable;
        NetChannels[ChannelType.ReliableGss].PacketAvailable += GSS_PacketAvailable;
        NetChannels[ChannelType.UnreliableGss].PacketAvailable += GSS_PacketAvailable;
    }

    public void HandlePacket(ReadOnlyMemory<byte> data, Packet packet)
    {
        if (NetClientStatus == Status.Connecting)
        {
            NetClientStatus = Status.Connected; // the connection must have been established in order to receive a packet, so we must now be connected
        }

        if (NetClientStatus != Status.Connected && NetClientStatus != Status.Idle)
        {
            return; // can't do anything if we're not ready yet!
        }

        var idx = 0;
        var hdrSize = Unsafe.SizeOf<GamePacketHeader>();
        while (idx + 2 < data.Length)
        {
            var hdr = Deserializer.ReadStruct<GamePacketHeader>(data.Slice(idx, 2).ToArray().Reverse().ToArray().AsMemory());

            if (hdr.Length == 0 || data.Length < hdr.Length + idx)
            {
                break;
            }

            var p = new GamePacket(hdr, data.Slice(idx + hdrSize, hdr.Length - hdrSize), packet.Received);

            //Program.Logger.Verbose("-> {0} = R:{1} S:{2} L:{3}", hdr.Channel, hdr.ResendCount, hdr.IsSplit, hdr.Length);

            NetChannels[hdr.Channel].HandlePacket(p);

            idx += hdr.Length;
        }

        NetLastActive = DateTime.Now;
    }

    public virtual void NetworkTick(double deltaTime, ulong currTime, CancellationToken ct)
    {
        foreach (var c in NetChannels.Values)
        {
            c.Process(ct);
        }
    }

    public void Send(Memory<byte> p)
    {
        NetLastActive = DateTime.Now;

        var t = new Memory<byte>(new byte[4 + p.Length]);
        p.CopyTo(t[4..]);
        Serializer.WriteStruct(Utils.SimpleFixEndianness(SocketId)).CopyTo(t);

        Sender.Send(t, RemoteEndpoint);
    }


    public void SendAck(ChannelType forChannel, ushort forSeqNum, DateTime? received = null)
    {
        if (received != null)
        {
            Program.Logger.Verbose("<-- {0} Ack for {1} on {2} after {3}ms.", ChannelType.Control, forSeqNum, forChannel, (DateTime.Now - received.Value).TotalMilliseconds);
        }
        else
        {
            Program.Logger.Verbose("<-- {0} Ack for {1} on {2}.", ChannelType.Control, forSeqNum, forChannel);
        }

        var forNum = Utils.SimpleFixEndianness(forSeqNum);
        var nextNum = Utils.SimpleFixEndianness(unchecked((ushort)(forSeqNum + 1)));

        if (forChannel == ChannelType.Matrix)
        {
            NetChannels[ChannelType.Control].SendClass(new MatrixAck { AckFor = forNum, NextSeqNum = nextNum });
        }
        else if (forChannel == ChannelType.ReliableGss)
        {
            NetChannels[ChannelType.Control].SendClass(new ReliableGSSAck { AckFor = forNum, NextSeqNum = nextNum });
        }
    }

    private void GSS_PacketAvailable(GamePacket packet)
    {
        var controllerId = packet.Read<Enums.GSS.Controllers>();
        Span<byte> entity = stackalloc byte[8];
        packet.Read(7).ToArray().CopyTo(entity);
        var entityId = BitConverter.ToUInt64(entity) << 8;
        var msgId = packet.Read<byte>();

        var conn = Factory.Get(controllerId);

        if (conn == null)
        {
            Program.Logger.Verbose("---> Unrecognized ControllerId for GSS Packet; Controller = {0} Entity = 0x{1:X16} MsgID = {2}!", controllerId, entityId, msgId);
            Program.Logger.Warning(">  {0}", BitConverter.ToString(packet.PacketData.ToArray()).Replace("-", " "));
            return;
        }

        Program.Logger.Verbose("--> {0}: Controller = {1} Entity = 0x{2:X16} MsgID = {3}", packet.Header.Channel, controllerId, entityId, msgId);
        conn.HandlePacket(this, Player, entityId, msgId, packet);
    }

    private void Matrix_PacketAvailable(GamePacket packet)
    {
        var messageId = packet.Read<MatrixPacketType>();
        Program.Logger.Verbose("--> {0}: MsgID = {1} ({2})", ChannelType.Matrix, messageId, (byte)messageId);

        switch (messageId)
        {
            case MatrixPacketType.Login:
                // Login
                var loginPacket = packet.Read<Login>();
                Player.Login(loginPacket.CharacterGUID);

                break;
            case MatrixPacketType.EnterZoneAck:
                Factory.Get<BaseController>().Init(this, Player, AssignedShard);

                break;
            case MatrixPacketType.KeyframeRequest:
                // TODO; See onKeyframeRequest in server_gamesocket.js
                var keyFrameRequestPackage = packet.Read<KeyFrameRequest>();

                break;
            case MatrixPacketType.ClientStatus:
                NetChannels[ChannelType.Matrix].SendClass(new MatrixStatus());
                break;
            case MatrixPacketType.LogInstrumentation:
                // Ignore

                break;
            default:
                Program.Logger.Error("---> Unrecognized Matrix Packet {0}[{1}]!!!", messageId, (byte)messageId);
                Program.Logger.Warning(">  {0}", BitConverter.ToString(packet.PacketData.ToArray()).Replace("-", " "));
                break;
        }
    }

    private void Control_PacketAvailable(GamePacket packet)
    {
        var messageId = packet.Read<ControlPacketType>();
        Program.Logger.Verbose("--> {0}: MsgID = {1} ({2})", ChannelType.Control, messageId, (byte)messageId);

        switch (messageId)
        {
            case ControlPacketType.CloseConnection:
                var ccPkt = packet.Read<CloseConnection>();
                // TODO: Cleanly dispose of client
                break;
            case ControlPacketType.MatrixAck:
                var mAckPkt = packet.Read<MatrixAck>();
                Program.Logger.Verbose("--> {0} Ack for {1} on {2}.", ChannelType.Control, Utils.SimpleFixEndianness(mAckPkt.AckFor), ChannelType.Matrix);
                // TODO: Track reliable packets
                break;
            case ControlPacketType.ReliableGSSAck:
                var gssAckPkt = packet.Read<ReliableGSSAck>();
                Program.Logger.Verbose("--> {0} Ack for {1} on {2}.", ChannelType.Control, Utils.SimpleFixEndianness(gssAckPkt.AckFor), ChannelType.ReliableGss);
                // TODO: Track reliable packets
                break;
            case ControlPacketType.TimeSyncRequest:
                var req = packet.Read<TimeSyncRequest>();

                NetChannels[ChannelType.Control].Send(new TimeSyncResponse(req.ClientTime, unchecked(AssignedShard.CurrentTimeLong * 1000)));
                break;
            case ControlPacketType.MTUProbe:
                var mtuPkt = packet.Read<MTUProbe>();
                // TODO: ???
                break;
            default:
                Program.Logger.Error("---> Unrecognized Control Packet {0} ({1:X2})!!!", messageId, (byte)messageId);
                Program.Logger.Warning(">  {0}", BitConverter.ToString(packet.PacketData.ToArray()).Replace("-", " "));
                break;
        }
    }
}