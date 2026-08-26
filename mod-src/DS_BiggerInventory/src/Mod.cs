using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine.Scripting;

namespace DSBiggerInventory
{
    /// <summary>
    /// Makes the BiggerBackpackAndToolbelt UI mod real: the game's actual storage arrays
    /// are enlarged so the extra slots persist in saves and sync over the network.
    /// Toolbelt: 10 -> 15 slots (Inventory.PUBLIC_SLOTS_PLAYMODE).
    /// Backpack: 45 -> 60 slots (BagSize passive effect in Config/entityclasses.xml;
    /// loaded arrays are padded here so old saves keep working).
    /// </summary>
    [Preserve]
    public class ModInit : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            Log.Out("[DSBI] Bigger Inventory initializing...");
            var harmony = new Harmony("DSBiggerInventory");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[DSBI] Bigger Inventory initialized. Toolbelt=" + Sizes.ToolbeltSlots + " Bag=" + Sizes.BagSlots);
        }
    }

    public static class Sizes
    {
        public const int ToolbeltSlots = 15; // was 10
        public const int BagSlots = 60;      // must match BagSize in Config/entityclasses.xml
    }

    /// <summary>Toolbelt size is hardcoded in code; patch the property getter.</summary>
    [HarmonyPatch(typeof(Inventory), "get_PUBLIC_SLOTS_PLAYMODE")]
    public static class PatchToolbeltSize
    {
        [HarmonyPrefix]
        [Preserve]
        public static bool Prefix(ref int __result)
        {
            __result = Sizes.ToolbeltSlots;
            return false;
        }
    }

    /// <summary>
    /// The bag persists its slot count, so old saves (45) would load short. Pad/truncate to
    /// the configured size on every load path (save read + network + direct assignment).
    /// </summary>
    [HarmonyPatch(typeof(Bag))]
    public static class PatchBagSize
    {
        private static readonly FieldInfo FItems = AccessTools.Field(typeof(Bag), "items");

        [HarmonyPatch("ReadInto")]
        [HarmonyPostfix]
        [Preserve]
        public static void ReadIntoPostfix(Bag __instance)
        {
            Normalize(__instance);
        }

        [HarmonyPatch("SetSlots")]
        [HarmonyPostfix]
        [Preserve]
        public static void SetSlotsPostfix(Bag __instance, ItemStack[] _slots)
        {
            Normalize(__instance);
        }

        private static void Normalize(Bag bag)
        {
            try
            {
                var slots = bag.GetSlots();
                if (slots == null || slots.Length == Sizes.BagSlots) return;
                var padded = ItemStack.CreateArray(Sizes.BagSlots);
                Array.Copy(slots, padded, Math.Min(slots.Length, Sizes.BagSlots));
                FItems.SetValue(bag, padded);
                if (bag.LockedSlots != null && bag.LockedSlots.Length < Sizes.BagSlots)
                {
                    bag.LockedSlots.Length = Sizes.BagSlots;
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSBI] Bag normalize error: " + e);
            }
        }
    }
}
