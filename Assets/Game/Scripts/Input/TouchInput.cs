using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FartOnline.Game
{
    /// <summary>
    /// On-screen touch controls for phones and tablets (and the web build on touch devices). The HUD's buttons write
    /// here; the first local player (<see cref="Owner"/>) merges it into its <see cref="PlayerInputState"/> exactly like
    /// keys, so touch works with every game mode, the AI crew and online play.
    /// Visibility: <see cref="Mode"/> Auto shows the buttons on mobile platforms and as soon as a touchscreen is used;
    /// On / Off force it (Settings, saved in PlayerPrefs).
    /// </summary>
    public static class TouchInput
    {
        public enum ShowMode { Auto, On, Off }

        public static float Move;               // -1 = walk left (A), +1 = walk right (D)
        public static bool Fart, Charge;
        public static int TapSeq, ChargeSeq, BrakeSeq;
        public static LocalPlayerInput Owner;
        static bool touchSeen;

        const string ModeKey = "fo.touch";
        public static ShowMode Mode
        {
            get => (ShowMode)PlayerPrefs.GetInt(ModeKey, 0);
            set { PlayerPrefs.SetInt(ModeKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Tests: force the touch layout regardless of the device.</summary>
        public static bool ForceForTests;

        /// <summary>True when the on-screen controls should be shown.</summary>
        public static bool Visible
        {
            get
            {
                if (ForceForTests) return true;
                var m = Mode;
                if (m == ShowMode.On) return true;
                if (m == ShowMode.Off) return false;
                if (!touchSeen && Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) touchSeen = true;
                return Application.isMobilePlatform || touchSeen || (Application.platform == RuntimePlatform.WebGLPlayer && Touchscreen.current != null);
            }
        }

        public static void Clear()
        {
            Move = 0f;
            Fart = Charge = false;
        }
    }

    /// <summary>A hold-to-act on-screen button (multi-touch safe: each button tracks its own pointer).</summary>
    public class TouchHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        public enum Action { WalkLeft, WalkRight, Fart, Charge, Brake }
        public Action action;
        public Graphic face;
        int pointer = int.MinValue;
        bool held;
        Vector3 baseScale;

        void Awake() => baseScale = transform.localScale;

        void OnDisable() { if (held) Release(); }

        public void OnPointerDown(PointerEventData e)
        {
            pointer = e.pointerId;
            Press();
        }

        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) Release(); }
        public void OnPointerExit(PointerEventData e) { if (e.pointerId == pointer && action != Action.Charge) Release(); }
        public void OnPointerEnter(PointerEventData e) { }

        void Press()
        {
            held = true;
            transform.localScale = baseScale * 0.92f;
            Debug.Log("[Touch] " + action + " down");
            if (face != null) face.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            switch (action)
            {
                case Action.WalkLeft: TouchInput.Move = -1f; break;
                case Action.WalkRight: TouchInput.Move = 1f; break;
                case Action.Fart: TouchInput.Fart = true; TouchInput.TapSeq++; break;
                case Action.Charge: TouchInput.Charge = true; break;
                case Action.Brake: TouchInput.BrakeSeq++; break;
            }
        }

        void Release()
        {
            held = false;
            pointer = int.MinValue;
            transform.localScale = baseScale;
            Debug.Log("[Touch] " + action + " up");
            if (face != null) face.color = Color.white;
            switch (action)
            {
                case Action.WalkLeft: if (TouchInput.Move < 0f) TouchInput.Move = 0f; break;
                case Action.WalkRight: if (TouchInput.Move > 0f) TouchInput.Move = 0f; break;
                case Action.Fart: TouchInput.Fart = false; break;
                case Action.Charge: TouchInput.Charge = false; TouchInput.ChargeSeq++; break;
            }
        }
    }
}
