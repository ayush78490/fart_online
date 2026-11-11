using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Test-only helper: starts a normal solo run and then seats bot drivers (<see cref="AICrewInput"/>) in seats
    /// 1..crew so the probes and the trailer recorder can play hands-free. The game never does this; only real
    /// players sit on the raft. The bots are children of the session, so <c>GameSession.EndLocal</c> removes them.
    /// </summary>
    public static class TestCrew
    {
        public static void StartSolo(GameSession session, SoloAssist assist, int crew, float skill = 0.5f)
        {
            session.StartSolo(assist);
            var raft = session.raft;
            crew = Mathf.Clamp(crew, 0, raft.seats.Seats.Length - 1);
            for (int i = 1; i <= crew; i++) Add(session, i, skill);
        }

        public static AICrewInput Add(GameSession session, int seatIndex, float skill = 0.5f)
        {
            var raft = session.raft;
            var go = new GameObject("TestBot_" + (seatIndex + 1));
            go.transform.SetParent(session.transform, false);
            var ai = go.AddComponent<AICrewInput>();
            var seat = raft.seats.Seats[seatIndex];
            ai.ReadyFor(raft, seat, skill);
            seat.Occupy("Bot " + seatIndex, RaftSeatManager.PlayerColors[seatIndex], ai, false, true, 0);
            return ai;
        }
    }
}
