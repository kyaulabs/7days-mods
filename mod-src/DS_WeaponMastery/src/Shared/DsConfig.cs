using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace DSWeaponMastery
{
    public class SkillDef
    {
        public string Skill;   // e.g. craftingBows
        public string[] Tags;  // e.g. ["bowSkill"]
        public string Buff;    // e.g. buffDSFocusBows
        public FastTags<TagGroup.Global>[] TagFasts;
        public bool IsTool;    // tools level by use only
    }

    public class DsConfig
    {
        public static DsConfig Instance = new DsConfig();

        // Kill XP curve: kills per level = Start + (Max - Start) * ((level-1)/599)^Power
        public double KillsPerLevelStart = 1.0;
        public double KillsPerLevelMax = 200.0;
        public double CurvePower = 30.0;
        public double StudyBuffMultiplier = 2.0;

        // Loot: found item quality = base*100 + skill*LootQualityBonusPerSkill (clamped 1..600)
        public double LootQualityBonusPerSkill = 0.5;

        // Use XP: chance per successful use (block hit / entity hit)
        public double WeaponUseChance = 0.05;  // weapons: small chance, kills are the main source
        public double ToolUseChance = 0.03;    // tools: small per-hit chance (2.5-5% range)
        public double ToolDestroyChance = 1.0; // tools: chance to grant when a block is destroyed (1.0 = every block)
        public double UseXpCooldownSeconds = 1.0; // min time between per-use grants per player+skill

        // Announce a player's weapon skill level-ups to THEM via chat, every N levels
        // (25, 50, 75, ...). 0 disables announcements.
        public int LevelUpAnnounceInterval = 25;

        // Diagnostics: log every use-XP roll/grant (server log)
        public bool DebugLogging = false;

        // Reset skills for every player on their first login after deploy
        public bool ResetOnFirstLogin = true;

        public List<SkillDef> Skills = new List<SkillDef>();

        public const int MaxLevel = 600;
        public const int ExpPerLevel = 1000; // must match base_exp_cost in Progression.xml

        public static void Load(string path)
        {
            var cfg = new DsConfig();
            try
            {
                if (File.Exists(path))
                {
                    var doc = XDocument.Load(path);
                    var root = doc.Root;
                    if (root != null)
                    {
                        cfg.KillsPerLevelStart = GetDouble(root, "KillsPerLevelStart", cfg.KillsPerLevelStart);
                        cfg.KillsPerLevelMax = GetDouble(root, "KillsPerLevelMax", cfg.KillsPerLevelMax);
                        cfg.CurvePower = GetDouble(root, "CurvePower", cfg.CurvePower);
                        cfg.StudyBuffMultiplier = GetDouble(root, "StudyBuffMultiplier", cfg.StudyBuffMultiplier);
                        cfg.LootQualityBonusPerSkill = GetDouble(root, "LootQualityBonusPerSkill", cfg.LootQualityBonusPerSkill);
                        cfg.WeaponUseChance = GetDouble(root, "WeaponUseChance", cfg.WeaponUseChance);
                        cfg.ToolUseChance = GetDouble(root, "ToolUseChance", cfg.ToolUseChance);
                        cfg.ToolDestroyChance = GetDouble(root, "ToolDestroyChance", cfg.ToolDestroyChance);
                        cfg.UseXpCooldownSeconds = GetDouble(root, "UseXpCooldownSeconds", cfg.UseXpCooldownSeconds);
                        cfg.LevelUpAnnounceInterval = GetInt(root, "LevelUpAnnounceInterval", cfg.LevelUpAnnounceInterval);
                        cfg.DebugLogging = GetBool(root, "DebugLogging", cfg.DebugLogging);
                        cfg.ResetOnFirstLogin = GetBool(root, "ResetOnFirstLogin", cfg.ResetOnFirstLogin);
                        foreach (var el in root.Descendants("Skill"))
                        {
                            var skill = el.Attribute("skill")?.Value;
                            var tagAttr = el.Attribute("tag")?.Value ?? el.Attribute("tags")?.Value;
                            var buff = el.Attribute("buff")?.Value;
                            var toolAttr = el.Attribute("tool");
                            if (skill != null && tagAttr != null)
                            {
                                var tags = tagAttr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                var fasts = new FastTags<TagGroup.Global>[tags.Length];
                                for (int i = 0; i < tags.Length; i++) fasts[i] = FastTags<TagGroup.Global>.Parse(tags[i].Trim());
                                cfg.Skills.Add(new SkillDef
                                {
                                    Skill = skill,
                                    Tags = tags,
                                    Buff = buff,
                                    TagFasts = fasts,
                                    IsTool = toolAttr != null && string.Equals(toolAttr?.Value, "true", StringComparison.OrdinalIgnoreCase)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] Failed to load DSConfig.xml: " + e);
            }
            if (cfg.Skills.Count == 0)
            {
                cfg.Skills = DefaultSkills();
            }
            Instance = cfg;
            Log.Out("[DSWM] Config loaded: kills/level " + cfg.KillsPerLevelStart + "->" + cfg.KillsPerLevelMax +
                    " power " + cfg.CurvePower + ", loot bonus " + cfg.LootQualityBonusPerSkill +
                    ", use chance weapon " + cfg.WeaponUseChance + " tool " + cfg.ToolUseChance +
                    ", destroy chance " + cfg.ToolDestroyChance +
                    ", cooldown " + cfg.UseXpCooldownSeconds + ", announce every " + cfg.LevelUpAnnounceInterval + " levels" +
                    ", resetOnFirstLogin " + cfg.ResetOnFirstLogin +
                    ", skills " + cfg.Skills.Count);
        }

        private static double GetDouble(XElement root, string name, double def)
        {
            // Descendants (not Element): the XML groups values under <KillXp>/<UseXp>/<Loot>
            // and the original root.Element() lookups silently matched nothing — the file
            // was decorative and every value fell back to its code default.
            var el = root.Descendants(name).FirstOrDefault();
            if (el == null || string.IsNullOrEmpty(el.Value)) return def;
            return double.TryParse(el.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static bool GetBool(XElement root, string name, bool def)
        {
            var el = root.Descendants(name).FirstOrDefault();
            if (el == null || string.IsNullOrEmpty(el.Value)) return def;
            return bool.TryParse(el.Value, out var v) ? v : def;
        }

        private static int GetInt(XElement root, string name, int def)
        {
            var el = root.Descendants(name).FirstOrDefault();
            if (el == null || string.IsNullOrEmpty(el.Value)) return def;
            return int.TryParse(el.Value, out var v) ? v : def;
        }

        private static List<SkillDef> DefaultSkills()
        {
            var list = new List<SkillDef>();
            void Add(string skill, string tags, string buff, bool tool)
            {
                var tagArr = tags.Split(',');
                var fasts = new FastTags<TagGroup.Global>[tagArr.Length];
                for (int i = 0; i < tagArr.Length; i++) fasts[i] = FastTags<TagGroup.Global>.Parse(tagArr[i].Trim());
                list.Add(new SkillDef { Skill = skill, Tags = tagArr, Buff = buff, TagFasts = fasts, IsTool = tool });
            }
            Add("craftingKnuckles", "knuckleSkill", "buffDSFocusKnuckles", false);
            Add("craftingBlades", "bladeSkill", "buffDSFocusBlades", false);
            Add("craftingClubs", "clubSkill", "buffDSFocusClubs", false);
            Add("craftingSledgehammers", "sledgeSkill", "buffDSFocusSledgehammers", false);
            Add("craftingSpears", "spearSkill", "buffDSFocusSpears", false);
            Add("craftingBows", "bowSkill", "buffDSFocusBows", false);
            Add("craftingHandguns", "handgunSkill", "buffDSFocusHandguns", false);
            Add("craftingShotguns", "shotgunSkill", "buffDSFocusShotguns", false);
            Add("craftingRifles", "rifleSkill", "buffDSFocusRifles", false);
            Add("craftingMachineGuns", "machinegunSkill", "buffDSFocusMachineguns", false);
            Add("craftingExplosives", "explosivesSkill", "buffDSFocusExplosives", false);
            Add("craftingRobotics", "roboticsSkill", "buffDSFocusRobotics", false);
            Add("craftingHarvestingTools", "harvestingSkill", "buffDSFocusHarvestingTools", true);
            Add("craftingSalvageTools", "salvagingSkill", "buffDSFocusSalvageTools", true);
            Add("craftingRepairTools", "repairingSkill,repairingTools", "buffDSFocusRepairTools", true);
            return list;
        }

        public SkillDef GetSkillDefForTags(FastTags<TagGroup.Global> tags)
        {
            if (tags.IsEmpty) return null;
            for (int i = 0; i < Skills.Count; i++)
            {
                var def = Skills[i];
                for (int j = 0; j < def.TagFasts.Length; j++)
                {
                    if (tags.Test_AnySet(def.TagFasts[j])) return def;
                }
            }
            return null;
        }

        public string GetBuffForSkill(string skill)
        {
            for (int i = 0; i < Skills.Count; i++)
            {
                if (Skills[i].Skill == skill) return Skills[i].Buff;
            }
            return null;
        }

        public SkillDef GetSkillDefByName(string skill)
        {
            for (int i = 0; i < Skills.Count; i++)
            {
                if (Skills[i].Skill == skill) return Skills[i];
            }
            return null;
        }

        /// <summary>Kills required to advance from the given level to the next.</summary>
        public double KillsPerLevel(int level)
        {
            double t = (level - 1) / 599.0;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return KillsPerLevelStart + (KillsPerLevelMax - KillsPerLevelStart) * Math.Pow(t, CurvePower);
        }
    }
}
