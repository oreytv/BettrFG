using System;
using System.Reflection;
using BetterFG.Services;
using FallGuysLib.Players;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BetterFG.UI
{
    public static class UGUIShip
    {
        // every dropdown panel registers here so opening one closes the rest. dead (destroyed)
        // entries get pruned lazily on open.
        static readonly System.Collections.Generic.List<GameObject> _openDropdownPanels = new System.Collections.Generic.List<GameObject>();

        public static void SetInputText(InputField field, string value, bool notify = false)
        {
            if (field == null) return;
            value = value ?? "";
            field.text = value;
            if (field.textComponent != null) field.textComponent.text = value;
            if (field.placeholder != null) field.placeholder.gameObject.SetActive(string.IsNullOrEmpty(value));
            if (notify) field.onValueChanged?.Invoke(value);
        }

        // �� Canvas ������������������������������������������������������������
        public static Canvas CreateCanvas(string name = "UGUIShip_Canvas")
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BetterFG.Services.UIScaleService.CurrentRef;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // �� Solid panel �������������������������������������������������������
        public static RectTransform CreatePanel(Transform parent, Rect rect,
            Color color, string name = "Panel")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetPixelRect(rt, rect);
            go.AddComponent<Image>().color = color;
            return rt;
        }

        // �� Gradient panel (top � bottom) �������������������������������������
        public static RectTransform CreateGradientPanel(Transform parent, Rect rect,
            Color topColor, Color bottomColor, string name = "GradientPanel")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetPixelRect(rt, rect);

            go.AddComponent<Image>().color = Color.white;

            var grad = go.AddComponent<GradientImage>();
            grad.Vertical = true;
            grad.TopColor = topColor;
            grad.BottomColor = bottomColor;

            return rt;
        }

        // �� Draggable window (solid bg) ���������������������������������������
        public static RectTransform CreateDraggableWindow(Transform parent, Rect rect,
            Color bgColor, string name = "Window")
        {
            var rt = CreatePanel(parent, rect, bgColor, name);
            rt.gameObject.AddComponent<DragHandler>();
            return rt;
        }

        // �� Label �������������������������������������������������������������
        public static Text CreateLabel(Transform parent, Rect rect, string text,
            int fontSize = 14, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = color ?? Color.white;
            t.alignment = anchor;
            t.raycastTarget = false;
            return t;
        }

        // small "?" help marker. hovering it pops `tip` on top of the "?" with no delay. the tooltip
        // itself lives on the root canvas (BetterFGUIMan draws it there) so it's never clipped by a
        // scroll viewport — this just wires the hover trigger. drop one after any label that needs a
        // note/credit. returns the GameObject so callers can position/size it however they like.
        public static GameObject CreateHelp(Transform parent, Rect rect, string tip, int fontSize = 11)
        {
            var go = new GameObject("Help");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            // transparent hit graphic on the root so the whole rect catches the hover
            var hit = go.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            // faint filled circle behind the "?" — square + centered so it stays round whatever the
            // rect aspect is. unicode circled glyphs don't render in Arial, so draw one. Knob.psd is
            // Unity's builtin round sprite.
            float d = Mathf.Min(rect.width, rect.height);
            var circGo = new GameObject("Circle");
            circGo.transform.SetParent(go.transform, false);
            var circRt = circGo.AddComponent<RectTransform>();
            circRt.anchorMin = circRt.anchorMax = new Vector2(0.5f, 0.5f);
            circRt.pivot = new Vector2(0.5f, 0.5f);
            circRt.anchoredPosition = Vector2.zero;
            circRt.sizeDelta = new Vector2(d, d);
            var circle = circGo.AddComponent<Image>();
            circle.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            circle.type = Image.Type.Simple;
            circle.color = new Color(1f, 1f, 1f, 0.18f);
            circle.raycastTarget = false;

            var qGo = new GameObject("Q");
            qGo.transform.SetParent(go.transform, false);
            var qRt = qGo.AddComponent<RectTransform>();
            qRt.anchorMin = Vector2.zero; qRt.anchorMax = Vector2.one;
            qRt.offsetMin = qRt.offsetMax = Vector2.zero;
            var t = qGo.AddComponent<Text>();
            t.text = "?";
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(1f, 1f, 1f, 0.85f);
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;

            var trig = go.AddComponent<TooltipTrigger>();
            trig.text = tip;
            trig.instant = true;
            return go;
        }

        // clickable URL/link label. shows `text` in `linkColor`, brightens to `hoverColor` on hover,
        // opens `url` on click (or runs onClick if given). transparent hit rect stretched behind the
        // text so the whole line is clickable. hover recolor is driven by a tiny watcher component so
        // callers don't have to poll it in their own Update.
        public static Text CreateLinkLabel(Transform parent, Rect rect, string text, string url,
            int fontSize = 15, Color? linkColor = null, Color? hoverColor = null, Action onClick = null,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var link = linkColor ?? new Color(0.55f, 0.80f, 1.00f, 1f);
            var hover = hoverColor ?? new Color(0.25f, 0.50f, 0.90f, 1f);

            var hitGo = new GameObject("LinkHit");
            hitGo.transform.SetParent(parent, false);
            SetPixelRect(hitGo.AddComponent<RectTransform>(), rect);
            var hit = hitGo.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            var btn = hitGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            var capturedUrl = url;
            AddButtonClick(btn, onClick ?? (() => { if (!string.IsNullOrEmpty(capturedUrl)) Application.OpenURL(capturedUrl); }));

            var textGo = new GameObject("LinkText");
            textGo.transform.SetParent(hitGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = link;
            t.alignment = anchor;
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;

            var w = hitGo.AddComponent<LinkHover>();
            w.Text = t; w.Idle = link; w.Hover = hover;
            return t;
        }

        // �� Button textures (cached) ������������������������������������������
        private static Sprite _btnSprite;
        private static Sprite _btnShineSprite;
        private static Sprite _radialGradCornerSprite;

        public static Sprite GetButtonSprite()
        {
            if (_btnSprite != null) return _btnSprite;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.general.button.png");
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _btnSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception ex) { Plugin.Log.LogError("UGUIShip: button.png load failed: " + ex.Message); }
            return _btnSprite;
        }

        private static Sprite GetButtonShineSprite()
        {
            if (_btnShineSprite != null) return _btnShineSprite;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.general.button_shine.png");
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _btnShineSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(16, 16, 16, 16)
                );
            }
            catch (Exception ex) { Plugin.Log.LogError("UGUIShip: button_shine.png load failed: " + ex.Message); }
            return _btnShineSprite;
        }

        public static Sprite GetRadialGradCornerSprite()
        {
            if (_radialGradCornerSprite != null) return _radialGradCornerSprite;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.general.radialgradcorner128.png");
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _radialGradCornerSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 1f));
            }
            catch (Exception ex) { Plugin.Log.LogError("UGUIShip: radialgradcorner128.png load failed: " + ex.Message); }
            return _radialGradCornerSprite;
        }

        private static GameObject BuildShine(GameObject parent)
        {
            var shineSprite = GetButtonShineSprite();
            if (shineSprite == null) return null;

            var shineGo = new GameObject("Shine");
            shineGo.transform.SetParent(parent.transform, false);
            var shineRt = shineGo.AddComponent<RectTransform>();
            shineRt.anchorMin = Vector2.zero;
            shineRt.anchorMax = Vector2.one;
            shineRt.offsetMin = shineRt.offsetMax = Vector2.zero;
            shineRt.localScale = Vector3.one;
            var shineImg = shineGo.AddComponent<Image>();
            shineImg.sprite = shineSprite;
            shineImg.type = Image.Type.Sliced;
            shineImg.pixelsPerUnitMultiplier = 5f;
            shineImg.color = new Color(1f, 1f, 1f, 0.4f);
            shineImg.raycastTarget = false;
            shineGo.SetActive(true);
            TabHoverStyle.RegisterShine(shineImg); // tint it + track for live tint changes
            return shineGo;
        }

        private static void WireShineHover(GameObject btn, GameObject shine)
        {
            var trigger = btn.GetComponent<EventTrigger>() ?? btn.AddComponent<EventTrigger>();
            var shineImg = shine.GetComponent<Image>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                if (shineImg != null) { var t = TabHoverStyle.Tint; shineImg.color = new Color(t.r, t.g, t.b, 1f); }
            }));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                if (shineImg != null) { var t = TabHoverStyle.Tint; shineImg.color = new Color(t.r, t.g, t.b, 0.4f); }
            }));
            trigger.triggers.Add(exit);
        }

        public static void WireButtonAudio(GameObject btn, bool skipHoverSound = false)
        {
            var trigger = btn.GetComponent<EventTrigger>() ?? btn.AddComponent<EventTrigger>();

            if (!skipHoverSound)
            {
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(new Action<BaseEventData>(_ => AudioService.PlayButtonHoverOn()));
                trigger.triggers.Add(enter);
            }

        }

        // Forward mouse-wheel scroll from this element up to the enclosing ScrollRect. UGUI routes a
        // scroll to the first ancestor implementing IScrollHandler; the EventTrigger we add for hover
        // /audio implements it, so it swallows the wheel and the list freezes while the pointer is over
        // a button. Handing the scroll back to the ScrollRect makes lists scroll no matter what's hovered.
        public static void ForwardScrollToParent(GameObject go)
        {
            if (go == null) return;
            var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
            var scroll = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            scroll.callback.AddListener(new Action<BaseEventData>(data =>
            {
                var sr = go.GetComponentInParent<ScrollRect>();
                var ped = data?.TryCast<PointerEventData>();
                if (sr != null && ped != null) sr.OnScroll(ped);
            }));
            trigger.triggers.Add(scroll);
        }

        private static void AddButtonClick(Button btn, Action onClick)
        {
            btn.onClick.AddListener(new Action(() =>
            {
                AudioService.PlayButtonClick();
                onClick?.Invoke();
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            }));
        }

        // �� Button (Rect overload) ��������������������������������������������
        public static Button CreateButton(Transform parent, Rect rect, string label,
            Color bgColor, Color textColor, int fontSize = 13, Action onClick = null,
            bool skipHoverSound = false, bool customSprite = true)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var img = go.AddComponent<Image>();
            img.color = bgColor;
            if (customSprite)
            {
                var btnSprite = GetButtonSprite();
                if (btnSprite != null)
                {
                    img.sprite = btnSprite;
                    img.type = Image.Type.Simple;
                }
            }

            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = bgColor;
            cols.highlightedColor = bgColor * 1.2f;
            cols.pressedColor = bgColor * 0.7f;
            cols.fadeDuration = 0f;
            btn.colors = cols;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            AddButtonClick(btn, onClick);

            if (customSprite)
            {
                var shineGo = BuildShine(go);
                if (shineGo != null) WireShineHover(go, shineGo);
            }

            WireButtonAudio(go, skipHoverSound);
            ForwardScrollToParent(go);

            CreateLabel(go.transform,
                new Rect(0, 0, rect.width, rect.height),
                label, fontSize, textColor, TextAnchor.MiddleCenter);

            return btn;
        }

        // �� Icon button (texture, no label, no background) ��������������������
        /// <summary>
        /// Creates a clickable RawImage button with no background or label.
        /// Navigation disabled, deselects on click, hover sound wired.
        /// Used by SideWheelManager icon slots.
        /// </summary>
        // hold-to-repeat button state: fires onFire once on press, then after HoldDelay seconds of
        // holding, repeats every RepeatInterval seconds until released. caller ticks
        // Tick(Time.unscaledDeltaTime) from its own ManagedUpdate/Update — no new MonoBehaviour/IL2Cpp
        // registration needed.
        public sealed class HoldButtonState
        {
            public float HoldDelay = 0.5f;
            public float RepeatInterval = 0.05f;
            public Action OnRelease; // fires once when the hold ends — used to commit one undo entry per hold
            private Action _fire;
            private bool _held;
            private bool _firedOnce;
            private float _timer;

            public void Tick(float dt)
            {
                if (!_held) return;
                _timer += dt;
                float threshold = _firedOnce ? RepeatInterval : HoldDelay;
                if (_timer >= threshold)
                {
                    _timer = 0f;
                    _firedOnce = true;
                    _fire?.Invoke();
                }
            }

            private void Press() { _held = true; _firedOnce = false; _timer = 0f; }
            private void Release()
            {
                if (!_held) return;
                _held = false;
                OnRelease?.Invoke();
            }

            public static HoldButtonState Wire(GameObject go, Action onFire)
            {
                var state = new HoldButtonState { _fire = onFire };
                var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();

                var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                down.callback.AddListener(new Action<BaseEventData>(_ => { state.Press(); onFire?.Invoke(); }));
                trigger.triggers.Add(down);

                var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
                up.callback.AddListener(new Action<BaseEventData>(_ => state.Release()));
                trigger.triggers.Add(up);

                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(new Action<BaseEventData>(_ => state.Release()));
                trigger.triggers.Add(exit);

                return state;
            }
        }

        // button that fires onFire immediately on press, then repeats while held (HoldDelay, then
        // RepeatInterval). returns the HoldButtonState via out param — caller must call Tick() every frame.
        public static Button CreateHoldButton(Transform parent, Rect rect, string label,
            Color bgColor, Color textColor, int fontSize, Action onFire, out HoldButtonState holdState)
        {
            var btn = CreateButton(parent, rect, label, bgColor, textColor, fontSize, onClick: null);
            holdState = HoldButtonState.Wire(btn.gameObject, onFire);
            return btn;
        }

        // plain clickable text, no button background/sprite — brightens on hover (LinkHover),
        // click sound + action on click. for "this setting moved, click here" style references that
        // shouldn't look like a button.
        public static Text CreateLinkText(Transform parent, Rect rect, string label,
            Action onClick, Color? idle = null, int fontSize = 11, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var idleColor = idle ?? new Color(0.4f, 0.7f, 1f, 1f);
            var go = new GameObject("Link_" + label);
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var text = go.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.color = idleColor;
            text.alignment = align;
            text.raycastTarget = true;

            var hover = go.AddComponent<LinkHover>();
            hover.Text = text;
            hover.Idle = idleColor;
            hover.Hover = Color.white;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            AddButtonClick(btn, onClick);

            return text;
        }

        public static Button CreateIconButton(Transform parent, Vector2 size, Texture2D icon,
            Action onClick, Action onHoverEnter = null, Action onHoverExit = null)
        {
            var go = new GameObject("IconButton");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            var raw = go.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = raw;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = Color.white;
            cols.pressedColor = Color.white;
            cols.disabledColor = Color.white;
            cols.colorMultiplier = 1f;
            btn.colors = cols;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            if (onClick != null)
                btn.onClick.AddListener(new Action(() =>
                {
                    onClick();
                    if (EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(null);
                }));

            var trigger = go.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                AudioService.PlayButtonHoverOn();
                onHoverEnter?.Invoke();
            }));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ => onHoverExit?.Invoke()));
            trigger.triggers.Add(exit);

            return btn;
        }

// �� Notice (info text + bean + take-me-there button) �����������������������
        // recurring "X has moved to Y � take me there" block. bean on the left at the row's full
        // height (aspect-fit), info label + button stacked to its right. returns the y-height
        // consumed so callers can advance their cursor by `cy += UGUIShip.CreateNotice(...) + PAD`.
        //
        // info: short label (supports \n). action: what the button does. beanRes defaults to
        // bean_victorious.png; pass null to omit the bean and stretch text+button full-width.
        public static float CreateNotice(Transform parent, float x, float y, float w,
            string info, Action action, string buttonLabel = "Take me there",
            string beanRes = "BetterFG.assets.ui.bean.bean_victorious.png")
        {
            const int lines = 2;
            float labelH = UIScale.LH * lines;
            float btnH = UIScale.BTN_H * 0.7f;
            float totalH = labelH + UIScale.SH + btnH;

            float textX = x;
            float textW = w;
            if (!string.IsNullOrEmpty(beanRes))
            {
                var beanTex = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture(beanRes);
                if (beanTex != null)
                {
                    float beanW = totalH * 0.6f;
                    CreateImage(parent, new Rect(x, y, beanW, totalH), beanTex, "NoticeBean");
                    textX = x + beanW + UIScale.PAD;
                    textW = w - beanW - UIScale.PAD;
                }
            }

            CreateLabel(parent, new Rect(textX, y, textW, labelH), info, UIScale.FS_SM,
                new Color(1f, 0.85f, 0.3f, 0.9f));
            var btnColor = new Color(0.45f, 0.35f, 0.25f, 1f);
            float btnW = Mathf.Min(textW, UIScale.BTN_W * 0.9f);
            CreateButton(parent, new Rect(textX, y + labelH + UIScale.SH, btnW, btnH),
                buttonLabel, btnColor, Color.white, UIScale.FS_SM, action);

            return totalH;
        }

        // non-interactive image. give it a rect (or just width/height) and a texture; aspect-fits
        // inside the rect so the source ratio is preserved. used for decorative beans / icons next to
        // text in tabs.
        public static RawImage CreateImage(Transform parent, Rect rect, Texture2D tex, string name = "Image")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var raw = go.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;

            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                float srcAspect = (float)tex.width / tex.height;
                float boxAspect = rect.width / rect.height;
                if (srcAspect > boxAspect)
                {
                    // letterbox vertically: shrink height to match texture aspect within the box width.
                    float h = rect.width / srcAspect;
                    float yOff = (rect.height - h) * 0.5f;
                    SetPixelRect(raw.rectTransform, new Rect(rect.x, rect.y + yOff, rect.width, h));
                }
                else if (srcAspect < boxAspect)
                {
                    // pillarbox horizontally.
                    float w = rect.height * srcAspect;
                    float xOff = (rect.width - w) * 0.5f;
                    SetPixelRect(raw.rectTransform, new Rect(rect.x + xOff, rect.y, w, rect.height));
                }
            }

            return raw;
        }

        // �� Sprite button (custom sprite, no shine, standard click + hover audio) ��
        // for icon buttons that use a Sprite (not a Texture2D RawImage). pass an optional
        // hoverSprite to swap on hover/press. returns the Image so callers can change the sprite
        // later (e.g. a toggle star). no shine � icon buttons don't get the shine overlay.
        public static (Button btn, Image img) CreateSpriteButton(Transform parent, Rect rect,
            Sprite idle, Sprite hover = null, Action onClick = null, bool preserveAspect = true)
        {
            var go = new GameObject("SpriteButton");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var img = go.AddComponent<Image>();
            img.color = Color.white;
            img.preserveAspect = preserveAspect;
            if (idle != null) img.sprite = idle;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            if (hover != null)
            {
                btn.transition = Selectable.Transition.SpriteSwap;
                var st = btn.spriteState;
                st.highlightedSprite = hover;
                st.pressedSprite = hover;
                st.selectedSprite = hover;
                btn.spriteState = st;
            }
            else btn.transition = Selectable.Transition.None;

            AddButtonClick(btn, onClick);
            WireButtonAudio(go); // hover sound, no shine

            return (btn, img);
        }

        // �� Button (layout group variant) ������������������������������������
        public static Button CreateButton(Transform parent, string label,
            Color bgColor, Color textColor, int fontSize = 13, Action onClick = null,
            bool skipHoverSound = false, bool customSprite = true, bool shine = true)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var img = go.AddComponent<Image>();
            img.color = bgColor;
            if (customSprite)
            {
                var btnSprite = GetButtonSprite();
                if (btnSprite != null)
                {
                    img.sprite = btnSprite;
                    img.type = Image.Type.Simple;
                }
            }

            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = bgColor;
            cols.highlightedColor = bgColor * 1.2f;
            cols.pressedColor = bgColor * 0.7f;
            cols.fadeDuration = 0f;
            btn.colors = cols;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            AddButtonClick(btn, onClick);

            if (customSprite && shine)
            {
                var shineGo = BuildShine(go);
                if (shineGo != null) WireShineHover(go, shineGo);
            }

            WireButtonAudio(go, skipHoverSound);
            ForwardScrollToParent(go);

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
            var t = lblGo.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = textColor;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;

            return btn;
        }

        // �� Icon button (texture only, no bg, nav disabled, hover sound) ������
        public static Button CreateIconButton(Transform parent, Rect rect, Texture2D icon,
            Action onClick = null, int hoveredIdx = -1, Action<int> onHoverEnter = null,
            Action<int> onHoverExit = null, int idx = -1)
        {
            var go = new GameObject("IconButton");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var raw = go.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = raw;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = Color.white;
            cols.pressedColor = Color.white;
            cols.disabledColor = Color.white;
            cols.colorMultiplier = 1f;
            btn.colors = cols;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            if (onClick != null)
                btn.onClick.AddListener(new Action(() =>
                {
                    onClick();
                    if (EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(null);
                }));

            var trigger = go.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                AudioService.PlayButtonHoverOn();
                onHoverEnter?.Invoke(idx);
            }));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ => onHoverExit?.Invoke(idx)));
            trigger.triggers.Add(exit);

            return btn;
        }
        /// <summary>
        /// Builds a labeled horizontal slider using Unity's exact DefaultControls hierarchy.
        /// Fill Area: anchored (0,0.25)-(1,0.75), offsetMin.x=5, offsetMax.x=-5
        /// Fill: anchorMin(0,0) anchorMax(1,1), offsetMax.x=0  � Slider writes anchorMax.x
        /// Handle Slide Area: full stretch, offsetMin.x=10, offsetMax.x=-10
        /// Handle: anchorMin(0,0) anchorMax(0,1), sizeDelta.x=20 � Slider writes anchorMin/Max.x
        /// </summary>
        public static Slider CreateSlider(Transform parent, float x, float y, float w,
            string lbl, float init, float lh, float pad, int fontSize, Action<float> onChange,
            Color? labelColor = null, Color? fillColor = null, bool reserveLabel = true)
        {
            bool hasLabel = reserveLabel && !string.IsNullOrEmpty(lbl);
            float lblW = hasLabel ? fontSize * 2f : 0f;
            float lblGap = hasLabel ? pad : 0f;
            float sldW = w - lblW - lblGap;

            // label
            if (hasLabel)
            {
                var lblGo = new GameObject("Label_" + lbl);
                lblGo.transform.SetParent(parent, false);
                SetPixelRect(lblGo.AddComponent<RectTransform>(), new Rect(x, y, lblW, lh));
                var lt = lblGo.AddComponent<Text>();
                lt.text = lbl;
                lt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                lt.fontSize = fontSize;
                lt.color = labelColor ?? Color.white;
                lt.alignment = TextAnchor.MiddleLeft;
                lt.raycastTarget = false;
            }

            // slider root � same height as lh, full row
            var sldGo = new GameObject("Slider_" + lbl);
            sldGo.transform.SetParent(parent, false);
            var sldRt = sldGo.AddComponent<RectTransform>();
            SetPixelRect(sldRt, new Rect(x + lblW + lblGap, y, sldW, lh));

            // Background � full stretch
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(sldGo.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.25f);
            bgRt.anchorMax = new Vector2(1f, 0.75f);
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgGo.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);

            // Fill Area � Unity DefaultControls exact values
            var fillAreaGo = new GameObject("Fill Area");
            fillAreaGo.transform.SetParent(sldGo.transform, false);
            var faRt = fillAreaGo.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.25f);
            faRt.anchorMax = new Vector2(1f, 0.75f);
            faRt.offsetMin = new Vector2(5f, 0f);
            faRt.offsetMax = new Vector2(-5f, 0f);

            // Fill � Slider component drives anchorMax.x at runtime
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = fillColor ?? new Color(0.8f, 0.8f, 0.8f, 1f);
            fillImg.raycastTarget = false;

            // Handle Slide Area � inset 10px each side (Unity default)
            var hsGo = new GameObject("Handle Slide Area");
            hsGo.transform.SetParent(sldGo.transform, false);
            var hsRt = hsGo.AddComponent<RectTransform>();
            hsRt.anchorMin = Vector2.zero;
            hsRt.anchorMax = Vector2.one;
            hsRt.offsetMin = new Vector2(10f, 0f);
            hsRt.offsetMax = new Vector2(-10f, 0f);

            // Handle � Slider drives anchorMin/Max.x; sizeDelta.x = width, height = stretch
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(hsGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            handleRt.sizeDelta = new Vector2(20f, 0f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = Color.white;
            handleImg.sprite = GetButtonSprite();

            // Slider component
            var slider = sldGo.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = init;

            slider.onValueChanged.AddListener(new Action<float>(v => onChange(v)));

            return slider;
        }

        // ── RGB sliders + compact hex input ──────────────────────────────────
        // builds R/G/B sliders plus a short #RRGGBB field on its own row. getR/G/B read the
        // current channel, setR/G/B write it, onApply saves + applies (swatch + game). editing
        // the hex moves the sliders; dragging a slider rewrites the hex — guarded so they don't
        // fight. sliders are returned in case the caller needs to push values into them later.
        public static void CreateColorControls(Transform parent, float x, ref float cy, float w,
            Func<float> getR, Func<float> getG, Func<float> getB,
            Action<float> setR, Action<float> setG, Action<float> setB, Action onApply,
            out Slider sR, out Slider sG, out Slider sB)
        {
            float lh = UIScale.LH, sh = UIScale.SH, pad = UIScale.PAD;
            int fs = UIScale.FS_SM;
            var suppress = new bool[1];   // shared 1-cell flag so all closures see the same value
            InputField hex = null;
            Slider lsR = null, lsG = null, lsB = null;

            void RefreshHex()
            {
                if (hex == null) return;
                suppress[0] = true;
                SetInputText(hex, "#" + ColorToHex(getR(), getG(), getB()));
                suppress[0] = false;
            }

            lsR = CreateSlider(parent, x, cy, w, "R", getR(), lh, pad, fs,
                v => { if (suppress[0]) return; setR(v); onApply(); RefreshHex(); },
                new Color(1f, 0.3f, 0.3f), new Color(1f, 0.3f, 0.3f));
            cy += lh + sh;
            lsG = CreateSlider(parent, x, cy, w, "G", getG(), lh, pad, fs,
                v => { if (suppress[0]) return; setG(v); onApply(); RefreshHex(); },
                new Color(0.3f, 1f, 0.3f), new Color(0.3f, 1f, 0.3f));
            cy += lh + sh;
            lsB = CreateSlider(parent, x, cy, w, "B", getB(), lh, pad, fs,
                v => { if (suppress[0]) return; setB(v); onApply(); RefreshHex(); },
                new Color(0.4f, 0.6f, 1f), new Color(0.4f, 0.6f, 1f));
            cy += lh + sh;

            float lblW = fs * 2.4f;
            float fieldW = fs * 7f;   // fits "#RRGGBB", not a whole row
            CreateLabel(parent, new Rect(x, cy, lblW, lh), "HEX", fs, new Color(1f, 1f, 1f, 0.35f));
            hex = CreateInputField(parent, new Rect(x + lblW, cy, fieldW, lh), "#RRGGBB", null, null, fs);
            hex.characterLimit = 7;
            hex.onEndEdit.AddListener(new Action<string>(txt =>
            {
                if (suppress[0]) return;
                if (!HexToColor(txt, out float r, out float g, out float b)) { RefreshHex(); return; }
                setR(r); setG(g); setB(b);
                suppress[0] = true;
                if (lsR != null) lsR.value = r;
                if (lsG != null) lsG.value = g;
                if (lsB != null) lsB.value = b;
                suppress[0] = false;
                onApply();
            }));
            RefreshHex();
            cy += lh + pad;

            sR = lsR; sG = lsG; sB = lsB;
        }

        public static string ColorToHex(float r, float g, float b)
        {
            int ir = Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255);
            int ig = Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255);
            int ib = Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255);
            return ir.ToString("X2") + ig.ToString("X2") + ib.ToString("X2");
        }

        public static bool HexToColor(string s, out float r, out float g, out float b)
        {
            r = g = b = 0f;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().TrimStart('#');
            if (s.Length == 3) s = "" + s[0] + s[0] + s[1] + s[1] + s[2] + s[2];   // #RGB → #RRGGBB
            if (s.Length != 6) return false;
            const System.Globalization.NumberStyles hx = System.Globalization.NumberStyles.HexNumber;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (!int.TryParse(s.Substring(0, 2), hx, ci, out int ir)) return false;
            if (!int.TryParse(s.Substring(2, 2), hx, ci, out int ig)) return false;
            if (!int.TryParse(s.Substring(4, 2), hx, ci, out int ib)) return false;
            r = ir / 255f; g = ig / 255f; b = ib / 255f;
            return true;
        }

        // �� InputField ��������������������������������������������������������
        public static InputField CreateInputField(Transform parent, Rect rect,
            string placeholder = "", Color? bgColor = null, Color? textColor = null,
            int fontSize = 13)
        {
            var bg = bgColor ?? new Color(0.15f, 0.15f, 0.15f, 1f);
            var tc = textColor ?? Color.white;

            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var img = go.AddComponent<Image>();
            img.color = bg;
            var fieldSprite = GetButtonSprite();
            if (fieldSprite != null)
            {
                img.sprite = fieldSprite;
                img.type = Image.Type.Simple;
            }

            var field = go.AddComponent<InputField>();
            var nav = field.navigation;
            nav.mode = Navigation.Mode.None;
            field.navigation = nav;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 2);
            textRt.offsetMax = new Vector2(-6, -2);
            var textComp = textGo.AddComponent<Text>();
            textComp.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            textComp.fontSize = fontSize;
            textComp.color = tc;
            textComp.alignment = TextAnchor.MiddleLeft;
            textComp.supportRichText = false;
            textComp.raycastTarget = false;
            textComp.horizontalOverflow = HorizontalWrapMode.Overflow;
            textComp.verticalOverflow = VerticalWrapMode.Overflow;

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var phRt = phGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(6, 2);
            phRt.offsetMax = new Vector2(-6, -2);
            var phText = phGo.AddComponent<Text>();
            phText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            phText.fontSize = fontSize;
            phText.color = new Color(tc.r, tc.g, tc.b, 0.4f);
            phText.fontStyle = FontStyle.Italic;
            phText.text = placeholder;
            phText.alignment = TextAnchor.MiddleLeft;
            phText.supportRichText = false;
            phText.raycastTarget = false;

            field.textComponent = textComp;
            field.placeholder = phText;
            SetInputText(field, "", false);

            // auto: any field with "search" in its placeholder gets the magnifying-glass icon +
            // left-pad shift. one place to maintain it; every search bar gets it for free.
            if (!string.IsNullOrEmpty(placeholder) &&
                placeholder.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0)
                AddSearchIcon(field);

            return field;
        }

        // sized off the field's font size at 0.75x — same ratio as the PersonalBestTab header
        // dropdown icons so all search bars match. exposed public for the one hand-rolled search
        // field that doesn't go through CreateInputField (CustomizationTab).
        public static void AddSearchIcon(InputField field, string resource = "BetterFG.assets.ui.button.search.png")
        {
            if (field == null) return;
            var sprite = BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite(resource);
            if (sprite == null) return;

            int fs = field.textComponent != null ? field.textComponent.fontSize : 13;
            float size = fs * 0.75f;
            float gap = 4f;
            float leftPad = 6f + size + gap;

            var iconGo = new GameObject("SearchIcon");
            iconGo.transform.SetParent(field.transform, false);
            var rt = iconGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(6f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            var img = iconGo.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;

            if (field.textComponent != null)
            {
                var trt = field.textComponent.GetComponent<RectTransform>();
                if (trt != null) trt.offsetMin = new Vector2(leftPad, trt.offsetMin.y);
            }
            if (field.placeholder != null)
            {
                var prt = field.placeholder.GetComponent<RectTransform>();
                if (prt != null) prt.offsetMin = new Vector2(leftPad, prt.offsetMin.y);
            }
        }

        // �� Flow label (inside vertical layout groups) ������������������������
        public static Text CreateFlowLabel(Transform parent, string text, int fontSize, Color color, bool multiline = false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = color;
            t.raycastTarget = false;
            if (multiline)
            {
                // grow the rect downward to fit every wrapped line instead of clipping to one
                le.minHeight = fontSize + 2f;
                t.alignment = TextAnchor.UpperLeft;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
            else
            {
                le.preferredHeight = fontSize + 2f;
                t.alignment = TextAnchor.MiddleLeft;
            }
            return t;
        }

        // �� Stretch label (anchored fill, centered) ���������������������������
        public static Text CreateStretchLabel(Transform parent, string text, int fontSize, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            return t;
        }

        // �� Divider (1px horizontal line) �������������������������������������
        public static void CreateDivider(Transform parent, float x, float y, float w)
        {
            CreatePanel(parent, new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
        }

        // �� Increment stepper ([-] value [+]) ��������������������������������
        // one place for every -/value/+ control. lays out the two step buttons either side of the value
        // inside `rect`, wired to get/set. wrap=true (default) loops min<->max on overflow (7 +1 -> 0),
        // wrap=false clamps. isFloat keeps decimals, otherwise the value stays whole. `fmt` formats the
        // value text. onChange fires after set, for the caller to save/refresh. the value sits in a real
        // InputField, so a pad can hold +/- and a keyboard can type the extreme the steps won't reach.
        // returns it so callers can resync if the value changes elsewhere.
        // pass a 2-long `holds` array to get hold-to-repeat on the −/+ buttons (filled minus-then-plus);
        // that makes it the caller's job to Tick() them every frame, which is why it's opt-in.
        private static readonly Color IncStepCol = new Color(0.22f, 0.32f, 0.42f, 1f);
        public static InputField CreateIncrement(Transform parent, Rect rect, float min, float max,
            Func<float> get, Action<float> set, float step, bool isFloat = false, bool wrap = true,
            int fontSize = 13, Func<float, string> fmt = null, Action<float> onChange = null,
            HoldButtonState[] holds = null)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (fmt == null) fmt = v => isFloat ? v.ToString(ci) : Mathf.RoundToInt(v).ToString(ci);

            float stepW = rect.height;                    // square step buttons
            float valW = rect.width - stepW * 2f;

            var field = CreateInputField(parent, new Rect(rect.x + stepW, rect.y, valW, rect.height),
                "", new Color(0.12f, 0.12f, 0.12f, 1f), Color.white, fontSize);
            field.contentType = isFloat ? InputField.ContentType.DecimalNumber : InputField.ContentType.IntegerNumber;
            field.textComponent.alignment = TextAnchor.MiddleCenter;
            SetInputText(field, fmt(get()), false);

            void Commit(float v)
            {
                v = Mathf.Clamp(v, min, max);
                if (!isFloat) v = Mathf.Round(v);
                set(v);
                SetInputText(field, fmt(v), false);
                onChange?.Invoke(v);
            }

            field.onEndEdit.AddListener(new Action<string>(s =>
            {
                if (float.TryParse(s, System.Globalization.NumberStyles.Float, ci, out var typed)) Commit(typed);
                else SetInputText(field, fmt(get()), false);
            }));

            void Step(float x, float delta, string glyph, int slot)
            {
                var fire = new Action(() =>
                {
                    // decimal, and anchored at 0 instead of min. float stepping drifts (0.1+0.05
                    // = 0.15000001) and the field renders every digit of it; anchoring the grid
                    // at a min of 0.01 walks you along 0.06/0.11/0.16 instead of round numbers
                    decimal grid = (decimal)step;
                    float nv = (float)(Math.Round(((decimal)get() + (decimal)delta) / grid) * grid);
                    float span = max - min + (isFloat ? 0f : 1f);
                    if (wrap && span > 0f) nv = min + Mathf.Repeat(nv - min, span);
                    Commit(nv);
                });
                var r = new Rect(x, rect.y, stepW, rect.height);
                if (holds == null) CreateButton(parent, r, glyph, IncStepCol, Color.white, fontSize, fire);
                else CreateHoldButton(parent, r, glyph, IncStepCol, Color.white, fontSize, fire, out holds[slot]);
            }

            Step(rect.x, -step, "−", 0);                 // minus sign, matches existing rows
            Step(rect.x + stepW + valW, step, "+", 1);
            return field;
        }

        // int flavour, the original shape — every existing caller still lands here
        public static InputField CreateIncrement(Transform parent, Rect rect, int min, int max,
            Func<int> get, Action<int> set, bool wrap = true, int fontSize = 13,
            Func<int, string> fmt = null, Action<int> onChange = null)
            => CreateIncrement(parent, rect, min, max, () => get(), v => set(Mathf.RoundToInt(v)),
                1f, false, wrap, fontSize,
                fmt == null ? null : new Func<float, string>(v => fmt(Mathf.RoundToInt(v))),
                onChange == null ? null : new Action<float>(v => onChange(Mathf.RoundToInt(v))));

        // �� Dropdown ����������������������������������������������������������
        // shared dropdown control (the one the Font tab and Skin Texture tab use). pass options
        // and an onChange; templateHeight controls how tall the open list gets. listWidth, when
        // > 0, fixes the open list to that pixel width (left-aligned) instead of matching the button.
        public static Dropdown CreateDropdown(Transform parent, Rect rect,
            System.Collections.Generic.List<string> options, int selected, Action<int> onChange,
            int fontSize = 10, float templateHeight = 120f, float listWidth = 0f)
        {
            var go = new GameObject("Dropdown");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);
            var bg = go.AddComponent<Image>();
            bg.color = Color.black; // fully black header, like the pb-tab dropdowns
            var btnSpr = GetButtonSprite();
            if (btnSpr != null) { bg.sprite = btnSpr; bg.type = Image.Type.Simple; }
            var dd = go.AddComponent<Dropdown>();
            dd.transition = Selectable.Transition.None;
            dd.alphaFadeSpeed = 0f; // no fade in/out on the popup

            // shine + hover/click audio so it feels like every other button
            if (btnSpr != null)
            {
                var shineGo = BuildShine(go);
                if (shineGo != null) WireShineHover(go, shineGo);
            }
            WireButtonAudio(go);
            // click sound when the dropdown is opened (pointer down), not just on value change
            {
                var trig = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
                var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                down.callback.AddListener(new Action<BaseEventData>(_ => AudioService.PlayButtonClick()));
                trig.triggers.Add(down);
            }

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(6f, 2f); lblRt.offsetMax = new Vector2(-24f, -2f);
            var lbl = lblGo.AddComponent<Text>();
            lbl.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            lbl.fontSize = fontSize; lbl.color = Color.white; lbl.alignment = TextAnchor.MiddleLeft;
            dd.captionText = lbl;

            var templateGo = new GameObject("Template");
            templateGo.transform.SetParent(go.transform, false);
            var tRt = templateGo.AddComponent<RectTransform>();
            if (listWidth > 0f)
            {
                // fixed-width list, left-aligned to the button instead of matching its width
                tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(0f, 0f);
                tRt.pivot = new Vector2(0f, 1f); tRt.anchoredPosition = Vector2.zero;
                tRt.sizeDelta = new Vector2(listWidth, templateHeight);
            }
            else
            {
                tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(1f, 0f);
                tRt.pivot = new Vector2(0.5f, 1f); tRt.anchoredPosition = Vector2.zero;
                tRt.sizeDelta = new Vector2(0f, templateHeight);
            }
            templateGo.AddComponent<Image>().color = Color.black; // fully black list, like the pb-tab dropdowns
            var sr2 = templateGo.AddComponent<ScrollRect>();
            sr2.horizontal = false; sr2.vertical = true; sr2.movementType = ScrollRect.MovementType.Clamped;
            sr2.scrollSensitivity = 20f;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(templateGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<Image>();
            vpGo.AddComponent<Mask>().showMaskGraphic = false;
            sr2.viewport = vpRt;

            var cGo = new GameObject("Content");
            cGo.transform.SetParent(vpGo.transform, false);
            var cRt = cGo.AddComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 1f); cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f); cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = new Vector2(0f, 28f);
            sr2.content = cRt;

            var itemGo = new GameObject("Item");
            itemGo.transform.SetParent(cGo.transform, false);
            var itemRt = itemGo.AddComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f); itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 20f);
            // faint 3% white base + brighten on hover, matching the pb-tab dropdown rows. Unity clones
            // this one item per option, so the hover uses the Toggle's color states (applied per-clone).
            var itemImg = itemGo.AddComponent<Image>();
            itemImg.color = Color.white; // tinted by the toggle's normalColor below
            var tog = itemGo.AddComponent<Toggle>();
            tog.transition = Selectable.Transition.ColorTint;
            tog.targetGraphic = itemImg;
            var itemCols = tog.colors;
            itemCols.normalColor = new Color(1f, 1f, 1f, 0.03f);
            itemCols.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
            itemCols.pressedColor = new Color(1f, 1f, 1f, 0.18f);
            itemCols.selectedColor = new Color(1f, 1f, 1f, 0.03f);
            itemCols.fadeDuration = 0f;
            tog.colors = itemCols;

            var iLblGo = new GameObject("Item Label");
            iLblGo.transform.SetParent(itemGo.transform, false);
            var iLblRt = iLblGo.AddComponent<RectTransform>();
            iLblRt.anchorMin = Vector2.zero; iLblRt.anchorMax = Vector2.one;
            iLblRt.offsetMin = new Vector2(6f, 0f); iLblRt.offsetMax = new Vector2(-28f, 0f);
            var iLbl = iLblGo.AddComponent<Text>();
            iLbl.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            iLbl.fontSize = fontSize; iLbl.color = Color.white; iLbl.alignment = TextAnchor.MiddleLeft;

            var chkGo = new GameObject("Checkmark");
            chkGo.transform.SetParent(itemGo.transform, false);
            var chkRt = chkGo.AddComponent<RectTransform>();
            chkRt.anchorMin = new Vector2(1f, 0f); chkRt.anchorMax = new Vector2(1f, 1f);
            chkRt.pivot = new Vector2(1f, 0.5f);
            chkRt.offsetMin = new Vector2(-24f, 0f); chkRt.offsetMax = new Vector2(-6f, 0f);
            var chk = chkGo.AddComponent<Text>();
            chk.text = "\u2714";
            chk.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            chk.fontSize = fontSize + 2;
            chk.color = new Color(1f, 0.85f, 0.2f);
            chk.alignment = TextAnchor.MiddleCenter;
            chk.raycastTarget = false;

            tog.isOn = true;
            tog.graphic = chk;
            dd.itemText = iLbl;
            dd.template = tRt;
            templateGo.SetActive(false);

            if (options != null && options.Count > 0)
            {
                dd.ClearOptions();
                foreach (var o in options) dd.options.Add(new Dropdown.OptionData(o));
                dd.value = Mathf.Clamp(selected, 0, options.Count - 1);
                dd.RefreshShownValue();
            }

            if (onChange != null)
                dd.onValueChanged.AddListener(new Action<int>(v => onChange(v)));

            return dd;
        }

        // �� Multi-select dropdown ���������������������������������������������
        // a button with a fixed label that opens a small panel of toggle rows, each with a
        // checkmark on the right showing on/off. onToggle(index, newState) fires per click.
        // returns the button so the caller can recolour it (e.g. yellow when any option is on).
        public static Button CreateMultiSelectDropdown(Transform parent, Rect rect, string label,
            System.Collections.Generic.List<string> options, System.Collections.Generic.List<bool> initial,
            Action<int, bool> onToggle, int fontSize = 10, float listWidth = 0f, float rowH = 20f,
            bool singleSelect = false, bool closeOnPick = false, bool showAbove = false,
            System.Collections.Generic.List<Sprite> rowSprites = null, bool rightAlignText = false)
        {
            int n = options?.Count ?? 0;
            float w = listWidth > 0f ? listWidth : rect.width;
            float panelH = rowH * n + 4f;
            var checks = new System.Collections.Generic.List<GameObject>(); // for single-select radio behaviour

            // header button � fully black like the panel (shine + audio come free from CreateButton)
            var btn = CreateButton(parent, rect, label, Color.black, Color.white, fontSize);

            // optional image overlay (stretched across the button, behind the text) + right-aligned text
            if (rowSprites != null)
            {
                int selIdx = 0;
                if (initial != null)
                    for (int k = 0; k < initial.Count; k++) if (initial[k]) { selIdx = k; break; }
                var headImgGo = new GameObject("HeaderImg");
                headImgGo.transform.SetParent(btn.transform, false);
                var hiRt = headImgGo.AddComponent<RectTransform>();
                hiRt.anchorMin = Vector2.zero; hiRt.anchorMax = Vector2.one;
                hiRt.offsetMin = hiRt.offsetMax = Vector2.zero;
                var hiImg = headImgGo.AddComponent<Image>();
                hiImg.raycastTarget = false;
                hiImg.preserveAspect = false;
                if (selIdx < rowSprites.Count) hiImg.sprite = rowSprites[selIdx];
                headImgGo.transform.SetAsFirstSibling(); // behind the label
            }
            if (rightAlignText)
            {
                var headLbl = btn.GetComponentInChildren<Text>();
                if (headLbl != null)
                {
                    headLbl.alignment = TextAnchor.MiddleRight;
                    // nudge text off the right edge without resizing the (top-left anchored) rect
                    var lrt = headLbl.GetComponent<RectTransform>();
                    if (lrt != null) lrt.sizeDelta = new Vector2(lrt.sizeDelta.x - 8f, lrt.sizeDelta.y);
                    headLbl.transform.SetAsLastSibling(); // on top of the image
                }
            }

            // panel sits under the button by default, or above it when showAbove is set (so it never
            // runs off the bottom). parent it to the SAME parent (not the button) and make it the last
            // sibling so it draws ON TOP. fully black, no button sprite (rounded edges wash it out).
            float panelY = showAbove ? rect.y - panelH - 2f : rect.y + rect.height + 2f;
            var panelGo = new GameObject("MSPanel");
            panelGo.transform.SetParent(parent, false);
            var pRt = panelGo.AddComponent<RectTransform>();
            SetPixelRect(pRt, new Rect(rect.x, panelY, w, panelH));
            var pImg = panelGo.AddComponent<Image>();
            pImg.color = Color.black;
            pImg.raycastTarget = true; // eats clicks so nothing behind the panel steals them
            panelGo.SetActive(false);

            // register so opening any dropdown closes the others (only one open at a time)
            _openDropdownPanels.Add(panelGo);

            // toggle the panel open/closed on header click. bring it to front EVERY time it opens
            // (anything created after this, like a scrollview, would otherwise draw on top of it).
            btn.onClick.AddListener(new Action(() =>
            {
                bool show = !panelGo.activeSelf;
                // close every other dropdown panel before opening this one
                for (int k = _openDropdownPanels.Count - 1; k >= 0; k--)
                {
                    var p = _openDropdownPanels[k];
                    if (p == null) { _openDropdownPanels.RemoveAt(k); continue; }
                    if (p != panelGo) p.SetActive(false);
                }
                panelGo.SetActive(show);
                if (show) panelGo.transform.SetAsLastSibling();
            }));

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                bool on = initial != null && i < initial.Count && initial[i];

                // each row is a plain top-left pixel rect inside the panel � no stretched anchors.
                // zebra: every other row 3% white, the rest fully clear (panel behind is black)
                var rowGo = new GameObject("MSRow_" + i);
                rowGo.transform.SetParent(panelGo.transform, false);
                SetPixelRect(rowGo.AddComponent<RectTransform>(), new Rect(2f, 2f + rowH * i, w - 4f, rowH));
                var rImg = rowGo.AddComponent<Image>();
                Color rowBase = (i % 2 == 0) ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0f);
                rImg.color = rowBase;

                // optional image overlay stretched across the row, behind the text
                if (rowSprites != null && i < rowSprites.Count && rowSprites[i] != null)
                {
                    var riGo = new GameObject("RowImg");
                    riGo.transform.SetParent(rowGo.transform, false);
                    var riRt = riGo.AddComponent<RectTransform>();
                    riRt.anchorMin = Vector2.zero; riRt.anchorMax = Vector2.one;
                    riRt.offsetMin = riRt.offsetMax = Vector2.zero;
                    var riImg = riGo.AddComponent<Image>();
                    riImg.sprite = rowSprites[i];
                    riImg.raycastTarget = false;
                }

                // hover brighten so rows feel interactive (the black bg made the default tint invisible)
                var hov = rowGo.AddComponent<EventTrigger>();
                var hEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                hEnter.callback.AddListener(new Action<BaseEventData>(_ => rImg.color = new Color(1f, 1f, 1f, 0.18f)));
                hov.triggers.Add(hEnter);
                var hExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                hExit.callback.AddListener(new Action<BaseEventData>(_ => rImg.color = rowBase));
                hov.triggers.Add(hExit);

                CreateLabel(rowGo.transform, new Rect(6f, 0f, w - 28f, rowH), options[i],
                    fontSize, Color.white, rightAlignText ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);

                // checkmark on the right, visible when on
                var chk = CreateLabel(rowGo.transform, new Rect(w - 24f, 0f, 18f, rowH), "\u2714",
                    fontSize + 2, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter);
                chk.gameObject.SetActive(on);
                checks.Add(chk.gameObject);

                var rowBtn = rowGo.AddComponent<Button>();
                var nav2 = rowBtn.navigation; nav2.mode = Navigation.Mode.None; rowBtn.navigation = nav2;
                rowBtn.transition = Selectable.Transition.None;
                rowBtn.targetGraphic = rImg;
                rowBtn.onClick.AddListener(new Action(() =>
                {
                    bool ns;
                    if (singleSelect)
                    {
                        // radio: this row on, all others off
                        for (int k = 0; k < checks.Count; k++)
                            if (checks[k] != null) checks[k].SetActive(k == idx);
                        ns = true;
                    }
                    else
                    {
                        ns = !chk.gameObject.activeSelf;
                        chk.gameObject.SetActive(ns);
                    }
                    AudioService.PlayButtonClick();
                    onToggle?.Invoke(idx, ns);
                    if (closeOnPick) panelGo.SetActive(false);
                }));
                WireButtonAudio(rowGo);
            }

            return btn;
        }

        // width the scroll view's viewport is inset by on each side (bar sits in the right one)
        public const float SCROLLBAR_INSET = 13f;

        // �� Scroll view �������������������������������������������������������
        public static (ScrollRect scrollRect, RectTransform content) CreateScrollView(
            Transform parent, Rect rect)
        {
            const float barW = 12f;
            const float barGap = 1f;

            var rootGo = new GameObject("ScrollView");
            rootGo.transform.SetParent(parent, false);
            var rootRt = rootGo.AddComponent<RectTransform>();
            SetPixelRect(rootRt, rect);

            var sr = rootGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 25f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;

            // needs an Image so the ScrollRect has a raycast surface for scroll events
            var rootImg = rootGo.AddComponent<Image>();
            rootImg.color = Color.clear;
            rootImg.raycastTarget = true;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(rootGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(barW + barGap, 0f);
            vpRt.offsetMax = new Vector2(-(barW + barGap), 0f);
            vpRt.pivot = new Vector2(0f, 1f);
            // Image needed on viewport too for RectMask2D to clip correctly
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = Color.clear;
            vpImg.raycastTarget = false;
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.offsetMin = contentRt.offsetMax = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, rect.height);

            sr.viewport = vpRt;
            sr.content = contentRt;

            var barGo = new GameObject("Scrollbar");
            barGo.transform.SetParent(rootGo.transform, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.offsetMin = new Vector2(-(barW + barGap), 0f);
            barRt.offsetMax = new Vector2(-barGap, 0f);
            var barBgGo = new GameObject("Background");
            barBgGo.transform.SetParent(barGo.transform, false);
            var barBgRt = barBgGo.AddComponent<RectTransform>();
            barBgRt.anchorMin = Vector2.zero;
            barBgRt.anchorMax = Vector2.one;
            barBgRt.offsetMin = barBgRt.offsetMax = Vector2.zero;
            barBgRt.localScale = new Vector3(1f, -1f, 1f);
            var barBg = barBgGo.AddComponent<Image>();
            barBg.sprite = GetButtonShineSprite();
            barBg.type = Image.Type.Sliced;
            barBg.pixelsPerUnitMultiplier = 8f;
            barBg.color = new Color(1f, 1f, 1f, 0.35f);

            var slideGo = new GameObject("Sliding Area");
            slideGo.transform.SetParent(barGo.transform, false);
            var slideRt = slideGo.AddComponent<RectTransform>();
            slideRt.anchorMin = Vector2.zero;
            slideRt.anchorMax = Vector2.one;
            slideRt.offsetMin = slideRt.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(slideGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = handleRt.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.35f, 0.35f, 0.35f, 1f);
            handleImg.sprite = GetButtonSprite();
            var handleShine = BuildShine(handleGo);
            if (handleShine != null)
                handleShine.GetComponent<Image>().raycastTarget = false;

            var bar = barGo.AddComponent<Scrollbar>();
            bar.handleRect = handleRt;
            bar.targetGraphic = handleImg;
            bar.direction = Scrollbar.Direction.BottomToTop;
            var barColors = bar.colors;
            barColors.normalColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            barColors.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            barColors.pressedColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            barColors.fadeDuration = 0f;
            bar.colors = barColors;
            var nav = bar.navigation;
            nav.mode = Navigation.Mode.None;
            bar.navigation = nav;

            sr.verticalScrollbar = bar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return (sr, contentRt);
        }



        // �� Helpers �����������������������������������������������������������
        public static void SetPixelRect(RectTransform rt, Rect rect)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
        }

        public static void SetButtonSelected(Button btn, bool selected, Color selectedColor)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            var cols = btn.colors;
            cols.normalColor = selected ? selectedColor : new Color(0.2f, 0.2f, 0.2f, 1f);
            cols.highlightedColor = selected ? selectedColor * 1.2f : new Color(0.3f, 0.3f, 0.3f, 1f);
            btn.colors = cols;
            if (img != null) img.color = cols.normalColor;
        }
    }

    // �� GradientImage ���������������������������������������������������������
    public class GradientImage : BaseMeshEffect
    {
        public GradientImage(IntPtr ptr) : base(ptr) { }

        public bool Vertical = false;

        public Color Left = new Color(0f, 1f, 0.1f, 0f);
        public Color LeftMid = new Color(0f, 1f, 0.1f, 0.18f);
        public Color RightMid = new Color(0f, 1f, 0.1f, 0.18f);
        public Color Right = new Color(0f, 1f, 0.1f, 0f);

        public Color TopColor = Color.white;
        public Color BottomColor = Color.black;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            int count = vh.currentVertCount;
            if (count == 0) return;

            var vert = new UIVertex();

            if (Vertical)
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    if (vert.position.y < minY) minY = vert.position.y;
                    if (vert.position.y > maxY) maxY = vert.position.y;
                }
                float h = maxY - minY;
                if (h < 0.001f) return;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    float t = 1f - (vert.position.y - minY) / h;
                    vert.color = Color.Lerp(TopColor, BottomColor, t);
                    vh.SetUIVertex(vert, i);
                }
            }
            else
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    if (vert.position.x < minX) minX = vert.position.x;
                    if (vert.position.x > maxX) maxX = vert.position.x;
                }
                float w = maxX - minX;
                if (w < 0.001f) return;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    float t = (vert.position.x - minX) / w;
                    vert.color = SampleHorizontal(t);
                    vh.SetUIVertex(vert, i);
                }
            }
        }

        private Color32 SampleHorizontal(float t)
        {
            if (t <= 0f) return Left;
            if (t >= 1f) return Right;
            if (t < 0.33f) return Color.Lerp(Left, LeftMid, t / 0.33f);
            if (t < 0.66f) return Color.Lerp(LeftMid, RightMid, (t - 0.33f) / 0.33f);
            return Color.Lerp(RightMid, Right, (t - 0.66f) / 0.34f);
        }
    }

    // recolors a link Text on hover. lives on the link's transparent hit rect so CreateLinkLabel
    // callers don't have to poll hover state themselves.
    public class LinkHover : MonoBehaviour
    {
        public LinkHover(IntPtr ptr) : base(ptr) { }
        public Text Text;
        public Color Idle;
        public Color Hover;
        private RectTransform _rt;
        private bool _over;

        void Awake() => _rt = GetComponent<RectTransform>();

        void Update()
        {
            if (_rt == null || Text == null) return;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(
                _rt, new Vector2(Input.mousePosition.x, Input.mousePosition.y), null);
            if (over == _over) return;
            _over = over;
            Text.color = over ? Hover : Idle;
        }
    }

    // �� DragHandler �����������������������������������������������������������
    public class DragHandler : MonoBehaviour
    {
        public DragHandler(IntPtr ptr) : base(ptr) { }

        private RectTransform _self;
        private RectTransform _target;
        private RectTransform _parentRt;
        private bool _dragging;
        private Vector2 _dragOffset;

        public DragHandler SetTarget(RectTransform target)
        {
            _target = target;
            return this;
        }

        public void Awake() { _self = GetComponent<RectTransform>(); }

        public void Start()
        {
            if (_target == null) _target = _self;

            var p = _target.parent;
            while (p != null)
            {
                var prt = p.GetComponent<RectTransform>();
                if (prt != null) { _parentRt = prt; break; }
                p = p.parent;
            }
        }

        public void Update()
        {
            if (_self == null || _target == null || _parentRt == null) return;

            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

            if (Input.GetMouseButtonDown(0))
            {
                if (IsDirectHit(mouse))
                {
                    _dragging = true;
                    _dragOffset = _target.anchoredPosition - ScreenToAnchored(mouse);
                }
            }

            if (Input.GetMouseButtonUp(0))
                _dragging = false;

            if (_dragging)
                _target.anchoredPosition = ScreenToAnchored(mouse) + _dragOffset;
        }

        private bool IsDirectHit(Vector2 mouse)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _self, mouse, null, out var local)) return false;
            if (!_self.rect.Contains(local)) return false;

            var windowRoot = _target;
            for (int i = 0; i < windowRoot.childCount; i++)
            {
                var child = windowRoot.GetChild(i)?.GetComponent<RectTransform>();
                if (child == null || child == _self) continue;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    child, mouse, null, out var childLocal)
                    && child.rect.Contains(childLocal))
                    return false;
            }
            return true;
        }

        private Vector2 ScreenToAnchored(Vector2 mouse)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRt, mouse, null, out var local);
            return local;
        }
    }

}
