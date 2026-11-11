using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// Bank cannon that lobs a cannonball onto a marked spot in the river. The red target ring on the water tightens
    /// while the cannon aims (the warning), the ball arcs over and hits the ring, and a splash column bursts up.
    /// Deadly parts: the ball on the last quarter of its fall and the first moments of the splash, both inside the
    /// target circle (the ball never falls outside it). Driven by the shared obstacle clock.
    /// Local space: the cannon stands at the origin facing +Z; the target is <see cref="targetDistance"/> m along +Z.
    /// </summary>
    public class MortarObstacle : KinematicObstacle
    {
        public Transform barrel, ball, target, splash;
        public Collider ballCollider, splashCollider;
        [Tooltip("Muzzle position in local space (the ball starts here)")] public Vector3 muzzle = new Vector3(0f, 2.1f, 1.2f);
        [Tooltip("Target distance along local +Z (m)")] public float targetDistance = 6f;
        public float arcHeight = 6.5f;
        public float period = 3.8f;
        [Tooltip("Aiming time before each shot (the warning)")] public float aimTime = 1.1f;
        public float flightTime = 1.35f, splashTime = 0.5f;
        [Tooltip("Size of the target ring and splash (1 = built size)")] public float zoneScale = 1f;

        Vector3 barrelRest;
        bool captured;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localOffset = Vector3.zero;
            localRotation = Quaternion.identity;
        }

        protected override void Animate(float t)
        {
            if (!captured && barrel != null) { barrelRest = barrel.localPosition; captured = true; }
            float u = Mathf.Repeat(t, Mathf.Max(2f, period));
            float fire = aimTime, land = fire + flightTime, dry = land + splashTime;
            Vector3 tgt = new Vector3(0f, 0.06f, targetDistance);

            if (target != null)
            {
                target.localPosition = tgt;
                float lockOn = Mathf.Clamp01(u / land);
                float s = u < land ? Mathf.Lerp(1.55f, 1f, lockOn) * (1f + 0.05f * Mathf.Sin(t * Mathf.Lerp(8f, 30f, lockOn))) : 1f;
                target.localScale = new Vector3(s, 1f, s) * zoneScale;
            }
            if (barrel != null)
            {
                float kick = u >= fire && u < fire + 0.35f ? Mathf.Sin((u - fire) / 0.35f * Mathf.PI) : 0f;
                barrel.localPosition = barrelRest + new Vector3(0f, -0.05f, -0.35f) * kick;
            }
            if (ball != null)
            {
                bool flying = u >= fire && u < land;
                if (ball.gameObject.activeSelf != flying) ball.gameObject.SetActive(flying);
                if (flying)
                {
                    float s = (u - fire) / flightTime;
                    ball.localPosition = Vector3.Lerp(muzzle, tgt, s) + Vector3.up * arcHeight * 4f * s * (1f - s);
                    if (ballCollider != null) ballCollider.enabled = s > 0.75f;
                }
            }
            if (splash != null)
            {
                bool on = u >= land && u < dry;
                if (splash.gameObject.activeSelf != on) splash.gameObject.SetActive(on);
                if (on)
                {
                    float s = (u - land) / splashTime;
                    splash.localPosition = tgt;
                    float w = Mathf.Lerp(0.5f, 1.15f, Mathf.Sqrt(s));
                    splash.localScale = new Vector3(w, Mathf.Sin(Mathf.Clamp01(s * 1.4f) * Mathf.PI) * 1.2f + 0.05f, w) * zoneScale;
                    if (splashCollider != null) splashCollider.enabled = s < 0.6f;
                }
            }
        }
    }
}
