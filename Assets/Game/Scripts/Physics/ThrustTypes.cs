using System;
using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>What kind of gas burst a seat produced. Drives the VFX variant, animation and audio cue.</summary>
    public enum ThrustKind : byte
    {
        None = 0,
        Sustained = 1,   // F held: continuous stream (ForceMode.Force every physics step)
        Tap = 2,         // F pressed: initial puff (one impulse)
        Charged = 3,     // released charge (one larger impulse)
        Brake = 4,       // burst against the raft's velocity (one impulse)
        Cooldown = 5,    // attempted fart while exhausted: no force, "pfft" feedback only
    }

    /// <summary>Solo assistance levels (see SharedRaftController / FartResourceController for the effects).</summary>
    public enum SoloAssist : byte { Off = 0, Standard = 1, High = 2 }

    /// <summary>
    /// One player's input for a physics tick, independent of the device and of the network.
    /// The raft is round: each player sits on the rim facing the centre. <see cref="move"/> walks the player along
    /// the rim (−1 = to the character's left, +1 = right) and farting pushes the raft from where they sit toward
    /// the centre, so WHERE you sit decides WHICH WAY the raft goes. Edge actions use wrapping sequence counters,
    /// so a press is never lost or doubled when states are resent over an unreliable network channel.
    /// </summary>
    [Serializable]
    public struct PlayerInputState : IEquatable<PlayerInputState>
    {
        public float move;          // −1..1 along the rim
        public bool fartHeld;       // F held (sustained stream after the initial puff)
        public bool charging;       // ChargeFart held
        public byte tapSeq;         // +1 per F press (initial puff)
        public byte chargeSeq;      // +1 per ChargeFart release
        public byte brakeSeq;       // +1 per Brake press

        public static PlayerInputState Idle => default;

        public bool Equals(PlayerInputState o) =>
            Mathf.Approximately(move, o.move) && fartHeld == o.fartHeld && charging == o.charging &&
            tapSeq == o.tapSeq && chargeSeq == o.chargeSeq && brakeSeq == o.brakeSeq;
        public override bool Equals(object obj) => obj is PlayerInputState o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(move, fartHeld, charging, tapSeq, chargeSeq, brakeSeq);
    }

    /// <summary>Anything that can supply a seat with input (local device, network, AI crew, test driver).</summary>
    public interface IThrustInputSource
    {
        PlayerInputState ReadInput();
    }

    /// <summary>
    /// Visible state of one seat, replicated to every client so seat positions, VFX, animation, audio and HUD agree
    /// with the authoritative physics. Bursts are delivered separately as <see cref="ThrustEvent"/>.
    /// </summary>
    [Serializable]
    public struct SeatThrustState : IEquatable<SeatThrustState>
    {
        public float angle;         // seat position on the rim, degrees clockwise from the bow (0 = front, 180 = back)
        public float moving;        // −1..1 current rim movement (for the shuffle animation)
        public bool sustaining;
        public Vector2 direction;   // raft-space push direction (x right, y forward), magnitude = intensity
        public float gas01;
        public float charge01;
        public bool exhausted;

        public bool Equals(SeatThrustState o) =>
            Mathf.Approximately(angle, o.angle) && Mathf.Approximately(moving, o.moving) && sustaining == o.sustaining &&
            direction == o.direction && Mathf.Approximately(gas01, o.gas01) && Mathf.Approximately(charge01, o.charge01) && exhausted == o.exhausted;
        public override bool Equals(object obj) => obj is SeatThrustState o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(angle, moving, sustaining, direction, gas01, charge01, exhausted);
    }

    /// <summary>A discrete burst produced by a seat (authoritative), replayed locally by every client for feedback.</summary>
    [Serializable]
    public struct ThrustEvent
    {
        public int seat;
        public ThrustKind kind;
        public Vector2 direction;   // raft-space push direction (normalised)
        public float strength01;    // 0..1 relative size of the burst
    }

    public static class RimMath
    {
        /// <summary>Unit vector in raft space (x right, y forward) from the centre to a rim angle (deg, clockwise from the bow).</summary>
        public static Vector2 Outward(float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        /// <summary>Raft-space push of a player sitting at this angle: toward the centre.</summary>
        public static Vector2 Push(float angleDeg) => -Outward(angleDeg);

        /// <summary>Rim angle a player must sit at to push the raft along a raft-space direction.</summary>
        public static float AngleForPush(Vector2 push) => Mathf.Atan2(-push.x, -push.y) * Mathf.Rad2Deg;

        public static float Wrap(float a) { a %= 360f; return a < 0f ? a + 360f : a; }
        public static float Delta(float from, float to) => Mathf.DeltaAngle(from, to);
    }
}
