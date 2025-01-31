﻿using MatrixServer.Packets;
using Shared.Udp;
using System;
using System.Threading;

namespace MatrixServer;

internal class MatrixServer : PacketServer
{
    protected Random random = new();

    public MatrixServer(ushort port) : base(port)
    {
    }

    protected override void HandlePacket(Packet packet, CancellationToken ct)
    {
        var mem = packet.PacketData;
        var socketId = Deserializer.ReadStruct<uint>(mem);
        if (socketId != 0)
        {
            return;
        }

        Program.Logger.Verbose("[MATRIX] " + packet.RemoteEndpoint + " sent " + packet.PacketData.Length + " bytes.");

        var matrixPkt = Deserializer.ReadStruct<MatrixPacketBase>(mem);

        switch (matrixPkt.Type)
        {
            case "POKE": // POKE
                var poke = Deserializer.ReadStruct<MatrixPacketPoke>(mem);
                Program.Logger.Verbose("[POKE]");
                var nextSocketId = GenerateSocketId();
                Program.Logger.Information("Assigning SocketID [" + nextSocketId + "] to [" + packet.RemoteEndpoint + "]");
                _ = Send(Serializer.WriteStruct(new MatrixPacketHehe(nextSocketId)), packet.RemoteEndpoint);
                break;
            case "KISS": // KISS
                var kiss = Deserializer.ReadStruct<MatrixPacketKiss>(mem);
                Program.Logger.Verbose("[KISS]");
                _ = Send(Serializer.WriteStruct(new MatrixPacketHugg(1, 25001)), packet.RemoteEndpoint);
                break;
            case "ABRT": // ABRT
                var abrt = Deserializer.ReadStruct<MatrixPacketAbrt>(mem);
                Program.Logger.Verbose("[ABRT]");
                break;
            default:
                Program.Logger.Error("Unknown Matrix Packet Type: " + matrixPkt.Type);
                return;
        }
    }

    protected uint GenerateSocketId()
    {
        return unchecked((uint)((0xff00ff << 8) | random.Next(0, 256)));
    }
}