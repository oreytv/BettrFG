using System;
using System.Collections.Generic;
using FG.Common.CMS;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    [Serializable]
    public class SkinInfo
    {
        public string name;
        public string type;
        public string author;
        public string description; // optional, may be null/empty
        public string group; // optional, old skins stay under Unsorted
        public string file;
        // optional: original source URL used to fetch this skin (catalog override)
        public string sourceRepo;
        // unix seconds UTC when this skin bundle was last cached/fetched locally
        public long lastCachedUtc;
        public bool isLocalImport;
        public string localPath;
        public string repoFolder; // e.g. "Costumes/costume_morgana" — set by catalog, used for download URL
        public float skinScale;    // e.g. 0.8 to shrink bean, 1.2 to enlarge — 0 = unset
        public List<BoneOffsetEntry> boneOffsets; // pre-fetched so apply is sync
        // true once this skin's info.json has already been read (by catalog or loader), even if it
        // had zero offsets. lets the apply path skip its fallback info.json fetch entirely.
        public bool infoFetched;

        // Costumes only
        public bool keepBase; // if true, base bean mesh is NOT hidden (suit/overlay costumes)

        // Emotes only — clip name inside the bundle + optional sound filename in the emote folder
        public string clip;
        public string audio;

        // Items only
        public float scale = 1f;
        public ItemHandInfo left;
        public ItemHandInfo right;
        // 0=use info.json default, 1=left only, 2=right only, 3=both
        public int handOverride = 0;
    }

    [Serializable]
    public class ItemHandInfo
    {
        public float[] position = new float[3]; // x y z
        public float[] rotation = new float[3]; // x y z euler
    }

    [Serializable]
    public class BoneOffsetEntry
    {
        public string bone;
        public Vector3 localPosition;
    }

    [Serializable]
    public class InfoJson
    {
        public string name;
        public string type;
        public string author;
        public string description;
        public string group;
        public string file;
        public float scale = 1f;
        public bool keepBase;
        public ItemHandInfo left;
        public ItemHandInfo right;
        public BoneOffsetEntry[] boneOffsets;
    }

    internal class AppliedSkinInfo
    {
        public GameObject instance;
        public GameObject bean;
        public SkinType type;
        public List<GameObject> disabledChildren;
        public Transform beanGEO;
        public bool restoreAllBaseGEO;
        // SMR GameObjects that the game's BindMeshToFallguy may have reparented out of `instance`.
        // Tracked so removal can destroy them — destroying `instance` alone leaves these visible.
        public List<GameObject> boundRenderers;
        // instance came from Addressables InstantiateAsync (game/UGC cosmetics). MUST be torn down
        // with Addressables.ReleaseInstance, not GameObject.Destroy — otherwise the addressable
        // handle + the whole loaded prefab leaks and stays resident forever.
        public bool addressableInstance;
        // game-cosmetic entry parked (SetActive false) under a UGC costume instead of destroyed, so
        // taking the costume off can re-show it instantly instead of re-instantiating (the 0.5s pop-in).
        public bool stashed;
    }

    public enum ApplyReason
    {
        FromMenu = 0,       // user clicked apply in the UI
        AutoReapply = 1,    // new bean found, reapplying existing skin
        Reload = 2,         // bundle was missing, redownloaded and reapplied
    }

    public class SkinApplyEvent
    {
        public SkinInfo skinInfo;
        public GameObject bean;
        public ApplyReason reason;
    }

    public enum SkinType
    {
        Unknown = 0,
        Costume = 1,
        Accessory = 2,
        Item = 3,
        Plinth = 4,
        Emote = 5
    }

    public static class SkinTypeParser
    {
        public static SkinType FromString(string s)
        {
            if (string.IsNullOrEmpty(s)) return SkinType.Unknown;
            switch (s.Trim().ToLowerInvariant())
            {
                case "costume": return SkinType.Costume;
                case "replacewholebean": return SkinType.Costume;
                case "accessory": return SkinType.Accessory;
                case "item": return SkinType.Item;
                case "plinth": return SkinType.Plinth;
                case "emote": return SkinType.Emote;
                default: return SkinType.Unknown;
            }
        }
    }
}
