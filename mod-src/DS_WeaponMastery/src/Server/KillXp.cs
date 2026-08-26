using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>Kill -> weapon skill XP. Server side only.</summary>
    public static class KillXp
    {        public static void OnKill(EntityAlive victim, EntityAlive killer)
        {
            if (victim == null || killer == null) return;
            if (GameManager.Instance == null || GameManager.Instance.World == null) return;

            EntityPlayer player = null;
            string skill = null;

            if (killer is EntityTurret)
            {
                // Kills by a placed turret credit its owner -> robotics
                var turret = (EntityTurret)killer;
                player = FindPlayerByUserId(turret.OwnerID);
                skill = "craftingRobotics";
            }
            else if (killer is EntityPlayer)
            {
                player = (EntityPlayer)killer;
                skill = GetSkillFromItem(victim, player);
            }

            if (player == null || skill == null) return;
            GrantXp(player, skill);
        }

        /// <summary>
        /// Determine the weapon class from the killing blow. Prefer the exact instrument
        /// recorded on the victim (bow, gun, grenade...), fall back to the killer's held item.
        /// </summary>
        private static string GetSkillFromItem(EntityAlive victim, EntityPlayer player)
        {
            try
            {
                var src = victim.woundedDamageSource;
                if (src != null && src.AttackingItem != null && src.AttackingItem.ItemClass != null)
                {
                    var def = DsConfig.Instance.GetSkillDefForTags(src.AttackingItem.ItemClass.ItemTags);
                    if (def != null) return def.Skill;
                }
                var held = player.inventory != null ? player.inventory.holdingItem : null;
                if (held != null)
                {
                    var def2 = DsConfig.Instance.GetSkillDefForTags(held.ItemTags);
                    if (def2 != null) return def2.Skill;
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] GetSkillFromItem error: " + e);
            }
            return null;
        }

        public static EntityPlayer FindPlayerByUserId(PlatformUserIdentifierAbs uid)
        {
            if (uid == null) return null;
            try
            {
                var ppl = GameManager.Instance.GetPersistentPlayerList();
                if (ppl == null) return null;
                var ppd = ppl.GetPlayerData(uid);
                if (ppd == null || ppd.EntityId == -1) return null;
                return GameManager.Instance.World.GetEntity(ppd.EntityId) as EntityPlayer;
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] FindPlayerByUserId error: " + e);
                return null;
            }
        }

        /// <summary>Grant kill progress toward the given crafting skill.</summary>
        public static void GrantXp(EntityPlayer player, string skill)
        {
            if (player == null || player.Progression == null) return;
            var pv = player.Progression.GetProgressionValue(skill);
            if (pv == null) return;
            var pc = pv.ProgressionClass;
            if (pc == null || pv.Level >= pc.MaxLevel) return;

            double mult = 1.0;
            var buff = DsConfig.Instance.GetBuffForSkill(skill);
            if (buff != null && player.Buffs != null && player.Buffs.HasBuff(buff))
            {
                mult = DsConfig.Instance.StudyBuffMultiplier;
            }

            double kpl = DsConfig.Instance.KillsPerLevel(pv.Level);
            int points = (int)Math.Round(DsConfig.ExpPerLevel / kpl * mult);
            if (points < 1) points = 1;

            int startLevel = pv.Level;
            pv.CostForNextLevel -= points;
            while (pv.CostForNextLevel <= 0)
            {
                if (pv.Level >= pc.MaxLevel)
                {
                    pv.CostForNextLevel = 1;
                    break;
                }
                pv.Level++;
                pv.CostForNextLevel += pc.CalculatedCostForLevel(pv.Level + 1);
            }

            player.Progression.bProgressionStatsChanged = true;
            player.bPlayerStatsChanged = true;

            // v1.0 runs a client-authoritative progression model on dedicated servers:
            // the client's local copy drives the crafting UI, so the client must be told
            // about the level-up or it keeps crafting at the old quality.
            if (pv.Level != startLevel)
            {
                if (DsConfig.Instance.DebugLogging)
                    Log.Out("[DSWM][xp] " + player.EntityName + " " + skill + " " + startLevel + " -> " + pv.Level);
                PushLevelsToClient(player, skill, pv.Level);
                AnnounceLevelUp(player, skill, startLevel, pv.Level);
            }
        }

        /// <summary>Force a player's weapon skills to the given level (admin/testing).</summary>
        public static void SetSkillLevel(EntityPlayer player, string skill, int level)
        {
            if (player == null || player.Progression == null) return;
            var pv = player.Progression.GetProgressionValue(skill);
            if (pv == null) return;
            var pc = pv.ProgressionClass;
            if (pc == null) return;
            int clamped = Math.Max(0, Math.Min(pc.MaxLevel, level));
            int startLevel = pv.Level;
            pv.Level = clamped;
            pv.CostForNextLevel = pc.CalculatedCostForLevel(pv.Level + 1);
            player.Progression.bProgressionStatsChanged = true;
            player.bPlayerStatsChanged = true;
            PushLevelsToClient(player, skill, pv.Level);
            AnnounceLevelUp(player, skill, startLevel, pv.Level);
        }

        public static void ResetPlayerWeaponSkills(EntityPlayer player)
        {
            if (player == null || player.Progression == null) return;
            foreach (var def in DsConfig.Instance.Skills)
            {
                var pv = player.Progression.GetProgressionValue(def.Skill);
                if (pv == null || pv.ProgressionClass == null) continue;
                pv.Level = 1;
                pv.CostForNextLevel = pv.ProgressionClass.CalculatedCostForLevel(2);
                PushLevelsToClient(player, def.Skill, 1);
            }
            player.Progression.bProgressionStatsChanged = true;
            player.bPlayerStatsChanged = true;
        }

        /// <summary>
        /// Push a single weapon-skill level to the owning client via the vanilla
        /// NetPackageEntitySetSkillLevelClient (the same package the game uses to sync
        /// skill levels to clients). The client's Progression is authoritative for its
        /// crafting UI, so without this push the client never sees server-side level-ups.
        /// </summary>
        public static void PushLevelsToClient(EntityPlayer player, string skill, int level)
        {
            if (player == null || player.entityId == -1) return;
            try
            {
                var client = SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.ForEntityId(player.entityId);
                if (client == null) return;
                client.SendPackage(NetPackageManager.GetPackage<NetPackageEntitySetSkillLevelClient>().Setup(player.entityId, skill, level));
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] PushLevelsToClient error: " + e);
            }
        }

        /// <summary>
        /// Announce a player's own weapon-skill level-up to them via chat, but only when
        /// the level crossed a milestone (LevelUpAnnounceInterval, default 25 → announces
        /// at 25, 50, 75, ...). The crossed milestone is what gets announced, so a jump
        /// from 24 to 26 announces "reached level 25". 0 disables announcements.
        /// </summary>
        private static void AnnounceLevelUp(EntityPlayer player, string skill, int startLevel, int newLevel)
        {
            try
            {
                int interval = DsConfig.Instance.LevelUpAnnounceInterval;
                if (interval <= 0 || newLevel <= startLevel) return;
                int milestone = (startLevel / interval + 1) * interval;
                if (newLevel < milestone) return;

                string display = Localization.Get(skill + "Name");
                if (string.IsNullOrEmpty(display) || display.Equals(skill + "Name", StringComparison.OrdinalIgnoreCase))
                    display = skill;

                string msg = "[Weapon Mastery] " + display + " reached level " + milestone + "!";
                GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, msg,
                    new List<int> { player.entityId }, EMessageSender.Server);
                if (DsConfig.Instance.DebugLogging)
                    Log.Out("[DSWM][announce] " + player.EntityName + " " + skill + " -> milestone " + milestone);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] AnnounceLevelUp error: " + e);
            }
        }

        /// <summary>Push every weapon skill's current level to the owning client (used at spawn to bring the client up to date).</summary>
        public static void PushAllSkillsToClient(EntityPlayer player)
        {
            if (player == null || player.entityId == -1 || player.Progression == null) return;
            try
            {
                var client = SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.ForEntityId(player.entityId);
                if (client == null) return;
                foreach (var def in DsConfig.Instance.Skills)
                {
                    var pv = player.Progression.GetProgressionValue(def.Skill);
                    if (pv == null) continue;
                    client.SendPackage(NetPackageManager.GetPackage<NetPackageEntitySetSkillLevelClient>().Setup(player.entityId, def.Skill, pv.Level));
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] PushAllSkillsToClient error: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(EntityAlive), "AwardKill")]
    public static class PatchAwardKill
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(EntityAlive __instance, EntityAlive killer)
        {
            try
            {
                KillXp.OnKill(__instance, killer);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] AwardKill patch error: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(LootContainer), "SpawnItem")]
    public static class PatchLootQuality
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(List<ItemStack> spawnedItems, EntityPlayer player)
        {
            if (player == null || player.Progression == null) return;
            if (spawnedItems == null || spawnedItems.Count == 0) return;
            try
            {
                var stack = spawnedItems[spawnedItems.Count - 1];
                if (stack == null || stack.IsEmpty()) return;
                var iv = stack.itemValue;
                if (iv == null || !iv.HasQuality || iv.Quality == 0 || iv.Quality > 6) return;
                var def = DsConfig.Instance.GetSkillDefForTags(iv.ItemClass.ItemTags);
                if (def == null) return;
                var pv = player.Progression.GetProgressionValue(def.Skill);
                if (pv == null) return;
                int lvl = pv.Level;
                if (lvl <= 0) return;
                int q = iv.Quality * 100 + (int)(lvl * DsConfig.Instance.LootQualityBonusPerSkill);
                iv.Quality = (ushort)Math.Max(1, Math.Min(600, q));
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] Loot quality patch error: " + e);
            }
        }
    }

    /// <summary>
    /// Use-based skill XP. Two grant types:
    ///  - Block DESTROYED (tree fell, stone pile broke, wrenching finished): guaranteed
    ///    grant for tool skills (ToolDestroyChance, default 1.0) — one kill's progress
    ///    per block, capped only by how fast blocks can break.
    ///  - Successful USE (block hit that didn't destroy, entity hit, repair/upgrade
    ///    action): chance roll (ToolUseChance / WeaponUseChance) + per-player+skill
    ///    cooldown so augers/chainsaws stay sane.
    /// On a dedicated server block interactions are client-reported (the client DLL
    /// sends NetPackageDSWMUseXp — v1.0 never runs ItemActionAttack.Hit/ItemActionRepair
    /// server-side for remote players); entity hits and SP/listen-server block hits
    /// arrive via the server-side hooks below. Everything is rolled and granted here,
    /// server-side, so clients can't farm skills.
    /// </summary>
    public static class UseXp
    {
        private static readonly Dictionary<string, double> LastGrant = new Dictionary<string, double>();

        /// <summary>Destroy grants are naturally bounded by block break speed; this tiny
        /// hard cap just keeps a hostile client from spamming fake destroy reports.</summary>
        private const double DestroyCooldownSeconds = 0.25;

        /// <summary>Entity hit (server-side DamageEntity hook): chance roll, no destroy event.</summary>
        public static void OnUse(EntityPlayer player)
        {
            RollGrant(player, null, false);
        }

        /// <summary>Block interaction on a non-remote world (SP/listen host): tool swing
        /// that damaged a block, or a repair/upgrade action. Destroyed = the block broke.</summary>
        public static void OnBlockHit(EntityPlayer player, bool destroyed)
        {
            RollGrant(player, null, destroyed);
        }

        /// <summary>Use report from the client DLL (dedicated server). The skill name came
        /// over the wire — re-validate it against the player's held item before granting.</summary>
        public static void OnReportedUse(EntityPlayer player, string skill, bool destroyed)
        {
            RollGrant(player, skill, destroyed);
        }

        private static void RollGrant(EntityPlayer player, string reportedSkill, bool destroyed)
        {
            try
            {
                if (player == null || player.Progression == null || player.inventory == null) return;
                var held = player.inventory.holdingItem;
                if (held == null) return;

                SkillDef def;
                if (reportedSkill != null)
                {
                    // wired reports: the skill must exist and the player's held item must
                    // actually map to it (client can't report a skill it isn't wielding)
                    def = DsConfig.Instance.GetSkillDefByName(reportedSkill);
                    if (def == null) return;
                    if (DsConfig.Instance.GetSkillDefForTags(held.ItemTags) != def) return;
                }
                else
                {
                    def = DsConfig.Instance.GetSkillDefForTags(held.ItemTags);
                    if (def == null)
                    {
                        LogUse(player, "held item '" + held.Name + "' maps to no weapon skill");
                        return;
                    }
                }

                if (destroyed)
                {
                    // the block broke: one grant per block, no chance roll (configurable)
                    if (!def.IsTool) return; // destroy grants are a tool perk
                    double dchance = DsConfig.Instance.ToolDestroyChance;
                    if (dchance <= 0.0) return;
                    if (UnityEngine.Random.value >= (float)dchance)
                    {
                        LogUse(player, "destroy roll failed for " + def.Skill);
                        return;
                    }
                    double now = Time.time;
                    string key = player.entityId + "|" + def.Skill + "|D";
                    if (LastGrant.TryGetValue(key, out var last) && now - last < DestroyCooldownSeconds)
                    {
                        LogUse(player, "destroy cooldown for " + def.Skill);
                        return;
                    }
                    LastGrant[key] = now;
                    LogUse(player, "block destroyed with '" + held.Name + "' -> " + def.Skill);
                    KillXp.GrantXp(player, def.Skill);
                    TryGrantRepairPractice(player, def, held);
                    return;
                }

                double chance = def.IsTool ? DsConfig.Instance.ToolUseChance : DsConfig.Instance.WeaponUseChance;
                if (chance <= 0.0) return;
                if (UnityEngine.Random.value >= (float)chance)
                {
                    LogUse(player, "roll failed for " + def.Skill + " (chance " + chance + ")");
                    return;
                }

                double now2 = Time.time;
                string key2 = player.entityId + "|" + def.Skill;
                if (LastGrant.TryGetValue(key2, out var last2) && now2 - last2 < DsConfig.Instance.UseXpCooldownSeconds)
                {
                    LogUse(player, "cooldown for " + def.Skill);
                    return;
                }
                LastGrant[key2] = now2;

                LogUse(player, "granting use-XP to " + def.Skill + " (held '" + held.Name + "')");
                KillXp.GrantXp(player, def.Skill);
                TryGrantRepairPractice(player, def, held);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] UseXp error: " + e);
            }
        }

        /// <summary>
        /// Repair-practice catch-up: the claw hammer (repair skill 100) is the first
        /// craftable repair tool, but repairing is the only thing that levels the repair
        /// skill — a dead end. Until the repair skill reaches 100, using any other TOOL
        /// (pickaxe/axe/shovel/wrench/stone axe) also earns repair-skill progress; the
        /// stone axe repairing blocks counts too (its repair actions level harvesting,
        /// the practice grant adds the repair side). At 100 the claw hammer unlocks and
        /// only actual repair tools (claw hammer, nailgun) level the skill further.
        /// Piggybacks on the tool's own roll/cooldown — no extra farming rate.
        /// </summary>
        private static void TryGrantRepairPractice(EntityPlayer player, SkillDef usedDef, ItemClass held)
        {
            try
            {
                if (!usedDef.IsTool) return;
                if (usedDef.Skill == "craftingRepairTools") return;
                var repairDef = DsConfig.Instance.GetSkillDefByName("craftingRepairTools");
                if (repairDef == null) return;
                var pv = player.Progression.GetProgressionValue(repairDef.Skill);
                if (pv == null || pv.Level >= 100) return;
                if (DsConfig.Instance.DebugLogging)
                    Log.Out("[DSWM][use] " + player.EntityName + ": repair practice (" + repairDef.Skill +
                            " -> level " + (pv.Level + 1) + ") from '" + held.Name + "'");
                KillXp.GrantXp(player, repairDef.Skill);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] Repair practice error: " + e);
            }
        }

        private static void LogUse(EntityPlayer player, string msg)
        {
            if (DsConfig.Instance.DebugLogging) Log.Out("[DSWM][use] " + player.EntityName + ": " + msg);
        }
    }

    /// <summary>
    /// v1.0 note: Block.DamageBlock receives _entityIdThatDamaged = -1 for player melee/tool
    /// swings (ItemActionAttack.Hit passes ownedEntityId = -1), so a DamageBlock postfix can
    /// never identify the attacker. Hook ItemActionAttack.Hit instead: it carries the real
    /// attacker entity id (_attackerEntityId) and whether the ray hit a block (bBlockHit).
    /// Runs on the server for remote players (Hit is the server-side damage simulation).
    /// </summary>
    [HarmonyPatch(typeof(ItemActionAttack), "Hit")]
    public static class PatchAttackHitUse
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(int _attackerEntityId, ItemActionAttack.AttackHitInfo _attackDetails)
        {
            try
            {
                if (_attackDetails == null || !_attackDetails.bBlockHit) return;
                if (_attackerEntityId == -1) return;
                if (GameManager.Instance == null || GameManager.Instance.World == null) return;
                if (GameManager.Instance.World.IsRemote()) return; // server-side only
                var player = GameManager.Instance.World.GetEntity(_attackerEntityId) as EntityPlayer;
                if (player == null) return;
                if (DsConfig.Instance.DebugLogging)
                    Log.Out("[DSWM][use] block hit by " + player.EntityName + " (entity " + _attackerEntityId + ", destroyed " + _attackDetails.bKilled + ")");
                UseXp.OnBlockHit(player, _attackDetails.bKilled);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] AttackHit use-XP error: " + e);
            }
        }
    }

    /// <summary>
    /// Repair tools (claw hammer, nailgun) level from repairing/upgrading blocks. Actual
    /// repair/upgrade applications are Block.DamageBlock calls with NEGATIVE damage
    /// (-repairAmount for a repair, -1 for a completed upgrade step) — hooking that (not
    /// ItemActionRepair.ExecuteAction, which also fires on failed right-clicks) counts
    /// only actions that really went through. On a dedicated server this never fires for
    /// remote players (repair is simulated client-side) — the client DLL reports instead.
    /// </summary>
    [HarmonyPatch(typeof(Block), "DamageBlock")]
    public static class PatchBlockDamageRepair
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(int _damagePoints, int _entityIdThatDamaged)
        {
            try
            {
                if (_damagePoints >= 0) return; // only repair/upgrade actions (negative damage)
                if (_entityIdThatDamaged == -1) return;
                if (GameManager.Instance == null || GameManager.Instance.World == null) return;
                if (GameManager.Instance.World.IsRemote()) return; // server-side only
                var player = GameManager.Instance.World.GetEntity(_entityIdThatDamaged) as EntityPlayer;
                if (player == null) return;
                if (DsConfig.Instance.DebugLogging)
                    Log.Out("[DSWM][use] repair/upgrade action by " + player.EntityName);
                UseXp.OnBlockHit(player, false);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] Repair use-XP error: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(EntityAlive), "DamageEntity")]
    public static class PatchEntityDamageUse
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(EntityAlive __instance, DamageSource _damageSource)
        {
            try
            {
                if (_damageSource == null || _damageSource.getEntityId() == -1) return;
                if (GameManager.Instance == null || GameManager.Instance.World == null) return;
                if (GameManager.Instance.World.IsRemote()) return;
                // only count hits the player landed (not damage the victim received)
                var attacker = GameManager.Instance.World.GetEntity(_damageSource.getEntityId()) as EntityPlayer;
                if (attacker == null) return;
                // ignore self-damage
                if (attacker.entityId == __instance.entityId) return;
                UseXp.OnUse(attacker);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] EntityDamage use-XP error: " + e);
            }
        }
    }
}
