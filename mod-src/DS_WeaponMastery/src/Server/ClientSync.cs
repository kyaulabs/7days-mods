using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>
    /// v1.0 dedicated servers run a client-authoritative progression model: the client
    /// pushes its Progression to the server (NetPackagePlayerStats) and its PlayerDataFile
    /// (NetPackagePlayerData) every ~30s, and the server mirrors/saves what it receives.
    /// The mod grants weapon-skill XP server-side (KillXp) and pushes the new levels to the
    /// client via NetPackageEntitySetSkillLevelClient, but the client's pushes can still
    /// clobber the server's authoritative state with stale copies. These patches keep the
    /// server (and the save it writes) authoritative for the mod's weapon skills.
    /// </summary>
    public static class ClientSyncGuard
    {
        public struct SkillState
        {
            public string Skill;
            public int Level;
            public int Cost;
        }

        // weapon-skill state of an entity captured before a client stats push replaces
        // its Progression; restored afterwards so the client's stale copy can't win
        internal static readonly Dictionary<int, List<SkillState>> Captured = new Dictionary<int, List<SkillState>>();

        public static List<SkillState> Capture(EntityPlayer player)
        {
            var list = new List<SkillState>();
            if (player == null || player.Progression == null) return list;
            foreach (var def in DsConfig.Instance.Skills)
            {
                var pv = player.Progression.GetProgressionValue(def.Skill);
                if (pv == null) continue;
                list.Add(new SkillState { Skill = def.Skill, Level = pv.Level, Cost = pv.CostForNextLevel });
            }
            return list;
        }

        public static void Restore(EntityPlayer player, List<SkillState> states)
        {
            if (player == null || player.Progression == null || states == null || states.Count == 0) return;
            foreach (var s in states)
            {
                var pv = player.Progression.GetProgressionValue(s.Skill);
                if (pv == null) continue;
                pv.Level = s.Level;
                pv.CostForNextLevel = s.Cost;
            }
            // make the server re-broadcast the corrected progression to other clients
            player.Progression.bProgressionStatsChanged = true;
            player.bPlayerStatsChanged = true;
        }
    }

    /// <summary>
    /// When a client pushes its stats (NetPackagePlayerStats) the server replaces the
    /// entity's whole Progression with the client's bytes. The client's copy of the mod's
    /// weapon skills is stale (it only learns about level-ups via the pushes from
    /// KillXp), so restore the server's authoritative weapon-skill state afterwards.
    /// Server-side only; client-side ToEntity calls (mirroring other players) are untouched.
    /// </summary>
    [HarmonyPatch(typeof(EntityAlive.EntityNetworkStats), "ToEntity")]
    public static class PatchEntityNetworkStatsToEntity
    {
        [HarmonyPrefix]
        [Preserve]
        public static void Prefix(EntityAlive _entity)
        {
            try
            {
                if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
                if (!(_entity is EntityPlayer ep) || ep.Progression == null) return;
                ClientSyncGuard.Captured[ep.entityId] = ClientSyncGuard.Capture(ep);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] ToEntity guard prefix error: " + e);
            }
        }

        [HarmonyPostfix]
        [Preserve]
        public static void Postfix(EntityAlive _entity)
        {
            try
            {
                if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
                if (!(_entity is EntityPlayer ep) || ep.Progression == null) return;
                if (ClientSyncGuard.Captured.TryGetValue(ep.entityId, out var states))
                {
                    ClientSyncGuard.Captured.Remove(ep.entityId);
                    ClientSyncGuard.Restore(ep, states);
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] ToEntity guard postfix error: " + e);
            }
        }
    }

    /// <summary>
    /// The client periodically pushes its PlayerDataFile (NetPackagePlayerData) and the
    /// server saves it verbatim — a stale client copy would overwrite the server's save
    /// with level-1 weapon skills. Rewrite the weapon-skill entries in the incoming
    /// progression blob from the live server entity before it hits disk.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "SavePlayerData")]
    public static class PatchGameManagerSavePlayerData
    {
        [HarmonyPrefix]
        [Preserve]
        public static void Prefix(GameManager __instance, ClientInfo _cInfo, PlayerDataFile _playerDataFile)
        {
            try
            {
                if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
                if (_cInfo == null || _playerDataFile == null) return;
                if (__instance.World == null) return;
                var entity = __instance.World.GetEntity(_cInfo.entityId) as EntityPlayer;
                if (entity == null || entity.Progression == null) return;
                var blob = _playerDataFile.progressionData;
                if (blob == null || blob.Length == 0) return;
                var patched = ProgressionBlob.RewriteWeaponSkills(blob.ToArray(), entity);
                if (patched != null)
                {
                    _playerDataFile.progressionData = new MemoryStream(patched);
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] SavePlayerData guard prefix error: " + e);
            }
        }
    }

    /// <summary>
    /// Parse/serialize the Progression blob (Progression.Write format: byte 3 version,
    /// u16 char level, i32 exp, u16 skill points, i32 count, then entries, then i32
    /// expDeficit). Entries may be v1 (byte level, vanilla client) or v2 (ushort level,
    /// mod client — see Shared/ProgressionValueSerialization.cs); output is always v2.
    /// </summary>
    public static class ProgressionBlob
    {
        public static byte[] RewriteWeaponSkills(byte[] data, EntityPlayer source)
        {
            try
            {
                int pos = 0;
                if (data == null || data.Length < 14 || data[pos] != 3) return null;
                pos++; // progression version
                pos += 2 + 4 + 2; // char level, expToNext, skillPoints
                int count = ReadI32(data, ref pos);
                if (count < 0 || count > 10000) return null;

                bool changed = false;
                using (var ms = new MemoryStream(data.Length + 64))
                using (var w = new BinaryWriter(ms))
                {
                    w.Write((byte)3);
                    w.Write(data, 1, 2 + 4 + 2 + 4); // char level + expToNext + skillPoints + count
                    for (int i = 0; i < count; i++)
                    {
                        if (pos >= data.Length) return null;
                        byte ver = data[pos++];
                        string name = ReadString(data, ref pos);
                        if (name == null) return null;
                        int level;
                        int cost;
                        if (ver >= 2)
                        {
                            if (pos + 6 > data.Length) return null;
                            level = ReadU16(data, ref pos);
                            cost = ReadI32(data, ref pos);
                        }
                        else
                        {
                            if (pos + 5 > data.Length) return null;
                            level = data[pos++];
                            cost = ReadI32(data, ref pos);
                        }
                        if (DsConfig.Instance.GetSkillDefByName(name) != null)
                        {
                            var pv = source.Progression.GetProgressionValue(name);
                            if (pv != null)
                            {
                                int newLevel = Math.Max(1, Math.Min(65535, pv.Level));
                                int newCost = pv.CostForNextLevel;
                                if (newLevel != level || newCost != cost) changed = true;
                                level = newLevel;
                                cost = newCost;
                            }
                        }
                        w.Write((byte)2);
                        WriteString(w, name);
                        w.Write((ushort)level);
                        w.Write(cost);
                    }
                    if (pos + 4 > data.Length) return null;
                    w.Write(ReadI32(data, ref pos)); // expDeficit
                    w.Flush();
                    return changed ? ms.ToArray() : null;
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] ProgressionBlob rewrite error: " + e);
                return null;
            }
        }

        private static string ReadString(byte[] d, ref int p)
        {
            int len = 0, shift = 0;
            while (true)
            {
                if (p >= d.Length) return null;
                byte b = d[p++];
                len |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 35) return null;
            }
            if (len < 0 || p + len > d.Length) return null;
            var s = System.Text.Encoding.UTF8.GetString(d, p, len);
            p += len;
            return s;
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            int len = bytes.Length;
            while (len >= 0x80)
            {
                w.Write((byte)((len & 0x7f) | 0x80));
                len >>= 7;
            }
            w.Write((byte)len);
            w.Write(bytes);
        }

        private static int ReadU16(byte[] d, ref int p)
        {
            if (p + 2 > d.Length) throw new EndOfStreamException();
            int v = d[p] | (d[p + 1] << 8);
            p += 2;
            return v;
        }

        private static int ReadI32(byte[] d, ref int p)
        {
            if (p + 4 > d.Length) throw new EndOfStreamException();
            int v = d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24);
            p += 4;
            return v;
        }
    }
}
