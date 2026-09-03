using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VehicleAdaptations
{
    /// <summary>
    /// Converts blast exposure into delayed vehicle ignition. Vanilla computes
    /// explosion damage in a temporary change set and otherwise deletes terminally
    /// damaged cars immediately, so adapted vehicles are removed from that change
    /// set after being armed. The later vehicle detonation still damages everything
    /// else normally.
    /// </summary>
    [HarmonyPatch(typeof(Explosion), nameof(Explosion.AttackBlocks))]
    public static class VehicleExplosionPatch
    {
        public static void Prefix(
            Explosion __instance,
            int _entityThatCausedExplosion,
            ItemValue _itemValueExplosionSource,
            out List<Vector3i> __state)
        {
            __state = new List<Vector3i>();
            World world = __instance?.world;
            if (world == null || world.IsRemote()) return;

            EntityAlive source = world.GetEntity(_entityThatCausedExplosion) as EntityAlive;
            FastTags<TagGroup.Global> tags = Explosion.explosionTag;
            if (_itemValueExplosionSource != null)
                tags |= _itemValueExplosionSource.ItemClass.ItemTags;
            float radius = EffectManager.GetValue(
                PassiveEffects.ExplosionRadius, _itemValueExplosionSource,
                __instance.explosionData.BlockRadius, source, null, tags);
            if (radius <= 0f) radius = 0.01f;
            int reach = Mathf.CeilToInt(radius + 1f);
            Vector3 center = __instance.worldPos;
            var seen = new HashSet<Vector3i>();

            int minX = Mathf.FloorToInt(center.x) - reach;
            int maxX = Mathf.FloorToInt(center.x) + reach;
            int minY = Mathf.FloorToInt(center.y) - reach;
            int maxY = Mathf.FloorToInt(center.y) + reach;
            int minZ = Mathf.FloorToInt(center.z) - reach;
            int maxZ = Mathf.FloorToInt(center.z) + reach;

            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            {
                var pos = new Vector3i(x, y, z);
                // Test the occupied cell, not the parent cell: construction vehicles
                // can span many blocks with their tile entity at one far endpoint.
                if (Vector3.Distance(pos.ToVector3Center(), center) > radius) continue;
                BlockValue value = world.GetBlock(pos);
                if (value.isair) continue;
                if (value.ischild)
                    pos = value.Block.multiBlockPos.GetParentPos(pos, value);
                if (!seen.Add(pos)) continue;

                TEFeatureVehicleAdaptation feature = VehicleDamagePatch.GetFeature(world, pos);
                if (feature == null || feature.Detonating) continue;

                feature.Ignite(world, "explosion chain");
                __state.Add(pos);
            }
        }

        public static void Postfix(Explosion __instance, List<Vector3i> __state)
        {
            if (__instance?.ChangedBlockPositions == null || __state == null) return;
            foreach (Vector3i position in __state)
            {
                // Keep the burning vehicle intact for its own visible fuse. Removing
                // its pending blast change prevents instant, invisible chain bursts.
                __instance.ChangedBlockPositions.Remove(position);
            }
        }
    }
}
