using System;
using System.Collections.Generic;
using BetterFG.Services;
using FallGuysIK;
using UnityEngine;

namespace BetterFG.Services
{
    public class PlayerScaleService : MonoBehaviour
    {
        public PlayerScaleService(IntPtr ptr) : base(ptr) { }

        public static PlayerScaleService Instance { get; private set; }

        private const string KEY_PLAYER_SCALE = "player.scale";
        private const string KEY_SCALE_USERSET = "player.scale.userset";
        private const string WRAPPER_NAME = "BetterFG_ScaleWrapper";

        private readonly Dictionary<int, Transform> _wrappers = new Dictionary<int, Transform>();

        void Awake()
        {
            Instance = this;
            Plugin.Log.LogInfo("PlayerScale: awake");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public static float GetPlayerScale()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return float.TryParse(SettingsService.Get(KEY_PLAYER_SCALE, "1"),
                System.Globalization.NumberStyles.Float, ci, out float v)
                ? Mathf.Clamp(v, 0.5f, 1.5f) : 1f;
        }

        public static void SavePlayerScale(float scale)
        {
            scale = Mathf.Clamp(scale, 0.5f, 1.5f);
            SettingsService.Set(KEY_PLAYER_SCALE, scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.Set(KEY_SCALE_USERSET, "true");
            Plugin.Log.LogInfo($"PlayerScale: saved scale: {scale}");
        }

        // saves skin-driven scale without flagging it as user-set
        public static void SaveSkinScale(float scale)
        {
            scale = Mathf.Clamp(scale, 0.5f, 1.5f);
            SettingsService.Set(KEY_PLAYER_SCALE, scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Plugin.Log.LogInfo($"PlayerScale: saved skin scale: {scale}");
        }

        public static bool HasUserSetScale() => SettingsService.Get(KEY_SCALE_USERSET, "false") == "true";

        public static void ClearUserSetScale() => SettingsService.Set(KEY_SCALE_USERSET, "false");

        // true only in a LIVE multiplayer round: there's a real local match controller AND other players
        // are on the roster. this is what separates actual gameplay from the menu/lobby/victory screens,
        // where FallGuy display beans still exist and the roster can still read >0 but there's no live
        // local fgcc — so your personal scale is fine there. the local fgcc check is the key: it only
        // resolves while you're actually controlling a bean in a match.
        public static bool InLivePublicRound()
        {
            var fgcc = BetterFG.Utilities.PlayerInformation.GetLocalPlayerFGCC();
            if (fgcc == null) return false;
            return FallGuysLib.Players.PlayerUtils.GetOtherPlayerIds().Count > 0;
        }

        // the equipped full-body costume that drives scale: its file + baked info.json skinScale. prefers
        // the slot passed in (the one being applied right now, guaranteed present) and otherwise scans
        // the active loadout. returns (null, 0) when no costume is equipped.
        private static (string file, float baked) ActiveCostume(BetterFG.Customization.Player.SkinInfo applying)
        {
            if (applying != null && !string.IsNullOrEmpty(applying.file))
                return (applying.file, applying.skinScale > 0f ? applying.skinScale : 0f);

            // equipped loadout comes from the profile store (skin.multi.*), not live scene slots.
            // the baked per-costume scale is saved alongside as skin.<file>.scale.
            var local = BetterFG.Network.RemoteProfileStore.LocalLoadout();
            if (local != null)
                foreach (var entry in local.skins)
                    if (BetterFG.Customization.Player.SkinTypeParser.FromString(entry.type) == BetterFG.Customization.Player.SkinType.Costume
                        && !string.IsNullOrEmpty(entry.file))
                        return (entry.file, SettingsService.TryGetSkinScale(entry.file, out float sc) ? sc : 0f);
            return (null, 0f);
        }

        // THE single source of truth for the local bean's scale. every path that scales the local player
        // resolves here so there's one rule and no place for two callers to disagree. pass the costume
        // being applied (if any) so its baked scale is used even before it lands in the active loadout.
        //
        //  - live public round only: the equipped costume's own baked skinScale, or 1 if it sets none.
        //    your personal scale is never allowed onto a shared bean while others can see it.
        //  - everywhere else (menu, lobby, victory, solo round): your saved per-costume value from
        //    skinScales.txt if a costume's equipped, else its baked scale, else your global slider.
        public static float ResolveLocalScale(BetterFG.Customization.Player.SkinInfo applyingCostume = null)
        {
            var (file, baked) = ActiveCostume(applyingCostume);

            if (InLivePublicRound())
            {
                float pub = baked > 0f ? baked : 1f;
          
                return pub;
            }

            if (!string.IsNullOrEmpty(file) && SettingsService.TryGetSkinScale(file, out float saved))
            {

                return saved;
            }

            float mine = baked > 0f ? baked : GetPlayerScale();
            return mine;
        }

        public enum BeanScaleMode { Local, Remote }

        // Manual = the user just clicked Apply; Auto = a reapply we drive ourselves (round load, skin
        // change, etc). only Manual surfaces the "couldn't scale, public lobby" tooltip.
        public enum ScaleReason { Auto, Manual }

        // applies scale to a specific bean — call this on anyone
        public static void ApplyToBean(GameObject bean, float scale, BeanScaleMode mode = BeanScaleMode.Local)
        {
            if (Instance == null || bean == null) return;
            Instance.ScaleBean(bean, scale, mode);
        }

        // the local costume-apply path calls this with the skin it's applying, so the resolver uses that
        // costume's baked scale even before the slot lands in the active loadout (no timing gap).
        public static void ApplyLocalCostumeScale(GameObject bean, BetterFG.Customization.Player.SkinInfo applyingCostume)
        {
            if (Instance == null || bean == null) return;
            Instance.ScaleBean(bean, 1f, BeanScaleMode.Local, ScaleReason.Auto, applyingCostume);
        }

        // the PB ghost must be the exact size you are. it's a copy of a fall guy (has a Character child)
        // but isn't a root FallGuy bean, so it can't go through the normal in-round path. scale it the
        // same way you're scaled — on a wrapper under Character with your resolved scale — so a residual
        // Character scale can't make it come out bigger/smaller than you. root stays 1.
        public static void ApplyGhostScale(GameObject ghost)
        {
            if (Instance == null || ghost == null) return;
            ghost.transform.localScale = Vector3.one;
            float resolved = ResolveLocalScale();
            var wrapper = Instance.GetOrCreateWrapper(ghost);
            if (wrapper != null)
                wrapper.localScale = new Vector3(resolved, resolved, resolved);
            else
                ghost.transform.localScale = new Vector3(resolved, resolved, resolved);
            Plugin.Log.LogInfo($"scale: ghost '{ghost.name}' -> {resolved} (wrapper={(wrapper != null)})");
        }

        public static void ApplyToAll(float? overrideScale = null, ScaleReason reason = ScaleReason.Auto)
        {
            if (Instance == null) return;
            float scale = overrideScale ?? GetPlayerScale();
            var local = BeanMonitorService.LocalPlayerBean;
            if (local != null) Instance.ScaleBean(local, scale, BeanScaleMode.Local, reason);
            foreach (var b in BeanMonitorService.GetTrackedBeans())
            {
                if (b == null || b == local) continue;
                Instance.ScaleBean(b, scale, BeanScaleMode.Remote);
            }
        }

        public static void ApplySkinScaleToBean(GameObject bean, float skinScale, BeanScaleMode mode = BeanScaleMode.Local)
        {
            if (Instance == null || bean == null) return;
            Instance.ScaleBean(bean, skinScale, mode);
        }

        public static void RestorePlayerScaleToBean(GameObject bean, BeanScaleMode mode = BeanScaleMode.Local)
        {
            if (Instance == null || bean == null) return;
            Instance.ScaleBean(bean, GetPlayerScale(), mode);
        }

        // ── Core ──────────────────────────────────────────────────────────────

        private void ScaleBean(GameObject bean, float scale, BeanScaleMode mode = BeanScaleMode.Local, ScaleReason reason = ScaleReason.Auto, BetterFG.Customization.Player.SkinInfo applyingCostume = null)
        {
            if (bean == null) return;
            int id = bean.GetInstanceID();

            if (mode == BeanScaleMode.Local)
            {
                // YOUR bean anywhere — menu, lobby, victory, or in a round. the scale is never taken from
                // the caller; it's resolved from current state so every path agrees. the passed value is
                // only "what you asked for", so we can tell you when a public round overrode it.
                float resolved = ResolveLocalScale(applyingCostume);
                bool inRound = IsInRoundBean(bean);
                // only worth a line when we didn't give you what you asked for
                if (!Mathf.Approximately(resolved, scale))
                    Plugin.Log.LogInfo($"{bean.name} wanted {scale}, got {resolved} (inRound={inRound}, {reason})");
                if (reason == ScaleReason.Manual && InLivePublicRound() && !Mathf.Approximately(resolved, scale))
                    UI.BetterFGUIMan.Instance?.ShowTooltipTimed("Couldn't scale you, you're in a public lobby.", 2f);

                // in-round beans carry scale on a wrapper under Character; menu/lobby beans just take it
                // on their own transform.
                if (inRound)
                {
                    var wrapper = GetOrCreateWrapper(bean);
                    if (wrapper != null)
                    {
                        wrapper.localScale = new Vector3(resolved, resolved, resolved);
                        _wrappers[id] = wrapper;
                    }
                }
                else
                    bean.transform.localScale = new Vector3(resolved, resolved, resolved);
            }
            else
            {
                if (bean != BeanMonitorService.LocalPlayerBean)
                {
                    Transform character = bean.transform.Find("Character");
                    if (character != null)
                    {
                        var ikController = character.GetComponent<FallGuyIkController>();
                        if (ikController != null)
                        {
                            ikController.enabled = false;
                            Plugin.Log.LogInfo($"PlayerScale: destroyed ik on {bean.name}");
                        }
                    }
                }

                bean.transform.localScale = new Vector3(scale, scale, scale);
            }
        }

        private Transform GetOrCreateWrapper(GameObject bean)
        {
            int id = bean.GetInstanceID();
            if (_wrappers.TryGetValue(id, out var existing) && existing != null)
                return existing;

            Transform character = bean.transform.Find("Character");
            if (character == null)
            {
                Plugin.Log.LogWarning($"PlayerScale: no 'Character' child on {bean.name}");
                return null;
            }

            Transform existingWrapper = bean.transform.Find(WRAPPER_NAME);
            if (existingWrapper != null)
            {
                _wrappers[id] = existingWrapper;
                return existingWrapper;
            }

            var wrapperGo = new GameObject(WRAPPER_NAME);
            wrapperGo.transform.SetParent(bean.transform, false);
            wrapperGo.transform.localPosition = Vector3.zero;
            wrapperGo.transform.localRotation = Quaternion.identity;
            wrapperGo.transform.localScale = Vector3.one;

            character.SetParent(wrapperGo.transform, true);
            character.localPosition = Vector3.zero;
            character.localRotation = Quaternion.identity;

            _wrappers[id] = wrapperGo.transform;
            Plugin.Log.LogInfo($"PlayerScale: wrapper created for {bean.name}");
            return wrapperGo.transform;
        }

        private static bool IsInRoundBean(GameObject bean)
        {
            if (bean == null) return false;
            return bean.transform.parent == null && bean.name.StartsWith("FallGuy");
        }
    }
}