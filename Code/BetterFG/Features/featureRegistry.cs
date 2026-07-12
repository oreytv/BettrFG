using System.Collections.Generic;
using BetterFG.Features.AllCosmetics;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Features.QualificationTime;
using BetterFG.Features.Stars;
using BetterFG.Features.TimePlacement;

namespace BetterFG.Features
{
    public static class featureRegistry
    {
        static readonly List<bfgfeature> _all = new List<bfgfeature>
        {
            FeatureQualificationTime.feature,
            FeatureStars.feature,
            FeatureMorePlatformIcon.feature,
            FeatureTimePlacement.feature,
            //FeatureAllCosmetics.feature
        };

        public static IReadOnlyList<bfgfeature> all => _all;

        public static bfgfeature Find(string id)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].id == id) return _all[i];
            return null;
        }

        public static bool IsOn(string featureId, string settingId)
        {
            var f = Find(featureId);
            return f != null && f.Get(settingId);
        }
    }
}
