using System;
using HarmonyLib;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>
    /// Old-school quality colors: quality is on the 1-600 scale, so map it back to the
    /// six vanilla color bands (1-100, 101-200, ..., 501-600) instead of clamping
    /// everything >= 6 to the legendary purple.
    /// </summary>
    [HarmonyPatch(typeof(QualityInfo))]
    public static class PatchQualityColors
    {
        [HarmonyPatch("GetTierColor")]
        [HarmonyPrefix]
        [Preserve]
        public static void TierColorPrefix(ref int _tier)
        {
            _tier = BandTier(_tier);
        }

        [HarmonyPatch("GetQualityColor")]
        [HarmonyPrefix]
        [Preserve]
        public static void QualityColorPrefix(ref int _quality)
        {
            _quality = BandTier(_quality);
        }

        [HarmonyPatch("GetQualityColorHex")]
        [HarmonyPrefix]
        [Preserve]
        public static void ColorHexPrefix(ref int _quality)
        {
            _quality = BandTier(_quality);
        }

        private static int BandTier(int quality)
        {
            if (quality <= 0) return 0;
            if (quality <= 6) return quality;             // vanilla small values unchanged
            int tier = (quality - 1) / 100 + 1;           // 1-600 -> 1-6
            return Math.Max(1, Math.Min(6, tier));
        }
    }
}
