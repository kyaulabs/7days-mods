using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VanillaPlus
{
    /// <summary>
    /// Supply Crate Guards: the moment any alive player approaches a landed
    /// supply crate, a small horde of zombies spawns around it (once per crate).
    /// Server-side only - the postfix bails out on clients (World.IsRemote()).
    /// </summary>
    [HarmonyPatch(typeof(EntitySupplyCrate), "Update")]
    public static class CrateGuardPatches
    {
        private static readonly HashSet<int> Triggered = new HashSet<int>();
        private static readonly Dictionary<int, ulong> LastCheck = new Dictionary<int, ulong>();

        private const ulong CheckIntervalTicks = 10; // every 0.5s (20 ticks/s)

        public static void Postfix(EntitySupplyCrate __instance)
        {
            World world = __instance.world;
            if (world == null || world.IsRemote())
                return; // server-side only

            int id = __instance.entityId;
            if (Triggered.Contains(id))
                return;
            if (!__instance.wasOnGround)
                return; // still descending on the parachute

            // throttle the distance check
            ulong now = world.GetWorldTime();
            if (LastCheck.TryGetValue(id, out ulong last) && now - last < CheckIntervalTicks)
                return;
            LastCheck[id] = now;

            EntityPlayer player = world.GetClosestPlayer(__instance.position,
                CrateGuardConfig.Instance.TriggerDistance, false);
            if (player == null)
                return;

            Triggered.Add(id);
            SpawnGuards(world, __instance.position);
            Log.Out("[Vanilla+] Supply crate guard horde spawned ({0} zombies, group '{1}') at {2}",
                CrateGuardConfig.Instance.ZombieCount,
                CrateGuardConfig.Instance.EntityGroup,
                __instance.position);
        }

        private static void SpawnGuards(World world, Vector3 cratePos)
        {
            GameRandom rand = world.GetGameRandom();
            int lastClassId = 0;
            for (int i = 0; i < CrateGuardConfig.Instance.ZombieCount; i++)
            {
                int classId = EntityGroups.GetRandomFromGroup(CrateGuardConfig.Instance.EntityGroup, ref lastClassId, rand);
                if (classId <= 0)
                    break; // unknown entity group - avoid spawning entity class 0

                float angle = rand.RandomFloat * 6.2831855f;
                float dist = rand.RandomRange(3f, 8f);
                float x = cratePos.x + Mathf.Cos(angle) * dist;
                float z = cratePos.z + Mathf.Sin(angle) * dist;
                Vector3 pos = new Vector3(x, world.GetHeightAt(x, z) + 0.5f, z);
                Vector3 rot = new Vector3(0f, rand.RandomFloat * 360f, 0f);

                Entity entity = EntityFactory.CreateEntity(classId, pos, rot);
                if (entity != null)
                    world.SpawnEntityInWorld(entity);
            }
        }
    }
}
