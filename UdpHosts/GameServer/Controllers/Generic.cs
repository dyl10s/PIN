﻿using AeroMessages.GSS.V66.Generic;
using GameServer.Enums;
using GameServer.Enums.GSS.Generic;
using GameServer.Extensions;
using GameServer.Packets;
using GameServer.Packets.Control;
using System;

namespace GameServer.Controllers;

[ControllerID(Enums.GSS.Controllers.Generic)]
public class Generic : Base
{
    public override void Init(INetworkClient client, IPlayer player, IShard shard)
    {
    }

    [MessageID((byte)Commands.ScheduleUpdateRequest)]
    public void ScheduleUpdateRequest(INetworkClient client, IPlayer player, ulong entityId, GamePacket packet)
    {
        var updateRequest = packet.Unpack<ScheduleUpdateRequest>();

        player.LastRequestedUpdate = client.AssignedShard.CurrentTime;
        player.RequestedClientTime = Math.Max(updateRequest.Time, player.RequestedClientTime);

        if (!player.FirstUpdateRequested)
        {
            player.FirstUpdateRequested = true;
            player.Respawn();
        }

        //Program.Logger.Error( "Update scheduled" );
    }

    [MessageID((byte)Commands.RequestLogout)]
    public void RequestLogout(INetworkClient client, IPlayer player, ulong entityId, GamePacket packet)
    {
        var resp = new CloseConnection { Unknown1 = 0 };
        client.NetChannels[ChannelType.Control].SendClass(resp, typeof(ControlPacketType));
    }
}