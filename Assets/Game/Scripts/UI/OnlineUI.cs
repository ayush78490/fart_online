using System.Collections.Generic;
using FartOnline.Game.Net;
using FartOnline.Game.Online;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FartOnline.Game
{
    /// <summary>
    /// The online screens, built on the HUD canvas with the same UI kit:
    ///  - ACCOUNT: log in (email + password) or sign up (email + password + username). Needed only for online play;
    ///  - PLAY ONLINE: create a room on the game server, or join one with its code;
    ///  - FRIENDS: add a friend by username, accept / decline requests, see who is online, invite friends to the room
    ///    you are in, remove friends;
    ///  - an invite pop-up ("bob invited you to raft!  Join / Later") whenever a friend invites you;
    ///  - the TALK button (push-to-talk voice chat; hold V on a keyboard).
    /// Hooks into <see cref="GameplayHUD"/> (Play Online / Friends on the menu, Invite in the lobby).
    /// </summary>
    public class OnlineUI : MonoBehaviour
    {
        GameplayHUD hud;
        GameRunManager run;
        AccountService account;
        NetworkSessionManager net;

        RectTransform accountLayer, onlineLayer, friendsLayer, inviteToast, talkButton;
        InputField emailField, passwordField, usernameField, codeField, addFriendField;
        RectTransform usernameRow;
        Text accountTitleHint, accountStatus, accountPrimaryLabel, accountSwitchLabel;
        Text onlineWho, onlineStatus, onlineFriendsLabel;
        Text friendsStatus, friendsMe, friendsEmpty;
        Text inviteText;
        RectTransform listContent;
        Image talkImage;
        Text talkLabel;
        UiHoldButton talkHold;
        bool signUpMode;
        bool busy;
        bool afterLoginFriends;
        InviteEntry shownInvite;
        readonly List<GameObject> rows = new List<GameObject>();
        bool rowsDirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fo-dedicated") >= 0) return;   // headless room server
            var h = FindAnyObjectByType<GameplayHUD>();
            if (h == null || h.GetComponent<OnlineUI>() != null) return;
            h.gameObject.AddComponent<OnlineUI>();
        }

        void Start()
        {
            hud = GetComponent<GameplayHUD>();
            run = hud.run;
            net = NetworkSessionManager.Instance;
            account = AccountService.Ensure();
            account.Changed += () => rowsDirty = true;
            account.InviteReceived += OnInvite;

            hud.PlayOnlineRequested = () => Gate(false);
            hud.FriendsRequested = () => Gate(true);
            hud.InviteFriendsRequested = () => ShowFriends();
            hud.AccountLine = AccountLine;
            hud.FriendsBadge = () => account.SignedIn ? account.IncomingRequests + account.Invites.Count : 0;

            BuildAccount();
            BuildOnline();
            BuildFriends();
            BuildInviteToast();
            BuildTalk();
            run.StateChanged += (a, b) => { if (b == RunState.Lobby || b == RunState.Countdown) HideAll(); };
        }

        string AccountLine()
        {
            if (!AccountService.Config.HasSupabase) return "Online accounts are not configured in this build";
            if (account.Restoring) return "Signing in...";
            if (!account.SignedIn) return "Not logged in: Play Online asks you to log in or sign up";
            int on = account.OnlineFriends;
            return "Logged in as " + account.Username + (on > 0 ? "  ·  " + on + " friend" + (on == 1 ? "" : "s") + " online" : "");
        }

        // ------------------------------------------------------------------ flow
        /// <summary>Online needs an account: show the login board first if needed.</summary>
        void Gate(bool friends)
        {
            if (!AccountService.Config.HasSupabase) { ShowAccount(); SetStatus(accountStatus, "Online accounts are not set up in this build (no Supabase keys).", true); return; }
            if (account.Restoring) return;
            if (!account.SignedIn) { afterLoginFriends = friends; ShowAccount(); return; }
            if (friends) ShowFriends(); else ShowOnline();
        }

        void ShowAccount()
        {
            HideAll();
            Show(accountLayer, true);
            if (string.IsNullOrEmpty(emailField.text)) emailField.text = account.Email;
            SetStatus(accountStatus, "", false);
            RefreshAccountMode();
        }

        void ShowOnline()
        {
            HideAll();
            Show(onlineLayer, true);
            onlineWho.text = "Logged in as  " + account.Username;
            SetStatus(onlineStatus, "", false);
            account.RefreshNow();
        }

        void ShowFriends()
        {
            if (!account.SignedIn) { Gate(true); return; }
            HideAll();
            Show(friendsLayer, true);
            friendsMe.text = "Your username:  " + account.Username + "   (friends add you with it)";
            SetStatus(friendsStatus, "", false);
            account.FriendsVisible = true;
            account.RefreshNow();
            rowsDirty = true;
        }

        void HideAll()
        {
            Show(accountLayer, false);
            Show(onlineLayer, false);
            Show(friendsLayer, false);
            if (account != null) account.FriendsVisible = false;
        }

        static void Show(RectTransform rt, bool on)
        {
            if (rt == null || rt.gameObject.activeSelf == on) return;
            rt.gameObject.SetActive(on);
            if (on) rt.SetAsLastSibling();
        }

        static void SetStatus(Text t, string msg, bool bad)
        {
            if (t == null) return;
            t.text = msg;
            t.color = bad ? UiKit.Danger : UiKit.WoodDark;
        }

        // ------------------------------------------------------------------ account board
        void BuildAccount()
        {
            accountLayer = Layer("Online_Account");
            var p = UiKit.Board(accountLayer, "Panel", "ACCOUNT", Vector2.zero, new Vector2(720f, 820f));
            accountTitleHint = UiKit.Para(p, "Hint", "", 26, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, 300f), new Vector2(620f, 70f));
            UiKit.Caption(p, "EMAIL", new Vector2(0f, 240f), 500f);
            emailField = Field(p, "Email", new Vector2(0f, 192f), 520f, "you@example.com", InputField.ContentType.EmailAddress, 80);
            UiKit.Caption(p, "PASSWORD", new Vector2(0f, 130f), 500f);
            passwordField = Field(p, "Password", new Vector2(0f, 82f), 520f, "at least 6 characters", InputField.ContentType.Password, 72);
            usernameRow = UiKit.At(p, "UsernameRow", new Vector2(0f, -8f), new Vector2(600f, 120f));
            UiKit.Caption(usernameRow, "USERNAME (friends find you by it)", new Vector2(0f, 30f), 560f);
            usernameField = Field(usernameRow, "Username", new Vector2(0f, -18f), 520f, "3-16 letters, numbers or _", InputField.ContentType.Standard, 16);
            usernameField.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '\0';
            var primary = UiKit.Button(p, "Log In", new Vector2(0f, -120f), new Vector2(420f, 96f), SubmitAccount, UiStyle.Green, null, 38);
            accountPrimaryLabel = primary.GetComponentInChildren<Text>();
            accountStatus = UiKit.Para(p, "Status", "", 24, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, -200f), new Vector2(640f, 70f));
            UiKit.Button(p, "Back", new Vector2(-190f, -300f), new Vector2(220f, 80f), () => HideAll(), UiStyle.Red, null, 32);
            var sw = UiKit.Button(p, "Sign up instead", new Vector2(110f, -300f), new Vector2(360f, 80f), () => { signUpMode = !signUpMode; RefreshAccountMode(); }, UiStyle.Wood, null, 28);
            accountSwitchLabel = sw.GetComponentInChildren<Text>();
            accountLayer.gameObject.SetActive(false);
        }

        void RefreshAccountMode()
        {
            usernameRow.gameObject.SetActive(signUpMode);
            accountPrimaryLabel.text = signUpMode ? "Create Account" : "Log In";
            accountSwitchLabel.text = signUpMode ? "I have an account" : "New? Sign up";
            accountTitleHint.text = signUpMode ? "Make an account to play online with friends.\nSingle player never needs one."
                                               : "Log in to play online and add friends.\nSingle player never needs an account.";
        }

        void SubmitAccount()
        {
            if (busy) return;
            string email = emailField.text.Trim(), pw = passwordField.text;
            if (email.Length < 5 || !email.Contains("@")) { SetStatus(accountStatus, "Enter your email address", true); return; }
            if (pw.Length < 6) { SetStatus(accountStatus, "The password must be at least 6 characters", true); return; }
            busy = true;
            SetStatus(accountStatus, signUpMode ? "Creating your account..." : "Logging in...", false);
            System.Action<bool, string> done = (ok, msg) =>
            {
                busy = false;
                if (!ok) { SetStatus(accountStatus, msg, true); return; }
                passwordField.text = "";
                if (!account.SignedIn)
                {
                    // e-mail confirmation is switched on in Supabase: log in after confirming
                    signUpMode = false;
                    RefreshAccountMode();
                    SetStatus(accountStatus, msg, false);
                    return;
                }
                if (afterLoginFriends) ShowFriends(); else ShowOnline();
            };
            if (signUpMode) account.SignUp(email, pw, usernameField.text, done);
            else account.SignIn(email, pw, done);
        }

        // ------------------------------------------------------------------ play online board
        void BuildOnline()
        {
            onlineLayer = Layer("Online_Play");
            var p = UiKit.Board(onlineLayer, "Panel", "PLAY ONLINE", Vector2.zero, new Vector2(760f, 820f));
            onlineWho = UiKit.Para(p, "Who", "", 30, TextAnchor.MiddleCenter, UiKit.Ink, new Vector2(0f, 300f), new Vector2(660f, 50f));
            UiKit.Button(p, "Create Room", new Vector2(0f, 205f), new Vector2(460f, 104f), CreateRoom, UiStyle.Green, "Icon_Play", 40);
            UiKit.Para(p, "CreateHint", "You get a room code. Invite friends from the lobby.", 22, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, 135f), new Vector2(660f, 34f));
            UiKit.Divider(p, new Vector2(0f, 95f), 640f);
            UiKit.Caption(p, "JOIN A FRIEND'S ROOM", new Vector2(0f, 60f), 600f);
            codeField = Field(p, "Code", new Vector2(-110f, 0f), 340f, "ROOM CODE", InputField.ContentType.Alphanumeric, 8);
            codeField.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) && c < 128 ? char.ToUpperInvariant(c) : '\0';
            UiKit.Button(p, "Join", new Vector2(200f, 0f), new Vector2(200f, 84f), () => JoinRoom(codeField.text), UiStyle.Wood, null, 34);
            UiKit.Divider(p, new Vector2(0f, -66f), 640f);
            var fb = UiKit.Button(p, "Friends", new Vector2(0f, -128f), new Vector2(460f, 88f), ShowFriends, UiStyle.Wood, null, 34);
            onlineFriendsLabel = fb.GetComponentInChildren<Text>();
            onlineStatus = UiKit.Para(p, "Status", "", 24, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, -205f), new Vector2(660f, 64f));
            UiKit.Button(p, "Back", new Vector2(-170f, -300f), new Vector2(260f, 80f), () => HideAll(), UiStyle.Red, null, 32);
            UiKit.Button(p, "Log Out", new Vector2(170f, -300f), new Vector2(260f, 80f), LogOut, UiStyle.Wood, null, 30);
            onlineLayer.gameObject.SetActive(false);
        }

        void LogOut()
        {
            if (net != null && net.IsOnline) net.Leave();
            account.SignOut();
            HideAll();
            hud.Toast("Logged out");
        }

        void CreateRoom()
        {
            if (busy) return;
            if (net == null) { SetStatus(onlineStatus, "Online play is not available in this build", true); return; }
            busy = true;
            SetStatus(onlineStatus, "Starting a room on the game server...", false);
            account.CreateRoom(t => { busy = false; Connect(t, onlineStatus); });
        }

        void JoinRoom(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (busy) return;
            if (code.Length < 4) { SetStatus(onlineStatus, "Type the room code your friend sees in their lobby", true); return; }
            busy = true;
            SetStatus(onlineStatus, "Looking for room " + code + "...", false);
            account.JoinRoom(code, t => { busy = false; Connect(t, onlineStatus); });
        }

        void Connect(RoomTicket t, Text status)
        {
            if (!string.IsNullOrEmpty(t.error)) { SetStatus(status, t.error, true); return; }
            if (net.IsOnline) net.Leave();
            if (!net.JoinRoom(t)) { SetStatus(status, "Could not connect to the game server", true); return; }
            SetStatus(status, "Joining room " + t.code + "...", false);
            HideAll();
        }

        // ------------------------------------------------------------------ friends board
        void BuildFriends()
        {
            friendsLayer = Layer("Online_Friends");
            var p = UiKit.Board(friendsLayer, "Panel", "FRIENDS", Vector2.zero, new Vector2(940f, 900f));
            friendsMe = UiKit.Para(p, "Me", "", 24, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, 345f), new Vector2(820f, 36f));
            addFriendField = Field(p, "AddField", new Vector2(-120f, 285f), 520f, "friend's username", InputField.ContentType.Standard, 16);
            addFriendField.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '\0';
            UiKit.Button(p, "Add Friend", new Vector2(270f, 285f), new Vector2(240f, 84f), AddFriend, UiStyle.Green, null, 30);
            friendsStatus = UiKit.Para(p, "Status", "", 24, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, 225f), new Vector2(820f, 36f));

            var view = UiKit.At(p, "List", new Vector2(0f, -40f), new Vector2(840f, 470f));
            var bg = view.gameObject.AddComponent<Image>();
            bg.sprite = UiKit.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.29f, 0.17f, 0.09f, 0.12f);
            view.gameObject.AddComponent<RectMask2D>();
            listContent = UiKit.Rect(view, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 0f));
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = listContent;
            sr.viewport = view;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            friendsEmpty = UiKit.Para(view, "Empty", "No friends yet. Add one with their username above!", 26, TextAnchor.MiddleCenter, UiKit.WoodDark, Vector2.zero, new Vector2(760f, 80f));

            UiKit.Button(p, "Back", new Vector2(0f, -350f), new Vector2(300f, 84f), () => { HideAll(); }, UiStyle.Red, null, 32);
            friendsLayer.gameObject.SetActive(false);
        }

        void AddFriend()
        {
            string u = addFriendField.text.Trim();
            if (u.Length == 0) { SetStatus(friendsStatus, "Type your friend's username", true); return; }
            SetStatus(friendsStatus, "Sending...", false);
            account.AddFriend(u, (ok, msg) => { SetStatus(friendsStatus, msg, !ok); if (ok) addFriendField.text = ""; });
        }

        void RebuildRows()
        {
            rowsDirty = false;
            foreach (var r in rows) Destroy(r);
            rows.Clear();
            var list = account.Friends;
            friendsEmpty.gameObject.SetActive(list.Count == 0);
            string room = net != null && net.IsOnline ? net.CurrentRoom : "";
            const float h = 92f;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                var row = UiKit.Rect(listContent, "Row_" + f.username, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f - i * h), new Vector2(-24f, h - 10f));
                var img = row.gameObject.AddComponent<Image>();
                img.sprite = UiKit.Rounded;
                img.type = Image.Type.Sliced;
                img.color = new Color(1f, 0.97f, 0.9f, 0.55f);
                var dot = UiKit.Dot(row, "Online", 26f, f.Accepted && f.online ? UiKit.Gas : new Color(0.55f, 0.48f, 0.42f, 1f));
                var dr = (RectTransform)dot.transform;
                dr.anchorMin = dr.anchorMax = new Vector2(0f, 0.5f);
                dr.anchoredPosition = new Vector2(34f, 0f);
                UiKit.Label(row, "Name", f.username, 30, TextAnchor.MiddleLeft, UiKit.Ink, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(230f, 10f), new Vector2(340f, 40f), false);
                string sub = f.Accepted ? (f.online ? "online" : "offline") : f.incoming ? "wants to be your friend" : "request sent";
                UiKit.Label(row, "Sub", sub, 20, TextAnchor.MiddleLeft, UiKit.WoodDark, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(230f, -22f), new Vector2(340f, 28f), false);
                var entry = f;
                if (!f.Accepted && f.incoming)
                {
                    RowButton(row, "Accept", -250f, 170f, UiStyle.Green, () => account.RespondFriend(entry, true, (ok, m) => SetStatus(friendsStatus, m, !ok)));
                    RowButton(row, "Decline", -80f, 150f, UiStyle.Red, () => account.RespondFriend(entry, false, (ok, m) => SetStatus(friendsStatus, m, !ok)));
                }
                else
                {
                    if (f.Accepted && !string.IsNullOrEmpty(room))
                        RowButton(row, "Invite", -170f, 170f, UiStyle.Green, () =>
                        {
                            SetStatus(friendsStatus, "Inviting " + entry.username + "...", false);
                            account.Invite(entry, room, (ok, m) => SetStatus(friendsStatus, m, !ok));
                        });
                    RowButton(row, f.Accepted ? "Remove" : "Cancel", -40f, 110f, UiStyle.Wood, () => account.RemoveFriend(entry, (ok, m) => SetStatus(friendsStatus, m, !ok)), 22);
                }
                rows.Add(row.gameObject);
            }
            listContent.sizeDelta = new Vector2(0f, list.Count * h + 16f);
        }

        static void RowButton(RectTransform row, string label, float x, float width, UiStyle style, UnityEngine.Events.UnityAction click, int font = 26)
        {
            var b = UiKit.Button(row, label, Vector2.zero, new Vector2(width, 64f), click, style, null, font);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(x - width * 0.5f + 40f, 0f);
        }

        // ------------------------------------------------------------------ invites
        void BuildInviteToast()
        {
            inviteToast = UiKit.Rect(hud.OverlayRoot, "Online_Invite", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(820f, 170f));
            UiKit.Skin(inviteToast, "Plank_Wide");
            inviteText = UiKit.Label(inviteToast, "Text", "", 30, TextAnchor.MiddleCenter, UiKit.Cream, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(760f, 50f));
            UiKit.Button(inviteToast, "Join", new Vector2(-140f, -30f), new Vector2(240f, 76f), () => AnswerInvite(true), UiStyle.Green, "Icon_Play", 32);
            UiKit.Button(inviteToast, "Later", new Vector2(140f, -30f), new Vector2(240f, 76f), () => AnswerInvite(false), UiStyle.Wood, null, 30);
            inviteToast.gameObject.SetActive(false);
        }

        void OnInvite(InviteEntry i)
        {
            if (net != null && net.IsOnline && net.CurrentRoom == i.room_code) { account.RespondInvite(i, true); return; }   // already there
            shownInvite = i;
            inviteText.text = i.from_username + " invited you to raft together!";
            Show(inviteToast, true);
            GameAudio.PlayUI(AudioCue.UIOpen);
        }

        void AnswerInvite(bool join)
        {
            var i = shownInvite;
            shownInvite = null;
            Show(inviteToast, false);
            if (i == null) return;
            account.RespondInvite(i, join);
            if (!join) return;
            hud.Toast("Joining " + i.from_username + "'s raft...");
            if (net != null && net.IsOnline) net.Leave();
            hud.session.EndSession();
            account.JoinRoom(i.room_code, t =>
            {
                if (!string.IsNullOrEmpty(t.error)) { ShowOnline(); SetStatus(onlineStatus, t.error, true); return; }
                Connect(t, onlineStatus);
            });
        }

        // ------------------------------------------------------------------ voice: push to talk
        void BuildTalk()
        {
            talkButton = UiKit.Rect(hud.HudLayer, "Talk", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-230f, 40f), new Vector2(150f, 150f));
            talkImage = UiKit.Skin(talkButton, "Btn_WoodRound", null, 0f, true);
            talkImage.preserveAspect = true;
            talkLabel = UiKit.Label(talkButton, "Text", "TALK", 30, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 6f), Vector2.zero);
            talkHold = talkButton.gameObject.AddComponent<UiHoldButton>();
            talkButton.gameObject.SetActive(false);
        }

        void Update()
        {
            if (hud == null) return;
            if (rowsDirty && friendsLayer.gameObject.activeSelf) RebuildRows();
            if (onlineLayer.gameObject.activeSelf && onlineFriendsLabel != null)
            {
                int on = account.OnlineFriends, req = account.IncomingRequests;
                onlineFriendsLabel.text = "Friends" + (on > 0 ? "  (" + on + " online)" : "") + (req > 0 ? "  · " + req + " new" : "");
            }
            if (inviteToast.gameObject.activeSelf && shownInvite != null && !account.SignedIn) Show(inviteToast, false);

            var voice = VoiceChat.Instance;
            bool showTalk = voice != null && VoiceChat.Supported && run.Networked && net != null && net.IsOnline
                            && VoiceChat.CurrentMode == VoiceChat.Mode.PushToTalk && !voice.MicDenied;
            if (talkButton.gameObject.activeSelf != showTalk) talkButton.gameObject.SetActive(showTalk);
            if (voice != null)
            {
                bool key = Keyboard.current != null && Keyboard.current.vKey.isPressed;
                if (!VoiceChat.TestTone) voice.PushHeld = (showTalk && talkHold.Held) || key;   // tests drive it themselves
                if (showTalk)
                {
                    bool on = voice.Transmitting;
                    talkImage.color = on ? new Color(1f, 0.85f, 0.4f, 1f) : Color.white;
                    talkLabel.text = on ? "LIVE" : "TALK";
                    talkButton.localScale = Vector3.one * (on ? 1.08f : 1f);
                }
            }
        }

        // ------------------------------------------------------------------ helpers
        RectTransform Layer(string name)
        {
            var rt = UiKit.Stretch(hud.OverlayRoot, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.12f, 0.06f, 0.03f, 0.82f);       // darker than the menu dim: the board stands alone
            return rt;
        }

        static InputField Field(RectTransform parent, string name, Vector2 pos, float width, string placeholder, InputField.ContentType type, int limit)
        {
            var rt = UiKit.At(parent, name, pos, new Vector2(width, 72f));
            var img = UiKit.Skin(rt, "Btn_White", null, 0f, true);
            var ph = UiKit.Label(rt, "Placeholder", placeholder, 24, TextAnchor.MiddleCenter, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.4f),
                                 Vector2.zero, Vector2.one, new Vector2(0f, 4f), new Vector2(-30f, 0f), false);
            var t = UiKit.Label(rt, "Text", "", 28, TextAnchor.MiddleCenter, UiKit.Ink, Vector2.zero, Vector2.one, new Vector2(0f, 4f), new Vector2(-30f, 0f), false);
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var field = rt.gameObject.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = t;
            field.placeholder = ph;
            field.customCaretColor = true;
            field.caretColor = UiKit.Ink;
            field.caretWidth = 3;
            field.selectionColor = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.5f);
            field.contentType = type;
            field.asteriskChar = '*';
            field.characterLimit = limit;
            rt.gameObject.AddComponent<UiButtonFx>();
            return field;
        }
    }
}
