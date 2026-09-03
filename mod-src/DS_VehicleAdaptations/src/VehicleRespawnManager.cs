using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace VehicleAdaptations
{
    public static class VehicleRespawnManager
    {
        private const ulong TicksPerGameDay = 24000UL;
        private const string StateFileName = "VehicleAdaptationsRespawns.xml";

        private sealed class Entry
        {
            public Vector3i Position;
            public string BlockName;
            public byte Rotation;
            public byte Meta;
            public byte Meta2;
            public byte Meta3;
            public ulong DestroyedWorldTime;
        }

        private static readonly Dictionary<Vector3i, Entry> Entries =
            new Dictionary<Vector3i, Entry>();
        private static bool eventsRegistered;
        private static bool loaded;
        private static DateTime nextCheckUtc;
        private static string statePath;

        public static void RegisterEvents()
        {
            if (eventsRegistered) return;
            eventsRegistered = true;
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.GameUpdate.RegisterHandler(OnGameUpdate);
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
        }

        public static void RecordDestroyed(TEFeatureVehicleAdaptation feature, World world)
        {
            if (feature == null || world == null || world.IsRemote() || !feature.RespawnEligible)
                return;

            int days = GamePrefs.GetInt(EnumGamePrefs.LootRespawnDays);
            if (days <= 0)
            {
                if (VehicleAdaptationsConfig.Instance.DebugLogging)
                    Log.Out("[VehicleAdaptations] Vehicle regeneration is disabled with loot respawn.");
                return;
            }

            EnsureLoaded();
            Vector3i position = feature.Parent.ToWorldPos();
            Entries[position] = new Entry
            {
                Position = position,
                BlockName = feature.OriginalBlockName,
                Rotation = feature.OriginalRotation,
                Meta = feature.OriginalMeta,
                Meta2 = feature.OriginalMeta2,
                Meta3 = feature.OriginalMeta3,
                DestroyedWorldTime = world.GetWorldTime()
            };
            Save();
            Log.Out("[VehicleAdaptations] Scheduled " + feature.OriginalBlockName + " at " +
                    position + " to regenerate after " + days + " game day(s).");
        }

        private static void OnGameStartDone(ref ModEvents.SGameStartDoneData data)
        {
            World world = GameManager.Instance?.World;
            if (world == null || world.IsRemote()) return;
            loaded = false;
            Entries.Clear();
            EnsureLoaded();
            nextCheckUtc = DateTime.UtcNow;
            Log.Out("[VehicleAdaptations] Loaded " + Entries.Count +
                    " pending vehicle regeneration record(s).");
        }

        private static void OnGameUpdate(ref ModEvents.SGameUpdateData data)
        {
            if (DateTime.UtcNow < nextCheckUtc) return;
            nextCheckUtc = DateTime.UtcNow.AddSeconds(1);

            World world = GameManager.Instance?.World;
            if (world == null || world.IsRemote()) return;
            EnsureLoaded();

            int lootRespawnDays = GamePrefs.GetInt(EnumGamePrefs.LootRespawnDays);
            if (lootRespawnDays <= 0 || Entries.Count == 0) return;

            ulong now = world.GetWorldTime();
            ulong interval = (ulong)lootRespawnDays * TicksPerGameDay;
            var completed = new List<Vector3i>();
            foreach (Entry entry in Entries.Values.ToArray())
            {
                if (now < entry.DestroyedWorldTime + interval) continue;

                BlockValue value = Block.GetBlockValue(entry.BlockName);
                if (value.isair)
                {
                    Log.Warning("[VehicleAdaptations] Cannot regenerate unknown block " +
                                entry.BlockName + " at " + entry.Position + ".");
                    continue;
                }

                value.rotationAndMeta3 = (byte)((entry.Rotation & 31) | ((entry.Meta3 & 1) << 5));
                value.meta = entry.Meta;
                value.meta2 = entry.Meta2;
                value.damage = 0;
                if (!CanRegenerate(world, entry, value)) continue;
                world.SetBlockRPC(entry.Position, value);

                if (world.GetBlock(entry.Position).type == value.type)
                {
                    completed.Add(entry.Position);
                    Log.Out("[VehicleAdaptations] Regenerated " + entry.BlockName +
                            " at " + entry.Position + ".");
                }
            }

            if (completed.Count == 0) return;
            foreach (Vector3i position in completed)
                Entries.Remove(position);
            Save();
        }

        private static bool CanRegenerate(World world, Entry entry, BlockValue value)
        {
            if (!CellIsLoadedAndAir(world, entry.Position)) return false;
            if (value.Block.isMultiBlock)
            {
                for (int i = 0; i < value.Block.multiBlockPos.Length; i++)
                {
                    Vector3i cell = entry.Position +
                        value.Block.multiBlockPos.Get(i, value.type, value.rotation);
                    if (!CellIsLoadedAndAir(world, cell)) return false;
                }
            }

            float clearRadius = VehicleAdaptationsConfig.Instance.RespawnPlayerClearRadius;
            float clearRadiusSq = clearRadius * clearRadius;
            Vector3 center = entry.Position.ToVector3Center();
            foreach (EntityPlayer player in world.Players.list)
            {
                if (player != null && (player.position - center).sqrMagnitude < clearRadiusSq)
                    return false;
            }
            return true;
        }

        private static bool CellIsLoadedAndAir(World world, Vector3i position)
        {
            return world.GetChunkSync(
                       World.toChunkXZ(position.x), World.toChunkXZ(position.z)) != null &&
                   world.GetBlock(position).isair;
        }

        private static void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData data)
        {
            World world = GameManager.Instance?.World;
            if (world != null && !world.IsRemote()) Save();
            loaded = false;
            Entries.Clear();
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Entries.Clear();
            statePath = Path.Combine(GameIO.GetSaveGameDir(), StateFileName);
            if (!File.Exists(statePath)) return;

            try
            {
                XElement root = XDocument.Load(statePath).Root;
                if (root == null) return;
                foreach (XElement node in root.Elements("vehicle"))
                {
                    var entry = new Entry
                    {
                        Position = new Vector3i(
                            ParseInt(node, "x"), ParseInt(node, "y"), ParseInt(node, "z")),
                        BlockName = (string)node.Attribute("block") ?? string.Empty,
                        Rotation = ParseByte(node, "rotation"),
                        Meta = ParseByte(node, "meta"),
                        Meta2 = ParseByte(node, "meta2"),
                        Meta3 = ParseByte(node, "meta3"),
                        DestroyedWorldTime = ParseUlong(node, "destroyed")
                    };
                    if (!string.IsNullOrEmpty(entry.BlockName))
                        Entries[entry.Position] = entry;
                }
            }
            catch (Exception e)
            {
                Log.Error("[VehicleAdaptations] Failed to load respawn state: " + e);
            }
        }

        private static void Save()
        {
            if (!loaded || string.IsNullOrEmpty(statePath)) return;
            try
            {
                var root = new XElement("vehicleAdaptationsRespawns",
                    new XAttribute("version", "1"),
                    Entries.Values
                        .OrderBy(e => e.Position.x)
                        .ThenBy(e => e.Position.z)
                        .ThenBy(e => e.Position.y)
                        .Select(e => new XElement("vehicle",
                            new XAttribute("x", e.Position.x),
                            new XAttribute("y", e.Position.y),
                            new XAttribute("z", e.Position.z),
                            new XAttribute("block", e.BlockName),
                            new XAttribute("rotation", e.Rotation),
                            new XAttribute("meta", e.Meta),
                            new XAttribute("meta2", e.Meta2),
                            new XAttribute("meta3", e.Meta3),
                            new XAttribute("destroyed", e.DestroyedWorldTime))));
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                string temporary = statePath + ".tmp";
                new XDocument(root).Save(temporary);
                if (File.Exists(statePath)) File.Delete(statePath);
                File.Move(temporary, statePath);
            }
            catch (Exception e)
            {
                Log.Error("[VehicleAdaptations] Failed to save respawn state: " + e);
            }
        }

        private static int ParseInt(XElement node, string name)
        {
            return int.TryParse((string)node.Attribute(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) ? value : 0;
        }

        private static byte ParseByte(XElement node, string name)
        {
            return byte.TryParse((string)node.Attribute(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out byte value) ? value : (byte)0;
        }

        private static ulong ParseUlong(XElement node, string name)
        {
            return ulong.TryParse((string)node.Attribute(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out ulong value) ? value : 0UL;
        }
    }
}
