using UnityEngine;

namespace DSWaterDouse
{
    /// <summary>
    /// Accessors into PlayerStealth's smell fields. PlayerStealth is a struct stored
    /// in the public field EntityPlayer.Stealth; the smell fields are publicized
    /// public in the game assembly, so a ref to the field storage allows direct
    /// in-place reads/writes (no boxing).
    /// </summary>
    public static class StealthAccess
    {
        public static float GetRadius(ref PlayerStealth stealth) => stealth.smellRadius;

        public static void SetRadius(ref PlayerStealth stealth, float value) => stealth.smellRadius = value;

        /// <summary>Force the next SmellUpdateItemsAndBlood pass (target recompute + client send).</summary>
        public static void SetSmellUpdateItemsTicks(ref PlayerStealth stealth, int value) => stealth.smellUpdateItemsTicks = value;

        /// <summary>
        /// Wash off the eating-smell source (ItemActionEat.SmellUse -> SetSmellEat).
        /// If a douse only cut the current radius, the lingering smellEatRadius would
        /// keep the radius target alive and the scent would just grow back — the
        /// "washed off" state must have no remaining source.
        /// </summary>
        public static void ClearEatSmell(ref PlayerStealth stealth)
        {
            stealth.smellEatRadius = 0f;
            stealth.smellEatTicks = 0;
        }

        /// <summary>
        /// Cut the current scent radius.
        /// Full clear goes through the vanilla public API (SetSmellRadiusTarget(-1)
        /// -&gt; SmellClear), exactly like walking through water: everything (radius,
        /// target, eat radius) drops to 0, then re-emits while smelly items are
        /// carried. Partial douse subtracts meters from the current radius AND washes
        /// off the eating smell; the aura then regenerates at the vanilla 5 m/s only
        /// if smelly items are still carried.
        /// </summary>
        public static float ReduceRadius(ref PlayerStealth stealth, bool fullClear, float meters)
        {
            if (fullClear)
            {
                float before = stealth.smellRadius;
                stealth.SetSmellRadiusTarget(-1, false, false); // SmellClear also zeroes the eat smell
                return before;
            }
            float radius = stealth.smellRadius;
            float removed = Mathf.Min(radius, Mathf.Max(0f, meters));
            stealth.smellRadius = radius - removed;
            ClearEatSmell(ref stealth);
            return removed;
        }
    }
}
