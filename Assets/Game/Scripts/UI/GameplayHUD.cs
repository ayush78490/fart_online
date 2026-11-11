using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FartOnline.Game
{
    /// <summary>
    /// Code-built HUD and menus, skinned with the cozy wood sprite set (see <see cref="UiKit"/>).
    /// In play: timer pill, checkpoint badge and course progress bar, one wooden card per player (colour, name,
    /// gas tank, farting / charging / empty), a raft radar that shows where everyone sits on the round raft as seen
    /// from the camera plus the combined push arrow, punchy centre banners (countdown, checkpoint, wipeout), parchment
    /// notes for the stuck prompt and first-run tips, connection status, a pause button and the results board.
    /// Menus: title screen (solo with the assist selector, local co-op, online host/join), pause, settings
    /// (volume), how-to-play and lobby. Also switches the music and river ambience with the run state.
    /// Endless river (<see cref="GameRunManager.Endless"/>): the top pill shows the distance, the badge the best
    /// distance, the bar the way to the best; milestones every 250 m; a crash ends the run with the game over board.
    /// Responsive: <see cref="UpdateResponsive"/> adapts the canvas scaling and the layout to the screen (landscape /
    /// portrait, phone / tablet / desktop) and shows on-screen touch controls on touch devices (<see cref="TouchInput"/>).
    /// The web build hides online play (browsers cannot host or open direct-IP connections) and the Quit button.
    /// Purely a view: it reads the run, seats and session and calls their public methods.
    /// </summary>
    public class GameplayHUD : MonoBehaviour
    {
        public GameRunManager run;
        public GameSession session;
        public SharedRaftController raft;
        public RaftRecoveryManager recovery;
        public Camera viewCamera;
        [Tooltip("Show the short how-to-play tips during the first solo run")] public bool tutorialTips = true;

        /// <summary>Set by the network layer: (address, port) host / join requests and a status line.</summary>
        public Action<string, ushort> HostRequested, JoinRequested;
        public Action LeaveRequested;
        public Func<string> ConnectionStatus;
        public Func<bool> CanStartOnline;   // host only: everyone ready
        public Action StartOnlineRequested;
        /// <summary>Run commands (0 = reset to checkpoint, 1 = restart level). Online clients route them to the server.</summary>
        public Action<byte> RunCommand;
        /// <summary>Set by the network layer: the player's online name (read / changed from the menu) and the lobby's
        /// "where to join" line (room code and address).</summary>
        public Func<string> PlayerNameAccess, SessionInfo;
        public Action<string> PlayerNameChanged;
        /// <summary>Online rooms: true when this player leads the room (gets the host's Start / Play Again / Lobby).</summary>
        public Func<bool> IsRoomLeader;
        /// <summary>Set by OnlineUI: the account-gated online menu, the friends board, the lobby's invite button, the
        /// menu's account line and the friends badge (requests waiting).</summary>
        public Action PlayOnlineRequested, FriendsRequested, InviteFriendsRequested;
        public Func<string> AccountLine;
        public Func<int> FriendsBadge;
        public RectTransform HudLayer => hud;
        public RectTransform OverlayRoot => (RectTransform)canvas.transform;

        Canvas canvas;
        RectTransform hud, mainMenu, pauseMenu, controls, results, lobby, settings, gameOver;
        Text timer, checkpoint, banner, prompt, status, tip, lobbyStatus, assistLabel;
        Text resTime, resCheckpoints, resWipeouts, resFarts, resDistance;
        Text cpCaption, finishLabel, timeSmall, goDistance, goReason, goTime, goBest, goFarts, goTip, lobbyInfo, onlineCaption;
        Button goAgain, goSecond;
        RectTransform addressRt, hostRt;
        InputField nameField;
        RectTransform goNewBest, timeSmallRoot;
        Button resetButton;
        CanvasScaler scaler;
        RectTransform logoRt, tagRt, hintRt, menuBoard, touchLayer, settingsTouchRow;
        Text rulesText, touchModeLabel;
        readonly List<GameObject> onlineBits = new List<GameObject>();
        GameObject webNote, quitButton, fullscreenButtonMenu;
        Vector2 lastViewport;
        bool lastPortrait, lastTouch, layoutDone;
        bool portraitLayout, touchLayout;
        /// <summary>Tests: lay the UI out for this viewport (pixels) instead of the real screen.</summary>
        public static Vector2? ViewportOverride;
        /// <summary>Canvas scale factor the scaler would use for the current viewport (tests set it by hand).</summary>
        public float ComputedScale { get; private set; } = 1f;
        public Canvas Canvas => canvas;
#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool IsWeb = true;
#else
        public static bool IsWeb = false;
#endif
        const string RulesDefault = "Everyone sits on the rim of ONE round raft. Your fart pushes the raft AWAY from you: sit on the right to go left.\n" +
                                    "The river never ends and keeps getting faster. Touch a rock, log, buoy or gate and the run is over!";
        const string RulesTouch = "Your fart pushes the round raft AWAY from you: sit on the right to go left.\n" +
                                  "Drag the stick to walk around the rim (flick down to brake), hold FART to fart. One bump ends the run!";
        float gameOverShowAt = -1f;
        int lastMilestone;
        bool bestAnnounced;
        const float Milestone = 250f;
        UiBar progress;
        RectTransform progressKnob, statusRoot, promptRoot, tipRoot, radar, pushArrow;
        UiPunch bannerPunch;
        Button lobbyStart;
        Text accountLine, friendsBadgeText, voiceModeLabel;
        RectTransform friendsBadge;
        readonly List<PlayerCard> cards = new List<PlayerCard>();
        readonly List<ResultRow> resultRows = new List<ResultRow>();
        readonly List<LobbySeat> lobbySeats = new List<LobbySeat>();
        readonly Image[] radarDots = new Image[4];
        float bannerUntil;
        bool localPause;
        int tipStep;
        float tipTimer;
        SoloAssist menuAssist = SoloAssist.Standard;
        string address = "127.0.0.1";

        class PlayerCard
        {
            public RectTransform root;
            public Image swatch;
            public UiBar gas;
            public Text name, state, number;
        }

        class ResultRow { public RectTransform root; public Image dot; public Text name, stats; }
        class LobbySeat { public Image dot; public Text number, name; }

        void Awake()
        {
            BuildCanvas();
            BuildHud();
            BuildMainMenu();
            BuildPauseMenu();
            BuildResults();
            BuildGameOver();
            BuildLobby();
            BuildControls();
            BuildSettings();
            BuildTouchControls();
            if (Application.isMobilePlatform) Application.targetFrameRate = 60;
            run.StateChanged += OnState;
            run.CheckpointReached += c =>
            {
                Banner("CHECKPOINT " + (c.index + 1) + "!", UiKit.Accent, 1.6f);
                GameAudio.PlayUI(AudioCue.Checkpoint);
            };
            recovery.WipedOut += r => Banner("WIPEOUT!", UiKit.Danger, 1.6f);
            OnState(RunState.MainMenu, run.State);
            UpdateResponsive(true);
        }

        // ------------------------------------------------------------------ construction
        void BuildCanvas()
        {
            var go = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvas.pixelPerfect = false;
            scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        /// <summary>Full-screen layer; optionally a warm dim with a slowly turning sunburst behind the boards.</summary>
        RectTransform Layer(string name, bool dim, bool rays = false)
        {
            var rt = UiKit.Stretch(canvas.transform, name);
            if (dim) { var img = rt.gameObject.AddComponent<Image>(); img.color = UiKit.Dim; }
            if (rays)
            {
                var r = UiKit.At(rt, "Rays", Vector2.zero, new Vector2(2600f, 2600f));
                var ri = r.gameObject.AddComponent<Image>();
                ri.sprite = UiKit.Rays;
                ri.color = new Color(1f, 0.85f, 0.55f, 0.09f);
                ri.raycastTarget = false;
                var idle = r.gameObject.AddComponent<UiIdle>();
                idle.bob = 0f;
                idle.spin = 3f;
            }
            return rt;
        }

        void BuildHud()
        {
            hud = Layer("HUD", false);

            // top centre: timer pill, checkpoint badge, progress bar
            var top = UiKit.Rect(hud, "TopBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(360f, 100f));
            UiKit.Skin(top, "Pill_Brown");
            timer = UiKit.Label(top, "Timer", "0:00.0", 56, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 3f), Vector2.zero);

            var cp = UiKit.Rect(hud, "CheckpointBadge", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(262f, -8f), new Vector2(124f, 124f));
            var cpImg = UiKit.Skin(cp, "Badge_Green");
            cpImg.preserveAspect = true;
            cpCaption = UiKit.Label(cp, "Caption", "CP", 22, TextAnchor.MiddleCenter, UiKit.Cream, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 22f), new Vector2(100f, 30f));
            checkpoint = UiKit.Label(cp, "Value", "0/5", 34, TextAnchor.MiddleCenter, UiKit.Cream, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(110f, 40f));

            progress = UiKit.Bar(hud, "Progress", new Vector2(0.5f, 1f), new Vector2(-320f, -150f), new Vector2(640f, 30f), UiKit.Accent);
            progressKnob = UiKit.Rect(progress.Root, "Knob", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
            var kn = progressKnob.gameObject.AddComponent<Image>();
            kn.sprite = UiKit.Or(UiKit.S("Slider_Knob"), UiKit.Circle);
            kn.preserveAspect = true;
            kn.raycastTarget = false;
            finishLabel = UiKit.Label(progress.Root, "Finish", "FINISH", 18, TextAnchor.MiddleLeft, UiKit.Cream, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(52f, 0f), new Vector2(90f, 24f));

            // endless: small survival timer under the bar
            timeSmallRoot = UiKit.Rect(hud, "SurvivalTime", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(170f, 46f));
            UiKit.Skin(timeSmallRoot, "Pill_Brown");
            timeSmall = UiKit.Label(timeSmallRoot, "Text", "0:00", 24, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), Vector2.zero);

            // top right: connection status + pause
            statusRoot = UiKit.Rect(hud, "Connection", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-230f, -66f), new Vector2(420f, 62f));
            UiKit.Skin(statusRoot, "Pill_Brown");
            status = UiKit.Label(statusRoot, "Text", "", 22, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), new Vector2(-40f, 0f), false);
            status.font = UiKit.Body;
            var pauseBtn = UiKit.IconButton(hud, "Pause", "Icon_Settings", Vector2.zero, 88f, () => session.TogglePause());
            var pr = (RectTransform)pauseBtn.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(1f, 1f);
            pr.anchoredPosition = new Vector2(-66f, -66f);
            var padBtn = UiKit.IconButton(hud, "Controller", "", Vector2.zero, 88f, ToggleController);
            var padRt = (RectTransform)padBtn.transform;
            padRt.anchorMin = padRt.anchorMax = new Vector2(1f, 1f);
            padRt.anchoredPosition = new Vector2(-166f, -66f);
            controllerLabel = UiKit.Label(padRt, "Text", "PAD", 26, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 4f), Vector2.zero);
            controllerLabel.raycastTarget = false;

            // centre: banner, prompt and tip notes
            banner = UiKit.Label(hud, "Banner", "", 132, TextAnchor.MiddleCenter, UiKit.Accent, new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.64f), Vector2.zero, new Vector2(1600f, 180f));
            bannerPunch = banner.gameObject.AddComponent<UiPunch>();
            prompt = UiKit.Note(hud, "Prompt", new Vector2(0.5f, 0.27f), Vector2.zero, new Vector2(920f, 92f), 28);
            promptRoot = (RectTransform)prompt.transform.parent;
            tip = UiKit.Note(hud, "Tip", new Vector2(0.5f, 0.15f), Vector2.zero, new Vector2(920f, 92f), 28);
            tipRoot = (RectTransform)tip.transform.parent;

            // bottom left: player cards
            for (int i = 0; i < 4; i++)
            {
                var c = new PlayerCard();
                c.root = UiKit.Rect(hud, "Player_" + (i + 1), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 24f + i * 116f), new Vector2(420f, 106f));
                UiKit.Skin(c.root, "Plank_Wide");
                c.swatch = UiKit.Dot(c.root, "Colour", 76f, Color.white);
                var sr = (RectTransform)c.swatch.transform;
                sr.anchorMin = sr.anchorMax = new Vector2(0f, 0.5f);
                sr.anchoredPosition = new Vector2(58f, 2f);
                c.number = UiKit.Label(sr, "Number", "P" + (i + 1), 26, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), Vector2.zero);
                c.name = UiKit.Label(c.root, "Name", "P" + (i + 1), 28, TextAnchor.MiddleLeft, UiKit.Cream, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(230f, 20f), new Vector2(220f, 36f));
                c.state = UiKit.Label(c.root, "State", "", 20, TextAnchor.MiddleRight, UiKit.Gas, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-82f, 22f), new Vector2(130f, 30f));
                c.gas = UiKit.Bar(c.root, "Gas", new Vector2(0f, 0.5f), new Vector2(112f, -20f), new Vector2(282f, 26f), UiKit.Gas);
                int seatIndex = i;
                var tap = c.root.gameObject.AddComponent<Button>();      // online: tap a card to mute / unmute that player
                tap.transition = Selectable.Transition.None;
                tap.onClick.AddListener(() => ToggleMuteSeat(seatIndex));
                var bg = c.root.GetComponent<Image>();
                if (bg != null) bg.raycastTarget = true;
                cards.Add(c);
            }

            // bottom right: raft radar
            radar = UiKit.Rect(hud, "Radar", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 36f), new Vector2(250f, 250f));
            var disc = UiKit.Skin(radar, "Circle_On");
            disc.preserveAspect = true;
            var inner = UiKit.At(radar, "Inner", Vector2.zero, new Vector2(150f, 150f));
            var ii = UiKit.Skin(inner, "Circle_Off", new Color(1f, 1f, 1f, 0.55f));
            ii.preserveAspect = true;
            var cap = UiKit.Rect(radar, "Caption", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(190f, 50f));
            UiKit.Skin(cap, "Pill_Brown");
            UiKit.Label(cap, "Text", "YOUR RAFT", 22, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), Vector2.zero);
            pushArrow = UiKit.Rect(radar, "PushArrow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(16f, 80f));
            var stem = UiKit.Box(pushArrow, UiKit.Gas);
            stem.raycastTarget = false;
            var head = UiKit.Rect(pushArrow, "Head", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.2f), Vector2.zero, new Vector2(54f, 30f));
            var hi = head.gameObject.AddComponent<Image>();
            hi.sprite = UiKit.Or(UiKit.S("Arrow_B"), UiKit.Circle);
            hi.color = UiKit.Gas;
            hi.raycastTarget = false;
            for (int i = 0; i < 4; i++)
            {
                radarDots[i] = UiKit.Dot(radar, "Seat_" + (i + 1), 48f, Color.white);
                UiKit.Label(radarDots[i].transform, "N", (i + 1).ToString(), 22, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero);
            }
        }

        void BuildMainMenu()
        {
            mainMenu = Layer("MainMenu", true, true);

            // left: logo, tagline, quick controls
            var logo = logoRt = UiKit.At(mainMenu, "Logo", new Vector2(-440f, 190f), new Vector2(760f, 380f));
            logo.gameObject.AddComponent<UiIdle>();
            UiKit.Label(logo, "Fart", "FART", 200, TextAnchor.MiddleCenter, UiKit.Accent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40f, 85f), new Vector2(760f, 200f));
            var onlineWord = UiKit.Label(logo, "Online", "ONLINE", 168, TextAnchor.MiddleCenter, UiKit.Cream, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30f, -80f), new Vector2(760f, 190f));
            onlineWord.transform.localRotation = Quaternion.Euler(0f, 0f, 3f);
            var tag = tagRt = UiKit.At(mainMenu, "Tagline", new Vector2(-440f, -60f), new Vector2(680f, 90f));
            UiKit.Skin(tag, "Plank_Wide");
            UiKit.Label(tag, "Text", "endless river  ·  everybody farts  ·  one bump and you're out", 26, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 3f), Vector2.zero);
            var hint = UiKit.Note(mainMenu, "Hint", new Vector2(0.5f, 0.5f), new Vector2(-440f, -225f), new Vector2(680f, 180f), 28);
            hint.text = "A / D  walk around the raft\nF  fart  (hold to keep farting)\nDodge every obstacle: one hit ends the run!";
            hintRt = (RectTransform)hint.transform.parent;

            // right: the menu board
            var p = menuBoard = UiKit.Board(mainMenu, "Panel", "LET'S RAFT!", new Vector2(430f, -20f), new Vector2(720f, 860f));
            UiKit.Button(p, "Play Solo", new Vector2(0f, 290f), new Vector2(480f, 108f), () => session.StartSolo(menuAssist), UiStyle.Green, "Icon_Play", 46);

            UiKit.Caption(p, "SOLO ASSIST", new Vector2(0f, 200f), 300f);
            assistLabel = UiKit.Selector(p, "Assist", new Vector2(0f, 150f), 300f,
                () => { menuAssist = (SoloAssist)(((int)menuAssist + 2) % 3); RefreshMenuLabels(); },
                () => { menuAssist = (SoloAssist)(((int)menuAssist + 1) % 3); RefreshMenuLabels(); });

            UiKit.Divider(p, new Vector2(0f, 88f), 600f);
            UiKit.Caption(p, "LOCAL CO-OP  (same keyboard / pads)", new Vector2(0f, 55f), 600f);
            UiKit.Button(p, "2 Players", new Vector2(-150f, -6f), new Vector2(280f, 84f), () => session.StartLocalCoop(2), UiStyle.Wood);
            UiKit.Button(p, "4 Players", new Vector2(150f, -6f), new Vector2(280f, 84f), () => session.StartLocalCoop(4), UiStyle.Wood);

            UiKit.Divider(p, new Vector2(0f, -72f), 600f);
            // online play needs an account (single player and local co-op never do)
            onlineCaption = UiKit.Caption(p, "ONLINE CO-OP  (account needed)", new Vector2(0f, -90f), 600f);
            accountLine = UiKit.Para(p, "Account", "", 20, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, -119f), new Vector2(620f, 28f));
            UiKit.Button(p, "Play Online", new Vector2(-150f, -184f), new Vector2(280f, 74f), () => PlayOnlineRequested?.Invoke(), UiStyle.Green, null, 32);
            var friendsBtn = UiKit.Button(p, "Friends", new Vector2(150f, -184f), new Vector2(280f, 74f), () => FriendsRequested?.Invoke(), UiStyle.Wood, null, 32);
            friendsBadge = UiKit.At((RectTransform)friendsBtn.transform, "Badge", new Vector2(128f, 32f), new Vector2(46f, 46f));
            UiKit.Dot(friendsBadge, "Dot", 46f, UiKit.Danger);
            friendsBadgeText = UiKit.Label(friendsBadge, "N", "1", 24, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), Vector2.zero);
            friendsBadge.gameObject.SetActive(false);
            webNote = new GameObject("WebNote");

            UiKit.Divider(p, new Vector2(0f, -236f), 600f);
            UiKit.Button(p, "How to Play", new Vector2(-180f, -304f), new Vector2(270f, 84f), () => Show(controls, true), UiStyle.Wood);
            UiKit.IconButton(p, "Settings", "Icon_Settings", new Vector2(20f, -300f), 92f, () => Show(settings, true));
            quitButton = UiKit.Button(p, "Quit", new Vector2(190f, -304f), new Vector2(230f, 84f), Quit, UiStyle.Red, "Icon_Quit").gameObject;
            fullscreenButtonMenu = UiKit.Button(p, "Fullscreen", new Vector2(190f, -304f), new Vector2(230f, 84f), ToggleFullscreen, UiStyle.Green, null, 30).gameObject;
            RefreshMenuLabels();
        }

        RectTransform TextField(RectTransform parent, string name, Vector2 pos, Vector2 size, string value, int fontSize, string placeholder, UnityEngine.Events.UnityAction<string> onEnd)
        {
            var rt = UiKit.At(parent, name, pos, size);
            var img = UiKit.Skin(rt, "Btn_White", null, 0f, true);
            var ph = UiKit.Label(rt, "Placeholder", placeholder, fontSize - 4, TextAnchor.MiddleCenter, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.4f),
                                 Vector2.zero, Vector2.one, new Vector2(0f, 4f), new Vector2(-30f, 0f), false);
            var t = UiKit.Label(rt, "Text", value, fontSize, TextAnchor.MiddleCenter, UiKit.Ink, Vector2.zero, Vector2.one, new Vector2(0f, 4f), new Vector2(-30f, 0f), false);
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var field = rt.gameObject.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = t;
            field.customCaretColor = true;
            field.caretColor = UiKit.Ink;
            field.caretWidth = 3;
            field.selectionColor = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.5f);
            field.placeholder = ph;
            field.text = value;
            field.onEndEdit.AddListener(onEnd);
            rt.gameObject.AddComponent<UiButtonFx>();
            return rt;
        }

        void Start()
        {
            if (nameField != null && PlayerNameAccess != null) nameField.text = PlayerNameAccess();
            RefreshVoiceLabel();
            if (IsWeb)
            {
                // the web app is usually served by the PC that hosts the game: suggest that machine
                string url = Application.absoluteURL ?? "";
                int s = url.IndexOf("://", StringComparison.Ordinal);
                if (s > 0)
                {
                    string host = url.Substring(s + 3);
                    int end = host.IndexOfAny(new[] { '/', ':', '?', '#' });
                    if (end >= 0) host = host.Substring(0, end);
                    if (host.Length > 0 && host != "localhost") address = host;
                }
                var f = addressRt != null ? addressRt.GetComponent<InputField>() : null;
                if (f != null) f.text = address;
            }
        }

        void RefreshMenuLabels()
        {
            if (assistLabel != null) assistLabel.text = menuAssist.ToString();
        }

        void Online(bool host)
        {
            ushort port = 7777;
            string a = address;
            int colon = a.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(a.Substring(colon + 1), out var p)) { port = p; a = a.Substring(0, colon); }
            var cb = host ? HostRequested : JoinRequested;
            if (cb == null) { Banner("Online play is not available in this build", UiKit.Warn, 2.5f); return; }
            cb(a, port);
        }

        void BuildPauseMenu()
        {
            pauseMenu = Layer("PauseMenu", true);
            var p = UiKit.Board(pauseMenu, "Panel", "PAUSED", Vector2.zero, new Vector2(620f, 820f));
            UiKit.Button(p, "Continue", new Vector2(0f, 255f), new Vector2(430f, 100f), Resume, UiStyle.Green, "Icon_Play", 42);
            resetButton = UiKit.Button(p, "Reset to Checkpoint", new Vector2(0f, 148f), new Vector2(430f, 82f), () => { Resume(); Command(0); }, UiStyle.Wood, null, 30);
            UiKit.Button(p, "Restart Run", new Vector2(0f, 56f), new Vector2(430f, 82f), () => { Resume(); Command(1); }, UiStyle.Wood, null, 30);
            UiKit.Button(p, "How to Play", new Vector2(0f, -36f), new Vector2(430f, 82f), () => Show(controls, true), UiStyle.Wood, null, 30);
            UiKit.Button(p, "Settings", new Vector2(0f, -128f), new Vector2(430f, 82f), () => Show(settings, true), UiStyle.Wood, "Icon_Settings", 30);
            UiKit.Button(p, "Quit to Menu", new Vector2(0f, -250f), new Vector2(430f, 90f), QuitToMenu, UiStyle.Red, "Icon_Quit", 34);
            CloseX(p, Resume);
        }

        /// <summary>Red round X button on a board's top-right corner.</summary>
        static void CloseX(RectTransform board, UnityEngine.Events.UnityAction onClick)
        {
            var rt = UiKit.Rect(board, "Close", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-14f, -14f), new Vector2(88f, 88f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiKit.Or(UiKit.S("Icon_Close"), UiKit.Circle);
            img.preserveAspect = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            rt.gameObject.AddComponent<UiButtonFx>();
            b.onClick.AddListener(() => GameAudio.PlayUI(AudioCue.UIBack));
            b.onClick.AddListener(onClick);
        }

        void BuildSettings()
        {
            settings = Layer("Settings", true);
            var p = UiKit.Board(settings, "Panel", "SETTINGS", Vector2.zero, new Vector2(700f, 860f));
            UiKit.Slider(p, "Master", new Vector2(0f, 220f), 580f, GameAudio.MasterVolume, v => GameAudio.MasterVolume = v);
            UiKit.Slider(p, "Music", new Vector2(0f, 130f), 580f, GameAudio.MusicVolume, v => GameAudio.MusicVolume = v);
            UiKit.Slider(p, "Sound FX", new Vector2(0f, 40f), 580f, GameAudio.SfxVolume, v => { GameAudio.SfxVolume = v; GameAudio.PlayUI(AudioCue.UIHover, 2f); });
            UiKit.Caption(p, "TOUCH BUTTONS", new Vector2(0f, -40f), 400f);
            touchModeLabel = UiKit.Selector(p, "TouchMode", new Vector2(0f, -95f), 340f,
                () => { TouchInput.Mode = (TouchInput.ShowMode)(((int)TouchInput.Mode + 2) % 3); RefreshTouchLabel(); },
                () => { TouchInput.Mode = (TouchInput.ShowMode)(((int)TouchInput.Mode + 1) % 3); RefreshTouchLabel(); });
            RefreshTouchLabel();
            UiKit.Caption(p, "VOICE CHAT (online)", new Vector2(0f, -160f), 400f);
            voiceModeLabel = UiKit.Selector(p, "VoiceMode", new Vector2(0f, -215f), 340f,
                () => { FartOnline.Game.Online.VoiceChat.CurrentMode = (FartOnline.Game.Online.VoiceChat.Mode)(((int)FartOnline.Game.Online.VoiceChat.CurrentMode + 2) % 3); RefreshVoiceLabel(); },
                () => { FartOnline.Game.Online.VoiceChat.CurrentMode = (FartOnline.Game.Online.VoiceChat.Mode)(((int)FartOnline.Game.Online.VoiceChat.CurrentMode + 1) % 3); RefreshVoiceLabel(); });
            UiKit.Button(p, "Fullscreen", new Vector2(-150f, -320f), new Vector2(270f, 86f), ToggleFullscreen, UiStyle.Wood, null, 32);
            UiKit.Button(p, "Done", new Vector2(150f, -320f), new Vector2(270f, 86f), () => { PlayerPrefs.Save(); Show(settings, false); }, UiStyle.Green);
            CloseX(p, () => { PlayerPrefs.Save(); Show(settings, false); });
        }

        void RefreshVoiceLabel()
        {
            if (voiceModeLabel == null) return;
            var m = FartOnline.Game.Online.VoiceChat.CurrentMode;
            voiceModeLabel.text = !FartOnline.Game.Online.VoiceChat.Supported ? "Not in browser"
                                : m == FartOnline.Game.Online.VoiceChat.Mode.PushToTalk ? "Push to talk"
                                : m == FartOnline.Game.Online.VoiceChat.Mode.OpenMic ? "Open mic" : "Off";
        }

        void RefreshTouchLabel()
        {
            if (touchModeLabel == null) return;
            var m = TouchInput.Mode;
            touchModeLabel.text = m == TouchInput.ShowMode.Auto ? "Auto" : m == TouchInput.ShowMode.On ? "Always on" : "Off";
            layoutDone = false;     // re-run the responsive layout
        }

        static void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        // ------------------------------------------------------------------ touch controls
        RectTransform stick, fartBtn, chargeBtn, brakeBtn;
        Text controllerLabel;

        /// <summary>
        /// The on-screen controller: a thumbstick bottom-left (drag left / right to walk around the rim, flick down to
        /// brake) and the action buttons bottom-right: a big FART, CHARGE and BRAKE.
        /// </summary>
        void BuildTouchControls()
        {
            touchLayer = UiKit.Stretch(hud, "TouchControls");
            stick = BuildStick();
            fartBtn = TouchButton("Fart", TouchHoldButton.Action.Fart, "Btn_Green_Circle", null, "FART", false, new Vector2(1f, 0f));
            chargeBtn = TouchButton("Charge", TouchHoldButton.Action.Charge, "Btn_Red_Circle", null, "CHARGE", false, new Vector2(1f, 0f));
            brakeBtn = TouchButton("Brake", TouchHoldButton.Action.Brake, "Btn_WoodRound", null, "BRAKE", false, new Vector2(1f, 0f));
        }

        RectTransform BuildStick()
        {
            // touch area (bigger than the drawn ring, invisible but raycastable)
            var area = UiKit.Rect(touchLayer, "Touch_Stick", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 420f));
            var hit = area.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            var ring = UiKit.At(area, "Base", Vector2.zero, new Vector2(260f, 260f));
            var ri = UiKit.Skin(ring, "Btn_WoodRound", new Color(1f, 1f, 1f, 0.78f), 0f, true);
            ri.preserveAspect = true;
            ri.raycastTarget = false;
            foreach (int sd in new[] { -1, 1 })
            {
                var ar = UiKit.At(ring, sd < 0 ? "ArrowL" : "ArrowR", new Vector2(sd * 86f, 4f), new Vector2(46f, 64f));
                var ai = ar.gameObject.AddComponent<Image>();
                ai.sprite = UiKit.Or(UiKit.S("Arrow_A"), UiKit.Circle);
                ai.preserveAspect = true;
                ai.raycastTarget = false;
                ai.color = new Color(1f, 1f, 1f, 0.9f);
                if (sd > 0) ar.localScale = new Vector3(-1f, 1f, 1f);
            }
            var down = UiKit.Label(ring, "BrakeHint", "BRAKE", 20, TextAnchor.MiddleCenter, UiKit.Cream, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(160f, 28f));
            down.raycastTarget = false;
            var knob = UiKit.At(ring, "Knob", Vector2.zero, new Vector2(132f, 132f));
            var ki = UiKit.Skin(knob, "Btn_Green_Circle", Color.white, 0f, true);
            ki.preserveAspect = true;
            ki.raycastTarget = false;
            var cap = UiKit.Label(area, "Caption", "MOVE", 26, TextAnchor.MiddleCenter, UiKit.Cream, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 162f), new Vector2(200f, 34f));
            cap.raycastTarget = false;
            var js = area.gameObject.AddComponent<TouchJoystick>();
            js.baseRing = ring;
            js.knob = knob;
            js.radius = 110f;
            return area;
        }

        /// <summary>HUD button: shows or hides the on-screen controller on any device (saved like the Settings option).</summary>
        void ToggleController()
        {
            TouchInput.Mode = TouchInput.Visible ? TouchInput.ShowMode.Off : TouchInput.ShowMode.On;
            RefreshTouchLabel();
            UpdateResponsive(true);
            Banner(TouchInput.Visible ? "CONTROLLER ON" : "CONTROLLER OFF", UiKit.Cream, 1.2f);
        }

        RectTransform TouchButton(string name, TouchHoldButton.Action action, string bg, string icon, string label, bool flip, Vector2 anchor)
        {
            var rt = UiKit.Rect(touchLayer, "Touch_" + name, anchor, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 200f));
            var img = UiKit.Skin(rt, bg, new Color(1f, 1f, 1f, 0.92f), 0f, true);
            img.preserveAspect = true;
            if (icon != null)
            {
                var ir = UiKit.At(rt, "Icon", new Vector2(0f, 6f), new Vector2(80f, 110f));
                var ii = ir.gameObject.AddComponent<Image>();
                ii.sprite = UiKit.Or(UiKit.S(icon), UiKit.Circle);
                ii.preserveAspect = true;
                ii.raycastTarget = false;
                if (flip) ir.localScale = new Vector3(-1f, 1f, 1f);
            }
            if (label != null)
            {
                var t = UiKit.Label(rt, "Text", label, 40, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 6f), Vector2.zero);
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 14;
                t.resizeTextMaxSize = 60;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                var tr = (RectTransform)t.transform;
                tr.offsetMin = new Vector2(18f, 18f);
                tr.offsetMax = new Vector2(-18f, -10f);
            }
            var b = rt.gameObject.AddComponent<TouchHoldButton>();
            b.action = action;
            b.face = img;
            return rt;
        }

        void PlaceStick(Vector2 pos, float scale)
        {
            stick.anchoredPosition = pos;
            stick.localScale = Vector3.one * scale;
        }

        static void Place(RectTransform rt, Vector2 pos, float size)
        {
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
        }

        // ------------------------------------------------------------------ responsive layout
        /// <summary>Viewport in pixels (the real screen, or the test override).</summary>
        static Vector2 Viewport => ViewportOverride ?? new Vector2(Mathf.Max(1, UnityEngine.Screen.width), Mathf.Max(1, UnityEngine.Screen.height));

        /// <summary>
        /// Picks the canvas scaling and layout for the screen. Landscape uses a 1920x1080 reference, portrait 1080x1920,
        /// and the scaler matches the limiting side, so the reference area always fits (phones 19.5:9, tablets 4:3,
        /// desktops). Runs whenever the screen size, orientation or touch mode changes.
        /// </summary>
        Rect lastSafe;

        public void UpdateResponsive(bool force = false)
        {
            Vector2 v = Viewport;
            bool portrait = v.y > v.x * 1.05f;
            bool touch = TouchInput.Visible;
            Rect safe = ViewportOverride.HasValue ? new Rect(0f, 0f, v.x, v.y) : UnityEngine.Screen.safeArea;
            if (!force && layoutDone && v == lastViewport && portrait == lastPortrait && touch == lastTouch && safe == lastSafe) return;
            lastViewport = v; lastPortrait = portrait; lastTouch = touch; layoutDone = true; lastSafe = safe;
            // phones: keep the HUD (timer, cards, pause, the on-screen controller) clear of notches, rounded corners
            // and system bars
            if (safe.width > 1f && safe.height > 1f)
            {
                hud.anchorMin = new Vector2(safe.xMin / v.x, safe.yMin / v.y);
                hud.anchorMax = new Vector2(safe.xMax / v.x, safe.yMax / v.y);
                hud.offsetMin = hud.offsetMax = Vector2.zero;
            }
            portraitLayout = portrait; touchLayout = touch;

            Vector2 reference = portrait ? new Vector2(1080f, 1920f) : new Vector2(1920f, 1080f);
            bool heightLimited = v.x / v.y >= reference.x / reference.y;
            scaler.referenceResolution = reference;
            scaler.matchWidthOrHeight = heightLimited ? 1f : 0f;
            ComputedScale = heightLimited ? v.y / reference.y : v.x / reference.x;
            Vector2 L = v / ComputedScale;          // logical canvas size

            // main menu
            if (portrait)
            {
                logoRt.GetComponent<UiIdle>().SetBasePosition(new Vector2(0f, L.y * 0.5f - 250f));
                logoRt.localScale = Vector3.one * 0.85f;
                tagRt.anchoredPosition = new Vector2(0f, L.y * 0.5f - 500f);
                tagRt.localScale = Vector3.one * 0.95f;
                hintRt.gameObject.SetActive(false);
                menuBoard.anchoredPosition = new Vector2(0f, -L.y * 0.5f + 520f);
            }
            else
            {
                logoRt.GetComponent<UiIdle>().SetBasePosition(new Vector2(-440f, 190f));
                logoRt.localScale = Vector3.one;
                tagRt.anchoredPosition = new Vector2(-440f, -60f);
                tagRt.localScale = Vector3.one;
                hintRt.gameObject.SetActive(!touch);
                menuBoard.anchoredPosition = new Vector2(430f, -20f);
            }
            foreach (var g in onlineBits) g.SetActive(!IsWeb);      // browsers can join but not host
            webNote.SetActive(false);
            quitButton.SetActive(!IsWeb && !Application.isMobilePlatform);
            fullscreenButtonMenu.SetActive(IsWeb || Application.isMobilePlatform);
            if (rulesText != null) rulesText.text = touch ? RulesTouch : RulesDefault;

            // every board shrinks to fit (pop-in animation keeps that scale)
            foreach (var pop in canvas.GetComponentsInChildren<UiPop>(true))
            {
                var rt = (RectTransform)pop.transform;
                Vector2 size = rt.sizeDelta + new Vector2(40f, 120f);   // title tab and close button stick out
                pop.SetBase(Mathf.Min(1f, (L.x - 30f) / size.x, (L.y - 30f) / size.y));
            }

            // HUD
            float cardScale = portrait ? 0.72f : touch ? 0.8f : 1f;
            cardTop = portrait || touch;
            cardOrigin = portrait ? new Vector2(20f, -250f) : touch ? new Vector2(20f, -20f) : new Vector2(24f, 24f);
            cardStep = 116f * cardScale;
            foreach (var c in cards)
            {
                c.root.anchorMin = c.root.anchorMax = c.root.pivot = cardTop ? new Vector2(0f, 1f) : Vector2.zero;
                c.root.localScale = Vector3.one * cardScale;
            }
            if (portrait || touch)
            {
                radar.anchorMin = radar.anchorMax = radar.pivot = new Vector2(1f, 1f);
                radar.anchoredPosition = portrait ? new Vector2(-20f, -270f) : new Vector2(-24f, -150f);
                radar.localScale = Vector3.one * (portrait ? 0.62f : 0.72f);
            }
            else
            {
                radar.anchorMin = radar.anchorMax = radar.pivot = new Vector2(1f, 0f);
                radar.anchoredPosition = new Vector2(-36f, 36f);
                radar.localScale = Vector3.one;
            }
            ((RectTransform)banner.transform).sizeDelta = new Vector2(portrait ? 1000f : 1600f, 180f);
            banner.fontSize = portrait ? 96 : 132;
            if (portrait || touch)
            {
                float y = portrait ? -620f : -250f;
                foreach (var note in new[] { tipRoot, promptRoot })
                {
                    note.anchorMin = note.anchorMax = new Vector2(0.5f, 1f);
                    note.anchoredPosition = new Vector2(0f, y);
                    note.localScale = Vector3.one * (portrait ? 0.95f : 0.85f);
                    y -= 100f;
                }
            }
            else
            {
                tipRoot.anchorMin = tipRoot.anchorMax = new Vector2(0.5f, 0.15f);
                promptRoot.anchorMin = promptRoot.anchorMax = new Vector2(0.5f, 0.27f);
                tipRoot.anchoredPosition = promptRoot.anchoredPosition = Vector2.zero;
                tipRoot.localScale = promptRoot.localScale = Vector3.one;
            }

            // touch controls: thumbstick bottom-left, FART bottom-right (thumb reach), charge and brake beside it
            touchLayer.gameObject.SetActive(touch);
            if (portrait)
            {
                PlaceStick(new Vector2(230f, 250f), 0.95f);
                Place(fartBtn, new Vector2(-185f, 240f), 280f);
                Place(chargeBtn, new Vector2(-430f, 130f), 150f);
                Place(brakeBtn, new Vector2(-185f, 480f), 140f);
            }
            else
            {
                PlaceStick(new Vector2(250f, 220f), 1f);
                Place(fartBtn, new Vector2(-200f, 200f), 290f);
                Place(chargeBtn, new Vector2(-480f, 120f), 160f);
                Place(brakeBtn, new Vector2(-200f, 450f), 150f);
            }
            if (!touch) TouchInput.Clear();
        }

        bool cardTop;
        Vector2 cardOrigin = new Vector2(24f, 24f);
        float cardStep = 116f;

        void BuildControls()
        {
            controls = Layer("Controls", true);
            var p = UiKit.Board(controls, "Panel", "HOW TO PLAY", Vector2.zero, new Vector2(1240f, 920f));
            rulesText = UiKit.Para(p, "Rules",
                "Everyone sits on the rim of ONE round raft. Your fart pushes the raft AWAY from you: sit on the right to go left.\n" +
                "The river never ends and keeps getting faster. Touch a rock, log, buoy or gate and the run is over!",
                26, TextAnchor.MiddleCenter, UiKit.Ink, new Vector2(0f, 335f), new Vector2(1080f, 90f));

            float[] cols = { -330f, -20f, 220f, 440f };
            UiKit.Label(p, "H_P1", "PLAYER 1", 26, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cols[1], 250f), new Vector2(230f, 34f), false);
            UiKit.Label(p, "H_P2", "PLAYER 2", 26, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cols[2], 250f), new Vector2(230f, 34f), false);
            UiKit.Label(p, "H_Pad", "GAMEPAD", 26, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cols[3], 250f), new Vector2(230f, 34f), false);
            string[,] rows =
            {
                { "Walk around the rim", "A / D", "Left / Right", "Stick / D-pad" },
                { "Fart (hold = stream)", "F", "Enter", "A" },
                { "Charged fart (hold, release)", "L-Shift", "R-Shift", "RT" },
                { "Brake burst", "S", "Down", "B / LT" },
                { "Restart after a crash", "R", "Backspace", "Y" },
                { "Pause", "Esc", "P", "Start" },
            };
            for (int r = 0; r < rows.GetLength(0); r++)
            {
                float y = 185f - r * 68f;
                if (r % 2 == 0)
                {
                    var band = UiKit.At(p, "Band_" + r, new Vector2(0f, y), new Vector2(1110f, 64f));
                    var bi = UiKit.Box(band, new Color(UiKit.WoodDark.r, UiKit.WoodDark.g, UiKit.WoodDark.b, 0.12f));
                    bi.raycastTarget = false;
                }
                UiKit.Label(p, "Action_" + r, rows[r, 0], 27, TextAnchor.MiddleLeft, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cols[0], y), new Vector2(420f, 40f), false);
                for (int c = 1; c < 4; c++) UiKit.KeyCap(p, rows[r, c], new Vector2(cols[c], y + 2f), Mathf.Min(210f, Mathf.Max(70f, rows[r, c].Length * 17f + 44f)));
            }
            UiKit.Para(p, "Footer",
                "The green bar is your gas: it refills while you rest; an empty tank means a short cooldown.\n" +
                "Players can't walk through each other. Fart together from the same side to go fast!",
                24, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, -240f), new Vector2(1080f, 80f));
            UiKit.Button(p, "Back", new Vector2(0f, -340f), new Vector2(280f, 86f), () => Show(controls, false), UiStyle.Wood);
            CloseX(p, () => Show(controls, false));
        }

        void BuildResults()
        {
            results = Layer("Results", true, true);
            var p = UiKit.Board(results, "Panel", "FINISHED!", Vector2.zero, new Vector2(860f, 820f));
            UiKit.Caption(p, "YOUR TIME", new Vector2(0f, 318f));
            var tp = UiKit.At(p, "TimePill", new Vector2(0f, 252f), new Vector2(440f, 108f));
            UiKit.Skin(tp, "Pill_Brown");
            tp.gameObject.AddComponent<UiIdle>().bob = 4f;
            resTime = UiKit.Label(tp, "Time", "0:00.0", 66, TextAnchor.MiddleCenter, UiKit.Accent, Vector2.zero, Vector2.one, new Vector2(0f, 3f), Vector2.zero);

            resCheckpoints = StatChip(p, "CHECKPOINTS", new Vector2(-285f, 112f));
            resWipeouts = StatChip(p, "WIPEOUTS", new Vector2(-95f, 112f));
            resFarts = StatChip(p, "FARTS", new Vector2(95f, 112f));
            resDistance = StatChip(p, "DISTANCE", new Vector2(285f, 112f));

            for (int i = 0; i < 4; i++)
            {
                var r = new ResultRow();
                r.root = UiKit.At(p, "Player_" + (i + 1), new Vector2(0f, 2f - i * 58f), new Vector2(720f, 52f));
                var bg = UiKit.Box(r.root, new Color(UiKit.WoodDark.r, UiKit.WoodDark.g, UiKit.WoodDark.b, 0.13f));
                bg.raycastTarget = false;
                r.dot = UiKit.Dot(r.root, "Dot", 40f, Color.white);
                ((RectTransform)r.dot.transform).anchoredPosition = new Vector2(-325f, 0f);
                r.name = UiKit.Label(r.root, "Name", "", 28, TextAnchor.MiddleLeft, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-170f, 2f), new Vector2(260f, 40f), false);
                r.stats = UiKit.Para(r.root, "Stats", "", 24, TextAnchor.MiddleRight, UiKit.WoodDark, new Vector2(150f, 0f), new Vector2(380f, 40f));
                resultRows.Add(r);
            }
            UiKit.Button(p, "Play Again", new Vector2(-170f, -300f), new Vector2(310f, 96f), () => Command(1), UiStyle.Green, "Icon_Play", 36);
            UiKit.Button(p, "Main Menu", new Vector2(170f, -300f), new Vector2(310f, 96f), QuitToMenu, UiStyle.Wood, null, 34);
        }

        void BuildGameOver()
        {
            gameOver = Layer("GameOver", true, true);
            var p = UiKit.Board(gameOver, "Panel", "GAME OVER", Vector2.zero, new Vector2(860f, 780f));
            goReason = UiKit.Para(p, "Reason", "", 30, TextAnchor.MiddleCenter, UiKit.Danger, new Vector2(0f, 300f), new Vector2(760f, 50f));
            UiKit.Caption(p, "DISTANCE", new Vector2(0f, 244f));
            var dp = UiKit.At(p, "DistancePill", new Vector2(0f, 175f), new Vector2(460f, 116f));
            UiKit.Skin(dp, "Pill_Brown");
            dp.gameObject.AddComponent<UiIdle>().bob = 4f;
            goDistance = UiKit.Label(dp, "Distance", "0 m", 72, TextAnchor.MiddleCenter, UiKit.Accent, Vector2.zero, Vector2.one, new Vector2(0f, 3f), Vector2.zero);
            goNewBest = UiKit.At(p, "NewBest", new Vector2(300f, 205f), new Vector2(150f, 150f));
            var nb = UiKit.Skin(goNewBest, "Badge_Red");
            nb.preserveAspect = true;
            goNewBest.localRotation = Quaternion.Euler(0f, 0f, -14f);
            UiKit.Label(goNewBest, "Text", "NEW\nBEST!", 30, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), Vector2.zero).lineSpacing = 0.85f;
            goNewBest.gameObject.AddComponent<UiIdle>().bob = 6f;

            goTime = StatChip(p, "SURVIVED", new Vector2(-200f, 20f));
            goBest = StatChip(p, "BEST", new Vector2(0f, 20f));
            goFarts = StatChip(p, "FARTS", new Vector2(200f, 20f));
            goTip = UiKit.Para(p, "Tip", GameOverTip, 24, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, -120f), new Vector2(760f, 40f));
            goAgain = UiKit.Button(p, "Play Again", new Vector2(-170f, -255f), new Vector2(310f, 96f), () => Command(1), UiStyle.Green, "Icon_Play", 36);
            goSecond = UiKit.Button(p, "Main Menu", new Vector2(170f, -255f), new Vector2(310f, 96f), GameOverSecond, UiStyle.Wood, null, 34);
        }

        void FillGameOver()
        {
            var st = run.Stats;
            goDistance.text = Mathf.FloorToInt(st.score) + " m";
            goReason.text = string.IsNullOrEmpty(st.crashReason) ? "The raft crashed!" : "You crashed into " + st.crashReason + "!";
            goTime.text = FormatTime(st.elapsed).Split('.')[0];
            goBest.text = Mathf.FloorToInt(run.BestScore) + " m";
            goFarts.text = st.thrustActivations.ToString();
            goNewBest.gameObject.SetActive(run.NewBest);
            // online: the host restarts (or takes everyone back to the lobby); clients wait or leave
            bool leaderClient = session.Mode == SessionMode.OnlineClient && IsRoomLeader != null && IsRoomLeader();
            bool host = session.Mode == SessionMode.OnlineHost || leaderClient, client = session.Mode == SessionMode.OnlineClient && !leaderClient;
            goAgain.interactable = !client;
            SetLabel(goAgain, client ? "Waiting..." : "Play Again");
            SetLabel(goSecond, host ? "Lobby" : client ? "Leave" : "Main Menu");
            goTip.text = client ? "The host starts the next run. Same river for everyone!" : host ? "Play Again starts a new river for the whole crew." : GameOverTip;
            if (host || client) goReason.text = string.IsNullOrEmpty(st.crashReason) ? "The crew crashed!" : "The crew crashed into " + st.crashReason + "!";
        }

        const string GameOverTip = "Tip: the river and its hazards speed up as you go. Brake before tight gaps!";

        static void SetLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        void GameOverSecond()
        {
            if (session.Mode == SessionMode.OnlineHost) run.EnterLobby();
            else if (session.Mode == SessionMode.OnlineClient && IsRoomLeader != null && IsRoomLeader()) RunCommand?.Invoke(3);
            else QuitToMenu();
        }

        static Text StatChip(RectTransform parent, string caption, Vector2 pos)
        {
            var rt = UiKit.At(parent, "Stat_" + caption, pos, new Vector2(176f, 130f));
            UiKit.Skin(rt, "Container1");
            UiKit.Label(rt, "Caption", caption, 18, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.zero, new Vector2(88f, 92f), new Vector2(170f, 26f));
            return UiKit.Label(rt, "Value", "0", 44, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.zero, new Vector2(88f, 48f), new Vector2(170f, 54f));
        }

        void BuildLobby()
        {
            lobby = Layer("Lobby", true, true);
            var p = UiKit.Board(lobby, "Panel", "LOBBY", Vector2.zero, new Vector2(940f, 760f));
            UiKit.Caption(p, "WHO'S ON THE RAFT", new Vector2(0f, 255f), 600f);
            for (int i = 0; i < 4; i++)
            {
                var s = new LobbySeat();
                float x = -315f + i * 210f;
                var slot = UiKit.At(p, "Seat_" + (i + 1), new Vector2(x, 130f), new Vector2(170f, 184f));
                UiKit.Skin(slot, "Slot");
                s.dot = UiKit.Dot(slot, "Dot", 96f, Color.white);
                ((RectTransform)s.dot.transform).anchoredPosition = new Vector2(0f, 8f);
                s.number = UiKit.Label(s.dot.transform, "N", "P" + (i + 1), 30, TextAnchor.MiddleCenter, UiKit.Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), Vector2.zero);
                s.name = UiKit.Label(p, "Name_" + (i + 1), "", 24, TextAnchor.MiddleCenter, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 10f), new Vector2(200f, 34f), false);
                lobbySeats.Add(s);
            }
            lobbyStatus = UiKit.Para(p, "Status", "", 28, TextAnchor.MiddleCenter, UiKit.WoodDark, new Vector2(0f, -85f), new Vector2(760f, 80f));
            var infoRt = UiKit.At(p, "JoinInfo", new Vector2(0f, -160f), new Vector2(800f, 58f));
            UiKit.Skin(infoRt, "Pill_Brown");
            lobbyInfo = UiKit.Label(infoRt, "Text", "", 28, TextAnchor.MiddleCenter, UiKit.Accent, Vector2.zero, Vector2.one, new Vector2(0f, 3f), new Vector2(-40f, 0f));
            lobbyStart = UiKit.Button(p, "Start", new Vector2(-290f, -262f), new Vector2(260f, 96f), () => StartOnlineRequested?.Invoke(), UiStyle.Green, "Icon_Play", 38);
            UiKit.Button(p, "Invite", new Vector2(0f, -262f), new Vector2(260f, 96f), () => InviteFriendsRequested?.Invoke(), UiStyle.Wood, null, 36);
            UiKit.Button(p, "Leave", new Vector2(290f, -262f), new Vector2(260f, 96f), () => { LeaveRequested?.Invoke(); QuitToMenu(); }, UiStyle.Red, "Icon_Quit", 36);
        }

        static void Show(RectTransform rt, bool on)
        {
            if (rt == null || rt.gameObject.activeSelf == on) return;
            rt.gameObject.SetActive(on);
            if (on) rt.SetAsLastSibling();
        }

        void Command(byte c)
        {
            if (RunCommand != null) RunCommand(c);
            else if (c == 0) run.RestartFromCheckpoint();
            else run.RestartRun();
        }

        void Resume()
        {
            if (localPause) { localPause = false; Show(pauseMenu, false); }
            else run.Resume();
        }

        /// <summary>Online: the pause menu is a local overlay; the shared run keeps going for everyone.</summary>
        public void ToggleLocalPause()
        {
            localPause = !localPause;
            Show(pauseMenu, localPause);
        }

        // ------------------------------------------------------------------ state
        void OnState(RunState from, RunState to)
        {
            bool menu = to == RunState.MainMenu || to == RunState.Disconnected;
            Show(mainMenu, menu);
            Show(lobby, to == RunState.Lobby);
            if (menu || to == RunState.Lobby) localPause = false;
            Show(pauseMenu, to == RunState.Paused || localPause);
            Show(results, to == RunState.Finished);
            if (to != RunState.GameOver) { Show(gameOver, false); gameOverShowAt = -1f; }
            Show(hud, !menu && to != RunState.Lobby && to != RunState.Finished);
            if (resetButton != null) resetButton.gameObject.SetActive(!run.Endless);
            Show(controls, false);
            Show(settings, false);

            GameAudio.SetMusic(menu || to == RunState.Lobby || to == RunState.Finished || to == RunState.GameOver ? MusicTrack.Menu : MusicTrack.Gameplay);
            GameAudio.SetAmbience(!menu && to != RunState.Lobby);

            if (to == RunState.Disconnected) Banner("DISCONNECTED", UiKit.Warn, 3f);
            if (to == RunState.Countdown || to == RunState.Ready) { tipStep = 0; tipTimer = 0f; lastMilestone = 0; bestAnnounced = false; }
            if (to == RunState.GameOver)
            {
                Banner("CRASH!", UiKit.Danger, 1.2f);
                GameAudio.PlayUI(AudioCue.Wipeout);
                RaftCameraShake.Add(0.8f);
                foreach (var s in raft.seats.Seats) if (s.IsOccupied && s.characterAnimator != null) s.characterAnimator.PlayWipeout();
                gameOverShowAt = Time.unscaledTime + 1.3f;
            }
            if (to == RunState.Finished) FillResults();
            if (to == RunState.Playing && from == RunState.Countdown) { Banner("GO!", UiKit.Gas, 0.9f); GameAudio.PlayUI(AudioCue.CountdownGo); }
            if (to == RunState.Finished)
            {
                GameAudio.PlayUI(AudioCue.Finish);
                int v = 0;
                foreach (var s in raft.seats.Seats) if (s.IsOccupied && s.characterAnimator != null) s.characterAnimator.PlayVictory(v++);
            }
        }

        Camera Cam => viewCamera != null ? viewCamera : Camera.main;

        void Banner(string text, Color c, float seconds)
        {
            banner.text = text;
            banner.color = c;
            bannerUntil = Time.unscaledTime + seconds;
            banner.enabled = true;
            bannerPunch.Punch();
        }

        void FillResults()
        {
            var st = run.Stats;
            resTime.text = FormatTime(st.elapsed);
            resCheckpoints.text = (st.checkpointIndex + 1) + "/" + st.checkpointCount;
            resWipeouts.text = st.wipeouts.ToString();
            resFarts.text = st.thrustActivations.ToString();
            resDistance.text = st.distance.ToString("F0") + " m";
            var seats = raft.seats.Seats;
            int row = 0;
            for (int i = 0; i < seats.Length && i < resultRows.Count; i++)
            {
                var s = seats[i];
                if (!s.IsOccupied) continue;
                var r = resultRows[row++];
                r.root.gameObject.SetActive(true);
                r.dot.color = s.PlayerColor;
                r.name.text = s.PlayerName;
                r.stats.text = st.activationsPerSeat[s.seatIndex] + " farts  ·  " + st.thrustSecondsPerSeat[s.seatIndex].ToString("F1") + " s of thrust";
            }
            for (; row < resultRows.Count; row++) resultRows[row].root.gameObject.SetActive(false);
        }

        static string FormatTime(float t) => $"{Mathf.FloorToInt(t / 60f)}:{t % 60f:00.0}";

        int lastCountdown = -1;

        void Update()
        {
            if (run == null) return;
            UpdateResponsive();
            var st = run.Stats;
            if (run.Endless) UpdateEndless(st);
            else
            {
                timer.text = FormatTime(st.elapsed);
                checkpoint.text = $"{Mathf.Max(0, st.checkpointIndex + 1)}/{Mathf.Max(1, st.checkpointCount)}";
                progress.Value = st.progress01;
                progressKnob.anchorMin = progressKnob.anchorMax = new Vector2(Mathf.Clamp01(st.progress01), 0.5f);
            }
            timeSmallRoot.gameObject.SetActive(run.Endless);
            if (gameOverShowAt > 0f && Time.unscaledTime >= gameOverShowAt && run.State == RunState.GameOver)
            {
                gameOverShowAt = -1f;
                FillGameOver();
                Show(gameOver, true);
                Show(hud, false);
                GameAudio.PlayUI(AudioCue.Finish, 0.6f);
            }
            string s = ConnectionStatus != null ? ConnectionStatus() : "";
            status.text = s;
            statusRoot.gameObject.SetActive(!string.IsNullOrEmpty(s));

            if (run.State == RunState.Countdown)
            {
                int n = Mathf.CeilToInt(run.CountdownRemaining);
                if (n != lastCountdown && n > 0) { Banner(n.ToString(), UiKit.Cream, 1.1f); GameAudio.PlayUI(AudioCue.Countdown); }
                lastCountdown = n;
            }
            else lastCountdown = -1;
            if (banner.enabled && Time.unscaledTime >= bannerUntil) banner.enabled = false;

            prompt.text = recovery.StuckPrompt ? "Stuck? Press R (Y on gamepad) to reset to the last checkpoint"
                        : run.State == RunState.Wipeout || run.State == RunState.Respawning ? "Back to the last checkpoint..."
                        : "";
            promptRoot.gameObject.SetActive(prompt.text.Length > 0);
            UpdateTips();
            tipRoot.gameObject.SetActive(tip.text.Length > 0 && !promptRoot.gameObject.activeSelf);
            UpdateCards();
            UpdateRadar();
            if (lobby.gameObject.activeSelf) UpdateLobby();
            if (mainMenu.gameObject.activeSelf)
            {
                if (accountLine != null) accountLine.text = AccountLine != null ? AccountLine() : "";
                int badge = FriendsBadge != null ? FriendsBadge() : 0;
                friendsBadge.gameObject.SetActive(badge > 0);
                if (badge > 0) friendsBadgeText.text = badge > 9 ? "9+" : badge.ToString();
            }
        }

        /// <summary>Online UI: a short message in the banner.</summary>
        public void Toast(string text, bool bad = false) => Banner(text, bad ? UiKit.Warn : UiKit.Cream, 2.4f);

        /// <summary>Endless HUD: distance, best, way to the best, milestones.</summary>
        void UpdateEndless(RunStats st)
        {
            float best = run.BestScore;
            timer.text = Mathf.FloorToInt(st.score) + " m";
            cpCaption.text = "BEST";
            checkpoint.text = Mathf.FloorToInt(Mathf.Max(best, st.score)).ToString();
            checkpoint.fontSize = checkpoint.text.Length > 3 ? 26 : 34;
            timeSmall.text = FormatTime(st.elapsed).Split('.')[0];
            float goal = best > Milestone * 0.5f && st.score < best ? best : (Mathf.Floor(st.score / Milestone) + 1f) * Milestone;
            float from = goal == best ? 0f : goal - Milestone;
            float f = Mathf.Clamp01((st.score - from) / Mathf.Max(1f, goal - from));
            progress.Value = f;
            progressKnob.anchorMin = progressKnob.anchorMax = new Vector2(f, 0.5f);
            finishLabel.text = goal == best ? "BEST" : Mathf.RoundToInt(goal) + " m";
            bool running = run.State == RunState.Playing || run.State == RunState.Checkpoint;
            if (!running) return;
            int m = Mathf.FloorToInt(st.score / Milestone);
            if (m > lastMilestone)
            {
                lastMilestone = m;
                int lvl = FartOnline.Game.Endless.ObstaclePlanner.DangerLevel(m * Milestone);
                if (m % 2 == 0) Banner("DANGER LEVEL " + lvl + "!  Hazards speed up!", UiKit.Danger, 2f);
                else Banner(Mathf.RoundToInt(m * Milestone) + " m!  FASTER!", UiKit.Accent, 1.6f);
                GameAudio.PlayUI(AudioCue.Checkpoint);
            }
            if (!bestAnnounced && best > 20f && st.score > best)
            {
                bestAnnounced = true;
                Banner("NEW BEST!", UiKit.Gas, 1.6f);
                GameAudio.PlayUI(AudioCue.Checkpoint);
            }
        }

        void UpdateTips()
        {
            if (!tutorialTips || session.Mode != SessionMode.Solo || run.State != RunState.Playing && run.State != RunState.Checkpoint) { tip.text = ""; return; }
            tipTimer += Time.deltaTime;
            string[] tips = run.Endless && touchLayout ? new[]
            {
                "Tap FART to fart: it pushes the raft AWAY from you.",
                "Squeeze through the gaps and stay off the spiked banks: one bump and it's game over!",
                "Drag the stick to walk around the raft: sit on the LEFT to slide RIGHT.",
                "Look for the open lane between the obstacles. BRAKE slows you down.",
            } : run.Endless ? new[]
            {
                "Press F to fart: it pushes the raft AWAY from you.",
                "Squeeze through the gaps and stay off the spiked banks: one bump and it's game over!",
                "A / D walk you around the raft: sit on the LEFT to slide RIGHT.",
                "Look for the open lane between the obstacles. S is a quick brake.",
            } : new[]
            {
                "Press F to fart: it pushes the raft AWAY from you.",
                "Hold F to keep farting. Watch the green gas bar!",
                "A / D walk you around the raft: sit opposite to where you want to go.",
                "Hold Shift and release for a charged blast. S is a quick brake.",
            };
            if (tipStep < tips.Length)
            {
                tip.text = tips[tipStep];
                if (tipTimer > 6f) { tipStep++; tipTimer = 0f; }
            }
            else tip.text = "";
        }

        void UpdateCards()
        {
            var seats = raft.seats.Seats;
            int row = 0;
            for (int i = 0; i < seats.Length; i++)
            {
                var s = seats[i];
                var c = cards[i];
                c.root.gameObject.SetActive(s.IsOccupied);
                if (!s.IsOccupied) continue;
                c.root.anchoredPosition = cardOrigin + new Vector2(0f, (cardTop ? -1f : 1f) * row++ * cardStep);
                var ts = s.Thrust.State;
                c.swatch.color = s.PlayerColor;
                c.number.text = "P" + (i + 1);
                c.name.text = s.PlayerName + (s.IsLocal ? " (you)" : s.IsAI && !s.PlayerName.Contains("AI") ? " (AI)" : "");
                c.gas.Value = ts.gas01;
                c.gas.Color = ts.exhausted ? UiKit.Warn : ts.charge01 > 0f ? UiKit.Accent : UiKit.Gas;
                c.state.text = ts.exhausted ? "EMPTY!" : ts.charge01 > 0f ? "CHARGE " + Mathf.RoundToInt(ts.charge01 * 100f) + "%" : ts.sustaining ? "PFFFT!" : Mathf.Abs(ts.moving) > 0.1f ? "walking" : "";
                c.state.color = ts.exhausted ? UiKit.Warn : ts.charge01 > 0f ? UiKit.Accent : UiKit.Gas;
                var voice = FartOnline.Game.Online.VoiceChat.Instance;
                if (voice != null && run.Networked && !s.IsAI)
                {
                    if (voice.IsMuted(s.OwnerClientId)) { c.state.text = "muted"; c.state.color = UiKit.Warn; }
                    else if (voice.IsTalking(s.OwnerClientId)) { c.state.text = "TALKING"; c.state.color = UiKit.Accent; }
                }
            }
        }

        void ToggleMuteSeat(int seatIndex)
        {
            var voice = FartOnline.Game.Online.VoiceChat.Instance;
            var s = raft.seats.Seats[seatIndex];
            if (voice == null || !run.Networked || !s.IsOccupied || s.IsLocal || s.IsAI) return;
            voice.ToggleMute(s.OwnerClientId);
            Banner(voice.IsMuted(s.OwnerClientId) ? s.PlayerName + " muted" : s.PlayerName + " unmuted", UiKit.Cream, 1.2f);
        }

        /// <summary>Radar: seats drawn around a circle as seen from the camera (up = camera forward on the water).</summary>
        void UpdateRadar()
        {
            var cam = Cam;
            if (cam == null) return;
            Vector3 cf = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 cr = Vector3.Cross(Vector3.up, cf);
            float radius = 92f;
            Vector3 total = Vector3.zero;
            var seats = raft.seats.Seats;
            for (int i = 0; i < seats.Length; i++)
            {
                var s = seats[i];
                var dot = radarDots[i];
                dot.gameObject.SetActive(s.IsOccupied);
                if (!s.IsOccupied) continue;
                Vector3 w = s.transform.position - raft.transform.position;
                Vector2 p = new Vector2(Vector3.Dot(w, cr), Vector3.Dot(w, cf)).normalized * radius;
                ((RectTransform)dot.transform).anchoredPosition = p;
                dot.color = s.PlayerColor;
                var ts = s.Thrust.State;
                float pulse = ts.sustaining ? 1.25f + Mathf.Sin(Time.unscaledTime * 18f) * 0.08f : 1f;
                dot.transform.localScale = Vector3.one * pulse;
                if (ts.sustaining) total += raft.transform.TransformDirection(new Vector3(ts.direction.x, 0f, ts.direction.y));
            }
            if (total.sqrMagnitude > 0.01f)
            {
                pushArrow.gameObject.SetActive(true);
                Vector2 d = new Vector2(Vector3.Dot(total, cr), Vector3.Dot(total, cf));
                pushArrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
                pushArrow.sizeDelta = new Vector2(16f, Mathf.Clamp(d.magnitude * 45f, 30f, 80f));
            }
            else pushArrow.gameObject.SetActive(false);
        }

        void UpdateLobby()
        {
            var seats = raft.seats.Seats;
            int taken = 0;
            for (int i = 0; i < lobbySeats.Count && i < seats.Length; i++)
            {
                var s = seats[i];
                var ls = lobbySeats[i];
                bool on = s.IsOccupied;
                if (on) taken++;
                ls.dot.color = on ? s.PlayerColor : new Color(1f, 1f, 1f, 0.35f);
                ls.number.enabled = on;
                ls.name.text = on ? s.PlayerName : "free seat";
                ls.name.color = on ? UiKit.Ink : new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.45f);
            }
            bool canStart = CanStartOnline != null && CanStartOnline();
            lobbyStart.interactable = canStart;
            lobbyStatus.text = (canStart ? "Everyone aboard? Press Start!" : "Waiting for the room leader to start...") + "\n" + taken + " / 4 pigs on the raft";
            string info = SessionInfo != null ? SessionInfo() : "";
            lobbyInfo.text = info;
            lobbyInfo.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(info));
        }

        void QuitToMenu()
        {
            Time.timeScale = 1f;
            if (session.Mode == SessionMode.OnlineHost || session.Mode == SessionMode.OnlineClient) LeaveRequested?.Invoke();
            session.EndSession();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Network layer: show the lobby screen.</summary>
        public void ShowLobby() => run.EnterLobby();
    }
}
