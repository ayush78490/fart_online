using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FartOnline.Game
{
    /// <summary>
    /// Reads one local player's "RaftGameplay" actions and converts them into a device-independent
    /// <see cref="PlayerInputState"/>: A/D (or ←/→, left stick) walk the character along the rim of the round raft,
    /// F (Enter, gamepad South) farts — a press gives a puff, holding keeps a stream going. Each player gets its own
    /// copy of the action asset restricted to one control scheme (KeyboardLeft, KeyboardRight or Gamepad) and, for
    /// gamepads, one device, so up to four people can share one computer. Presses are counted in Update and read in
    /// FixedUpdate, so a quick tap between two physics steps is never lost.
    /// The first player on scheme "" (solo, online) or KeyboardLeft also takes the on-screen touch controls
    /// (<see cref="TouchInput"/>), merged with the keys.
    /// </summary>
    public class LocalPlayerInput : MonoBehaviour, IThrustInputSource
    {
        public InputActionAsset actionsAsset;
        [Tooltip("Control scheme name in the asset; empty = all bindings")] public string controlScheme = "";
        public Transform raft;
        public Camera viewCamera;

        public event Action PausePressed;
        public event Action InteractPressed;

        public InputActionAsset Actions { get; private set; }
        InputAction left, right, burst, charge, brake, interact, pause, look;
        PlayerInputState state;
        int touchTap, touchCharge, touchBrake;

        bool AcceptsTouch => string.IsNullOrEmpty(controlScheme) || controlScheme == "KeyboardLeft";

        public static Gamepad[] AvailableGamepads => Gamepad.all.ToArray();

        public void Init(InputActionAsset asset, string scheme, InputDevice device, Transform raftTransform, Camera cam)
        {
            actionsAsset = asset;
            controlScheme = scheme;
            raft = raftTransform;
            viewCamera = cam;
            Setup(device);
        }

        void Awake() { if (actionsAsset != null && Actions == null) Setup(null); }

        void Setup(InputDevice device)
        {
            if (Actions != null) Destroy(Actions);
            Actions = Instantiate(actionsAsset);
            if (!string.IsNullOrEmpty(controlScheme)) Actions.bindingMask = InputBinding.MaskByGroup(controlScheme);
            if (device != null) Actions.devices = new[] { device };
            var map = Actions.FindActionMap("RaftGameplay", true);
            left = map["MoveLeft"]; right = map["MoveRight"];
            burst = map["FartBurst"]; charge = map["ChargeFart"]; brake = map["Brake"];
            interact = map["Interact"]; pause = map["Pause"]; look = map["CameraLook"];
            if (isActiveAndEnabled) map.Enable();
            if (AcceptsTouch && TouchInput.Owner == null)
            {
                TouchInput.Owner = this;
                TouchInput.Clear();
                touchTap = TouchInput.TapSeq; touchCharge = TouchInput.ChargeSeq; touchBrake = TouchInput.BrakeSeq;
            }
        }

        void OnEnable() { Actions?.Enable(); }
        void OnDisable() { Actions?.Disable(); }
        void OnDestroy()
        {
            if (Actions != null) Destroy(Actions);
            if (TouchInput.Owner == this) { TouchInput.Owner = null; TouchInput.Clear(); }
        }

        public Vector2 LookDelta => look != null ? look.ReadValue<Vector2>() : Vector2.zero;

        void Update()
        {
            if (Actions == null) return;
            state.move = Mathf.Clamp(right.ReadValue<float>() - left.ReadValue<float>(), -1f, 1f);
            state.fartHeld = burst.IsPressed();
            state.charging = charge.IsPressed();
            if (burst.WasPressedThisFrame()) state.tapSeq++;
            if (charge.WasReleasedThisFrame()) state.chargeSeq++;
            if (brake.WasPressedThisFrame()) state.brakeSeq++;
            if (pause.WasPressedThisFrame()) PausePressed?.Invoke();
            if (interact.WasPressedThisFrame()) InteractPressed?.Invoke();
            if (TouchInput.Owner == this)
            {
                state.move = Mathf.Clamp(state.move + TouchInput.Move, -1f, 1f);
                state.fartHeld |= TouchInput.Fart;
                state.charging |= TouchInput.Charge;
                state.tapSeq = (byte)(state.tapSeq + (TouchInput.TapSeq - touchTap));
                state.chargeSeq = (byte)(state.chargeSeq + (TouchInput.ChargeSeq - touchCharge));
                state.brakeSeq = (byte)(state.brakeSeq + (TouchInput.BrakeSeq - touchBrake));
                touchTap = TouchInput.TapSeq; touchCharge = TouchInput.ChargeSeq; touchBrake = TouchInput.BrakeSeq;
            }
        }

        public PlayerInputState ReadInput() => state;

        /// <summary>Human-readable bindings of this player's scheme, for the controls screen.</summary>
        public string DescribeBindings()
        {
            if (Actions == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var a in Actions.FindActionMap("RaftGameplay").actions)
            {
                string b = string.IsNullOrEmpty(controlScheme) ? a.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions)
                                                               : a.GetBindingDisplayString(InputBinding.MaskByGroup(controlScheme), InputBinding.DisplayStringOptions.DontIncludeInteractions);
                if (!string.IsNullOrEmpty(b)) sb.AppendLine($"{a.name}: {b}");
            }
            return sb.ToString();
        }
    }
}
