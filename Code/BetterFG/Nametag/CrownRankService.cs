using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FGClient;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Nametag
{
    // In-game crown rank badge on the local player's nametag (PlayerInfoDisplayGameObject.
    // _crownRankBadgeViewModel). Local player only. Two things:
    //   1. override the crown rank text on your own badge (only when the game is showing it — the game's
    //      own crown rank display option decides visibility, never us)
    //   2. recolour the badge sprites (Container/FlareBG glow + Container/Crown) to custom colours
    //
    // We don't patch anything — the badge VM's SetCrownRank(int) is callable directly, so we just drive
    // it from the round lifecycle hooks the rest of the nametag stuff already rides (cleanup +
    // HandleServerStartRound). The badge sprites tint white even though the art is coloured, so the
    // recolour is per-pixel on the texture. Two regions: the crown's orange body → main colour, its
    // light-yellow highlight → highlight colour; FlareBG (one flat glow) follows the main colour. Each
    // pixel keeps its own luminance so the shading survives.
    public static class CrownRankService
    {
        const string KEY_ENABLED = "crownrank.enabled";
        const string KEY_TEXT_ON = "crownrank.text.on";
        const string KEY_TEXT = "crownrank.text";
        const string KEY_MAIN_R = "crownrank.main.r";
        const string KEY_MAIN_G = "crownrank.main.g";
        const string KEY_MAIN_B = "crownrank.main.b";
        const string KEY_HI_R = "crownrank.hi.r";
        const string KEY_HI_G = "crownrank.hi.g";
        const string KEY_HI_B = "crownrank.hi.b";
        const string KEY_RECOLOUR_ON = "crownrank.recolour.on";
        const string KEY_OUT_R = "crownrank.out.r";
        const string KEY_OUT_G = "crownrank.out.g";
        const string KEY_OUT_B = "crownrank.out.b";
        const string KEY_SWAP_SIDE = "crownrank.swapside";

        public static bool Enabled => SettingsService.Get(KEY_ENABLED, "false") == "true";
        public static bool TextOn => SettingsService.Get(KEY_TEXT_ON, "false") == "true";
        public static bool RecolourOn => SettingsService.Get(KEY_RECOLOUR_ON, "false") == "true";
        // crown on the left of the name instead of the right.
        public static bool SwapSide => SettingsService.Get(KEY_SWAP_SIDE, "false") == "true";

        public static string RankText
        {
            get => SettingsService.Get(KEY_TEXT, "");
            set => SettingsService.Set(KEY_TEXT, value ?? "");
        }

        static readonly System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
        static float F(string k, float def) =>
            float.TryParse(SettingsService.Get(k, def.ToString(CI)), System.Globalization.NumberStyles.Float, CI, out float v) ? v : def;

        public static Color MainColour
        {
            get => new Color(F(KEY_MAIN_R, 1f), F(KEY_MAIN_G, 0.55f), F(KEY_MAIN_B, 0.1f));
            set { SettingsService.Set(KEY_MAIN_R, value.r.ToString(CI)); SettingsService.Set(KEY_MAIN_G, value.g.ToString(CI)); SettingsService.Set(KEY_MAIN_B, value.b.ToString(CI)); }
        }
        public static Color HighlightColour
        {
            get => new Color(F(KEY_HI_R, 1f), F(KEY_HI_G, 0.92f), F(KEY_HI_B, 0.55f));
            set { SettingsService.Set(KEY_HI_R, value.r.ToString(CI)); SettingsService.Set(KEY_HI_G, value.g.ToString(CI)); SettingsService.Set(KEY_HI_B, value.b.ToString(CI)); }
        }
        public static Color OutlineColour
        {
            get => new Color(F(KEY_OUT_R, 0f), F(KEY_OUT_G, 0f), F(KEY_OUT_B, 0f));
            set { SettingsService.Set(KEY_OUT_R, value.r.ToString(CI)); SettingsService.Set(KEY_OUT_G, value.g.ToString(CI)); SettingsService.Set(KEY_OUT_B, value.b.ToString(CI)); }
        }

        public static void SetEnabled(bool on) => SettingsService.Set(KEY_ENABLED, on ? "true" : "false");
        public static void SetTextOn(bool on) => SettingsService.Set(KEY_TEXT_ON, on ? "true" : "false");
        public static void SetRecolourOn(bool on) => SettingsService.Set(KEY_RECOLOUR_ON, on ? "true" : "false");
        public static void SetSwapSide(bool on) => SettingsService.Set(KEY_SWAP_SIDE, on ? "true" : "false");

        // the badge's untouched game state, snapshotted the first time we ever drive a given badge (before
        // any mutation). respawn spawns a brand-new badge instance, so this is keyed by instance id — a fresh
        // badge we haven't touched is already pristine and reverting on it is a correct no-op. we restore
        // from this on toggle-off instead of leaving our overrides baked in.
        class BadgePristine { public int rank; public string text; public Sprite flare, crown; public Material tmpMat; public bool ignoreLayout, csfOn; public Vector3 pos; }
        static readonly Dictionary<int, BadgePristine> _pristine = new Dictionary<int, BadgePristine>();

        // drive the local player's in-game crown rank badge. resolves the local PlayerInfoDisplay via the
        // same finder the rest of the nametag code uses, so it works from the round lifecycle hooks. runs
        // even when disabled so it can put a previously-overridden badge back the way the game had it.
        // everything the crown apply needs, so the pipeline takes values instead of reading these statics.
        public struct CrownCfg
        {
            public bool enabled, textOn, recolourOn, swapSide;
            public string text;
            public Color main, highlight, outline;
        }

        public static CrownCfg CfgFromSettings() => new CrownCfg
        {
            enabled = Enabled, textOn = TextOn, recolourOn = RecolourOn, swapSide = SwapSide,
            text = RankText, main = MainColour, highlight = HighlightColour, outline = OutlineColour,
        };

        // in-game entry: local display + saved settings. deferred re-pin allowed (it targets the live tag).
        // the ONLY surface the forced crown count/rank rides — the PlayerInfoDisplay badge (3D + canvas) that
        // sits on the local nametag. menu nametag, party rows and the reward-screen level text are deliberately
        // left alone: forcing a crown there reads as spoofing next to unrelated UI, not customising your tag.
        public static void ApplyLocal() => ApplyCrownTo(NametagFinder.FindLocalDisplay(), CfgFromSettings(), allowDeferred: true);

        // does this player key belong to the local player? matched on the CLEANED name (not object identity) so
        // it still works once profiles swap real names for custom ones. same pattern as the rest of the codebase.
        public static bool IsLocalPlayerKey(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return false;
            string localKey = "";
            try { localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? ""; } catch { }
            if (string.IsNullOrEmpty(localKey)) return false;
            return FallGuysLib.Players.PlayerUtils.CleanPlayerName(playerKey)
                .Equals(FallGuysLib.Players.PlayerUtils.CleanPlayerName(localKey), System.StringComparison.OrdinalIgnoreCase);
        }

        // allowDeferred: run RepinCrown's next-frame coroutine. false for the preview clone — that coroutine
        // re-applies against the LIVE local tag, which would stomp the real nametag from a preview refresh.
        public static void ApplyCrownTo(PlayerInfoDisplay display, CrownCfg cfg, bool allowDeferred = false)
        {
            // both display subtypes carry _crownRankBadgeViewModel — the 3D one in-game, the canvas one for the
            // config-tab preview clone. resolve from whichever this is.
            var go = display != null ? display.TryCast<PlayerInfoDisplayGameObject>() : null;
            var canvas = go == null && display != null ? display.TryCast<PlayerInfoDisplayCanvas>() : null;
            if (go == null && canvas == null) { if (cfg.enabled) Plugin.Log.LogWarning("crownrank: no local PlayerInfoDisplay to drive"); return; }

            var helper = go != null ? go._crownRankPlayerTagLayoutHelper : canvas._crownRankPlayerTagLayoutHelper;
            var badge = go != null ? go._crownRankBadgeViewModel : canvas._crownRankBadgeViewModel;
            if (badge == null) { if (cfg.enabled) Plugin.Log.LogWarning("crownrank: local display has no _crownRankBadgeViewModel"); return; }

            if (!DriveBadge(badge, cfg)) return;   // reverted (feature off)
            if (helper != null && !cfg.enabled) helper.CenterNameAndCrownRank();

            PositionCrown(display);
            if (allowDeferred) RepinCrown();
        }

        // qualification-screen cells carry their own crown badge at InfoUI/NameLayout/Generic_UI_CrownRankCounter_
        // 3D_Prefab. drive it as just another target — no display, no layout helper, so no positioning/swap, just
        // the badge's count/text/recolour. works for auto (Qualified) and manual apply alike.
        public static void ApplyCrownTo(CellBehaviour cell, CrownCfg cfg)
        {
            if (cell == null) return;
            var t = cell.transform.Find("InfoUI/NameLayout/Generic_UI_CrownRankCounter_3D_Prefab");
            var badge = t != null ? t.GetComponent<CrownRankBadgeViewModel>() : null;
            if (badge == null) return;
            DriveBadge(badge, cfg);
        }

        // apply the badge visuals (rank text/recolour) for a cfg. returns false when the feature is off (badge
        // reverted, caller should stop). shared by every ApplyCrownTo target so there's one implementation.
        static bool DriveBadge(CrownRankBadgeViewModel badge, CrownCfg cfg)
        {
            var root = badge.transform;
            int id = badge.GetInstanceID();
            bool haveSnap = _pristine.TryGetValue(id, out var snap);

            if (!cfg.enabled)
            {
                if (haveSnap) RevertBadge(badge, root, snap);
                return false;
            }

            // cache once, before we mutate anything on this badge instance.
            if (!haveSnap)
                _pristine[id] = snap = SnapshotBadge(badge, root);

            // custom text on → stamp our text onto the badge. visibility is the game's call, always — its own
            // crown rank display option ("hide mine") and rank-0 hiding decide whether the badge shows, we
            // never force it. off → leave the badge's LIVE rank/active/text exactly as the game set it (do NOT
            // stamp the snapshot back: behind the loading screen the badge isn't populated yet, so the empty
            // snapshot wiped the crown while recolour still ran)
            if (cfg.textOn && !string.IsNullOrEmpty(cfg.text))
                badge.CrownRankText = cfg.text;

            if (cfg.recolourOn)
            {
                RecolourChild(root, "Container/FlareBG", cfg.highlight, cfg.highlight);
                RecolourChild(root, "Container/Crown", cfg.highlight, cfg.main);
                ApplyTextOutline(root, cfg.outline);
            }
            else
            {
                // recolour turned off while the feature stays on — put the original game sprites back, and the
                // original TMP material (which carries the game's outline/underlay colours we overrode).
                RestoreOriginalSprite(root, "Container/FlareBG");
                RestoreOriginalSprite(root, "Container/Crown");
                if (haveSnap && snap.tmpMat != null)
                {
                    var tmp = root.GetComponentInChildren<TMP_Text>(true);
                    if (tmp != null) tmp.fontMaterial = snap.tmpMat;
                }
            }
            Plugin.Log.LogInfo($"crownrank applied — text {(cfg.textOn && !string.IsNullOrEmpty(cfg.text) ? cfg.text : "(game)")}, main {ColorUtility.ToHtmlStringRGB(cfg.main)}");
            return true;
        }

        // with an icon we own the whole layout (name in a wrapper, crown pinned out of layout, both hand-
        // placed). with no icon there's nothing to reposition around, so instead of pinning the crown out of
        // layout — which strands the name at dead-centre — hand it back to the game's own layout helper and
        // let CenterNameAndCrownRank centre the name+crown pair the way vanilla does.
        static void PositionCrown(PlayerInfoDisplay display)
        {
            var goDisp = display != null ? display.TryCast<PlayerInfoDisplayGameObject>() : null;
            var canvasDisp = goDisp == null && display != null ? display.TryCast<PlayerInfoDisplayCanvas>() : null;

            // canvas nametag: the crown is a UGUI layout child, so its side is purely its sibling index — set it
            // here directly, every apply, independent of the icon branch and the CenterName postfix (which was
            // never reached with an icon present, so swapping did nothing). first = left, last = right.
            if (canvasDisp != null)
            {
                var chelper = canvasDisp._crownRankPlayerTagLayoutHelper;
                var ccrown = chelper != null ? chelper.crownRankObject : null;
                if (ccrown != null)
                {
                    if (SwapSide) ccrown.transform.SetAsFirstSibling();
                    else ccrown.transform.SetAsLastSibling();
                }
                chelper?.CenterNameAndCrownRank();
                return;
            }

            bool hasIcon = SettingsService.Get("nametag.icon.mode", "none") != "none";
            if (hasIcon)
            {
                // cheap crown-only re-place. the icon path's RepositionLocalCrown already honours SwapSide, and
                // the name+icon get their side from RepositionNextFrame (run once on icon attach). don't re-run
                // the full ApplyIcon here — that rebuilds the icon GameObject and stacks duplicates when this
                // fires several times a frame. the swap TOGGLE re-runs ApplyIcon itself to reflect the group.
                NametagIconApplicator.RepositionLocalCrown();
                return;
            }

            var helper = goDisp != null ? goDisp._crownRankPlayerTagLayoutHelper : null;
            if (helper == null) return;

            // hand the crown back to the game's layout (RepositionLocalCrown pins it out for the icon case), then
            // let the game centre. the SwapSide flip is enforced by the postfix on CenterNameAndCrownRank.
            var le = helper.crownRankLayoutElement;
            if (le != null) le.ignoreLayout = false;
            var crownGo = helper.crownRankObject;
            var csf = crownGo != null ? crownGo.GetComponent<UnityEngine.UI.ContentSizeFitter>() : null;
            if (csf != null) csf.enabled = true;

            // the game centres off the name's TMP width; force a mesh update so a shorter new name doesn't
            // centre on a stale (wider) width and leave the crown put.
            var nameAndCrown = helper._crownParentTransform;
            var nameT = nameAndCrown != null ? (nameAndCrown.Find("NameText") ?? nameAndCrown.Find("BetterFG_NameWrapper/NameText")) : null;
            var tmp = nameT != null ? nameT.GetComponent<TMP_Text>() : null;
            if (tmp != null) tmp.ForceMeshUpdate(false, true);

            helper.CenterNameAndCrownRank();
        }

        // called from the postfix on the game's CenterNameAndCrownRank — runs right after the game has laid the
        // name+crown pair out (name left, crown right). if the user wants the crown on the left, flip the pair
        // as a unit: mirror both name and crown across the midpoint between them. moving BOTH keeps the spacing
        // intact and avoids the crown overlapping the name. deterministic — no coroutine, no race, and because
        // it's driven by the game's own centre call it's re-applied every time the game re-centres.
        public static void EnforceCrownSide(CrownRankPlayerTagLayoutHelper helper)
        {
            if (helper == null) return;

            var nameAndCrown = helper._crownParentTransform;
            var crownGo = helper.crownRankObject;
            var crown = crownGo != null ? crownGo.transform : null;
            if (nameAndCrown == null || crown == null) return;

            // canvas nametag (UGUI): the crown is a layout child, so its SIDE is just its sibling index — first
            // sibling = left of the name, last = right. no position math, and it leaves the icon where it is
            // (to the right of the name). handles both swap states, so run regardless of SwapSide.
            if (crown.GetComponentInParent<PlayerInfoDisplayCanvas>() != null)
            {
                if (SwapSide) crown.SetAsFirstSibling();
                else crown.SetAsLastSibling();
                return;
            }

            if (!SwapSide) return;
            // 3D nametag: no sibling-driven layout, so keep the mirror. only touch the no-icon layout — the icon
            // path owns its own placement (RepositionLocalCrown).
            if (SettingsService.Get("nametag.icon.mode", "none") != "none") return;

            Transform nameT = nameAndCrown.Find("NameText") ?? nameAndCrown.Find("BetterFG_NameWrapper/NameText");
            if (nameT == null)
            {
                foreach (var t in nameAndCrown.GetComponentsInChildren<TMP_Text>(true))
                    if (t != null && !t.transform.IsChildOf(crown)) { nameT = t.transform; break; }
            }
            if (nameT == null) return;

            var cp = crown.localPosition;
            var np = nameT.localPosition;
            // ABSOLUTE placement: crown to the negative side, name to the positive side, every time. idempotent
            // so multiple re-centres in a frame don't parity-flip back to the right.
            crown.localPosition = new Vector3(-Mathf.Abs(cp.x), cp.y, cp.z);
            nameT.localPosition = new Vector3(Mathf.Abs(np.x), np.y, np.z);
        }

        // respawn re-runs the game's layout and drags things back. re-assert a frame later so it lands after
        // the game's re-layout: re-run the name/icon apply, then place the crown for whichever mode we're in.
        public static void RepinCrown()
        {
            if (!Enabled) return;
            // the level editor has no player nametag/crown, but the game's checkpoint respawn still fires the
            // teleport-End that calls this — so every editor respawn was paying several whole-scene
            // FindObjectsOfType scans for nothing (the hitch). nothing to re-pin here, skip it.
            if (BetterFG.Features.UnityRound.Editor.UnityRoundLoader.InLevelEditor) return;
            var host = BeanMonitorService.Instance;
            if (host != null) host.StartCoroutine(PinNextFrame().WrapToIl2Cpp());
        }

        static IEnumerator PinNextFrame()
        {
            yield return null;
            var display = NametagFinder.FindLocalDisplay();
            if (display == null) yield break;
            NametagIconApplicator.ApplyNametag();
            PositionCrown(display);
        }

        static BadgePristine SnapshotBadge(CrownRankBadgeViewModel badge, Transform root)
        {
            var snap = new BadgePristine { rank = badge._currentCrownRank, text = badge.CrownRankText };
            var flare = root.Find("Container/FlareBG");
            var crown = root.Find("Container/Crown");
            snap.flare = flare != null ? SpriteOf(flare) : null;
            snap.crown = crown != null ? SpriteOf(crown) : null;
            var tmp = root.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) snap.tmpMat = tmp.fontMaterial;
            var le = root.GetComponent<LayoutElement>();
            snap.ignoreLayout = le != null && le.ignoreLayout;
            var csf = root.GetComponent<ContentSizeFitter>();
            snap.csfOn = csf != null && csf.enabled;
            snap.pos = root.localPosition;
            return snap;
        }

        static void RevertBadge(CrownRankBadgeViewModel badge, Transform root, BadgePristine snap)
        {
            badge.SetCrownRank(snap.rank);
            badge.CrownRankText = snap.text;

            var flare = root.Find("Container/FlareBG");
            var crown = root.Find("Container/Crown");
            if (flare != null && snap.flare != null) SetSprite(flare, snap.flare);
            if (crown != null && snap.crown != null) SetSprite(crown, snap.crown);
            if (snap.tmpMat != null)
            {
                var tmp = root.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) tmp.fontMaterial = snap.tmpMat;
            }
            // hand the crown back to the game's layout exactly as we found it.
            var le = root.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = snap.ignoreLayout;
            var csf = root.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = snap.csfOn;
            root.localPosition = snap.pos;
            _pristine.Remove(badge.GetInstanceID());
            Plugin.Log.LogInfo("crownrank reverted badge to the game's original state");
        }

        static Sprite SpriteOf(Transform t)
        {
            var img = t.GetComponent<Image>();
            if (img != null) return img.sprite;
            var sr = t.GetComponent<SpriteRenderer>();
            return sr != null ? sr.sprite : null;
        }

        static void SetSprite(Transform t, Sprite s)
        {
            var img = t.GetComponent<Image>();
            if (img != null) { img.sprite = s; img.color = Color.white; return; }
            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null) { sr.sprite = s; sr.color = Color.white; }
        }

        // recolour the badge label's existing outline. per-instance material so it doesn't smear onto other
        // text sharing the font asset. TMP lives somewhere under the badge — UGUI or world-space.
        static void ApplyTextOutline(Transform root, Color outline)
        {
            var tmp = root.GetComponentInChildren<TMP_Text>(true);
            if (tmp == null) { Plugin.Log.LogWarning("crownrank: no TMP label under the badge to recolour"); return; }

            var inst = new Material(tmp.fontMaterial);
            inst.SetColor("_OutlineColor", outline);
            inst.SetColor("_UnderlayColor", outline);
            tmp.fontMaterial = inst;
        }

        // per graphic (by instance id): the pristine game sprite + its cropped-out original pixels. apply
        // runs several times per round, so after the first pass the graphic holds OUR sprite — we always
        // recolour from the remembered original pixels, never from our own output (which would re-darken),
        // and we remember which colour is currently applied so we don't rebake needlessly.
        class Snapshot { public Sprite gameSprite; public Color[] origPixels; public int w, h; public float ppu; public Vector2 pivot; public string lastKey; public Sprite applied; }
        static readonly Dictionary<int, Snapshot> _snaps = new Dictionary<int, Snapshot>();

        // finds Container/FlareBG (or Container/Crown) under the badge and recolours whichever graphic it
        // has — world badges tend to be SpriteRenderer, canvas ones Image, so try both. it's a fixed-size
        // prefab, so we crop the sprite's atlas region into its own texture and make a plain sprite from
        // it — no atlas leak, and the fixed RectTransform keeps the on-screen size identical.
        static void RecolourChild(Transform root, string path, Color main, Color highlight)
        {
            var t = root.Find(path);
            if (t == null) { Plugin.Log.LogWarning($"crownrank: badge child '{path}' not found"); return; }

            var img = t.GetComponent<Image>();
            var sr = img == null ? t.GetComponent<SpriteRenderer>() : null;
            var cur = img != null ? img.sprite : (sr != null ? sr.sprite : null);
            if (cur == null) { Plugin.Log.LogWarning($"crownrank: '{path}' has no Image/SpriteRenderer sprite to recolour"); return; }

            int id = (img != null ? (Component)img : sr).GetInstanceID();
            if (!_snaps.TryGetValue(id, out var snap) || snap.gameSprite == null)
            {
                snap = SnapshotRegion(cur);
                _snaps[id] = snap;
            }

            string colourKey = ColorUtility.ToHtmlStringRGB(main) + "|" + ColorUtility.ToHtmlStringRGB(highlight);
            if (snap.lastKey != colourKey || snap.applied == null)
            {
                snap.applied = BakeSprite(snap, main, highlight);
                snap.lastKey = colourKey;
            }
            if (img != null) { if (img.sprite != snap.applied) img.sprite = snap.applied; img.color = Color.white; }
            else { if (sr.sprite != snap.applied) sr.sprite = snap.applied; sr.color = Color.white; }
        }

        // undo a recolour: put the remembered original game sprite back on the graphic. no-op if we never
        // recoloured this one (nothing cached), which is the correct behaviour — it's already original.
        static void RestoreOriginalSprite(Transform root, string path)
        {
            var t = root.Find(path);
            if (t == null) return;
            var img = t.GetComponent<Image>();
            var sr = img == null ? t.GetComponent<SpriteRenderer>() : null;
            var comp = img != null ? (Component)img : sr;
            if (comp == null) return;

            if (_snaps.TryGetValue(comp.GetInstanceID(), out var snap) && snap.gameSprite != null)
                SetSprite(t, snap.gameSprite);
        }

        // crop the sprite's atlas region into a readable RGBA32 copy and stash the original pixels + the
        // sprite's geometry (size, ppu, normalized pivot).
        static Snapshot SnapshotRegion(Sprite spr)
        {
            var atlas = spr.texture;
            var tr = spr.textureRect;
            int rx = Mathf.Clamp(Mathf.FloorToInt(tr.x), 0, atlas.width);
            int ry = Mathf.Clamp(Mathf.FloorToInt(tr.y), 0, atlas.height);
            int rw = Mathf.Clamp(Mathf.CeilToInt(tr.width), 1, atlas.width - rx);
            int rh = Mathf.Clamp(Mathf.CeilToInt(tr.height), 1, atlas.height - ry);

            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var crop = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
            crop.ReadPixels(new Rect(rx, ry, rw, rh), 0, 0);
            crop.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var r = spr.rect;
            var pivotNorm = r.size.x > 0f && r.size.y > 0f
                ? new Vector2(spr.pivot.x / r.size.x, spr.pivot.y / r.size.y)
                : new Vector2(0.5f, 0.5f);

            return new Snapshot
            {
                gameSprite = spr,
                origPixels = crop.GetPixels(),
                w = rw, h = rh,
                ppu = spr.pixelsPerUnit,
                pivot = pivotNorm,
            };
        }

        static Sprite BakeSprite(Snapshot snap, Color main, Color highlight)
        {
            var px = (Color[])snap.origPixels.Clone();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a < 0.02f) continue;
                Color.RGBToHSV(c, out float h, out float _, out float v);
                // the crown's light-yellow highlight is the bright, near-yellow band; everything else
                // coloured is the orange body. hue ~0.10-0.20 and high value = highlight.
                bool isHighlight = v > 0.72f && h >= 0.10f && h <= 0.20f;
                var tint = isHighlight ? highlight : main;
                // keep this pixel's own luminance so the sprite's internal shading is preserved.
                Color.RGBToHSV(tint, out float th, out float ts, out float tv);
                var outc = Color.HSVToRGB(th, ts, tv * v);
                px[i] = new Color(outc.r, outc.g, outc.b, c.a);
            }
            var tex = new Texture2D(snap.w, snap.h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels(px);
            tex.Apply();
            // plain full-region sprite: rect starts at (0,0) because the texture IS just the region.
            return Sprite.Create(tex, new Rect(0, 0, snap.w, snap.h), snap.pivot, snap.ppu, 0, SpriteMeshType.FullRect);
        }

        // colours changed — force the next apply to rebake. pristine pixels are kept, so it recolours from
        // the original, not our output.
        public static void InvalidateCache()
        {
            foreach (var s in _snaps.Values) { s.lastKey = null; s.applied = null; }
        }
    }

}
