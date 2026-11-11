using System;

namespace FartOnline.Game.Online
{
    // JSON shapes of the Supabase and room-service replies (parsed with JsonUtility: public fields, exact names).

    [Serializable]
    public class AuthSession
    {
        public string access_token;
        public string refresh_token;
        public long expires_in;
        public long expires_at;
        public AuthUser user;
    }

    [Serializable]
    public class AuthUser
    {
        public string id;
        public string email;
    }

    [Serializable]
    public class ApiError
    {
        public string msg, message, error, error_description, error_code, hint;

        public string Text
        {
            get
            {
                string t = !string.IsNullOrEmpty(msg) ? msg : !string.IsNullOrEmpty(error_description) ? error_description
                         : !string.IsNullOrEmpty(message) ? message : error;
                return string.IsNullOrEmpty(t) ? "" : t;
            }
        }
    }

    [Serializable]
    public class Profile
    {
        public string id;
        public string username;
    }

    [Serializable]
    public class FriendEntry
    {
        public string friend_id;
        public string username;
        /// <summary>"pending" or "accepted".</summary>
        public string status;
        /// <summary>Pending: true = they asked me (I can accept), false = I asked them.</summary>
        public bool incoming;
        public long request_id;
        public bool online;

        public bool Accepted => status == "accepted";
    }

    [Serializable]
    public class InviteEntry
    {
        public long invite_id;
        public string from_username;
        public string room_code;
    }

    /// <summary>Room service reply: where the room's game server is and the signed ticket to get in.</summary>
    [Serializable]
    public class RoomTicket
    {
        public string code;
        public string host;
        public int port;
        public bool tls;
        public int players;
        public string ticket;
        public string error;
    }

    [Serializable] class ProfileList { public Profile[] items; }
    [Serializable] class FriendList { public FriendEntry[] items; }
    [Serializable] class InviteList { public InviteEntry[] items; }

    static class Json
    {
        /// <summary>JsonUtility can't read a top-level array: wrap it.</summary>
        public static T[] Array<T, TList>(string json, Func<TList, T[]> items) where TList : class
        {
            if (string.IsNullOrEmpty(json) || json[0] != '[') return new T[0];
            var list = UnityEngine.JsonUtility.FromJson<TList>("{\"items\":" + json + "}");
            return list != null ? items(list) ?? new T[0] : new T[0];
        }

        public static FriendEntry[] Friends(string json) => Array<FriendEntry, FriendList>(json, l => l.items);
        public static InviteEntry[] Invites(string json) => Array<InviteEntry, InviteList>(json, l => l.items);
        public static Profile[] Profiles(string json) => Array<Profile, ProfileList>(json, l => l.items);

        /// <summary>A JSON string scalar ("sent") or a bare value.</summary>
        public static string Scalar(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            json = json.Trim();
            if (json.Length >= 2 && json[0] == '"' && json[json.Length - 1] == '"') return json.Substring(1, json.Length - 2);
            return json;
        }

        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new System.Text.StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Str(string s) => "\"" + Escape(s) + "\"";
    }
}
