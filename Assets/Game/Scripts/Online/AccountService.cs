using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FartOnline.Game.Online
{
    /// <summary>
    /// The player's online account (Supabase Auth + the functions in Backend/supabase/schema.sql), over plain HTTPS so
    /// it works the same on Android, desktop and the web build:
    ///  - sign up (email + password + unique username), log in, log out; the refresh token is kept in PlayerPrefs so
    ///    the player stays signed in; the access token is refreshed 5 minutes before it expires;
    ///  - friends: add by username, accept / decline requests, remove; the list is polled (every 4 s while the friends
    ///    board is open, every 20 s otherwise) with an "online" flag from a 30 s heartbeat;
    ///  - invites: polled every 5 s; <see cref="InviteReceived"/> fires once per new invite;
    ///  - rooms: asks the room service (AWS) to create / join a room and returns the signed join ticket.
    /// Single player never touches any of this; online play requires <see cref="SignedIn"/>.
    /// </summary>
    public class AccountService : MonoBehaviour
    {
        const string RefreshKey = "fo.auth.refresh", EmailKey = "fo.auth.email";
        public static AccountService Instance { get; private set; }

        public static AccountService Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AccountService");
            DontDestroyOnLoad(go);
            return go.AddComponent<AccountService>();
        }

        AuthSession session;
        float sessionExpiresAt;
        readonly List<FriendEntry> friends = new List<FriendEntry>();
        readonly List<InviteEntry> invites = new List<InviteEntry>();
        readonly HashSet<long> seenInvites = new HashSet<long>();
        Coroutine poller;

        public bool SignedIn => session != null && !string.IsNullOrEmpty(session.access_token) && !string.IsNullOrEmpty(Username);
        public bool Restoring { get; private set; }
        public string Username { get; private set; } = "";
        public string UserId { get; private set; } = "";
        public string Email => PlayerPrefs.GetString(EmailKey, "");
        public IReadOnlyList<FriendEntry> Friends => friends;
        public IReadOnlyList<InviteEntry> Invites => invites;
        public int IncomingRequests { get { int n = 0; foreach (var f in friends) if (!f.Accepted && f.incoming) n++; return n; } }
        public int OnlineFriends { get { int n = 0; foreach (var f in friends) if (f.Accepted && f.online) n++; return n; } }
        /// <summary>The friends board is open: poll faster.</summary>
        public bool FriendsVisible { get; set; }
        public static BackendConfig Config => BackendConfig.Instance;

        /// <summary>Sign-in state, friends or invites changed.</summary>
        public event Action Changed;
        public event Action<InviteEntry> InviteReceived;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            string refresh = PlayerPrefs.GetString(RefreshKey, "");
            if (!string.IsNullOrEmpty(refresh) && Config.HasSupabase) StartCoroutine(Restore(refresh));
        }

        // ------------------------------------------------------------------ HTTP
        public struct Reply
        {
            public long status;
            public string text;
            public bool Ok => status >= 200 && status < 300;
            public string Error
            {
                get
                {
                    if (status == 0) return "No connection to the server";
                    var e = string.IsNullOrEmpty(text) ? null : SafeJson<ApiError>(text);
                    string t = e != null ? e.Text : "";
                    return Friendly(string.IsNullOrEmpty(t) ? "Server error " + status : t);
                }
            }
        }

        static T SafeJson<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); } catch { return null; }
        }

        static string Friendly(string t)
        {
            string l = t.ToLowerInvariant();
            if (l.Contains("invalid login credentials")) return "Wrong email or password";
            if (l.Contains("email not confirmed")) return "Please confirm your email first (check your inbox), then log in";
            if (l.Contains("already registered") || l.Contains("already been registered")) return "That email already has an account: log in instead";
            if (l.Contains("database error saving new user") || l.Contains("invalid_username")) return "That username is taken or invalid";
            if (l.Contains("password should be")) return "The password must be at least 6 characters";
            if (l.Contains("unable to validate email") || l.Contains("invalid format")) return "That email address doesn't look right";
            if (l.Contains("rate limit")) return "Too many attempts, wait a minute and try again";
            if (l.Contains("jwt expired")) return "Your login expired, please log in again";
            return t;
        }

        IEnumerator Send(string method, string url, string body, bool auth, Action<Reply> done)
        {
            using (var req = new UnityWebRequest(url, method))
            {
                if (body != null) req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 20;
                req.SetRequestHeader("Content-Type", "application/json");
                if (url.StartsWith(Config.SupabaseUrl, StringComparison.Ordinal)) req.SetRequestHeader("apikey", Config.supabaseAnonKey);
                string bearer = auth && session != null ? session.access_token : Config.supabaseAnonKey;
                if (auth || url.StartsWith(Config.SupabaseUrl, StringComparison.Ordinal)) req.SetRequestHeader("Authorization", "Bearer " + bearer);
                yield return req.SendWebRequest();
                done(new Reply { status = req.responseCode, text = req.downloadHandler != null ? req.downloadHandler.text : "" });
            }
        }

        /// <summary>Supabase RPC (security-definer function) as the signed-in player (or anon).</summary>
        IEnumerator Rpc(string fn, string argsJson, Action<Reply> done, bool auth = true)
        {
            if (auth) yield return FreshToken();
            yield return Send("POST", Config.SupabaseUrl + "/rest/v1/rpc/" + fn, string.IsNullOrEmpty(argsJson) ? "{}" : argsJson, auth, done);
        }

        // ------------------------------------------------------------------ session
        void SetSession(AuthSession s)
        {
            session = s;
            sessionExpiresAt = Time.realtimeSinceStartup + Mathf.Max(60f, s.expires_in > 0 ? s.expires_in : 3600f);
            PlayerPrefs.SetString(RefreshKey, s.refresh_token ?? "");
            if (s.user != null && !string.IsNullOrEmpty(s.user.email)) PlayerPrefs.SetString(EmailKey, s.user.email);
            PlayerPrefs.Save();
            UserId = s.user != null ? s.user.id : UserId;
        }

        void ClearSession()
        {
            session = null;
            Username = UserId = "";
            friends.Clear();
            invites.Clear();
            seenInvites.Clear();
            PlayerPrefs.DeleteKey(RefreshKey);
            PlayerPrefs.Save();
            if (poller != null) { StopCoroutine(poller); poller = null; }
            Changed?.Invoke();
        }

        IEnumerator Restore(string refresh)
        {
            Restoring = true;
            Changed?.Invoke();
            bool ok = false;
            yield return Send("POST", Config.SupabaseUrl + "/auth/v1/token?grant_type=refresh_token", "{\"refresh_token\":" + Json.Str(refresh) + "}", false, r =>
            {
                var s = r.Ok ? SafeJson<AuthSession>(r.text) : null;
                if (s != null && !string.IsNullOrEmpty(s.access_token)) { SetSession(s); ok = true; }
                else if (r.status >= 400 && r.status < 500) PlayerPrefs.DeleteKey(RefreshKey);   // revoked: forget it
            });
            if (ok) yield return LoadProfile();
            Restoring = false;
            Changed?.Invoke();
        }

        IEnumerator LoadProfile()
        {
            bool ok = false;
            yield return Rpc("my_profile", "{}", r =>
            {
                var p = r.Ok ? Json.Profiles(r.text) : new Profile[0];
                if (p.Length > 0) { Username = p[0].username; UserId = p[0].id; ok = true; }
            });
            if (!ok) { session = null; Username = ""; yield break; }
            if (poller != null) StopCoroutine(poller);
            poller = StartCoroutine(Poll());
        }

        /// <summary>Refreshes the access token when it has less than 5 minutes left.</summary>
        IEnumerator FreshToken()
        {
            if (session == null || Time.realtimeSinceStartup < sessionExpiresAt - 300f) yield break;
            yield return Send("POST", Config.SupabaseUrl + "/auth/v1/token?grant_type=refresh_token", "{\"refresh_token\":" + Json.Str(session.refresh_token) + "}", false, r =>
            {
                var s = r.Ok ? SafeJson<AuthSession>(r.text) : null;
                if (s != null && !string.IsNullOrEmpty(s.access_token)) SetSession(s);
            });
        }

        public void SignIn(string email, string password, Action<bool, string> done)
        {
            if (!Config.HasSupabase) { done(false, "Online accounts are not set up in this build"); return; }
            StartCoroutine(SignInRoutine(email.Trim(), password, done));
        }

        IEnumerator SignInRoutine(string email, string password, Action<bool, string> done)
        {
            string err = null;
            AuthSession got = null;
            yield return Send("POST", Config.SupabaseUrl + "/auth/v1/token?grant_type=password",
                              "{\"email\":" + Json.Str(email) + ",\"password\":" + Json.Str(password) + "}", false, r =>
            {
                got = r.Ok ? SafeJson<AuthSession>(r.text) : null;
                if (got == null || string.IsNullOrEmpty(got.access_token)) err = r.Error;
            });
            if (err != null) { done(false, err); yield break; }
            SetSession(got);
            yield return LoadProfile();
            Changed?.Invoke();
            done(SignedIn, SignedIn ? "" : "This account has no player profile");
        }

        public static bool ValidUsername(string u) => !string.IsNullOrEmpty(u) && System.Text.RegularExpressions.Regex.IsMatch(u, "^[A-Za-z0-9_]{3,16}$");

        public void SignUp(string email, string password, string username, Action<bool, string> done)
        {
            if (!Config.HasSupabase) { done(false, "Online accounts are not set up in this build"); return; }
            username = (username ?? "").Trim();
            if (!ValidUsername(username)) { done(false, "Username: 3-16 letters, numbers or _"); return; }
            if (string.IsNullOrEmpty(password) || password.Length < 6) { done(false, "The password must be at least 6 characters"); return; }
            StartCoroutine(SignUpRoutine(email.Trim(), password, username, done));
        }

        IEnumerator SignUpRoutine(string email, string password, string username, Action<bool, string> done)
        {
            bool free = false;
            string err = null;
            yield return Rpc("username_available", "{\"name\":" + Json.Str(username) + "}", r => { if (r.Ok) free = Json.Scalar(r.text) == "true"; else err = r.Error; }, false);
            if (err != null) { done(false, err); yield break; }
            if (!free) { done(false, "The username '" + username + "' is taken"); yield break; }
            AuthSession got = null;
            yield return Send("POST", Config.SupabaseUrl + "/auth/v1/signup",
                              "{\"email\":" + Json.Str(email) + ",\"password\":" + Json.Str(password) + ",\"data\":{\"username\":" + Json.Str(username) + "}}", false, r =>
            {
                if (!r.Ok) { err = r.Error; return; }
                got = SafeJson<AuthSession>(r.text);
            });
            if (err != null) { done(false, err); yield break; }
            PlayerPrefs.SetString(EmailKey, email);
            if (got == null || string.IsNullOrEmpty(got.access_token))
            {
                done(true, "Account created! Check your email to confirm it, then log in.");
                yield break;
            }
            SetSession(got);
            yield return LoadProfile();
            Changed?.Invoke();
            done(SignedIn, SignedIn ? "" : "Account created, but the profile could not be loaded. Try logging in.");
        }

        public void SignOut()
        {
            if (session != null && Config.HasSupabase) StartCoroutine(Send("POST", Config.SupabaseUrl + "/auth/v1/logout", "{}", true, _ => { }));
            ClearSession();
        }

        // ------------------------------------------------------------------ polling
        IEnumerator Poll()
        {
            float nextFriends = 0f, nextInvites = 0f, nextBeat = 0f;
            while (session != null)
            {
                float now = Time.realtimeSinceStartup;
                if (now >= nextBeat) { nextBeat = now + 30f; yield return Rpc("heartbeat", "{}", _ => { }); }
                if (now >= nextInvites) { nextInvites = now + 5f; yield return LoadInvites(); }
                if (now >= nextFriends || FriendsVisible && now >= nextFriends - 16f)
                {
                    nextFriends = now + 20f;
                    yield return LoadFriends();
                }
                yield return new WaitForSecondsRealtime(FriendsVisible ? 1f : 2f);
            }
        }

        IEnumerator LoadFriends()
        {
            yield return Rpc("list_friends", "{}", r =>
            {
                if (!r.Ok) return;
                friends.Clear();
                friends.AddRange(Json.Friends(r.text));
                Changed?.Invoke();
            });
        }

        IEnumerator LoadInvites()
        {
            yield return Rpc("my_invites", "{}", r =>
            {
                if (!r.Ok) return;
                invites.Clear();
                invites.AddRange(Json.Invites(r.text));
                foreach (var i in invites)
                    if (seenInvites.Add(i.invite_id)) InviteReceived?.Invoke(i);
                Changed?.Invoke();
            });
        }

        public void RefreshNow() { if (SignedIn) { StartCoroutine(LoadFriends()); StartCoroutine(LoadInvites()); } }

        // ------------------------------------------------------------------ friends
        /// <summary>Result text for the player ("Request sent to bob").</summary>
        public void AddFriend(string username, Action<bool, string> done)
        {
            username = (username ?? "").Trim().TrimStart('@');
            if (!SignedIn) { done(false, "Log in first"); return; }
            if (!ValidUsername(username)) { done(false, "Usernames are 3-16 letters, numbers or _"); return; }
            StartCoroutine(Rpc("send_friend_request", "{\"target_username\":" + Json.Str(username) + "}", r =>
            {
                if (!r.Ok) { done(false, r.Error); return; }
                string s = Json.Scalar(r.text);
                switch (s)
                {
                    case "sent": done(true, "Friend request sent to " + username); break;
                    case "accepted": done(true, "You and " + username + " are now friends!"); break;
                    case "already_friends": done(false, "You're already friends with " + username); break;
                    case "already_sent": done(false, "You already asked " + username + ": waiting for them"); break;
                    case "not_found": done(false, "No player called '" + username + "'"); break;
                    case "self": done(false, "That's you!"); break;
                    default: done(false, s); break;
                }
                StartCoroutine(LoadFriends());
            }));
        }

        public void RespondFriend(FriendEntry f, bool accept, Action<bool, string> done = null)
        {
            StartCoroutine(Rpc("respond_friend_request", "{\"request_id\":" + f.request_id + ",\"accept\":" + (accept ? "true" : "false") + "}", r =>
            {
                done?.Invoke(r.Ok, r.Ok ? (accept ? "You and " + f.username + " are now friends!" : "Request declined") : r.Error);
                StartCoroutine(LoadFriends());
            }));
        }

        public void RemoveFriend(FriendEntry f, Action<bool, string> done = null)
        {
            StartCoroutine(Rpc("remove_friend", "{\"friend_id\":" + Json.Str(f.friend_id) + "}", r =>
            {
                done?.Invoke(r.Ok, r.Ok ? (f.Accepted ? f.username + " removed" : "Request cancelled") : r.Error);
                StartCoroutine(LoadFriends());
            }));
        }

        // ------------------------------------------------------------------ invites
        public void Invite(FriendEntry f, string roomCode, Action<bool, string> done)
        {
            StartCoroutine(Rpc("send_invite", "{\"friend_id\":" + Json.Str(f.friend_id) + ",\"room\":" + Json.Str(roomCode) + "}", r =>
            {
                if (!r.Ok) { done(false, r.Error); return; }
                string s = Json.Scalar(r.text);
                done(s == "sent", s == "sent" ? "Invite sent to " + f.username : s == "not_friends" ? f.username + " isn't your friend yet" : s);
            }));
        }

        public void RespondInvite(InviteEntry i, bool accept)
        {
            invites.Remove(i);
            Changed?.Invoke();
            StartCoroutine(Rpc("respond_invite", "{\"invite_id\":" + i.invite_id + ",\"accept\":" + (accept ? "true" : "false") + "}", _ => { }));
        }

        // ------------------------------------------------------------------ rooms (AWS room service)
        public void CreateRoom(Action<RoomTicket> done) => StartCoroutine(RoomRequest("/rooms", "{}", done));
        public void JoinRoom(string code, Action<RoomTicket> done) =>
            StartCoroutine(RoomRequest("/rooms/join", "{\"code\":" + Json.Str((code ?? "").Trim().ToUpperInvariant()) + "}", done));

        IEnumerator RoomRequest(string path, string body, Action<RoomTicket> done)
        {
            if (!SignedIn) { done(new RoomTicket { error = "Log in to play online" }); yield break; }
            yield return FreshToken();
            yield return Send("POST", Config.RoomServiceUrl + path, body, true, r =>
            {
                var t = string.IsNullOrEmpty(r.text) ? null : SafeJson<RoomTicket>(r.text);
                if (r.Ok && t != null && !string.IsNullOrEmpty(t.ticket)) { done(t); return; }
                string e = t != null && !string.IsNullOrEmpty(t.error) ? t.error : r.status == 0 ? "Can't reach the game server" : "Game server error " + r.status;
                done(new RoomTicket { error = e });
            });
        }
    }
}
