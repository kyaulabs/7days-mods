using System;
using System.Collections.Generic;
using UnityEngine;

namespace DSWaterDouse
{
    /// <summary>
    /// Applies a douse to a player's authoritative scent state. Shared by:
    ///  - the server-side net package handler (NetPackageDSDouse): validates the
    ///    player still carries a douseable water item before touching anything
    ///    (forged packages are ignored);
    ///  - the SP / listen-host direct path (no net package exists in offline mode;
    ///    the item was already consumed in-process, so validation is skipped).
    ///
    /// The server never computes the smell of remote players itself (that happens on
    /// their client and is sent as a radius target); it only decays smellRadius
    /// toward that target at 2 m/s. Without this the douse would take up to ~50 s to
    /// fully apply server-side. Here the authoritative cut happens instantly: zombie
    /// acquisition (PlayerStealth.SmellTickServer emit) and the "N M" smell display
    /// react immediately, exactly like the vanilla wet clear.
    /// </summary>
    public static class DouseApply
    {
        public static void Apply(EntityPlayer player, float meters, bool fullClear, bool validateItems)
        {
            try
            {
                if (player == null || player.IsDead() || player.IsSpectator || player.IsGodMode.Value) return;

                if (validateItems)
                {
                    // Anti-cheat: the player must actually carry a douseable water item.
                    float itemMeters = MaxMetersOnPlayer(player, out bool hasFullClearItem);
                    if (fullClear)
                    {
                        // Only items flagged DouseSmellFull (pure water) may wipe everything.
                        if (!hasFullClearItem) return;
                    }
                    else
                    {
                        if (itemMeters <= 0f) return;
                        meters = Mathf.Min(meters, Mathf.Min(itemMeters, DouseConfig.Instance.MaxMetersRemoved));
                        if (meters <= 0f) return;
                    }
                }
                else
                {
                    meters = Mathf.Min(meters, DouseConfig.Instance.MaxMetersRemoved);
                    if (!fullClear && meters <= 0f) return;
                }

                float radius;
                ref PlayerStealth stealth = ref player.Stealth;
                if (fullClear)
                {
                    stealth.SetSmellRadiusTarget(-1, false, false); // vanilla SmellClear path
                    radius = 0f;
                }
                else
                {
                    radius = Mathf.Max(0f, StealthAccess.GetRadius(ref stealth) - meters);
                    StealthAccess.SetRadius(ref stealth, radius);
                    // Wash off the eating-smell source (ItemActionEat.SmellUse) as
                    // well: it would otherwise keep the radius target alive and the
                    // scent would just grow back. No-op for remote players (their
                    // server struct never tracks it), essential for SP/listen where
                    // this struct is the live one.
                    StealthAccess.ClearEatSmell(ref stealth);
                    // Recompute the target immediately so the (now stale) eat
                    // contribution can't keep the aura alive.
                    StealthAccess.SetSmellUpdateItemsTicks(ref stealth, 0);
                }

                // The smell cvar drives buffSmellCheck/buffSmell ("N M" display + zombie
                // acquisition). Vanilla refreshes it every 20 ticks; push it now so the
                // effect is immediate — same call pattern as PlayerStealth.SmellTickServer.
                player.Buffs.SetCustomVar("smell", radius);
                player.Buffs.GetBuff("buffSmellCheck")?.DurationTriggerUpdate();

                if (DouseConfig.Instance.DebugLogging)
                {
                    Log.Out("[DSDouse] " + player.EntityName + " doused (full=" + fullClear +
                            ", meters=" + meters + ") -> smell radius " + radius.ToString("0.#"));
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSDouse] Apply error: " + e);
            }
        }

        /// <summary>
        /// Highest DouseSmellMeters among the player's toolbelt + backpack items, and
        /// whether any of them is a full-clear item. Same inventory scan pattern as
        /// PlayerStealth.SmellCountItems.
        /// </summary>
        public static float MaxMetersOnPlayer(EntityPlayer player, out bool hasFullClearItem)
        {
            float max = 0f;
            bool full = false;
            foreach (ItemStack stack in AllStacks(player))
            {
                if (stack == null || stack.IsEmpty() || stack.itemValue == null) continue;
                ItemClass ic = stack.itemValue.ItemClass;
                if (ic == null) continue;
                if (DouseConfig.IsFullClear(ic)) full = true;
                max = Mathf.Max(max, DouseConfig.MetersFor(ic));
            }
            hasFullClearItem = full;
            return max;
        }

        /// <summary>All toolbelt + backpack stacks.</summary>
        private static IEnumerable<ItemStack> AllStacks(EntityPlayer player)
        {
            Inventory inv = player.inventory;
            if (inv != null)
            {
                int slots = inv.GetSlotCount();
                for (int i = 0; i < slots; i++) yield return inv.GetItemStack(i);
            }
            if (player.bag != null)
            {
                foreach (ItemStack s in player.bag.GetSlots()) yield return s;
            }
        }
    }
}
