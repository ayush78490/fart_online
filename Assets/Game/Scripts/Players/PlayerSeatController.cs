using System;
using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// One seat on the shared raft: the character anchor, the thrust origin, the seat's thrust controller and whoever
    /// occupies it (local player, remote player or AI crew). Characters are parented to the seat and carry no
    /// colliders or rigidbodies, so they can never push the raft around.
    /// </summary>
    [RequireComponent(typeof(FartThrustController))]
    public class PlayerSeatController : MonoBehaviour
    {
        public int seatIndex;
        public Transform characterAnchor;
        public GameObject character;
        public CharacterThrustAnimator characterAnimator;

        public FartThrustController Thrust => thrust != null ? thrust : (thrust = GetComponent<FartThrustController>());
        FartThrustController thrust;
        public bool IsOccupied { get; private set; }
        public bool IsLocal { get; private set; }
        public bool IsAI { get; private set; }
        public ulong OwnerClientId { get; private set; }
        public string PlayerName { get; private set; } = "";
        public Color PlayerColor { get; private set; } = Color.white;
        public IThrustInputSource InputSource { get; private set; }

        /// <summary>Position on the rim: degrees clockwise from the bow (0 = front, 90 = right, 180 = back).</summary>
        public float Angle { get; private set; }
        /// <summary>Current rim movement −1..1 (authority: from input; clients: replicated).</summary>
        public float Moving { get; set; }

        public event Action<PlayerSeatController> OccupancyChanged;

        /// <summary>Places the seat on the rim circle, facing the raft centre (the gas then leaves outward).</summary>
        public void SetAngle(float angleDeg, float radius, float height)
        {
            Angle = RimMath.Wrap(angleDeg);
            Vector2 o = RimMath.Outward(Angle);
            transform.localPosition = new Vector3(o.x * radius, height, o.y * radius);
            transform.localRotation = Quaternion.LookRotation(new Vector3(-o.x, 0f, -o.y), Vector3.up);
        }

        PlayerInputState remoteInput;

        void Awake()
        {
            Thrust.seatIndex = seatIndex;
            if (character != null) character.SetActive(false);
        }

        public void Occupy(string playerName, Color color, IThrustInputSource source, bool isLocal, bool isAI, ulong clientId)
        {
            IsOccupied = true;
            PlayerName = playerName;
            PlayerColor = color;
            InputSource = source;
            IsLocal = isLocal;
            IsAI = isAI;
            OwnerClientId = clientId;
            remoteInput = PlayerInputState.Idle;
            Thrust.ResetSeat();
            if (character != null) character.SetActive(true);
            if (characterAnimator != null) characterAnimator.SetPlayerColor(color);
            OccupancyChanged?.Invoke(this);
        }

        public void Vacate()
        {
            if (!IsOccupied) return;
            IsOccupied = false;
            InputSource = null;
            IsLocal = IsAI = false;
            remoteInput = PlayerInputState.Idle;   // a leaving player's thrust stops immediately
            Thrust.ResetSeat();
            if (character != null) character.SetActive(false);
            OccupancyChanged?.Invoke(this);
        }

        /// <summary>Network layer: latest input received from the owning client (server side).</summary>
        public void SetRemoteInput(PlayerInputState s) => remoteInput = s;

        public PlayerInputState ReadInput() => InputSource != null ? InputSource.ReadInput() : remoteInput;
    }
}
