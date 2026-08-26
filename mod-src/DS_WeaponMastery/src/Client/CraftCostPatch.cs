using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>
    /// Crafting material cost tier. The crafting UI's tier selector is
    /// ceil(skill/100) (see PatchRecipeGetCraftingTier) so players can craft an item
    /// at their exact skill quality, but the crafted item's quality TIER is
    /// floor(quality/100). Recipe ingredient costs (CraftingIngredientCount) were
    /// evaluated at the selector tier, i.e. one tier too high for any quality that
    /// isn't an exact multiple of 100 — a quality-336 steel pickaxe cost like a
    /// vanilla tier-4 item (40 forged steel) instead of tier 3 (30).
    ///
    /// This clamps the tier used for CraftingIngredientCount to the output item's
    /// quality tier: quality 200 = vanilla tier-2 cost, quality 600 = vanilla tier-6
    /// cost. The recipe's effects are keyed on level = tier (2..6), which is what the
    /// vanilla cost curve expects, so the vanilla values stay untouched.
    /// </summary>
    [HarmonyPatch(typeof(EffectManager), "GetValue")]
    public static class PatchCraftIngredientTier
    {
        [HarmonyPrefix]
        [Preserve]
        public static void Prefix(PassiveEffects _passiveEffect, ItemValue _originalItemValue,
            EntityAlive _entity, Recipe _recipe, ref int craftingTier)
        {
            try
            {
                if (_passiveEffect != PassiveEffects.CraftingIngredientCount) return;
                if (craftingTier <= 0) return;
                if (_recipe == null) return;
                var ic = _recipe.GetOutputItemClass();
                if (ic == null) return;
                var def = DsConfig.Instance.GetSkillDefForTags(ic.ItemTags);
                if (def == null) return; // vanilla recipe: keep vanilla behavior

                int quality;
                if (_originalItemValue != null && _originalItemValue.HasQuality)
                {
                    // CanCraft passes the item being crafted (quality already final)
                    quality = _originalItemValue.Quality;
                }
                else if (_entity != null && _entity.Progression != null)
                {
                    // UI display calls (ingredient list, craft count, tracker) pass no
                    // item — derive the cost tier from the player's skill level
                    var pv = _entity.Progression.GetProgressionValue(def.Skill);
                    if (pv == null) return;
                    quality = pv.Level;
                }
                else
                {
                    return;
                }
                if (quality <= 0) return;

                int costTier = Mathf.Clamp(quality / 100, 1, 6);
                if (costTier < craftingTier) craftingTier = costTier;
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] CraftingIngredientCount tier patch error: " + e);
            }
        }
    }
}
