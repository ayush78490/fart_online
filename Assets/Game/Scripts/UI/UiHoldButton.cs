using UnityEngine;
using UnityEngine.EventSystems;

namespace FartOnline.Game
{
    /// <summary>A UI element that reports while a finger / mouse button holds it (push-to-talk).</summary>
    public class UiHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }

        public void OnPointerDown(PointerEventData e) => Held = true;
        public void OnPointerUp(PointerEventData e) => Held = false;
        public void OnPointerExit(PointerEventData e) => Held = false;     // finger slid off: stop talking
        void OnDisable() => Held = false;
    }
}
