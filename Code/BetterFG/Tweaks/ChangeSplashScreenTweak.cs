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
    public class ChangeSplashScreenTweak : BfgTweak
    {
        public ChangeSplashScreenTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "custom_splash";
        public override string TweakLabel => "Custom Splash Screen";
        public override bool DefaultEnabled => true;

        private const string PathKey = "tweak.custom_splash.path";

        private Image _targetImg;
        private Sprite _originalSprite;
        private Sprite _customSprite;

        public static ChangeSplashScreenTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "Load PNG", Width = 46f, OnClick = PickAndReload }
        };

        private void PickAndReload()
        {
            WinDialogs.PickPng("Select Splash PNG", path =>
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
            if (IsEnabled) DisableTweak();
            _customSprite = LoadSprite(bytes);
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img.sprite == null || img.sprite.name != "UI_Splash") continue;
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
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img.sprite == null || img.sprite.name != "UI_Splash") continue;
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
        }

        private byte[] LoadBytes()
        {
            var saved = SettingsService.Get(PathKey, "");
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                return File.ReadAllBytes(saved);
            return null;
        }

        private static Sprite LoadSprite(byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            ImageConversion.LoadImage(tex, bytes);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        public void ReapplySplash()
        {
            if (_customSprite == null) return;
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img.sprite == null || img.sprite.name != "UI_Splash") continue;
                if (_originalSprite == null) _originalSprite = img.sprite;
                img.sprite = _customSprite;
                _targetImg = img;
                break;
            }
        }

        // called from the shared LoadingScreenViewModel.UpdateDisplay hub in GameStatePatches.
        public static void OnLoadingScreenUpdateDisplay()
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            inst.ReapplySplash();
        }
    }
}