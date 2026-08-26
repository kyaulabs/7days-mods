using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>
    /// The crafting UI shows a selectable quality tier (1..max). Vanilla computes the max
    /// from the CraftingTier passive effect. We derive it from the weapon/tool skill:
    /// max tier = ceil(skill / 100), so skill 75 -> tier 1, skill 437 -> tiers 1..5, skill 600 -> 6.
    /// The actual crafted quality is min(skill, tier*100) (see PatchCraftQuality), and
    /// PatchCraftingInfoQualityDisplay makes the menu show that real value.
    /// </summary>
    [HarmonyPatch(typeof(Recipe), "GetCraftingTier")]
    public static class PatchRecipeGetCraftingTier
    {
        [HarmonyPrefix]
        [Preserve]
        public static bool Prefix(Recipe __instance, EntityPlayer _ep, ref int __result)
        {
            try
            {
                if (_ep == null || _ep.Progression == null) return true;
                var ic = __instance.GetOutputItemClass();
                if (ic == null) return true;
                var def = DsConfig.Instance.GetSkillDefForTags(ic.ItemTags);
                if (def == null) return true;
                var pv = _ep.Progression.GetProgressionValue(def.Skill);
                int lvl = pv?.Level ?? 0;
                if (lvl <= 0) return true;
                __result = Mathf.Clamp((lvl + 99) / 100, 1, 6);
                return false;
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] GetCraftingTier patch error: " + e);
                return true;
            }
        }
    }

    /// <summary>
    /// The crafting menu's quality label/color come from the selected tier (1-6), which is
    /// misleading now that quality is on the 1-600 scale. Show the real output quality
    /// (min(skill, tier*100)) and its proper color band instead.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_CraftingInfoWindow), "GetBindingValueInternal")]
    public static class PatchCraftingInfoQualityDisplay
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(XUiC_CraftingInfoWindow __instance, ref string value, string bindingName)
        {
            try
            {
                if (bindingName != "durabilitytext" && bindingName != "durabilitycolor") return;
                var recipe = __instance.recipe;
                if (recipe == null) return;
                var ic = recipe.GetOutputItemClass();
                if (ic == null) return;
                var def = DsConfig.Instance.GetSkillDefForTags(ic.ItemTags);
                if (def == null) return;
                var player = __instance.xui?.playerUI?.entityPlayer;
                if (player == null || player.Progression == null) return;
                var pv = player.Progression.GetProgressionValue(def.Skill);
                int lvl = pv?.Level ?? 0;
                if (lvl <= 0) return;
                int quality = Mathf.Min(lvl, __instance.selectedCraftingTier * 100);
                if (bindingName == "durabilitytext")
                {
                    value = quality.ToString();
                }
                else
                {
                    int band = quality <= 6 ? quality : Mathf.Clamp((quality - 1) / 100 + 1, 1, 6);
                    value = ((Color32)QualityInfo.GetTierColor(band)).ToXuiColorString();
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] CraftingInfo display patch error: " + e);
            }
        }
    }

    /// <summary>
    /// Crafted weapon quality = min(skill, selectedTier * 100). Selecting the max tier crafts
    /// an item whose quality equals your exact skill level (skill 437 -> "Quality 437").
    /// Lower tiers craft at the tier boundary (100/200/...) and cost fewer ingredients.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_RecipeStack), "outputStack")]
    public static class PatchCraftQuality
    {
        [HarmonyPrefix]
        [Preserve]
        public static void Prefix(XUiC_RecipeStack __instance)
        {
            try
            {
                var recipe = __instance.recipe;
                if (recipe == null) return;
                var orig = __instance.originalItem;
                if (orig != null && !orig.IsEmpty()) return; // repair path, not crafting
                var player = __instance.xui?.playerUI?.entityPlayer;
                if (player == null || player.Progression == null) return;
                var ic = recipe.GetOutputItemClass();
                if (ic == null) return;
                var def = DsConfig.Instance.GetSkillDefForTags(ic.ItemTags);
                if (def == null) return;
                var pv = player.Progression.GetProgressionValue(def.Skill);
                int lvl = pv?.Level ?? 0;
                if (lvl <= 0) return;
                int tier = recipe.craftingTier;
                if (tier <= 0) tier = Mathf.Clamp((lvl + 99) / 100, 1, 6);
                __instance.outputQuality = Mathf.Min(lvl, tier * 100);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] outputStack patch error: " + e);
            }
        }
    }
}
