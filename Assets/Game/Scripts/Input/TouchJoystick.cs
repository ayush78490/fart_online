using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FartOnline.Game
{
    /// <summary>
    /// On-screen thumbstick for phones and tablets: drag left / right to walk around the raft's rim (analog, with a
    /// dead zone), flick it down to brake. Writes <see cref="TouchInput.Move"/> and <see cref="TouchInput.BrakeSeq"/>,
    /// which the first local player merges with the keys. Multi-touch safe: it follows only the finger that grabbed it.
    /// The touch area (this RectTransform) is larger than the drawn base so it is easy to hit without looking.
    /// </summary>
    public class TouchJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform baseRing, knob;
        [Tooltip("Knob travel in canvas units (the base ring's radius)")] public float radius = 110f;
        [Range(0f, 0.6f)] public float deadZone = 0.18f;
        [Tooltip("Pull this far down (0..1 of the radius) to brake once")] public float brakePull = 0.75f;

        int pointer = int.MinValue;
        bool brakeArmed = true;
        float peakMove;                       // for the touch log: strongest walk input of this drag
        Vector3 baseScale;
        bool scaleCaptured;

        // the HUD assigns baseRing right after AddComponent (after Awake), so the ring's rest scale is captured on
        // first use; capturing it in Awake gave a zero scale, and pressing the stick shrank it to nothing
        Vector3 BaseScale
        {
            get
            {
                if (!scaleCaptured && baseRing != null) { baseScale = baseRing.localScale; scaleCaptured = true; }
                return scaleCaptured ? baseScale : Vector3.one;
            }
        }

        void OnDisable() { if (pointer != int.MinValue) Release(); }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue) return;
            pointer = e.pointerId;
            peakMove = 0f;
            if (baseRing != null) baseRing.localScale = BaseScale * 1.06f;
            Debug.Log("[Touch] stick down");
            Track(e);
        }

        public void OnDrag(PointerEventData e) { if (e.pointerId == pointer) Track(e); }

        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) Release(); }

        void Track(PointerEventData e)
        {
            if (baseRing == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(baseRing, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 v = Vector2.ClampMagnitude(local / Mathf.Max(1f, radius), 1f);
            if (knob != null) knob.anchoredPosition = v * radius;
            float x = Mathf.Abs(v.x) < deadZone ? 0f : Mathf.Sign(v.x) * Mathf.InverseLerp(deadZone, 0.85f, Mathf.Abs(v.x));
            TouchInput.Move = Mathf.Clamp(x, -1f, 1f);
            if (Mathf.Abs(TouchInput.Move) > Mathf.Abs(peakMove)) peakMove = TouchInput.Move;
            if (v.y < -brakePull && Mathf.Abs(v.x) < 0.6f)
            {
                if (brakeArmed) { TouchInput.BrakeSeq++; brakeArmed = false; Debug.Log("[Touch] stick flick down: brake"); }
            }
            else if (v.y > -brakePull * 0.5f) brakeArmed = true;
        }

        void Release()
        {
            pointer = int.MinValue;
            brakeArmed = true;
            TouchInput.Move = 0f;
            Debug.Log($"[Touch] stick up (peak walk {peakMove:F2}, owner {(TouchInput.Owner != null ? "yes" : "none")})");
            if (knob != null) knob.anchoredPosition = Vector2.zero;
            if (baseRing != null) baseRing.localScale = BaseScale;
        }
    }
}
