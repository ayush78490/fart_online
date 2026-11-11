using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>Test input source: the test code walks the player along the rim and farts programmatically.</summary>
    public class ScriptedThrustInput : MonoBehaviour, IThrustInputSource
    {
        public PlayerInputState state;
        public PlayerInputState ReadInput() => state;
        /// <summary>Press and hold F (initial puff, then the sustained stream).</summary>
        public void HoldFart() { if (!state.fartHeld) state.tapSeq++; state.fartHeld = true; }
        public void ReleaseFart() => state.fartHeld = false;
        /// <summary>Tap F once (puff only).</summary>
        public void Tap() { state.tapSeq++; state.fartHeld = false; }
        public void Move(float m) => state.move = m;
        public void Brake() => state.brakeSeq++;
        public void BeginCharge() => state.charging = true;
        public void ReleaseCharge() { state.charging = false; state.chargeSeq++; }
        public void Release() { state.move = 0f; state.fartHeld = false; state.charging = false; }
    }
}
