using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using Catapult.Modules.Items.Protocol.Dtos;
using Events;
using FallGuys.Player.Protocol.Client.Cosmetics;
using FG.Common;
using FG.Common.CMS;
using FG.Common.Definition;
using FGClient;
using FGClient.CatapultServices;
using FGClient.Customiser;
using HarmonyLib;
using MPG.Utility;
using UnityEngine;

namespace BetterFG.Features.AllCosmetics
{
    
    public static class FeatureAllCosmetics
    {
        public static readonly bfgfeature feature = new bfgfeature("allcosmetics", "All Cosmetics");

        /*
        static EventSystem.Handle _menuHandle;
        static CustomisationSelections _lastSelections;
        static bool On => SettingsService.Get("feature.allcosmetics", "true") == "true";

        static void OnDisplayMenu(FGClient.OnMainMenuDisplayed evt)
        {
            Apply();
        }

        public static bool Apply()
        {
            if (!On) return false;

            var services = CatapultServices.Instance;
            var collection = services?.PlayerCosmeticsService?.CosmeticsCollection;
            if (collection == null) return false;

            try
            {
                var nicknames = BuildCosmList<NicknameDto, NicknamesSO, Nickname>(
                    () => Resources.FindObjectsOfTypeAll<NicknamesSO>().FirstOrDefault(),
                    delegate (NicknamesSO so)
                    {
                        var list = new List<Nickname>();
                        foreach (Il2CppSystem.Collections.Generic.KeyValuePair<string, Nickname> item in so.Nicknames)
                            list.Add(item.Value);
                        return list;
                    },
                    def => (CMSItemDefinition)def,
                    item => new NicknameDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var emoticons = BuildCosmList<EmoticonDto, CosmeticsEmoticonsSO, CustomiserEmoticons>(
                    () => Resources.FindObjectsOfTypeAll<CosmeticsEmoticonsSO>().FirstOrDefault(),
                    delegate (CosmeticsEmoticonsSO so)
                    {
                        var list = new List<CustomiserEmoticons>();
                        foreach (Il2CppSystem.Collections.Generic.KeyValuePair<string, CustomiserEmoticons> item in so.Emoticons)
                            list.Add(item.Value);
                        return list;
                    },
                    def => (CMSItemDefinition)def,
                    item => new EmoticonDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var phrases = BuildCosmList<PhraseDto, CosmeticsPhrasesSO, CustomiserPhrases>(
                    () => Resources.FindObjectsOfTypeAll<CosmeticsPhrasesSO>().FirstOrDefault(),
                    delegate (CosmeticsPhrasesSO so)
                    {
                        var list = new List<CustomiserPhrases>();
                        foreach (Il2CppSystem.Collections.Generic.KeyValuePair<string, CustomiserPhrases> item in so.Phrases)
                            list.Add(item.Value);
                        return list;
                    },
                    def => (CMSItemDefinition)def,
                    item => new PhraseDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var colours = PushCosmList<ColourOption, ColourSchemeDto>(
                    def => def.CMSData,
                    item => new ColourSchemeDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var patterns = PushCosmList<SkinPatternOption, PatternDto>(
                    def => def.CMSData,
                    item => new PatternDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var faceplates = PushCosmList<FaceplateOption, FaceplateDto>(
                    def => def.CMSData,
                    item => new FaceplateDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var uppers = PushCosmList<CostumeOption, UpperCostumePieceDto>(
                    def => def.CMSData,
                    item => new UpperCostumePieceDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var lowers = PushCosmList<CostumeOption, LowerCostumePieceDto>(
                    def => def.CMSData,
                    item => new LowerCostumePieceDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var punchlines = PushCosmList<VictoryOption, PunchlineDto>(
                    def => def.CMSData,
                    item => new PunchlineDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var nameplates = PushCosmList<NameplateOption, NameplateDto>(
                    def => def.CMSData,
                    item => new NameplateDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                var emotes = PushCosmList<EmotesOption, EmoteDto>(
                    def => def.CMSData,
                    item => new EmoteDto { EarnedAt = Il2CppSystem.DateTime.Now, Item = item, IsFavourite = false });

                collection.Nicknames = nicknames;
                collection.Emoticons = emoticons;
                collection.Phrases = phrases;
                collection.ColourSchemes = colours;
                collection.Patterns = patterns;
                collection.Faceplates = faceplates;
                collection.UpperCostumePieces = uppers;
                collection.LowerCostumePieces = lowers;
                collection.Punchlines = punchlines;
                collection.Nameplates = nameplates;
                collection.Emotes = emotes;

                Plugin.Log.LogInfo($"all cosmetics: filled collection u:{uppers?.Count ?? -1} l:{lowers?.Count ?? -1} c:{colours?.Count ?? -1}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("all cosmetics failed: " + ex.Message);
                return false;
            }
        }

        static Il2CppSystem.Collections.Generic.List<TDto> PushCosmList<TDefinition, TDto>(
            Func<TDefinition, CMSItemDefinition> getCMS,
            Func<ItemDto, TDto> makeDto)
            where TDefinition : UnityEngine.Object
            where TDto : Il2CppSystem.Object
        {
            var list = new Il2CppSystem.Collections.Generic.List<TDto>();
            var defs = Resources.FindObjectsOfTypeAll<TDefinition>();

            for (int i = 0; i < defs.Length; i++)
            {
                var cms = getCMS(defs[i]);
                if (cms == null) continue;
                list.Add(makeDto(ToItem(cms)));
            }

            return list;
        }

        public static IEnumerator ApplyAgainAfterMenuSettles()
        {
            for (int i = 0; i < 11; i++)
            {
                if (Apply()) yield break;
                yield return new WaitForSeconds(1f);
            }
        }

        public static IEnumerator RememberMenuSelectionAfterFail()
        {
            yield return new WaitForSeconds(1f);
            RememberMenuSelection();
        }

        public static bool RememberMenuSelection()
        {
            if (!On) return false;

            var mm = Resources.FindObjectsOfTypeAll<MainMenuManager>().FirstOrDefault();
            var bean = mm?._menuFallGuy ?? mm?._lobbyFallGuy;
            if (bean == null)
            {
                Plugin.Log.LogWarning("all cosmetics: no menu bean for selection");
                return false;
            }

            var special = bean.GetComponent<FallGuyCustomisationSpecialCasesHandler>();
            var selections = special?.CustomisationSelections;
            if (selections == null)
            {
                Plugin.Log.LogWarning("all cosmetics: no special selections");
                return false;
            }

            _lastSelections = selections;
            Plugin.Log.LogInfo("all cosmetics: cached menu selection");
            ApplyCachedToBean(BeanMonitorService.LocalPlayerBean);
            return true;
        }

        public static bool ApplyCachedToBean(GameObject bean)
        {
            if (!On || bean == null || _lastSelections == null) return false;

            try
            {
                var manager = SingletonBehaviour<CustomisationManager>.Instance;
                if (manager == null) return false;

                manager.ApplyCustomisationsToFallGuy(bean, _lastSelections, -1, true);
                Plugin.Log.LogInfo("all cosmetics: applied cached selection to bean");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("all cosmetics: apply cached failed " + ex.Message);
                return false;
            }
        }

        static Il2CppSystem.Collections.Generic.List<TDto> BuildCosmList<TDto, TSO, TSource>(
            Func<TSO> getSO,
            Func<TSO, IEnumerable<TSource>> getSrc,
            Func<TSource, CMSItemDefinition> getCMS,
            Func<ItemDto, TDto> makeDto)
            where TDto : Il2CppSystem.Object
            where TSO : ScriptableObject
        {
            var list = new Il2CppSystem.Collections.Generic.List<TDto>();
            var so = getSO();
            if (so == null) return null;

            var src = getSrc(so);
            if (src == null) return null;
            foreach (TSource item in src)
            {
                var cms = getCMS(item);
                if (cms == null) continue;
                list.Add(makeDto(ToItem(cms)));
            }

            return list;
        }

        static ItemDto ToItem(CMSItemDefinition def)
        {
            return new ItemDto
            {
                ContentId = def.Id,
                Id = def.FullItemId,
                ContentType = def.GroupId,
                Quantity = 1
            };
        }

        [HarmonyPatch(typeof(MainMenuManager), "Awake")]
        [HarmonyPostfix]
        static void MainMenuAwake(MainMenuManager __instance)
        {
            if (_menuHandle != null) return;
            _menuHandle = Broadcaster.Instance.Register<FGClient.OnMainMenuDisplayed>(new Action<FGClient.OnMainMenuDisplayed>(OnDisplayMenu));
        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OnMainMenuEntered), new[] { typeof(bool), typeof(bool) })]
        [HarmonyPostfix]
        static void MainMenuEntered(MainMenuManager __instance)
        {
            __instance.StartCoroutine(ApplyAgainAfterMenuSettles().WrapToIl2Cpp());
            __instance.StartCoroutine(RememberMenuSelectionAfterFail().WrapToIl2Cpp());
        }

        [HarmonyPatch(typeof(CustomiserScreenViewModel), "OnEnable")]
        [HarmonyPostfix]
        static void CustomiserOpened()
        {
            Apply();
        }

        [HarmonyPatch(typeof(MainMenuManager), "OnDestroy")]
        [HarmonyPostfix]
        static void MainMenuDestroyed(MainMenuManager __instance)
        {
            if (_menuHandle == null) return;
            Broadcaster.Instance.Deregister(_menuHandle);
            _menuHandle = null;
        }

        [HarmonyPatch(typeof(CustomiserScreenViewModel), nameof(CustomiserScreenViewModel.HandleConfigureRequestFailed)), HarmonyPrefix]
        static bool HandleConfigureRequestFailed(CustomiserScreenViewModel __instance, Il2CppSystem.Exception error, CustomisationSelections previousSelections, bool isEmotes)
        {
            __instance.HideSpinner();
            __instance.StartCoroutine(RememberMenuSelectionAfterFail().WrapToIl2Cpp());
            Plugin.Log.LogInfo("all cosmetics: configure fail captured");
            return false;
        }
     */
    }
}
