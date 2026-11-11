using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FartOnline.Game.Net
{
    /// <summary>Compact wire form of <see cref="PlayerInputState"/> (11 bytes).</summary>
    public struct NetInput : INetworkSerializeByMemcpy, IEquatable<NetInput>
    {
        public sbyte move;       // −127..127
        public byte flags;       // 1 = fartHeld, 2 = charging
        public byte tapSeq, chargeSeq, brakeSeq;

        public static NetInput From(PlayerInputState s) => new NetInput
        {
            move = (sbyte)Mathf.RoundToInt(Mathf.Clamp(s.move, -1f, 1f) * 127f),
            flags = (byte)((s.fartHeld ? 1 : 0) | (s.charging ? 2 : 0)),
            tapSeq = s.tapSeq, chargeSeq = s.chargeSeq, brakeSeq = s.brakeSeq,
        };

        public PlayerInputState ToState() => new PlayerInputState
        {
            move = move / 127f,
            fartHeld = (flags & 1) != 0,
            charging = (flags & 2) != 0,
            tapSeq = tapSeq, chargeSeq = chargeSeq, brakeSeq = brakeSeq,
        };

        public bool Equals(NetInput o) => move == o.move && flags == o.flags && tapSeq == o.tapSeq && chargeSeq == o.chargeSeq && brakeSeq == o.brakeSeq;
    }

    /// <summary>Who sits in a seat (server writes, everyone reads).</summary>
    public struct NetSeatInfo : INetworkSerializeByMemcpy, IEquatable<NetSeatInfo>
    {
        public bool occupied;
        public bool ai;
        public ulong clientId;
        public FixedString32Bytes name;

        public bool Equals(NetSeatInfo o) => occupied == o.occupied && ai == o.ai && clientId == o.clientId && name.Equals(o.name);
    }

    /// <summary>Quantised <see cref="SeatThrustState"/>: what every client needs to draw a seat.</summary>
    public struct NetSeatState : INetworkSerializeByMemcpy, IEquatable<NetSeatState>
    {
        public ushort angle;     // degrees × 100
        public sbyte moving;
        public byte flags;       // 1 sustaining, 2 exhausted
        public sbyte dirX, dirY;
        public byte gas, charge;

        public static NetSeatState From(SeatThrustState s) => new NetSeatState
        {
            angle = (ushort)Mathf.RoundToInt(RimMath.Wrap(s.angle) * 100f),
            moving = (sbyte)Mathf.RoundToInt(Mathf.Clamp(s.moving, -1f, 1f) * 127f),
            flags = (byte)((s.sustaining ? 1 : 0) | (s.exhausted ? 2 : 0)),
            dirX = (sbyte)Mathf.RoundToInt(Mathf.Clamp(s.direction.x, -1f, 1f) * 127f),
            dirY = (sbyte)Mathf.RoundToInt(Mathf.Clamp(s.direction.y, -1f, 1f) * 127f),
            gas = (byte)Mathf.RoundToInt(Mathf.Clamp01(s.gas01) * 255f),
            charge = (byte)Mathf.RoundToInt(Mathf.Clamp01(s.charge01) * 255f),
        };

        public SeatThrustState ToState() => new SeatThrustState
        {
            angle = angle / 100f,
            moving = moving / 127f,
            sustaining = (flags & 1) != 0,
            exhausted = (flags & 2) != 0,
            direction = new Vector2(dirX / 127f, dirY / 127f),
            gas01 = gas / 255f,
            charge01 = charge / 255f,
        };

        public bool Equals(NetSeatState o) =>
            angle == o.angle && moving == o.moving && flags == o.flags && dirX == o.dirX && dirY == o.dirY && gas == o.gas && charge == o.charge;
    }

    /// <summary>Shared run state mirrored to clients.</summary>
    public struct NetRunState : INetworkSerializeByMemcpy, IEquatable<NetRunState>
    {
        public byte state;
        public float elapsed;
        public sbyte checkpoint;
        public ushort wipeouts;
        public ushort thrusts;
        public byte progress;    // 0..255
        public float countdown;
        public int seed;         // endless river seed (0 = fixed course)
        public float score;      // endless metres travelled
        public FixedString64Bytes crash;   // endless: what the raft crashed into (game over board)

        public bool Equals(NetRunState o) =>
            state == o.state && Mathf.Approximately(elapsed, o.elapsed) && checkpoint == o.checkpoint && wipeouts == o.wipeouts &&
            thrusts == o.thrusts && progress == o.progress && Mathf.Approximately(countdown, o.countdown) && seed == o.seed &&
            Mathf.Approximately(score, o.score) && crash.Equals(o.crash);
    }

    public struct NetBurst : INetworkSerializeByMemcpy
    {
        public byte seat, kind;
        public sbyte dirX, dirY;
        public byte strength;

        public static NetBurst From(ThrustEvent e) => new NetBurst
        {
            seat = (byte)e.seat, kind = (byte)e.kind,
            dirX = (sbyte)Mathf.RoundToInt(e.direction.x * 127f), dirY = (sbyte)Mathf.RoundToInt(e.direction.y * 127f),
            strength = (byte)Mathf.RoundToInt(Mathf.Clamp01(e.strength01) * 255f),
        };

        public ThrustEvent ToEvent() => new ThrustEvent
        {
            seat = seat, kind = (ThrustKind)kind, direction = new Vector2(dirX / 127f, dirY / 127f), strength01 = strength / 255f,
        };
    }
}
