using HarmonyLib;
using UnityEngine;

namespace DSZipline
{
    /// <summary>
    /// Authoritatively validates zipline links. For a distant first endpoint the
    /// TileEntity is unloaded, but its persistent PowerItem remains available, so
    /// establish the native power-graph relationship directly from those items.
    /// </summary>
    [HarmonyPatch(typeof(NetPackageWireActions), nameof(NetPackageWireActions.ProcessPackage))]
    public static class ZiplineWireValidationPatch
    {
        public static bool Prefix(NetPackageWireActions __instance, World _world)
        {
            if (_world == null ||
                !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer ||
                __instance.currentOperation != NetPackageWireActions.WireActions.SetParent ||
                __instance.wireChildren == null ||
                __instance.wireChildren.Count < 1)
                return true;

            Vector3i childPos = __instance.tileEntityPosition;
            Vector3i parentPos = __instance.wireChildren[0];
            float distance = Vector3.Distance(
                ZiplineLink.AnchorPoint(childPos), ZiplineLink.AnchorPoint(parentPos));

            bool childIsAnchor = ZiplineLink.TryGetAnchor(
                _world, childPos, out BlockZiplineAnchor child);
            PowerItem childItem = PowerManager.Instance.GetPowerItemByWorldPos(childPos);
            PowerItem parentItem = PowerManager.Instance.GetPowerItemByWorldPos(parentPos);
            BlockZiplineAnchor parent = AnchorFromPowerItem(parentItem);
            bool parentIsAnchor = parent != null;

            if (!childIsAnchor && !parentIsAnchor)
                return true;
            if (!childIsAnchor || !parentIsAnchor)
                return Reject(childPos, parentPos, "anchor-to-electrical link");
            if (childItem == null || parentItem == null)
                return Reject(childPos, parentPos, "persistent endpoint data is unavailable");
            if (child.IsSonicTier != parent.IsSonicTier)
                return Reject(childPos, parentPos, "mixed anchor tiers");

            float maximum = ZiplineLink.MaximumLengthFor(child);
            if (distance > maximum)
                return Reject(childPos, parentPos, "tier range " + maximum + " m");

            if (__instance.Sender != null && __instance.Sender.entityId != -1)
            {
                EntityPlayer player = _world.GetEntity(__instance.Sender.entityId) as EntityPlayer;
                float useRange = Constants.cDigAndBuildDistance + 2f;
                if (player == null || Vector3.Distance(
                        player.position,
                        childPos.ToVector3() + new Vector3(0.5f, 0.5f, 0.5f)) > useRange)
                    return Reject(childPos, parentPos, "player is not near clicked endpoint");
            }

            if (!childItem.CanParent(parentItem))
                return Reject(childPos, parentPos, "endpoint cannot accept that parent");

            PowerItem oldParent = childItem.Parent;
            PowerManager.Instance.SetParent(childItem, parentItem);
            if (childItem.Parent != parentItem)
                return Reject(childPos, parentPos, "circular or invalid power relationship");

            RefreshLoadedParent(oldParent);
            RefreshLoadedParent(parentItem);
            TileEntityPowered childTile = childItem.TileEntity;
            if (childTile != null)
            {
                childTile.parentPosition = parentPos;
                childTile.SetModified();
                childTile.RemoveWires();
                childTile.DrawWires();
            }
            Log.Out("[DSZipline] Connected " +
                    (child.IsSonicTier ? "Sonic" : "wooden") + " route " +
                    childPos + " -> " + parentPos + " (" + distance.ToString("0.0") + " m).");
            return false; // Distant endpoints have no TileEntity for vanilla to resolve.

        }

        public static void Postfix(NetPackageWireActions __instance, World _world)
        {
            if (_world == null ||
                !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            TileEntityPowered child =
                _world.GetTileEntity(__instance.tileEntityPosition) as TileEntityPowered;
            if (__instance.currentOperation == NetPackageWireActions.WireActions.RemoveParent)
            {
                if (child == null || child.GetPowerItem()?.Parent != null) return;
                child.parentPosition = new Vector3i(-9999, -9999, -9999);
                child.SetModified();
                return;
            }
            if (__instance.currentOperation != NetPackageWireActions.WireActions.SetParent ||
                __instance.wireChildren == null || __instance.wireChildren.Count < 1)
                return;

            Vector3i parentPos = __instance.wireChildren[0];
            if (child?.GetPowerItem()?.Parent == null ||
                child.GetPowerItem().Parent.Position != parentPos)
                return; // Prefix rejected it, or vanilla could not create the relation.

            // Vanilla only sets parentPosition on the sending client. Persist it on
            // the server too so newly loading clients can render from the child end
            // while the far parent chunk is outside their view radius.
            child.parentPosition = parentPos;
            child.SetModified();
        }

        private static BlockZiplineAnchor AnchorFromPowerItem(PowerItem item)
        {
            if (item == null || item.BlockID >= Block.list.Length) return null;
            return Block.list[item.BlockID] as BlockZiplineAnchor;
        }

        private static void RefreshLoadedParent(PowerItem item)
        {
            TileEntityPowered tile = item?.TileEntity;
            if (tile == null) return;
            tile.CreateWireDataFromPowerItem();
            tile.SendWireData();
            tile.RemoveWires();
            tile.DrawWires();
            tile.SetModified();
        }

        private static bool Reject(Vector3i child, Vector3i parent, string reason)
        {
            Log.Warning("[DSZipline] Rejected invalid link " + child + " -> " + parent +
                        " (" + reason + ").");
            return false;
        }
    }

    /// <summary>Backfill parent coordinates for routes created before V0.3.4.</summary>
    [HarmonyPatch(typeof(TileEntityPowered), nameof(TileEntityPowered.InitializePowerData))]
    public static class ZiplineParentPersistencePatch
    {
        public static void Postfix(TileEntityPowered __instance)
        {
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer ||
                !(__instance?.block is BlockZiplineAnchor) ||
                __instance.GetPowerItem()?.Parent == null)
                return;

            Vector3i parent = __instance.GetPowerItem().Parent.Position;
            if (parent == __instance.parentPosition) return;
            __instance.parentPosition = parent;
            __instance.SetModified();
        }
    }
}
