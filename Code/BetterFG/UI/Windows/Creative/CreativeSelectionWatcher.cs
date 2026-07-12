using System;
using System.Collections;
using BetterFG.Core;
using BetterFG.Features.UnityRound.Editor;
using FGClient.UI.Core;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;

namespace BetterFG.UI.Windows.Creative
{
    // Watches the level-editor selection. While ≥1 object is selected it shows a nav prompt
    // ("Batch edit"); pressing it opens the BatchEditWindow. The prompt is torn down the moment
    // the selection is empty or the editor is exited, and the window closes itself once nothing's
    // selected. Persistent singleton spawned from Plugin.InitGameObjects.
    public class CreativeSelectionWatcher : MonoBehaviour
    {
        public CreativeSelectionWatcher(IntPtr ptr) : base(ptr) { }

        public static CreativeSelectionWatcher Instance { get; private set; }

        private NavPromptHandle _prompt;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            bool shouldPrompt = BatchEditWindow.FeatureEnabled && UnityRoundLoader.InLevelEditor && BatchRecolour.SelectionCount() >= 1;

            // turned off while the window's up → close it
            if (!BatchEditWindow.FeatureEnabled && BatchEditWindow.Instance != null)
                BatchEditWindow.Instance.Close();

            if (shouldPrompt) EnsurePrompt();
            else DestroyPrompt();

            if (_prompt != null && _prompt.IsAlive && _prompt.IsPressed())
                OpenWindow();
        }

        private void EnsurePrompt()
        {
            if (_prompt != null && _prompt.IsAlive) return;
            var parent = NavPromptCore.GetCustomNavPromptRoot();
            if (parent == null) return;
            // LE_Edit is the editor "edit selection" glyph. AllowWhileUnfocused because the editor's
            // own UI owns focus while you're placing/selecting, so the default gameplay-focus gate
            // would swallow the press.
            _prompt = NavPromptCore.From(NavPrompt.Report)
                .WithLabel("Batch edit", "bfg_creative_batchedit")
                .AnchoredAt(NavPromptAnchor.BottomCenter)
                .AllowWhileUnfocused()
                .PollActions(RewiredConsts.Action.Menu_Report)
                .SpawnOn(parent);
        }

        private void DestroyPrompt()
        {
            if (_prompt == null) return;
            _prompt.Destroy();
            _prompt = null;
        }

        private void OpenWindow()
        {
            if (BatchEditWindow.Instance != null) return;
            var go = new GameObject("BetterFG_BatchEditWindow");
            go.AddComponent<BatchEditWindow>().Configure();
        }

        // the window destroys itself on close, so it can't run this itself — we host it. one frame after
        // the window's gone (AnyOpen already false) fire a single place so the batch-edited selection
        // commits at its current spot without you clicking off the blocked-attempt backlog.
        public void PlaceAfterFrame()
        {
            StartCoroutine(PlaceNextFrame().WrapToIl2Cpp());
        }

        private IEnumerator PlaceNextFrame()
        {
            yield return null;
            var h = Patches.BatchEditBlockPlacePatch.LiveHandler;
            if (h != null) h.PlaceMultiSelection();
        }
    }
}
