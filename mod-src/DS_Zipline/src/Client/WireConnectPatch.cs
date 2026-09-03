using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DSZipline
{
    /// <summary>
    /// Gives immediate tier/range feedback and bypasses vanilla's requirement that
    /// the first endpoint chunk remain client-loaded for the second click.
    /// </summary>
    [HarmonyPatch(typeof(ItemActionConnectPower), nameof(ItemActionConnectPower.OnHoldingUpdate))]
    public static class ZiplineWireConnectPatch
    {
        private static readonly Dictionary<ItemActionConnectPower.ConnectPowerData, bool> StartTiers =
            new Dictionary<ItemActionConnectPower.ConnectPowerData, bool>();

        public static bool Prefix(
            ItemActionConnectPower __instance,
            ItemActionData _actionData)
        {
            if (_actionData?.invData?.item == null ||
                _actionData.invData.item.GetItemName() != "DSZiplineTool" ||
                !(_actionData is ItemActionConnectPower.ConnectPowerData data) ||
                !data.StartLink)
                return true;

            float animationDelay = AnimationDelayData.AnimationDelay[
                data.invData.item.HoldType.Value].RayCast;
            if (Time.time - data.lastUseTime < animationDelay)
                return true;

            EntityPlayerLocal player = data.invData.holdingEntity as EntityPlayerLocal;
            WorldRayHitInfo hit = player?.HitInfo;
            if (hit == null || !hit.bHitValid || hit.tag.StartsWith("E_"))
                return true;

            WorldBase world = data.invData.world;
            Vector3i targetPos = hit.hit.blockPos;
            if (!ZiplineLink.TryGetAnchor(world, targetPos, out BlockZiplineAnchor target))
                return Reject(data, player, "DSZiplineAnchorOnly");

            if (!data.HasStartPoint)
            {
                // Cache the tier while the first endpoint is loaded. At 250–500 m
                // that chunk will normally unload before the player reaches the
                // second anchor, so querying its block again would incorrectly fail.
                StartTiers[data] = target.IsSonicTier;
                return true;
            }

            if (!StartTiers.TryGetValue(data, out bool sonicTier))
            {
                if (!ZiplineLink.TryGetAnchor(world, data.startPoint, out BlockZiplineAnchor start))
                    return Reject(data, player, "DSZiplineAnchorOnly");
                sonicTier = start.IsSonicTier;
                StartTiers[data] = sonicTier;
            }

            if (sonicTier != target.IsSonicTier)
                return Reject(data, player, "DSZiplineTierMismatch");

            float maximum = sonicTier
                ? ZiplineLink.SonicMaximumLength
                : ZiplineLink.WoodenMaximumLength;
            float distance = Vector3.Distance(
                ZiplineLink.AnchorPoint(data.startPoint),
                ZiplineLink.AnchorPoint(targetPos));
            if (distance > maximum)
                return RejectRange(data, player, maximum);
            if (data.startPoint == targetPos)
                return RejectRange(data, player, maximum);

            // Vanilla resolves the first TileEntity from the local chunk cache and
            // silently fails once that chunk unloads. Send the same authoritative
            // wire package directly; the server patch safely loads and validates
            // both endpoint chunks before vanilla processes it.
            data.StartLink = false;
            Vector3i startPos = data.startPoint;
            var package = NetPackageManager.GetPackage<NetPackageWireActions>().Setup(
                NetPackageWireActions.WireActions.SetParent,
                targetPos,
                new List<Vector3i> { startPos },
                player != null ? player.entityId : -1);
            package.wiringEntityID = player != null ? player.entityId : -1;

            // Mirror vanilla SetParentWithWireTool's immediate local state so the
            // child-owned long renderer appears without waiting for a chunk resync.
            TileEntityPowered targetTile = world.GetTileEntity(targetPos) as TileEntityPowered;
            if (targetTile != null)
            {
                targetTile.parentPosition = startPos;
                targetTile.SetModified();
                targetTile.DrawWires();
            }

            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                World localWorld = world as World;
                if (localWorld?.ChunkCache != null &&
                    !(localWorld.GetChunkFromWorldPos(
                        startPos.x, startPos.y, startPos.z) is Chunk))
                    localWorld.ChunkCache.GetChunkSync(
                        World.toChunkXZ(startPos.x), startPos.y, World.toChunkXZ(startPos.z));
                package.ProcessPackage(localWorld, GameManager.Instance);
            }
            else
            {
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(package);
            }

            data.HasStartPoint = false;
            StartTiers.Remove(data);
            data.invData.holdingEntity.RightArmAnimationUse = true;
            if (data.wireNode != null)
            {
                WireManager.Instance.RemoveActiveWire(data.wireNode);
                Object.Destroy(data.wireNode.gameObject);
                data.wireNode = null;
            }
            __instance.DecreaseDurability(data);
            return false;
        }

        internal static void Forget(ItemActionData actionData)
        {
            if (actionData is ItemActionConnectPower.ConnectPowerData data)
                StartTiers.Remove(data);
        }

        private static bool Reject(
            ItemActionConnectPower.ConnectPowerData data,
            EntityPlayerLocal player,
            string localizationKey)
        {
            data.StartLink = false;
            if (player != null)
            {
                GameManager.ShowTooltip(player, Localization.Get(localizationKey), false, false, 0f);
                player.PlayOneShot("ui_denied");
            }
            return false;
        }

        private static bool RejectRange(
            ItemActionConnectPower.ConnectPowerData data,
            EntityPlayerLocal player,
            float maximum)
        {
            data.StartLink = false;
            if (player != null)
            {
                string message = string.Format(Localization.Get("DSZiplineRangeExceeded"), maximum);
                GameManager.ShowTooltip(player, message, false, false, 0f);
                player.PlayOneShot("ui_denied");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemActionConnectPower), nameof(ItemActionConnectPower.StopHolding))]
    public static class ZiplineWireStopHoldingPatch
    {
        public static void Postfix(ItemActionData _data)
        {
            ZiplineWireConnectPatch.Forget(_data);
        }
    }
}
