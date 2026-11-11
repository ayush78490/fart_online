using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FartOnline.Game
{
    public enum UiStyle { Green, Red, Wood, Cream }

    /// <summary>
    /// Code-only uGUI toolkit skinned with the cozy wood sprite set in <c>Resources/UI</c> (Bubble Bits GUI and the
    /// Cozy UI Pack demo, see Assets/Game/UI/UI_CREDITS.md). Panels with a title tab, chunky green / red / wood /
    /// cream buttons with hover and press motion and sounds, rounded progress bars, selectors, sliders and keycaps.
    /// Fonts: Lilita One for display text, Fredoka for body text (both SIL OFL). Falls back to generated shapes and
    /// the built-in font if a resource is missing, so the HUD never breaks.
    /// </summary>
    public static class UiKit
    {
        // palette sampled from the sprite set
        public static readonly Color Ink = Hex(0x4A2C17);
        public static readonly Color WoodDark = Hex(0x8F5A3A);
        public static readonly Color Wood = Hex(0xB47B4F);
        public static readonly Color Parchment = Hex(0xE8C69F);
        public static readonly Color Cream = Hex(0xFFF4DC);
        public static readonly Color Gas = Hex(0xA8D13F);
        public static readonly Color Warn = Hex(0xF2804A);
        public static readonly Color Danger = Hex(0xD9535F);
        public static readonly Color Accent = Hex(0xFFC94A);
        public static readonly Color Panel = new Color(0.29f, 0.17f, 0.09f, 0.82f);
        public static readonly Color Dim = new Color(0.12f, 0.06f, 0.03f, 0.55f);

        static Font display, body, fallback;
        static Sprite rounded, circle, ring, rays;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        /// <summary>Unity-null-safe fallback (never use ?? on UnityEngine.Object).</summary>
        public static T Or<T>(T a, T b) where T : UnityEngine.Object => a != null ? a : b;

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        static Font Fallback => fallback != null ? fallback : (fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        public static Font Display => display != null ? display : (display = Or(Resources.Load<Font>("UI/Fonts/LilitaOne-Regular"), Fallback));
        public static Font Body => body != null ? body : (body = Or(Resources.Load<Font>("UI/Fonts/Fredoka-SemiBold"), Fallback));
        public static Font Font => Display;

        /// <summary>Sprite from Resources/UI by file name (cached); null if missing.</summary>
        public static Sprite S(string name)
        {
            if (sprites.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("UI/" + name);
            sprites[name] = s;
            return s;
        }

        public static Sprite Rounded => rounded != null ? rounded : (rounded = MakeRounded(64, 18));
        public static Sprite Circle => circle != null ? circle : (circle = MakeCircle(64, 0f));
        public static Sprite Ring => ring != null ? ring : (ring = MakeCircle(128, 0.86f));
        /// <summary>Soft sunburst used behind menus (like the sprite pack's showcase backgrounds).</summary>
        public static Sprite Rays => rays != null ? rays : (rays = MakeRays(512, 14));

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeCircle(int size, float inner)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d);
                    if (inner > 0f) a *= Mathf.Clamp01(d - r * inner);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeRays(int size, int count)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) * count;
                    float f = Mathf.Abs(ang - Mathf.Round(ang));                 // 0 at the ray centre, 0.5 between rays
                    float ray = Mathf.Clamp01((0.22f - f) * 40f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    float a = ray * Mathf.Clamp01(dist * 3f) * Mathf.Clamp01((1f - dist) * 2.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // ------------------------------------------------------------------ layout
        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        /// <summary>Centre-anchored rect.</summary>
        public static RectTransform At(Transform parent, string name, Vector2 pos, Vector2 size) => Rect(parent, name, Mid, Mid, Mid, pos, size);

        public static RectTransform Stretch(Transform parent, string name) => Rect(parent, name, Vector2.zero, Vector2.one, Mid, Vector2.zero, Vector2.zero);

        /// <summary>Plain tinted image (generated rounded shape when no sprite is given).</summary>
        public static Image Box(RectTransform rt, Color c, Sprite sprite = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : Rounded;
            img.type = sprite == null || sprite == Rounded ? Image.Type.Sliced : Image.Type.Simple;
            img.color = c;
            return img;
        }

        /// <summary>
        /// 9-sliced sprite from the skin. <paramref name="scale"/> scales the borders (1 = source pixels); pass
        /// 0 to scale them with the rect height, so the piece keeps the proportions of the original art.
        /// </summary>
        public static Image Skin(RectTransform rt, string sprite, Color? tint = null, float scale = 0f, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            var s = S(sprite);
            img.color = tint ?? Color.white;
            img.raycastTarget = raycast;
            if (s == null) { img.sprite = Rounded; img.type = Image.Type.Sliced; if (tint == null) img.color = Wood; return img; }
            img.sprite = s;
            bool sliced = s.border.sqrMagnitude > 0f;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = !sliced;
            if (sliced)
            {
                float h = Mathf.Max(8f, rt.rect.height > 0f ? rt.rect.height : rt.sizeDelta.y);
                float k = scale > 0f ? scale : h / s.rect.height;
                img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, k);
            }
            return img;
        }

        // ------------------------------------------------------------------ text
        public static Text Label(Transform parent, string name, string text, int size, TextAnchor align, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta, bool shadow = true)
        {
            var rt = Rect(parent, name, anchorMin, anchorMax, Mid, pos, sizeDelta);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Display;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Normal;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            if (shadow) Outlined(t, Ink, Mathf.Clamp(size / 14f, 2f, 6f));
            return t;
        }

        /// <summary>Body-font paragraph (no outline), wrapping inside its rect.</summary>
        public static Text Para(Transform parent, string name, string text, int size, TextAnchor align, Color color, Vector2 pos, Vector2 sizeDelta)
        {
            var t = Label(parent, name, text, size, align, color, Mid, Mid, pos, sizeDelta, false);
            t.font = Body;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.lineSpacing = 1.1f;
            return t;
        }

        /// <summary>Chunky sticker outline plus a drop shadow, like the pack's titles.</summary>
        public static void Outlined(Graphic g, Color c, float px)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(px, -px);
            var o2 = g.gameObject.AddComponent<Outline>();
            o2.effectColor = c;
            o2.effectDistance = new Vector2(-px * 0.7f, px * 0.7f);
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(c.r, c.g, c.b, 0.6f);
            s.effectDistance = new Vector2(0f, -px * 1.6f);
        }

        // ------------------------------------------------------------------ buttons
        static string ButtonSprite(UiStyle style, float height)
        {
            switch (style)
            {
                case UiStyle.Green: return "Btn_Green_Large";
                case UiStyle.Red: return "Btn_Red_Large";
                case UiStyle.Cream: return "Btn_White";
                default: return "Btn_Wood";
            }
        }

        public static Button Button(Transform parent, string label, Vector2 pos, Vector2 size, UnityAction onClick, UiStyle style = UiStyle.Green, string icon = null, int fontSize = 0)
        {
            var rt = At(parent, "Btn_" + label, pos, size);
            var img = Skin(rt, ButtonSprite(style, size.y), null, 0f, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f, 1f);
            cb.selectedColor = new Color(1.04f, 1.03f, 1f, 1f);
            cb.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            rt.gameObject.AddComponent<UiButtonFx>();
            b.onClick.AddListener(() => GameAudio.PlayUI(style == UiStyle.Red && label.StartsWith("Back") ? AudioCue.UIBack : AudioCue.UIClick));
            b.onClick.AddListener(onClick);

            bool light = style == UiStyle.Cream;
            Color textColor = light ? Ink : Cream;
            int fs = fontSize > 0 ? fontSize : Mathf.RoundToInt(Mathf.Clamp(size.y * 0.42f, 20f, 46f));
            // the sprites have a thicker bottom lip: nudge the label up a little
            float lift = size.y * 0.04f;
            var txt = Label(rt, "Text", label, fs, TextAnchor.MiddleCenter, textColor, Vector2.zero, Vector2.one, new Vector2(0f, lift), Vector2.zero, !light);
            if (!light) { foreach (var o in txt.GetComponents<Outline>()) o.effectColor = style == UiStyle.Wood ? Ink : Shade(style); }
            if (!string.IsNullOrEmpty(icon) && S(icon) != null)
            {
                float isz = size.y * 0.46f;
                var ir = Rect(rt, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Mid, new Vector2(size.y * 0.62f, lift), new Vector2(isz, isz));
                var ii = ir.gameObject.AddComponent<Image>();
                ii.sprite = S(icon);
                ii.preserveAspect = true;
                ii.raycastTarget = false;
                ii.color = Cream;
                var tr = (RectTransform)txt.transform;
                tr.offsetMin = new Vector2(size.y * 0.75f, tr.offsetMin.y);
                tr.offsetMax = new Vector2(-size.y * 0.25f, tr.offsetMax.y);
            }
            return b;
        }

        static Color Shade(UiStyle s) => s == UiStyle.Green ? Hex(0x4E6B12) : s == UiStyle.Red ? Hex(0x7A2430) : Ink;

        /// <summary>Round icon button (green / red / wood circle with a white icon).</summary>
        public static Button IconButton(Transform parent, string name, string icon, Vector2 pos, float size, UnityAction onClick, UiStyle style = UiStyle.Wood)
        {
            var rt = At(parent, "Btn_" + name, pos, new Vector2(size, size));
            string bg = style == UiStyle.Green ? "Btn_Green_Circle" : style == UiStyle.Red ? "Btn_Red_Circle" : "Btn_WoodRound";
            var img = Skin(rt, bg, null, 0f, true);
            img.preserveAspect = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            rt.gameObject.AddComponent<UiButtonFx>();
            b.onClick.AddListener(() => GameAudio.PlayUI(style == UiStyle.Red ? AudioCue.UIBack : AudioCue.UIClick));
            b.onClick.AddListener(onClick);
            if (S(icon) != null)
            {
                var ir = At(rt, "Icon", new Vector2(0f, size * 0.03f), new Vector2(size * 0.48f, size * 0.48f));
                var ii = ir.gameObject.AddComponent<Image>();
                ii.sprite = S(icon);
                ii.preserveAspect = true;
                ii.raycastTarget = false;
                ii.color = Cream;
            }
            return b;
        }

        // ------------------------------------------------------------------ panels
        /// <summary>
        /// Wooden board with a title tab overlapping its top edge (pack style). Returns the board; children are laid
        /// out from its centre. The board pops in each time it is shown.
        /// </summary>
        public static RectTransform Board(Transform parent, string name, string title, Vector2 pos, Vector2 size, float tabWidth = 0f)
        {
            var p = At(parent, name, pos, size);
            Skin(p, "Panel_Main", null, 0.62f, true);
            p.gameObject.AddComponent<UiPop>();
            if (!string.IsNullOrEmpty(title))
            {
                float w = tabWidth > 0f ? tabWidth : Mathf.Clamp(title.Length * 34f + 120f, 280f, size.x - 80f);
                var tab = Rect(p, "TitleTab", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Mid, new Vector2(0f, 6f), new Vector2(w, 104f));
                Skin(tab, "Pill_Brown");
                Label(tab, "Title", title, 58, TextAnchor.MiddleCenter, Cream, Vector2.zero, Vector2.one, new Vector2(0f, 3f), Vector2.zero);
            }
            return p;
        }

        /// <summary>Thin wood divider line inside a board.</summary>
        public static void Divider(Transform parent, Vector2 pos, float width)
        {
            var d = At(parent, "Divider", pos, new Vector2(width, 8f));
            var img = Box(d, WoodDark);
            img.raycastTarget = false;
        }

        /// <summary>Small caps section caption (dark wood text on the parchment).</summary>
        public static Text Caption(Transform parent, string text, Vector2 pos, float width = 400f, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = Label(parent, "Caption_" + text, text, 24, align, WoodDark, Mid, Mid, pos, new Vector2(width, 30f), false);
            return t;
        }

        // ------------------------------------------------------------------ bars, dots, selectors
        /// <summary>Rounded skin bar (dark track, coloured fill, wood rim). Anchored by its left edge.</summary>
        public static UiBar Bar(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color fill)
        {
            var bg = Rect(parent, name, anchor, anchor, new Vector2(0f, 0.5f), pos, size);
            Skin(bg, "Bar_Back", new Color(1f, 1f, 1f, 0.95f));
            float inset = Mathf.Max(3f, size.y * 0.16f);
            var area = Rect(bg, "FillArea", Vector2.zero, Vector2.one, Mid, Vector2.zero, new Vector2(-inset * 2f, -inset * 2f));
            var f = Rect(area, "Fill", Vector2.zero, new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            var img = Pill(f, fill, size.y - inset * 2f);
            var shine = Rect(f, "Shine", new Vector2(0f, 0.55f), new Vector2(1f, 0.85f), Mid, Vector2.zero, new Vector2(-size.y * 0.5f, 0f));
            var si = Box(shine, new Color(1f, 1f, 1f, 0.28f));
            si.raycastTarget = false;
            var rim = Rect(bg, "Rim", Vector2.zero, Vector2.one, Mid, Vector2.zero, new Vector2(4f, 4f));
            Skin(rim, "Bar_Border", Color.white, (size.y + 4f) / 78f);
            return new UiBar(f, img, size.y - inset * 2f);
        }

        /// <summary>White rounded capsule (exact tint, unlike the brown fill sprite) with ends of radius h/2.</summary>
        static Image Pill(RectTransform rt, Color c, float height)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 18f / Mathf.Max(2f, height * 0.5f);
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static Image Dot(Transform parent, string name, float size, Color c)
        {
            var rt = At(parent, name, Vector2.zero, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Or(S("Circle_On"), Circle);
            img.preserveAspect = true;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>◀ value ▶ selector on a wood pill; returns the value label.</summary>
        public static Text Selector(Transform parent, string name, Vector2 pos, float width, UnityAction prev, UnityAction next)
        {
            var rt = At(parent, "Sel_" + name, pos, new Vector2(width, 70f));
            Skin(rt, "Pill_Brown");
            var t = Label(rt, "Value", "", 30, TextAnchor.MiddleCenter, Cream, Vector2.zero, Vector2.one, new Vector2(0f, 2f), new Vector2(-120f, 0f));
            Arrow(rt, "Prev", new Vector2(-width * 0.5f + 34f, 0f), false, prev);
            Arrow(rt, "Next", new Vector2(width * 0.5f - 34f, 0f), true, next);
            return t;
        }

        static void Arrow(RectTransform parent, string name, Vector2 pos, bool right, UnityAction onClick)
        {
            var rt = At(parent, name, pos, new Vector2(44f, 58f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Or(S("Arrow_A"), Circle);
            img.preserveAspect = true;
            if (right) rt.localScale = new Vector3(-1f, 1f, 1f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            rt.gameObject.AddComponent<UiButtonFx>();
            b.onClick.AddListener(() => GameAudio.PlayUI(AudioCue.UIClick, 0.8f));
            b.onClick.AddListener(onClick);
        }

        /// <summary>Labelled skin slider (0..1).</summary>
        public static Slider Slider(Transform parent, string label, Vector2 pos, float width, float value, UnityAction<float> onChange)
        {
            var row = At(parent, "Slider_" + label, pos, new Vector2(width, 60f));
            Label(row, "Label", label, 30, TextAnchor.MiddleLeft, Ink, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(90f, 2f), new Vector2(180f, 40f), false);
            float trackW = width - 200f;
            var track = Rect(row, "Track", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(190f, 0f), new Vector2(trackW, 36f));
            Skin(track, "Bar_Back", null, 36f / 60f, true);
            var fillArea = Rect(track, "FillArea", Vector2.zero, Vector2.one, Mid, Vector2.zero, new Vector2(-12f, -12f));
            var fill = Rect(fillArea, "Fill", Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            Pill(fill, Hex(0xE0A35C), 24f);
            var handleArea = Rect(track, "HandleArea", Vector2.zero, Vector2.one, Mid, Vector2.zero, new Vector2(-30f, 0f));
            var handle = Rect(handleArea, "Handle", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Mid, Vector2.zero, new Vector2(46f, 46f));
            var hi = handle.gameObject.AddComponent<Image>();
            hi.sprite = Or(S("Slider_Knob"), Circle);
            hi.color = S("Slider_Knob") != null ? Color.white : Cream;
            var s = track.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hi;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = 0f;
            s.maxValue = 1f;
            s.value = value;
            s.onValueChanged.AddListener(onChange);
            return s;
        }

        /// <summary>Cream keycap with a short key name.</summary>
        public static RectTransform KeyCap(Transform parent, string key, Vector2 pos, float width = 0f)
        {
            float w = width > 0f ? width : Mathf.Max(64f, key.Length * 18f + 40f);
            var rt = At(parent, "Key_" + key, pos, new Vector2(w, 58f));
            Skin(rt, "Btn_White");
            Label(rt, "Text", key, key.Length > 6 ? 20 : 24, TextAnchor.MiddleCenter, Ink, Vector2.zero, Vector2.one, new Vector2(0f, 3f), Vector2.zero, false);
            return rt;
        }

        /// <summary>Parchment note strip with dark text (tips, prompts).</summary>
        public static Text Note(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize)
        {
            var rt = Rect(parent, name, anchor, anchor, Mid, pos, size);
            Skin(rt, "Note_Paper", null, size.y / 330f * 1.25f);
            var t = Para(rt, "Text", "", fontSize, TextAnchor.MiddleCenter, Ink, new Vector2(24f, 0f), new Vector2(size.x - 120f, size.y - 10f));
            return t;
        }
    }

    /// <summary>Rounded fill bar driven by a 0..1 value (keeps rounded ends at any value).</summary>
    public class UiBar
    {
        readonly RectTransform fill;
        readonly Image image;
        readonly float minWidth;
        float value = -1f;

        public UiBar(RectTransform fill, Image image, float height) { this.fill = fill; this.image = image; minWidth = height; }

        public float Value
        {
            get => value;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, this.value)) return;
                this.value = v;
                fill.anchorMax = new Vector2(v, 1f);
                fill.sizeDelta = Vector2.zero;
                image.enabled = v > 0.001f;
                // keep the rounded caps intact for tiny values
                if (v > 0.001f && ((RectTransform)fill.parent).rect.width * v < minWidth)
                    fill.sizeDelta = new Vector2(minWidth - ((RectTransform)fill.parent).rect.width * v, 0f);
            }
        }

        public Color Color { set => image.color = value; }
        public RectTransform Root => (RectTransform)fill.parent.parent;
    }

    /// <summary>Hover / press scale and hover sound for skin buttons (unscaled time: works in pause).</summary>
    public class UiButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        float target = 1f, current = 1f, vel;
        bool hover, down;
        Vector3 baseScale;
        Selectable selectable;

        void Awake() { baseScale = transform.localScale; selectable = GetComponent<Selectable>(); }
        void OnEnable() { hover = down = false; current = target = 1f; transform.localScale = baseScale; }

        public void OnPointerEnter(PointerEventData e) { hover = true; if (Interactable) GameAudio.PlayUI(AudioCue.UIHover); }
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
        public void OnPointerDown(PointerEventData e) { down = true; }
        public void OnPointerUp(PointerEventData e) { down = false; }
        public void OnSelect(BaseEventData e) { hover = true; }
        public void OnDeselect(BaseEventData e) { hover = false; }

        bool Interactable => selectable == null || selectable.IsInteractable();

        void Update()
        {
            target = !Interactable ? 1f : down ? 0.93f : hover ? 1.06f : 1f;
            current = Mathf.SmoothDamp(current, target, ref vel, 0.06f, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.localScale = new Vector3(baseScale.x * current, baseScale.y * current, 1f);
        }
    }

    /// <summary>Pop-in when enabled: a quick overshoot scale and fade (unscaled time).</summary>
    public class UiPop : MonoBehaviour
    {
        public float duration = 0.28f;
        public float from = 0.82f;
        /// <summary>Resting scale (the responsive layout shrinks boards to fit small screens).</summary>
        public float baseScale = 1f;
        float t;
        CanvasGroup group;

        /// <summary>Changes the resting scale; applied at once when the pop-in has finished.</summary>
        public void SetBase(float scale)
        {
            baseScale = scale;
            if (t >= 1f) transform.localScale = new Vector3(scale, scale, 1f);
        }

        void OnEnable()
        {
            t = 0f;
            if (group == null) { group = GetComponent<CanvasGroup>(); if (group == null) group = gameObject.AddComponent<CanvasGroup>(); }
            Apply(0f);
            GameAudio.PlayUI(AudioCue.UIOpen);
        }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
            Apply(t);
        }

        void Apply(float k)
        {
            // easeOutBack
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float e = 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
            float s = Mathf.LerpUnclamped(from, 1f, e);
            transform.localScale = new Vector3(s * baseScale, s * baseScale, 1f);
            group.alpha = Mathf.Clamp01(k * 2.5f);
        }
    }

    /// <summary>Banner punch: scales in from big with a tiny wobble each time <see cref="Punch"/> is called.</summary>
    public class UiPunch : MonoBehaviour
    {
        float t = 1f;
        public void Punch() { t = 0f; }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.35f);
            float e = 1f - Mathf.Pow(1f - t, 3f);
            float s = Mathf.Lerp(1.6f, 1f, e) + Mathf.Sin(t * Mathf.PI * 3f) * 0.05f * (1f - t);
            transform.localScale = new Vector3(s, s, 1f);
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 4f) * 4f * (1f - t));
        }
    }

    /// <summary>Gentle idle bob and slow spin helpers for menu decoration.</summary>
    public class UiIdle : MonoBehaviour
    {
        public float bob = 8f, bobSpeed = 1.4f, spin;
        Vector2 basePos;
        RectTransform rt;

        void Awake() { rt = (RectTransform)transform; basePos = rt.anchoredPosition; }

        /// <summary>Moves the bob's rest position (responsive layouts).</summary>
        public void SetBasePosition(Vector2 p)
        {
            if (rt == null) rt = (RectTransform)transform;
            basePos = p;
            rt.anchoredPosition = p;
        }

        void Update()
        {
            float t = Time.unscaledTime;
            if (bob != 0f) rt.anchoredPosition = basePos + new Vector2(0f, Mathf.Sin(t * bobSpeed) * bob);
            if (spin != 0f) rt.localRotation = Quaternion.Euler(0f, 0f, t * spin);
        }
    }
}
