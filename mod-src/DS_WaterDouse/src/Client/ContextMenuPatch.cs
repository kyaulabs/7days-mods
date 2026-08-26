using System;
using HarmonyLib;
using UnityEngine.Scripting;

namespace DSWaterDouse
{
    /// <summary>
    /// Adds the "Douse" entry to the item context menu (right-click / E) for any item
    /// carrying a DouseSmellMeters / DouseSmellFull property. Runs after vanilla
    /// AddActionActions, so the entry appears below the vanilla Use/Drink entry.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_ItemActionList), "AddActionActions")]
    public static class ContextMenuPatch
    {
        public static void Postfix(XUiC_ItemActionList __instance, ItemClass itemClass, XUiC_ItemStack stackController)
        {
            try
            {
                if (__instance == null || itemClass == null || stackController == null) return;
                if (!DouseConfig.IsDouseable(itemClass)) return;
                __instance.AddActionListEntry(new ItemActionEntryDouse(stackController));
            }
            catch (Exception e)
            {
                Log.Error("[DSDouse] ContextMenuPatch error: " + e);
            }
        }
    }
}
