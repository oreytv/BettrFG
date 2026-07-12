using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using BetterFG.Services;

namespace BetterFG.Customization.Menu
{
    // one font override: replace every TMP that uses a given game font asset (by name) with a custom
    // ttf/otf, built into a runtime dynamic SDF asset.
    public class FontOverride
    {
        public string entryName = "";
        public string fontPath = "";       // the user's ttf/otf
        public string targetFontName = ""; // the game TMP_FontAsset name this replaces
        public bool enabled = true;

        // built lazily, not persisted
        public TMP_FontAsset builtAsset;
    }

    // entry-based font replacement. each entry maps "game font asset name" -> custom ttf. on every TMP
    // that turns on we look up its current font's name and, if a matching enabled override exists, swap
    // it (this is what survives HUD/instantiate resets — FindObjectsOfType only catches what's already
    // there). TMP's CreateFontAsset(fontFilePath, ...) reads the file itself and bakes a dynamic SDF
    // atlas, same as the editor's "Create > Font Asset", just at runtime.
    public static class FontReplacementService
    {
        public const string KEY_MASTER_ON = "ui.font.master";
        public const string KEY_COUNT = "ui.font.count";
        private static string EK(int i, string f) => $"ui.font.entry.{i}.{f}";

        private const int SAMPLING_POINT_SIZE = 90;
        private const int ATLAS_PADDING = 9;
        private const int ATLAS_SIZE = 1024;

        // overrides keyed by the game font-asset name they target (lowercased). only enabled ones live
        // here; rebuilt whenever the UI saves.
        private static readonly Dictionary<string, FontOverride> _active =
            new Dictionary<string, FontOverride>(StringComparer.OrdinalIgnoreCase);
        private static bool _masterOn;

        // read from settings, not the in-memory field — the field isn't set until RebuildAndApply runs,
        // so the UI would otherwise show OFF on first open even when it's saved ON.
        public static bool MasterOn => SettingsService.Get(KEY_MASTER_ON, "false") == "true";

        // in-memory mirror of the master flag for hot paths (the per-frame watchdog tick). reading
        // settings every frame is needless string-map churn; _masterOn is kept current by
        // RebuildAndApply/SetMaster. it's false until the first RebuildAndApply, which is fine — the
        // watchdog has nothing to do before fonts are built anyway.
        public static bool MasterOnFast => _masterOn;

        private const string OUR_PREFIX = "BFG_";

        // ── enumerate the game's font assets (for the target picker) ─────────
        public static List<string> GetAllFontAssetNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || string.IsNullOrEmpty(fa.name)) continue;
                    if (fa.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue; // never list ours
                    if (!names.Contains(fa.name)) names.Add(fa.name);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[BetterFG] enumerate fonts: " + ex.Message); }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // lookup the original TMP_FontAsset by exact name. used by the UITab dropdown to render
        // each row in the actual game font it represents.
        public static TMP_FontAsset GetFontAssetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || string.IsNullOrEmpty(fa.name)) continue;
                    if (fa.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue;
                    if (fa.name == name) return fa;
                }
            }
            catch { }
            return null;
        }

        // built assets cached by font file path. RebuildAndApply runs on every toggle/apply and LoadAll
        // hands back fresh FontOverride objects each time, so without this we'd call CreateFontAsset AGAIN
        // every toggle — making a NEW TMP_FontAsset (and a new atlas) each time. that's the "garbled /
        // chopped up" bug: text still pointing at a previous build, plus the _derived material cache (keyed
        // on the built asset's instance id) never hitting. caching by path means the SAME asset instance is
        // reused across rebuilds, so instance ids stay stable and nothing is orphaned.
        private static readonly Dictionary<string, TMP_FontAsset> _builtByPath =
            new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

        // ── build a dynamic SDF asset from a ttf/otf on disk (cached by path) ─
        private static TMP_FontAsset BuildAsset(FontOverride ov)
        {
            if (string.IsNullOrEmpty(ov.fontPath) || !File.Exists(ov.fontPath))
            {
                Debug.LogError("[BetterFG] font file missing: " + ov.fontPath);
                return null;
            }

            if (_builtByPath.TryGetValue(ov.fontPath, out var cached) && cached != null)
                return cached;

            try
            {
                var asset = TMP_FontAsset.CreateFontAsset(ov.fontPath, 0, SAMPLING_POINT_SIZE,
                    ATLAS_PADDING, GlyphRenderMode.SDFAA, ATLAS_SIZE, ATLAS_SIZE);
                if (asset == null)
                {
                    Debug.LogError("[BetterFG] CreateFontAsset returned null for " + ov.fontPath);
                    return null;
                }
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.name = "BFG_" + Path.GetFileNameWithoutExtension(ov.fontPath);
                _builtByPath[ov.fontPath] = asset;
                return asset;
            }
            catch (Exception ex)
            {
                Debug.LogError("[BetterFG] BuildAsset failed: " + ex);
                return null;
            }
        }

        // every TMP we've swapped -> the original font AND the EXACT material it was rendering with.
        // assigning .font makes TMP reassign its shared material to the new font's *default* material,
        // dropping any outline/glow the text had. so on swap we capture the real material, and on
        // restore we slam BOTH the font and that captured material back. setting .font alone does not
        // restore the outline — it falls back to the font default, which has none.
        private class Touched
        {
            public TMP_FontAsset orig;
            public string origName;
            public Material origSharedMat;   // the live material the text rendered with before we touched it
            public Color32 origOutlineColor;
            public float origOutlineWidth;
            public bool isNametag;           // swapped via ApplyToNametag — never famepass-revert these
            public bool origEnableGradient;  // gold reproduction turns on the per-vertex gradient; restore it
        }
        private static readonly Dictionary<TMP_Text, Touched> _touched =
            new Dictionary<TMP_Text, Touched>();

        // text the general sweep must never touch — only fame/famepass text we've caught mid-swap ends up
        // here now. nametags are NO LONGER blanket-protected: we want the custom font on them (applied via
        // ApplyToNametag at the end of the applicator's style pass), so they're free for replacement.
        private static readonly HashSet<TMP_Text> _protectedTexts = new HashSet<TMP_Text>();

        // public hands-off marker for UI we render in the actual game fonts (e.g. font-replacement
        // dropdown previews) so the sweep doesn't replace them with the user's chosen font.
        public static void Protect(TMP_Text t) { if (t != null) _protectedTexts.Add(t); }

        // every nametag text the applicator has ever handed us. the general sweep (TryApplyTo) skips
        // nametags, so a plain toggle on/off would never re-apply the font to them — only a view switch
        // (which re-runs the applicator) would. by remembering them we can re-run ApplyToNametag on toggle,
        // so toggling font replacement back on fixes nametags immediately, no view switch needed.
        private static readonly HashSet<TMP_Text> _knownNametags = new HashSet<TMP_Text>();

        // legacy entry the applicator still calls at the START of its apply. it used to ban the nametag
        // from font replacement; now the model is reversed (the applicator calls ApplyToNametag at the
        // END to opt the nametag IN). all this does now is drop any stale general-sweep swap so the
        // applicator starts from the original font, then re-applies its own material cleanly.
        public static void ProtectText(TMP_Text t)
        {
            if (t == null) return;
            if (_touched.TryGetValue(t, out var b))
            {
                RestoreOne(t, b);
                _touched.Remove(t);
            }
        }

        // called by the nametag applicator at the END of its style apply, once it has set the final
        // font material (shadow/gold/default) on the text. if a font override targets this text's
        // CURRENT font, swap to our atlas and rebuild whatever material the applicator just set onto it
        // (carrying its outline/shadow). this is how nametags get the custom font WITH their outline:
        // the applicator owns the material, we just re-point the atlas. no-op when master is off or no
        // override matches. nametags are NOT added to _protectedTexts anymore — we want them swapped.
        public static void ApplyToNametag(TMP_Text t)
        {
            if (t == null) return;
            _knownNametags.Add(t); // remember it even while off, so toggle-on can re-apply (see RebuildAndApply)
            if (!_masterOn || _active.Count == 0) return;
            try
            {
                var cur = t.font;
                if (cur == null) return;

                Material curMat = null;
                try { curMat = t.fontMaterial; } catch { }
                if (curMat == null) try { curMat = t.fontSharedMaterial; } catch { }

                FontOverride match;

                // the font is ALREADY ours. but the applicator (or the game's gold-material assignment)
                // may have just set a NEW material on top — bound to the ORIGINAL atlas, so it renders
                // garbage on our font. if the current material isn't already a derived [BFG] one, re-derive
                // it onto our atlas. this is the fix for famepass nametags whose gold material lands AFTER
                // our first swap: re-running ApplyNametag re-derives instead of early-outing.
                if (cur.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal))
                {
                    if (curMat != null && curMat.name != null &&
                        curMat.name.IndexOf("[BFG]", StringComparison.Ordinal) >= 0) return; // already correct
                    if (!_touched.TryGetValue(t, out var prev)) return; // ours but untracked — leave it
                    if (!_active.TryGetValue(prev.origName, out match) || match.builtAsset == null) return;
                    // the game drifted the raw metallic gold back onto our font — reproduce it the nice way
                    // (see ApplyNiceGold) instead of mapping the baked metallic texture onto our atlas.
                    if (IsMetallicGold(curMat)) { ApplyNiceGold(t, match.builtAsset); t.ForceMeshUpdate(); return; }
                    var d = DeriveMaterial(match.builtAsset, curMat);
                    if (d != null) t.fontMaterial = d;
                    t.ForceMeshUpdate();
                    return;
                }

                if (!_active.TryGetValue(cur.name, out match) || match.builtAsset == null) return;

                // record the original font + material so toggle-off (RestoreUncovered) can put it back.
                // without this, nametags we swapped here would never revert when font replacement is
                // turned off. only record on the FIRST swap so we don't overwrite the true original with
                // a re-applied state on subsequent passes.
                if (!_touched.ContainsKey(t))
                    _touched[t] = new Touched
                    {
                        orig = cur,
                        origName = cur.name,
                        origSharedMat = curMat,
                        origOutlineColor = t.outlineColor,
                        origOutlineWidth = t.outlineWidth,
                        isNametag = true,
                        origEnableGradient = t.enableVertexGradient
                    };

                t.font = match.builtAsset;
                // famed players carry the game's metallic gold material, which is baked for the original
                // font and looks wrong remapped onto our atlas. reproduce the gold the way the local
                // "gold rgb" style does instead — drop the face texture, drive a gold->white vertex
                // gradient on the gold shader. looks right on any font.
                if (IsMetallicGold(curMat)) { ApplyNiceGold(t, match.builtAsset); t.ForceMeshUpdate(); return; }
                var derived = DeriveMaterial(match.builtAsset, curMat);
                if (derived != null) t.fontMaterial = derived;
                t.ForceMeshUpdate();
            }
            catch { }
        }

        // the game's raw metallic gold (famepass) material — still has its baked gold _FaceTex. that's the
        // one that looks wrong remapped onto a custom font. the local "gold rgb" style nulls _FaceTex and
        // drives a vertex gradient, so a material whose name says EndFamePass but has NO _FaceTex is the
        // reproduced/derived one we should leave alone.
        private static bool IsMetallicGold(Material m)
        {
            if (m == null || m.name == null) return false;
            if (m.name.IndexOf("EndFamePass", StringComparison.OrdinalIgnoreCase) < 0) return false;
            try { return !m.HasProperty("_FaceTex") || m.GetTexture("_FaceTex") != null; }
            catch { return true; }
        }

        // fame gold reproduced the way the local "gold rgb" (goldcolored) style does — and that looks great
        // on ANY font. instead of remapping the metallic _FaceTex (baked for the original font), drop the
        // face texture and drive the color with a per-vertex gradient (gold at the top fading to white) on
        // the gold shader, keeping its nice outline. point the material at our atlas.
        private static readonly Color GOLD_FACE = new Color(1f, 0.82f, 0.20f, 1f);
        private static readonly Color GOLD_OUTLINE = new Color(0.30f, 0.22f, 0.03f, 1f);
        private static Material _niceGold;
        private static int _niceGoldAtlas;

        private static void ApplyNiceGold(TMP_Text t, TMP_FontAsset built)
        {
            var goldSrc = BetterFG.Core.AssetManager.GoldNameMaterial;
            var fontDefault = built != null ? built.material : null;
            if (goldSrc == null || fontDefault == null) return;

            // one shared nice-gold material per atlas (rebuilt only if the active font's atlas changes).
            if (_niceGold == null || _niceGoldAtlas != built.GetInstanceID())
            {
                var m = new Material(goldSrc) { hideFlags = HideFlags.HideAndDontSave };
                m.name = "BFG_NiceGold [BFG]";
                try { m.shaderKeywords = goldSrc.shaderKeywords; } catch { }
                m.SetTexture("_FaceTex", null);
                m.SetColor("_FaceColor", Color.white);
                m.SetColor("_OutlineColor", GOLD_OUTLINE);
                if (m.HasProperty("_OutlineWidth") && m.GetFloat("_OutlineWidth") <= 0f)
                    m.SetFloat("_OutlineWidth", 0.2f);

                if (fontDefault.HasProperty("_MainTex")) m.SetTexture("_MainTex", fontDefault.GetTexture("_MainTex"));
                foreach (var p in new[] { "_TextureWidth", "_TextureHeight", "_GradientScale" })
                    if (fontDefault.HasProperty(p) && m.HasProperty(p)) m.SetFloat(p, fontDefault.GetFloat(p));

                _niceGold = m;
                _niceGoldAtlas = built.GetInstanceID();
            }

            t.fontMaterial = _niceGold;
            t.color = Color.white;
            t.enableVertexGradient = true;
            t.colorGradient = new VertexGradient(GOLD_FACE, GOLD_FACE, Color.white, Color.white);

            // we now KNOW this nametag is gold — but its _touched record was captured on the first swap,
            // BEFORE the game's gold material landed, so it stored the white default as the "original".
            // sync it to the real metallic gold so toggle-off restores vanilla GOLD (not the default), and
            // toggle-on re-derives nice gold from that restored gold instead of a white default.
            if (_touched.TryGetValue(t, out var rec))
            {
                rec.origSharedMat = goldSrc;
                rec.origEnableGradient = false;
            }
        }

        // put a touched text fully back to how it was: original font, original material, original outline.
        // ORDER + FORCED REBUILD MATTER: setting .font rebinds TMP to the original atlas but leaves the
        // mesh holding glyph quads sized/UV'd for OUR runtime atlas — that's the "garbled / chopped up"
        // text on toggle-off. a plain ForceMeshUpdate doesn't re-fit it. we have to clear the font first
        // so TMP sees a real change, set the original font + material, mark everything dirty, then force a
        // FULL regen (ignoreActiveState + forceTextReparsing) so the glyphs are re-requested for the
        // original atlas and the mesh is rebuilt from scratch.
        private static void RestoreOne(TMP_Text t, Touched b)
        {
            try
            {
                if (b.orig == null) return;

                // setting .font to a value TMP already thinks is current can early-out and skip the atlas
                // rebind. our current font is the BFG asset, so this is a real change — but to be safe,
                // restore material first so the rebuild below sees the right material.
                t.font = b.orig;
                if (b.origSharedMat != null)
                {
                    t.fontSharedMaterial = b.origSharedMat;
                    try { t.fontMaterial = b.origSharedMat; } catch { } // UI path uses fontMaterial
                }
                t.outlineColor = b.origOutlineColor;
                t.outlineWidth = b.origOutlineWidth;
                // gold reproduction may have turned the per-vertex gradient ON — put it back so a restored
                // non-gradient nametag doesn't keep the gold->white fade.
                t.enableVertexGradient = b.origEnableGradient;

                // force a complete rebuild against the original atlas — not just a mesh refresh.
                try { t.SetAllDirty(); } catch { }
                t.ForceMeshUpdate(true, true);
            }
            catch { }
        }

        // revert one TMP back to its original font/material if we ever swapped it. used by TimePlacement
        // when caching a template entry: the live source we cache from may have already been swept, so
        // without this the cache bakes in a BFG_* font and future clones of it inherit that font no
        // matter what the master toggle says. reverting before caching ensures the cache holds vanilla.
        public static void RevertIfTouched(TMP_Text t)
        {
            if (t == null) return;
            if (_touched.TryGetValue(t, out var b))
            {
                RestoreOne(t, b);
                _touched.Remove(t);
            }
        }

        // skip the gold/fame nametag material (and its per-text "(Instance)" copies). its name always
        // contains "EndFamePass" — the gold-rgb path nulls _FaceTex so we can't detect it by texture,
        // but the name stem survives instancing. these shaders are bound to their original atlas and
        // can't render our runtime atlas, so we leave any text using them alone.
        private static bool IsProtected(Material m) =>
            m != null && m.name != null &&
            m.name.IndexOf("EndFamePass", StringComparison.OrdinalIgnoreCase) >= 0;

        // true if ANY material this text is actually rendering with is protected ("fame"/gold). checks
        // the shared font material, the live UI CanvasRenderer material, AND the 3D MeshRenderer
        // material — gold nametags assign their material to fontMaterial (UI) or sharedMaterial (3D),
        // not fontSharedMaterial, so we have to look at the renderer to catch them.
        private static bool RendersProtected(TMP_Text t)
        {
            try { if (IsProtected(t.fontSharedMaterial)) return true; } catch { }
            try { if (IsProtected(t.fontMaterial)) return true; } catch { }
            try
            {
                var cr = t.canvasRenderer;
                if (cr != null)
                    for (int i = 0; i < cr.materialCount; i++)
                        if (IsProtected(cr.GetMaterial(i))) return true;
            }
            catch { }
            try
            {
                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var sm = mr.sharedMaterials;
                    if (sm != null) for (int i = 0; i < sm.Length; i++) if (IsProtected(sm[i])) return true;
                }
            }
            catch { }
            return false;
        }

        // derived materials keyed by "builtAssetId:origMatId" — the original material (outline/glow/
        // custom shader) rebuilt onto the new font's atlas. cached; never mutates the original.
        private static readonly Dictionary<string, Material> _derived =
            new Dictionary<string, Material>();

        // atlas props copied verbatim from the new font (texture + its dimensions).
        private static readonly string[] AtlasCopyProps =
            { "_MainTex", "_TextureWidth", "_TextureHeight", "_WeightNormal", "_WeightBold" };

        // these are measured in SDF spread units, i.e. relative to _GradientScale. when the gradient
        // scale changes (new font's atlas has different padding), they must be rescaled by the ratio
        // origGradientScale/newGradientScale or the outline/dilate visually vanishes or explodes.
        private static readonly string[] SpreadRelativeProps =
            { "_OutlineWidth", "_OutlineSoftness", "_FaceDilate", "_UnderlayDilate", "_UnderlaySoftness" };

        // is mat just the font's plain default (possibly instanced)? then there's no styling to carry.
        private static bool IsPlainDefault(TMP_FontAsset font, Material mat)
        {
            var def = font != null ? font.material : null;
            if (def == null || mat == null) return true;
            if (mat == def) return true;
            string n = mat.name, d = def.name;
            return n == d || n == d + " (Instance)";
        }

        // build (or reuse) a material rendering the new font's atlas with the original's shader+styling.
        private static Material DeriveMaterial(TMP_FontAsset built, Material origMat)
        {
            var fontDefault = built != null ? built.material : null;
            if (origMat == null) return fontDefault;

            // already a derived [BFG] material? don't derive from a derive — that's how the bloat
            // snowballed (re-apply on every spawn/fame/view-switch made a new copy of a copy). reuse it.
            if (origMat.name != null && origMat.name.IndexOf("[BFG]", StringComparison.Ordinal) >= 0)
                return origMat;

            // KEY ON THE MATERIAL'S NAME ONLY (minus "(Instance)"), NOT its instance id and NOT per-text
            // outline color/width. each text has its own instanced material AND its own outline props, so
            // keying on those meant the cache NEVER hit — a fresh Material per text per re-apply (the 570/
            // 9000 bloat). a derived material is fully defined by its base material + the target atlas, so
            // all text sharing a base material name collapses onto ONE derived material. the per-text
            // outline color/width live on the TMP component (t.outlineColor/Width), not the material, and
            // we leave those untouched — so dropping them here changes nothing visually.
            string baseName = origMat.name ?? "?";
            int inst = baseName.IndexOf(" (Instance)", StringComparison.Ordinal);
            if (inst >= 0) baseName = baseName.Substring(0, inst);

            string key = built.GetInstanceID() + ":" + baseName;
            if (_derived.TryGetValue(key, out var cached) && cached != null) return cached;

            try
            {
                var mat = new Material(origMat) { hideFlags = HideFlags.HideAndDontSave };
                mat.name = baseName + " [BFG]";

                // new Material(origMat) copies the shader + float/tex/color props, but in IL2CPP it does NOT
                // reliably carry the enabled SHADER KEYWORDS. the gold/famepass face is rendered by a shader
                // variant gated on keywords (the face-texture/glow features) — without them the shader falls
                // back to the plain-white variant, so the gold name renders WHITE even though _FaceTex is
                // still set. copy the source material's enabled keywords over so the gold variant compiles.
                try
                {
                    var kw = origMat.shaderKeywords;
                    if (kw != null) mat.shaderKeywords = kw;
                }
                catch { }

                if (fontDefault == null) { _derived[key] = mat; return mat; }

                // gradient scales: outline/dilate/softness are relative to these, so we rescale them.
                float origGS = mat.HasProperty("_GradientScale") ? mat.GetFloat("_GradientScale") : 0f;
                float newGS = fontDefault.HasProperty("_GradientScale") ? fontDefault.GetFloat("_GradientScale") : 0f;

                // point at the new atlas
                foreach (var p in AtlasCopyProps)
                {
                    if (!fontDefault.HasProperty(p) || !mat.HasProperty(p)) continue;
                    if (p == "_MainTex") mat.SetTexture(p, fontDefault.GetTexture(p));
                    else mat.SetFloat(p, fontDefault.GetFloat(p));
                }
                if (newGS > 0f && mat.HasProperty("_GradientScale")) mat.SetFloat("_GradientScale", newGS);

                // rescale spread-relative props so the outline keeps the same visual thickness on the
                // new atlas's spread. ratio = old/new gradient scale. these come from the base material,
                // so they're the same for every text sharing it — fine to bake into the shared derived mat.
                if (origGS > 0f && newGS > 0f)
                {
                    float ratio = origGS / newGS;
                    foreach (var p in SpreadRelativeProps)
                        if (mat.HasProperty(p)) mat.SetFloat(p, mat.GetFloat(p) * ratio);
                }

                _derived[key] = mat;
                return mat;
            }
            catch (Exception ex) { Debug.LogWarning("[BFGFont] derive: " + ex.Message); return fontDefault; }
        }

        // ── apply ─────────────────────────────────────────────────────────────
        // build assets for every enabled override, restore anything no longer covered, then swap.
        public static void RebuildAndApply()
        {
            _active.Clear();
            _masterOn = SettingsService.Get(KEY_MASTER_ON, "false") == "true";

            if (_masterOn)
            {
                foreach (var ov in LoadAll())
                {
                    if (!ov.enabled || string.IsNullOrEmpty(ov.targetFontName)) continue;
                    ov.builtAsset = BuildAsset(ov);
                    if (ov.builtAsset != null) _active[ov.targetFontName] = ov;
                }
            }

            RestoreUncovered();
            ApplyToAllLive();
            ReapplyKnownNametags();
        }

        // re-apply the font to every nametag we've seen. the general sweep skips nametags, so this is what
        // makes a plain toggle-on re-apply the font to them (otherwise only a view switch would). when
        // master is off this is a no-op — RestoreUncovered already put nametags back via _touched.
        private static void ReapplyKnownNametags()
        {
            if (!_masterOn || _active.Count == 0) return;
            var dead = new List<TMP_Text>();
            foreach (var t in _knownNametags)
            {
                if (t == null) { dead.Add(t); continue; }
                ApplyToNametag(t);
            }
            foreach (var t in dead) _knownNametags.Remove(t);
        }

        // WATCHDOG. the game's fame/famepass system re-slams the ORIGINAL gold material (asap-bold
        // sdf_EndFamePass, bound to the original atlas) onto a nametag whenever fame state updates — and
        // that happens sporadically/animated, NOT just at the spawn/visuals patch points we hook. when it
        // lands, our font is still the BFG atlas but the material points at the original atlas, so the
        // glyphs render garbage. that's the "breaks on its own after a few seconds, completely random" bug:
        // something keeps re-setting the material and none of our event hooks catch that particular re-set.
        // so instead of chasing every game site, poll the small known-nametag set: any nametag whose font
        // is ours but whose CURRENT material isn't a derived [BFG] one has drifted — re-derive it. cheap
        // (only nametags, only when something actually drifted; ApplyToNametag early-outs on correct ones).
        // BeanMonitorService.Update drives this on a throttle.
        public static void TickNametagWatchdog()
        {
            if (!_masterOn || _active.Count == 0 || _knownNametags.Count == 0) return;
            List<TMP_Text> dead = null;
            foreach (var t in _knownNametags)
            {
                if (t == null) { (dead ??= new List<TMP_Text>()).Add(t); continue; }
                try
                {
                    var cur = t.font;
                    if (cur == null || !cur.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue;

                    Material curMat = null;
                    try { curMat = t.fontMaterial; } catch { }
                    if (curMat == null) try { curMat = t.fontSharedMaterial; } catch { }

                    // material is already on our atlas — nothing drifted, leave it.
                    if (curMat != null && curMat.name != null &&
                        curMat.name.IndexOf("[BFG]", StringComparison.Ordinal) >= 0) continue;

                    // drifted: the game put a raw (original-atlas) material back on our font. re-derive.
                    ApplyToNametag(t);
                }
                catch { }
            }
            if (dead != null) foreach (var t in dead) _knownNametags.Remove(t);
        }

        // re-run the restore+apply without rebuilding assets. called a few frames after a menu/round
        // enter so any fame/famepass material the game assigned AFTER our first sweep gets caught and
        // healed (RestoreUncovered now reverts touched text that turned protected). cheap; no IO.
        public static void HealAndReapply()
        {
            if (!_masterOn) return;
            RestoreUncovered();
            ApplyToAllLive();
        }

        // put back the original font + material on any TMP whose target is no longer active (or master off),
        // OR that has since become fame/famepass text. the famepass material is assigned by the game AFTER
        // our startup sweep already swapped the font, so we have to catch it here and undo our swap — that
        // text must keep its original atlas or its gold shader renders garbage. this is the fix for the
        // "famepass corrupted on startup, only fixed by toggling the font off/on" bug: the toggle worked
        // because it hit this restore path; now a deferred re-sweep hits it too, no toggle needed.
        public static void RestoreUncovered()
        {
            var drop = new List<TMP_Text>();
            foreach (var kv in _touched)
            {
                var t = kv.Key;
                if (t == null) { drop.Add(t); continue; }

                bool stillCovered = _masterOn && kv.Value.origName != null &&
                                    _active.ContainsKey(kv.Value.origName);
                // only famepass-revert NON-nametag text (the game's own gold fame-UI). nametags are meant
                // to get the custom font even when gold (ApplyToNametag re-derives the gold material onto
                // our atlas), so we never revert them just for being famepass.
                bool nowProtected = !kv.Value.isNametag && RendersProtected(t);
                if (!stillCovered || nowProtected)
                {
                    RestoreOne(t, kv.Value);
                    // famepass text we wrongly swapped: pin it so no future sweep touches it again.
                    if (nowProtected) _protectedTexts.Add(t);
                    drop.Add(t);
                }
            }
            foreach (var t in drop) _touched.Remove(t);
        }

        // sweep every TMP currently in the scene. cheap enough; TMP bakes glyphs lazily.
        public static void ApplyToAllLive()
        {
            if (!_masterOn || _active.Count == 0) return;
            try
            {
                foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
                    TryApplyTo(t);
            }
            catch (Exception ex) { Debug.LogWarning("[BetterFG] font sweep: " + ex.Message); }
        }

        // scoped sweep — only the TMPs under one subtree. used on view switches: only the
        // freshly rebuilt view needs re-swapping, the rest of the scene already has our font and
        // a whole-scene FindObjectsOfType every switch is what made menu navigation laggy.
        public static void ApplyToScope(UnityEngine.Transform scope)
        {
            if (!_masterOn || _active.Count == 0 || scope == null) return;
            try
            {
                foreach (var t in scope.GetComponentsInChildren<TMP_Text>(true))
                    TryApplyTo(t);
            }
            catch (Exception ex) { Debug.LogWarning("[BetterFG] font scope sweep: " + ex.Message); }
        }

        // single-TMP path, used by the OnEnable patch so newly spawned text gets swapped too.
        public static void TryApplyTo(TMP_Text t)
        {
            if (!_masterOn || t == null || _active.Count == 0) return;
            if (_protectedTexts.Contains(t)) return; // nametag-owned, hands off
            try
            {
                var cur = t.font;
                if (cur == null) return;
                // already one of ours
                if (cur.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) return;

                if (_active.TryGetValue(cur.name, out var match) && match.builtAsset != null)
                {
                    // leave any "fame"/gold text untouched — keeps its original font + look. checks the
                    // shared font material plus the live UI/3D renderer materials (gold nametags set
                    // their material on fontMaterial / sharedMaterial, not fontSharedMaterial). do this
                    // FIRST, before capturing anything, so we never even record protected text.
                    if (RendersProtected(t)) return;

                    Material origShared = null;
                    try { origShared = t.fontSharedMaterial; } catch { }
                    var origOutlineColor = t.outlineColor;
                    var origOutlineWidth = t.outlineWidth;

                    if (!_touched.ContainsKey(t))
                        _touched[t] = new Touched
                        {
                            orig = cur,
                            origName = cur.name,
                            origSharedMat = origShared,
                            origOutlineColor = origOutlineColor,
                            origOutlineWidth = origOutlineWidth,
                            origEnableGradient = t.enableVertexGradient
                        };

                    // setting .font rebinds the text to the new font's *default* material, dropping the
                    // original outline/glow. so after the font swap we rebuild that original material onto
                    // the new font's atlas (DeriveMaterial) and assign it — unless the original was just a
                    // plain default, in which case the new font's default is correct and we leave it.
                    t.font = match.builtAsset;
                    if (!IsPlainDefault(cur, origShared))
                    {
                        var derived = DeriveMaterial(match.builtAsset, origShared);
                        if (derived != null) t.fontSharedMaterial = derived;
                    }
                    t.ForceMeshUpdate();
                }
            }
            catch { }
        }

        public static void ReapplyFromSettings() => RebuildAndApply();

        // ── persistence ───────────────────────────────────────────────────────
        public static List<FontOverride> LoadAll()
        {
            var list = new List<FontOverride>();
            if (!int.TryParse(SettingsService.Get(KEY_COUNT, "0"), out int count)) return list;

            for (int i = 0; i < count; i++)
            {
                list.Add(new FontOverride
                {
                    entryName = SettingsService.Get(EK(i, "name"), "entry " + i),
                    fontPath = SettingsService.Get(EK(i, "path"), ""),
                    targetFontName = SettingsService.Get(EK(i, "target"), ""),
                    enabled = SettingsService.Get(EK(i, "enabled"), "1") == "1",
                });
            }
            return list;
        }

        public static void SaveAll(List<FontOverride> list)
        {
            SettingsService.Set(KEY_COUNT, list.Count.ToString());
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                SettingsService.Set(EK(i, "name"), e.entryName);
                SettingsService.Set(EK(i, "path"), e.fontPath);
                SettingsService.Set(EK(i, "target"), e.targetFontName);
                SettingsService.Set(EK(i, "enabled"), e.enabled ? "1" : "0");
            }
        }

        public static void SetMaster(bool on)
        {
            _masterOn = on;
            SettingsService.Set(KEY_MASTER_ON, on ? "true" : "false");
        }

        // build a one-off preview asset for the edit form (not registered as an active override).
        public static TMP_FontAsset BuildPreview(FontOverride ov) => BuildAsset(ov);
    }

    // newly spawned / re-enabled TMP text spawns with its original font, so reassert our override
    // whenever a TMP turns on. this is the fix for HUD/instantiated text resetting. OnEnable is
    // declared on the concrete types, not the abstract TMP_Text base, so patch both.
    [HarmonyLib.HarmonyPatch]
    internal static class TMPTextOnEnableFontPatch
    {
        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var u = HarmonyLib.AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable");
            if (u != null) yield return u;
            var w = HarmonyLib.AccessTools.Method(typeof(TextMeshPro), "OnEnable");
            if (w != null) yield return w;
        }

        [HarmonyLib.HarmonyPostfix]
        public static void Postfix(TMP_Text __instance) => FontReplacementService.TryApplyTo(__instance);
    }
}
