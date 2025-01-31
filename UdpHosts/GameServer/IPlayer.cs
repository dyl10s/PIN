﻿using GameServer.Data;
using System.Threading;
using Character = GameServer.Entities.Character.Character;

namespace GameServer;

public interface IPlayer
{
    public enum PlayerStatus
    {
        Invalid = -1,
        Unknown = 0,
        Connecting = 1,
        Connected,
        LoggingIn,
        LoggedIn,
        Loading,

        Playing = 999
    }

    ulong CharacterId { get; }
    ulong EntityId => CharacterId & 0xffffffffffffff00; // Ignore last byte
    Character CharacterEntity { get; }
    PlayerStatus Status { get; }
    Zone CurrentZone { get; }
    uint LastRequestedUpdate { get; set; }
    uint RequestedClientTime { get; set; }
    bool FirstUpdateRequested { get; set; }

    /// <summary>
    ///     The player's user id on steam
    /// </summary>
    /// <remarks>
    ///     ToDo: Maybe persist the player's steam id?
    ///     ToDo: Maybe Parse and enable/disable stuff according to https://developer.valvesoftware.com/wiki/SteamID
    /// </remarks>
    ulong SteamUserId { get; set; }

    void Init(IShard shard);

    void Login(ulong characterId);
    void Ready();
    void Respawn();
    void Jump();
    void Tick(double deltaTime, ulong currentTime, CancellationToken ct);
}