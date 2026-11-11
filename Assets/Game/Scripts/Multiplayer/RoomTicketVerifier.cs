using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FartOnline.Game.Net
{
    /// <summary>
    /// Checks the join tickets signed by the room service (Backend/room-service/server.js, makeTicket):
    ///   base64url(JSON {r: room code, u: user id, n: username, o: 1 if room owner, e: expiry unix s}) "." base64url(HMAC-SHA256)
    /// The secret reaches the dedicated server through the FO_TICKET_SECRET environment variable; it never ships in
    /// a client build.
    /// </summary>
    public static class RoomTicketVerifier
    {
        [Serializable]
        public class Ticket
        {
            public string r;
            public string u;
            public string n;
            public int o;
            public long e;

            public string Room => r;
            public string UserId => u;
            public string Username => n;
            public bool Owner => o == 1;
        }

        public static bool Verify(string ticket, string secret, string room, out Ticket info, out string error)
        {
            info = null;
            error = "Invalid ticket";
            if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(secret)) return false;
            int dot = ticket.IndexOf('.');
            if (dot <= 0 || dot == ticket.Length - 1) return false;
            string body = ticket.Substring(0, dot), sig = ticket.Substring(dot + 1);
            byte[] expected;
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret))) expected = h.ComputeHash(Encoding.ASCII.GetBytes(body));
            byte[] given = FromB64Url(sig);
            if (given == null || !FixedTimeEquals(expected, given)) return false;
            byte[] json = FromB64Url(body);
            if (json == null) return false;
            try { info = JsonUtility.FromJson<Ticket>(Encoding.UTF8.GetString(json)); } catch { return false; }
            if (info == null || string.IsNullOrEmpty(info.u) || string.IsNullOrEmpty(info.n)) return false;
            if (!string.Equals(info.r, room, StringComparison.Ordinal)) { error = "That ticket is for another room"; return false; }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (info.e < now) { error = "The join ticket expired: join again"; return false; }
            error = "";
            return true;
        }

        static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        static byte[] FromB64Url(string s)
        {
            try
            {
                string b = s.Replace('-', '+').Replace('_', '/');
                switch (b.Length % 4) { case 2: b += "=="; break; case 3: b += "="; break; case 1: return null; }
                return Convert.FromBase64String(b);
            }
            catch { return null; }
        }
    }
}
