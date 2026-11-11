using FartOnline.Obstacles;
using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Whoosh for swinging and sweeping hazards, added at runtime by <see cref="Attach"/>. A pendulum whooshes each
    /// time it passes the bottom of its swing; a sweeper whooshes when its arm tip passes close to the listener.
    /// Only obstacles within <see cref="hearRange"/> of the camera make a sound.
    /// </summary>
    public class ObstacleWhoosh : MonoBehaviour
    {
        public float hearRange = 40f;
        Pendulum pendulum;
        RotatingSweeper sweeper;
        float lastSign;
        float lastTipDist = float.MaxValue;
        bool approaching;

        public static void Attach()
        {
            foreach (var p in FindObjectsByType<Pendulum>())
                if (p.GetComponent<ObstacleWhoosh>() == null) p.gameObject.AddComponent<ObstacleWhoosh>();
            foreach (var s in FindObjectsByType<RotatingSweeper>())
                if (s.GetComponent<ObstacleWhoosh>() == null) s.gameObject.AddComponent<ObstacleWhoosh>();
        }

        void Awake()
        {
            pendulum = GetComponent<Pendulum>();
            sweeper = GetComponent<RotatingSweeper>();
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 listener = cam.transform.position;
            if ((transform.position - listener).sqrMagnitude > hearRange * hearRange) return;

            if (pendulum != null)
            {
                // the swing angle about local X changes sign at the bottom, where the log is fastest
                Vector3 down = transform.rotation * Vector3.down;
                float sign = Mathf.Sign(Vector3.Dot(Vector3.Cross(transform.parent != null ? transform.parent.up : Vector3.up, down), transform.right));
                if (lastSign != 0f && sign != lastSign)
                    GameAudio.Play(AudioCue.PendulumWhoosh, transform.position + down * pendulum.length, 1f, 1);
                lastSign = sign;
            }
            else if (sweeper != null)
            {
                Vector3 tip = transform.position + transform.forward * sweeper.reach;
                float d = Vector3.Distance(tip, listener);
                if (d < lastTipDist) approaching = true;
                else if (approaching && d < 18f) { GameAudio.Play(AudioCue.PendulumWhoosh, tip, 0.8f, 2); approaching = false; }
                else approaching = false;
                lastTipDist = d;
            }
        }
    }
}
