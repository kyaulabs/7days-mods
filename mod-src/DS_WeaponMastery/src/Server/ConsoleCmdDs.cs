using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>ds commands: ds reset, ds set &lt;player&gt; &lt;skill&gt; &lt;level&gt;, ds xp &lt;player&gt; &lt;skill&gt; &lt;points&gt;, ds info [player]</summary>
    [Preserve]
    public class ConsoleCmdDs : ConsoleCmdAbstract
    {
        public override string[] getCommands() => new[] { "ds" };

        public override string getDescription() => "DS Weapon Mastery commands";

        public override string GetHelp()
        {
            return "ds reset - reset all players' weapon skills to 1 (also re-arms auto reset on next login)\n" +
                   "ds set <player> <skill> <level> - set a player's weapon skill level (craftingBows, craftingHandguns, ...)\n" +
                   "ds xp <player> <skill> <points> - grant kill-XP points to a player's weapon skill\n" +
                   "ds info [player] - show weapon skill levels";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            if (_params.Count == 0)
            {
                SdtdConsole.Instance.Output(GetHelp());
                return;
            }
            try
            {
                switch (_params[0].ToLower())
                {
                    case "reset":
                    {
                        int count = 0;
                        foreach (var p in GameManager.Instance.World.Players.list)
                        {
                            if (p is EntityPlayer ep)
                            {
                                KillXp.ResetPlayerWeaponSkills(ep);
                                count++;
                            }
                        }
                        ResetTracker.ClearAll();
                        SdtdConsole.Instance.Output("[DSWM] Reset weapon skills of " + count + " online players. Offline players will reset on next login.");
                        Log.Out("[DSWM] Manual reset executed for " + count + " online players");
                        break;
                    }
                    case "set":
                    {
                        if (_params.Count < 4) { SdtdConsole.Instance.Output("Usage: ds set <player> <skill> <level>"); return; }
                        var player = FindPlayer(_params[1]);
                        if (player == null) { SdtdConsole.Instance.Output("Player not found: " + _params[1]); return; }
                        if (DsConfig.Instance.GetSkillDefByName(_params[2]) == null) { SdtdConsole.Instance.Output("Unknown skill: " + _params[2]); return; }
                        if (!int.TryParse(_params[3], out var level)) { SdtdConsole.Instance.Output("Invalid level: " + _params[3]); return; }
                        KillXp.SetSkillLevel(player, _params[2], level);
                        SdtdConsole.Instance.Output("[DSWM] Set " + player.EntityName + " " + _params[2] + " to " + level);
                        break;
                    }
                    case "xp":
                    {
                        if (_params.Count < 4) { SdtdConsole.Instance.Output("Usage: ds xp <player> <skill> <points>"); return; }
                        var player = FindPlayer(_params[1]);
                        if (player == null) { SdtdConsole.Instance.Output("Player not found: " + _params[1]); return; }
                        if (!int.TryParse(_params[3], out var points)) { SdtdConsole.Instance.Output("Invalid points: " + _params[3]); return; }
                        for (int i = 0; i < points; i++) KillXp.GrantXp(player, _params[2]);
                        SdtdConsole.Instance.Output("[DSWM] Granted " + points + " kill(s) to " + player.EntityName + " " + _params[2]);
                        break;
                    }
                    case "info":
                    {
                        EntityPlayer target = null;
                        if (_params.Count > 1)
                        {
                            target = FindPlayer(_params[1]);
                            if (target == null) { SdtdConsole.Instance.Output("Player not found: " + _params[1]); return; }
                        }
                        foreach (var p in GameManager.Instance.World.Players.list)
                        {
                            if (p is EntityPlayer ep && (target == null || ep == target))
                            {
                                var line = new List<string> { "[DSWM] " + ep.EntityName + ":" };
                                foreach (var def in DsConfig.Instance.Skills)
                                {
                                    var pv = ep.Progression?.GetProgressionValue(def.Skill);
                                    if (pv != null) line.Add(def.Skill + "=" + pv.Level);
                                }
                                SdtdConsole.Instance.Output(string.Join(" ", line));
                            }
                        }
                        break;
                    }
                    default:
                        SdtdConsole.Instance.Output(GetHelp());
                        break;
                }
            }
            catch (Exception e)
            {
                SdtdConsole.Instance.Output("[DSWM] Error: " + e.Message);
                Log.Error("[DSWM] Command error: " + e);
            }
        }

        private static EntityPlayer FindPlayer(string nameOrId)
        {
            foreach (var p in GameManager.Instance.World.Players.list)
            {
                if (p is EntityPlayer ep)
                {
                    if (ep.EntityName.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)) return ep;
                    if (ep.entityId.ToString() == nameOrId) return ep;
                }
            }
            return null;
        }
    }
}
