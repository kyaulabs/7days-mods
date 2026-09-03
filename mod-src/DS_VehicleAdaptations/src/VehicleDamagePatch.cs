using HarmonyLib;

namespace VehicleAdaptations
{
    /// <summary>
    /// Records positive block damage before vanilla applies visual downgrades. On a
    /// dedicated game, local melee/tool block damage is client-simulated, so the
    /// identical client DLL reports each accepted hit to the server.
    /// </summary>
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockDamaged))]
    public static class VehicleDamagePatch
    {
        public static bool Prefix(
            WorldBase _world,
            BlockValueRef _bvRef,
            BlockValue _blockValue,
            int _damagePoints,
            int _entityIdThatDamaged,
            int _recDepth,
            ref int __result)
        {
            if (!(_world is World world) || _damagePoints <= 0 ||
                !_bvRef.TryGetBlockPos(out Vector3i blockPos))
                return true;

            TEFeatureVehicleAdaptation feature = GetFeature(world, blockPos);
            if (feature == null) return true;

            if (_recDepth == 0 && !feature.Burning)
            {
                bool burning = feature.RecordDamage(
                    world, _damagePoints,
                    _entityIdThatDamaged >= 0 ? "direct damage" : "environmental damage");

                if (world.IsRemote() && _entityIdThatDamaged == world.GetPrimaryPlayerId())
                {
                    SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                        NetPackageManager.GetPackage<NetPackageVehicleAdaptationDamage>()
                            .Setup(blockPos, _damagePoints));
                }

                if (burning)
                {
                    // Keep the ignition-causing hit from deleting or downgrading the
                    // warning object before the authoritative five-second fuse ends.
                    __result = _blockValue.damage;
                    return false;
                }
            }

            if (feature.Burning)
            {
                __result = _blockValue.damage;
                return false;
            }
            return true;
        }

        internal static TEFeatureVehicleAdaptation GetFeature(WorldBase world, Vector3i blockPos)
        {
            BlockValue value = world.GetBlock(blockPos);
            if (value.ischild)
                blockPos = value.Block.multiBlockPos.GetParentPos(blockPos, value);
            return (world.GetTileEntity(blockPos) as TileEntityComposite)
                ?.GetFeature<TEFeatureVehicleAdaptation>();
        }
    }
}
