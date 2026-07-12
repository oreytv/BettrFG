using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI;
using FGClient;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class ChangeFallGuysLogo : BfgTweak
    {
        public ChangeFallGuysLogo(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "custom_logo";
        public override string TweakLabel => "Custom Splash Logo";
        public override bool DefaultEnabled => true;

        private const string PathKey = "tweak.custom_logo.path";

        public static ChangeFallGuysLogo Instance { get; private set; }
        void Awake() => Instance = this;

        private Texture2D _targetTex;
        private Image _targetImg;
        private Sprite _originalSprite;
        private Sprite _customSprite;

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "Load PNG", Width = 46f, OnClick = PickAndReload }
        };

        private void PickAndReload()
        {
            WinDialogs.PickPng("Select Logo PNG", path =>
            {
                if (path == null) return;
                var bytes = File.ReadAllBytes(path);
                SettingsService.Set(PathKey, path);
                StartCoroutine(ApplyBytes(bytes).WrapToIl2Cpp());
            });
        }

        private System.Collections.IEnumerator ApplyBytes(byte[] bytes)
        {
            yield return null;
            if (IsEnabled) { DisableTweak(); }
            _customSprite = LoadSprite(bytes);
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite.name != "UI_S11_Logo_Splash") continue;
                _targetTex = sprite.texture;
                _targetTex.LoadImage(bytes);
                break;
            }
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img.name != "TitleScreenLogo") continue;
                _targetImg = img;
                _originalSprite = img.sprite;
                img.sprite = _customSprite;
                break;
            }
        }

        public override void EnableTweak()
        {
            var bytes = LoadBytes();
            if (bytes == null) return;

            _customSprite = LoadSprite(bytes);

            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite.name != "UI_S11_Logo_Splash") continue;
                _targetTex = sprite.texture;
                _targetTex.LoadImage(bytes);
                break;
            }

            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img.name != "TitleScreenLogo") continue;
                _targetImg = img;
                _originalSprite = img.sprite;
                img.sprite = _customSprite;
                break;
            }
        }

        public override void DisableTweak()
        {
            if (_targetImg != null && _originalSprite != null)
                _targetImg.sprite = _originalSprite;
            if (_targetTex != null && _originalSprite != null)
                _targetTex.LoadImage(GetEmbeddedBytes() ?? new byte[0]);
        }

        private byte[] LoadBytes()
        {
            var saved = SettingsService.Get(PathKey, "");
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                return File.ReadAllBytes(saved);
            return GetEmbeddedBytes();
        }

        private static Sprite LoadSprite(byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            ImageConversion.LoadImage(tex, bytes);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        private static byte[] GetEmbeddedBytes()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.betterfg_splash.png");
            if (stream == null)
            {
                Plugin.Log.LogWarning("ChangeFallGuysLogo: embedded splash not found");
                return null;
            }
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            return bytes;
        }
    }

    // the game sometimes re-sets the splash logo back to default. when the title screen vm
    // enables, reapply our custom logo (if the tweak is on) and the custom menu background
    // (gradient colours + circle pattern) onto the title screen's season background.
    [HarmonyPatch(typeof(TitleScreenViewModel), nameof(TitleScreenViewModel.OnEnable))]
    internal static class ChangeFallGuysLogoReapplyPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var tweak = ChangeFallGuysLogo.Instance;
            if (tweak != null && tweak.IsEnabled)
            {
                try { tweak.EnableTweak(); }
                catch (Exception ex) { Plugin.Log?.LogWarning("[ChangeFallGuysLogo] reapply failed " + ex.Message); }
            }

            try { Customization.Menu.MenuCustomizationApplication.Instance?.ReapplyTitleScreenBg(); }
            catch (Exception ex) { Plugin.Log?.LogWarning("[ChangeFallGuysLogo] title bg reapply failed " + ex.Message); }
        }
    }
}