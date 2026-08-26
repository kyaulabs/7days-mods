using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>
    /// Dedicated-server use reporter (client DLL). In 1.0, melee/tool swings and
    /// repair/upgrade actions are simulated on the CLIENT for the local player — the
    /// server never runs ItemActionAttack.Hit / ItemActionRepair for remote players and
    /// only receives the resulting block change (no attacker id). So the client reports
    /// successful block interactions to the server, which rolls chances/cooldowns and
    /// grants XP. Only active on a remote world; in SP/listen-server the server-side
    /// hooks in KillXp.cs handle everything and this reporter stays silent.
    /// </summary>
    public static class UseReporter
    {
        /// <summary>True while connected to a dedicated server (client-side world).</summary>
        public static bool Active()
        {
            return GameManager.Instance != null && GameManager.Instance.World != null
                && GameManager.Instance.World.IsRemote();
        }

        public static void Report(EntityPlayerLocal player, bool destroyed)
        {
            try
            {
                if (player == null || player.inventory == null) return;
                var held = player.inventory.holdingItem;
                if (held == null) return;
                var def = DsConfig.Instance.GetSkillDefForTags(held.ItemTags);
                if (def == null) return;
                if (SingletonMonoBehaviour<ConnectionManager>.Instance == null) return;
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageDSWMUseXp>().Setup(def.Skill, destroyed));
            }
            catch (Exception e)
            {
                Log.Error("[DSWM][use] client report error: " + e);
            }
        }
    }

    /// <summary>
    /// Melee/tool swing that landed damage on a block — the client-side mirror of the
    /// server's PatchAttackHitUse. Reports "use" (hit, chance roll) or "destroy" (the
    /// block broke, guaranteed tool grant). Only the local player's swings count.
    /// </summary>
    [HarmonyPatch(typeof(ItemActionAttack), "Hit")]
    public static class PatchClientAttackHitUse
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(int _attackerEntityId, ItemActionAttack.AttackHitInfo _attackDetails)
        {
            try
            {
                if (_attackDetails == null || !_attackDetails.bBlockHit) return; // damaging block hit only
                if (!UseReporter.Active()) return; // dedicated-server client only
                var player = GameManager.Instance.World.GetEntity(_attackerEntityId) as EntityPlayerLocal;
                if (player == null) return; // AI/turret swings etc.
                UseReporter.Report(player, _attackDetails.bKilled);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM][use] client hit report error: " + e);
            }
        }
    }

    /// <summary>
    /// Repair/upgrade actions apply negative block damage (DamageBlock with
    /// _damagePoints &lt; 0: -repairAmount for a repair, -1 for a completed upgrade step).
    /// Fires only when the action actually went through — failed right-clicks (no
    /// resources, out of range, broken tool) never reach DamageBlock.
    /// </summary>
    [HarmonyPatch(typeof(Block), "DamageBlock")]
    public static class PatchClientRepairUse
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(int _damagePoints, int _entityIdThatDamaged)
        {
            try
            {
                if (_damagePoints >= 0) return; // repair/upgrade only (melee hits handled by the Hit hook)
                if (!UseReporter.Active()) return; // dedicated-server client only
                var player = GameManager.Instance.World.GetEntity(_entityIdThatDamaged) as EntityPlayerLocal;
                if (player == null) return;
                UseReporter.Report(player, false); // repair has no "destroy" event
            }
            catch (Exception e)
            {
                Log.Error("[DSWM][use] client repair report error: " + e);
            }
        }
    }
}
