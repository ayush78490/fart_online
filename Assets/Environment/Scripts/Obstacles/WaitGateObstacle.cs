using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// Wait gate: a spiked log that blocks the only gap of a row (hung under a bridge between two posts) and lifts up
    /// now and then. Players have to hold the raft back and go when it opens. Cycle (seconds, shared obstacle clock):
    /// closed <see cref="closedTime"/>, lifting <see cref="moveTime"/>, open <see cref="openTime"/>, dropping
    /// <see cref="moveTime"/>. The open window is longer than the raft needs to pass at the slowest current (checked by
    /// the endless probe). Its speed is never raised by the danger ramp. Moves along local +Y.
    /// </summary>
    public class WaitGateObstacle : KinematicObstacle
    {
        public float closedTime = 2.2f, moveTime = 0.45f, openTime = 2.6f;
        [Tooltip("Lift height (m): closed at the start pose, open this much higher")] public float lift = 2.75f;
        [Tooltip("Distance along the river (set by the spawner; the AI uses it)")] public float riverDistance;

        public static readonly List<WaitGateObstacle> Active = new List<WaitGateObstacle>();

        public float Period => closedTime + openTime + 2f * moveTime;
        /// <summary>The gate's own clock now (what <see cref="IsOpenAt"/> expects).</summary>
        public float ClockNow => Clock;

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);

        /// <summary>Lift fraction (0 closed, 1 open) at gate-clock time t.</summary>
        public float LiftAt(float t)
        {
            float u = Mathf.Repeat(t, Period);
            if (u < closedTime) return 0f;
            u -= closedTime;
            if (u < moveTime) return Mathf.SmoothStep(0f, 1f, u / moveTime);
            u -= moveTime;
            if (u < openTime) return 1f;
            u -= openTime;
            return 1f - Mathf.SmoothStep(0f, 1f, u / moveTime);
        }

        /// <summary>True when the gate is fully open for the whole span [t, t + span] (gate-clock seconds).</summary>
        public bool IsOpenAt(float t, float span)
        {
            for (float x = 0f; x <= span; x += 0.1f) if (LiftAt(t + x) < 0.999f) return false;
            return LiftAt(t + span) >= 0.999f;
        }

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localRotation = Quaternion.identity;
            float k = LiftAt(t);
            // a little shudder while closed, a hint that it is about to move
            float u = Mathf.Repeat(t, Period);
            float shake = u < closedTime && u > closedTime - 0.6f ? Mathf.Sin(t * 60f) * 0.05f : 0f;
            localOffset = Vector3.up * (lift * k + shake);
        }
    }
}
