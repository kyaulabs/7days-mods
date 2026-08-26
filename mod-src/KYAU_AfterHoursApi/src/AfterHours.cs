using System;
using System.Collections.Generic;
using UnityEngine.Scripting;
using Utf8Json;
using Webserver;
using Webserver.LiveData;
using Webserver.WebAPI;

namespace KYAU.AfterHours
{
    /// <summary>
    /// Mod entry point. The web API class below is auto-discovered by the
    /// game's web server (ReflectionHelpers.FindTypesImplementingBase), this
    /// IModApi just ensures the assembly gets loaded and logs that fact.
    /// </summary>
    public class AfterHoursApiMod : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            Log.Out("[AfterHoursApi] Loaded - public status endpoint at /api/afterhours");
        }
    }

    /// <summary>
    /// Public (anonymous, read-only) server status endpoint for the AfterHours
    /// community website. Exposes only safe fields: no IPs, no platform IDs,
    /// no tokens. GET /api/afterhours
    /// </summary>
    [Preserve]
    public class AfterHours : AbsRestApi
    {
        static readonly byte[] kServer = JsonWriter.GetEncodedPropertyNameWithBeginObject("server");
        static readonly byte[] kName = JsonWriter.GetEncodedPropertyNameWithBeginObject("name");
        static readonly byte[] kMaxPlayers = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("maxPlayers");
        static readonly byte[] kWorld = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("world");
        static readonly byte[] kVersion = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("version");
        static readonly byte[] kGameTime = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("gameTime");
        static readonly byte[] kBloodmoon = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("bloodmoon");
        static readonly byte[] kBmActive = JsonWriter.GetEncodedPropertyNameWithBeginObject("active");
        static readonly byte[] kBmDay = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("day");
        static readonly byte[] kBmStart = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("start");
        static readonly byte[] kBmEnd = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("end");
        static readonly byte[] kCounts = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("counts");
        static readonly byte[] kCountPlayers = JsonWriter.GetEncodedPropertyNameWithBeginObject("players");
        static readonly byte[] kCountHostiles = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("hostiles");
        static readonly byte[] kCountAnimals = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("animals");
        static readonly byte[] kPlayers = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("players");
        static readonly byte[] kPName = JsonWriter.GetEncodedPropertyNameWithBeginObject("name");
        static readonly byte[] kPPosition = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("position");
        static readonly byte[] kPLevel = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("level");
        static readonly byte[] kPHealth = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("health");
        static readonly byte[] kPStamina = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("stamina");
        static readonly byte[] kPScore = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("score");
        static readonly byte[] kPDeaths = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("deaths");
        static readonly byte[] kPZombieKills = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("zombieKills");
        static readonly byte[] kPPlayerKills = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("playerKills");
        static readonly byte[] kPPing = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("ping");
        static readonly byte[] kPDead = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("dead");
        static readonly byte[] kZombies = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("zombies");
        static readonly byte[] kZId = JsonWriter.GetEncodedPropertyNameWithBeginObject("id");
        static readonly byte[] kZPosition = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("position");
        static readonly FastTags<TagGroup.Global> kZombieTag = FastTags<TagGroup.Global>.Parse("zombie");

        // traders sub-route
        static readonly byte[] kTraders = JsonWriter.GetEncodedPropertyNameWithBeginObject("traders");
        static readonly byte[] kTName = JsonWriter.GetEncodedPropertyNameWithBeginObject("name");
        static readonly byte[] kTPos = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("position");

        public override void HandleRestGet(RequestContext _context)
        {
            string sub = _context.RequestPath;
            if (!string.IsNullOrEmpty(sub))
            {
                if (sub.Equals("traders", StringComparison.OrdinalIgnoreCase))
                {
                    HandleTraders(_context);
                    return;
                }
                SendEmptyResponse(_context, System.Net.HttpStatusCode.NotFound, null, "NOT_FOUND");
                return;
            }

            var world = GameManager.Instance.World;
            ulong worldTime = world.worldTime;
            var (days, hours, minutes) = GameUtils.WorldTimeToElements(worldTime);
            int bmDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
            var duskDawn = GameUtils.CalcDuskDawnHours(GamePrefs.GetInt(EnumGamePrefs.DayLightLength));

            PrepareEnvelopedResult(out var w);

            // ---- server info ----
            w.WriteRaw(kServer);
            w.WriteRaw(kName);
            w.WriteString(GamePrefs.GetString(EnumGamePrefs.ServerName));
            w.WriteRaw(kMaxPlayers);
            w.WriteInt32(GamePrefs.GetInt(EnumGamePrefs.ServerMaxPlayerCount));
            w.WriteRaw(kWorld);
            w.WriteString(GamePrefs.GetString(EnumGamePrefs.GameWorld));
            w.WriteRaw(kVersion);
            w.WriteString(Constants.cVersionInformation.LongStringNoBuild);
            w.WriteEndObject();

            // ---- game time ----
            w.WriteRaw(kGameTime);
            JsonCommons.WriteGameTimeObject(ref w, days, hours, minutes);

            // ---- bloodmoon ----
            w.WriteRaw(kBloodmoon);
            w.WriteRaw(kBmActive);
            w.WriteBoolean(GameUtils.IsBloodMoonTime(worldTime, duskDawn, bmDay));
            w.WriteRaw(kBmDay);
            w.WriteInt32(bmDay);
            w.WriteRaw(kBmStart);
            JsonCommons.WriteGameTimeObject(ref w, bmDay, duskDawn.Item1, 0);
            w.WriteRaw(kBmEnd);
            JsonCommons.WriteGameTimeObject(ref w, bmDay + 1, duskDawn.Item2, 0);
            w.WriteEndObject();

            // ---- entity counts ----
            w.WriteRaw(kCounts);
            w.WriteRaw(kCountPlayers);
            w.WriteInt32(world.Players.Count);
            w.WriteRaw(kCountHostiles);
            w.WriteInt32(Hostiles.Instance.GetCount());
            w.WriteRaw(kCountAnimals);
            w.WriteInt32(Animals.Instance.GetCount());
            w.WriteEndObject();

            // ---- online players (safe subset only) ----
            w.WriteRaw(kPlayers);
            w.WriteBeginArray();
            int written = 0;
            var clients = SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.List;
            for (int i = 0; i < clients.Count; i++)
            {
                ClientInfo ci = clients[i];
                if (ci == null) continue;
                if (!world.Players.dict.TryGetValue(ci.entityId, out var ep) || ep == null) continue;

                if (written > 0) w.WriteValueSeparator();
                written++;

                w.WriteRaw(kPName);
                w.WriteString(ci.playerName);
                w.WriteRaw(kPPosition);
                JsonCommons.WriteVector3(ref w, ep.GetPosition());
                w.WriteRaw(kPLevel);
                w.WriteInt32(ep.Progression != null ? ep.Progression.Level : 1);
                w.WriteRaw(kPHealth);
                w.WriteInt32(ep.Health);
                w.WriteRaw(kPStamina);
                w.WriteSingle(ep.Stamina);
                w.WriteRaw(kPScore);
                w.WriteInt32(ep.Score);
                w.WriteRaw(kPDeaths);
                w.WriteInt32(ep.Died);
                w.WriteRaw(kPZombieKills);
                w.WriteInt32(ep.KilledZombies);
                w.WriteRaw(kPPlayerKills);
                w.WriteInt32(ep.KilledPlayers);
                w.WriteRaw(kPPing);
                w.WriteInt32(ci.ping);
                w.WriteRaw(kPDead);
                w.WriteBoolean(ep.IsDead());
                w.WriteEndObject();
            }
            w.WriteEndArray();

            // ---- active zombies (positions only) ----
            // Hostiles.Get provides the game's existing alive-entity snapshot.
            // The zombie tag includes humanoids and infected animals while
            // excluding ordinary hostile wolves/bears.
            w.WriteRaw(kZombies);
            w.WriteBeginArray();
            int zombieWritten = 0;
            var hostiles = new List<EntityEnemy>();
            Hostiles.Instance.Get(hostiles);
            for (int i = 0; i < hostiles.Count; i++)
            {
                EntityEnemy zombie = hostiles[i];
                if (zombie == null || !IsZombie(zombie)) continue;
                if (zombieWritten > 0) w.WriteValueSeparator();
                zombieWritten++;

                // Runtime entity ids are transient (not account/platform ids) and
                // let the map keep following the same moving zombie between polls.
                w.WriteRaw(kZId);
                w.WriteInt32(zombie.entityId);
                w.WriteRaw(kZPosition);
                JsonCommons.WriteVector3(ref w, zombie.GetPosition());
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
            SendEnvelopedResult(_context, ref w);
        }

        static bool IsZombie(EntityEnemy entity)
        {
            return entity.HasAnyTags(kZombieTag);
        }

        void HandleTraders(RequestContext _context)
        {
            List<TraderArea> areas = null;
            List<PrefabInstance> prefabs = null;
            var world = GameManager.Instance.World;
            if (world != null)
            {
                try
                {
                    var deco = world.ChunkCache.ChunkProvider.GetDynamicPrefabDecorator();
                    if (deco != null)
                    {
                        areas = deco.GetTraderAreas();
                        prefabs = deco.allPrefabs;
                    }
                }
                catch (Exception) { areas = null; }
            }

            PrepareEnvelopedResult(out var w);
            w.WriteRaw(kTraders);
            w.WriteBeginArray();
            int written = 0;
            if (areas != null)
            {
                for (int i = 0; i < areas.Count; i++)
                {
                    TraderArea ta = areas[i];
                    if (ta == null) continue;
                    if (written > 0) w.WriteValueSeparator();
                    written++;

                    string name = null;
                    if (ta.owningTrader != null && !string.IsNullOrEmpty(ta.owningTrader.EntityName))
                    {
                        name = ta.owningTrader.EntityName;
                    }
                    else if (prefabs != null)
                    {
                        // find the trader POI prefab this area belongs to
                        for (int j = 0; j < prefabs.Count; j++)
                        {
                            PrefabInstance pi = prefabs[j];
                            if (pi == null || pi.prefab == null) continue;
                            Vector3i bmin = pi.boundingBoxPosition;
                            Vector3i bmax = pi.boundingBoxPosition + pi.boundingBoxSize;
                            if (ta.Position.x < bmin.x || ta.Position.x >= bmax.x) continue;
                            if (ta.Position.z < bmin.z || ta.Position.z >= bmax.z) continue;
                            string pn = pi.prefab.PrefabName;
                            if (string.IsNullOrEmpty(pn)) continue;
                            if (!pn.StartsWith("trader_", StringComparison.OrdinalIgnoreCase)) continue;
                            name = Localization.Get(pn);
                            if (string.IsNullOrEmpty(name)) name = pn;
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(name)) name = "Trader";

                    w.WriteRaw(kTName);
                    w.WriteString(name ?? "Trader");
                    w.WriteRaw(kTPos);
                    // center of the trader compound, roughly where the NPC stands
                    var center = new UnityEngine.Vector3(
                        ta.Position.x + ta.PrefabSize.x * 0.5f,
                        ta.Position.y,
                        ta.Position.z + ta.PrefabSize.z * 0.5f);
                    JsonCommons.WriteVector3(ref w, center);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
            SendEnvelopedResult(_context, ref w);
        }

        /// <summary>2000 = guest/anonymous. This endpoint is public by design.</summary>
        public override int DefaultPermissionLevel()
        {
            return 2000;
        }
    }
}
