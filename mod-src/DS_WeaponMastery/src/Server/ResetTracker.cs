using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>Tracks which players have already had their weapon skills reset.</summary>
    public static class ResetTracker
    {
        private static string _file;
        private static HashSet<string> _processed = new HashSet<string>();

        public static void Init(string modFolder)
        {
            try
            {
                _file = Path.Combine(modFolder, "DSResetDone.txt");
                if (File.Exists(_file))
                {
                    foreach (var line in File.ReadAllLines(_file))
                    {
                        var id = line.Trim();
                        if (id.Length > 0) _processed.Add(id);
                    }
                }
                Log.Out("[DSWM] Reset tracker: " + _processed.Count + " players already processed");
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] ResetTracker.Init error: " + e);
            }
        }

        public static bool IsProcessed(string id) => _processed.Contains(id);

        public static void Mark(string id)
        {
            _processed.Add(id);
            try
            {
                if (_file != null) File.AppendAllText(_file, id + "\n");
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] ResetTracker.Mark error: " + e);
            }
        }

        public static void ClearAll()
        {
            _processed.Clear();
            try
            {
                if (_file != null && File.Exists(_file)) File.Delete(_file);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] ResetTracker.ClearAll error: " + e);
            }
        }
    }

    /// <summary>Reset weapon skills to 1 for every player on their first login after deploy.</summary>
    [HarmonyPatch(typeof(GameManager), "PlayerSpawnedInWorld")]
    public static class PatchPlayerSpawnedReset
    {
        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(ClientInfo _cInfo, int _entityId)
        {
            try
            {
                if (GameManager.Instance == null || GameManager.Instance.World == null) return;
                var player = GameManager.Instance.World.GetEntity(_entityId) as EntityPlayer;
                if (player == null) return;

                if (DsConfig.Instance.ResetOnFirstLogin && _cInfo != null && _cInfo.PlatformId != null)
                {
                    string id = _cInfo.PlatformId.CombinedString;
                    if (!ResetTracker.IsProcessed(id))
                    {
                        KillXp.ResetPlayerWeaponSkills(player);
                        ResetTracker.Mark(id);
                        Log.Out("[DSWM] Weapon skills reset for " + id + " (" + _cInfo.playerName + ")");
                    }
                }

                // The client's local Progression is authoritative for its crafting UI, so
                // on every spawn push the server's current weapon-skill levels to the
                // client (covers both the post-reset state and pre-existing progress).
                KillXp.PushAllSkillsToClient(player);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] Reset patch error: " + e);
            }
        }
    }
}
