using System;
using UnityEngine.Scripting;

namespace DSZipline
{
    /// <summary>Client hook used by the shared block class without pulling client patches into the server build.</summary>
    public static class ZiplineRideBridge
    {
        public static Func<EntityPlayerLocal, WorldBase, Vector3i, bool> StartRide;
    }

    /// <summary>Powered-block anchor so vanilla wire tile entities persist and synchronize the cable endpoints.</summary>
    [Preserve]
    public class BlockZiplineAnchor : BlockPowered
    {
        public const string RideCommand = "DSZiplineRide";

        public virtual bool IsSonicTier => true;
        public virtual float RideSpeed => 16f;

        private static bool loggedLegacyUpgrade;

        public override void OnBlockLoaded(WorldBase world, Vector3i blockPos, BlockValue blockValue)
        {
            base.OnBlockLoaded(world, blockPos, blockValue);

            // V0.1.12 changed the anchor from a single block to a 1x3x1 native
            // multiblock so upper-pole hits resolve to the powered parent. Upgrade
            // already-placed legacy parents on the authoritative world when both
            // newly reserved cells are still available.
            if (world == null || world.IsRemote() || blockValue.ischild ||
                !isMultiBlock || multiBlockPos == null)
                return;

            bool needsChildren = false;
            for (int y = 1; y <= 2; y++)
            {
                BlockValue occupant = world.GetBlock(blockPos + new Vector3i(0, y, 0));
                if (occupant.isair)
                {
                    needsChildren = true;
                    continue;
                }
                if (occupant.type != blockValue.type || !occupant.ischild)
                {
                    Log.Warning("[DSZipline] Cannot add interaction children above legacy anchor at " +
                                blockPos + "; reserved cell is occupied.");
                    return;
                }
            }
            if (!needsChildren) return;

            Chunk chunk = world.ChunkCache?.GetChunkFromWorldPos(blockPos.x, blockPos.y, blockPos.z) as Chunk;
            if (chunk == null) return;
            multiBlockPos.AddChilds(world, chunk, blockPos, blockValue);
            if (!loggedLegacyUpgrade)
            {
                loggedLegacyUpgrade = true;
                Log.Out("[DSZipline] Upgraded legacy anchors to 1x3x1 interaction bounds.");
            }
        }

        private Vector3i ResolveParentPosition(BlockValue blockValue, Vector3i blockPos)
        {
            return blockValue.ischild && multiBlockPos != null
                ? multiBlockPos.GetParentPos(blockPos, blockValue)
                : blockPos;
        }

        private static string UseBinding(EntityAlive entityFocusing)
        {
            PlayerActionsLocal input = (entityFocusing as EntityPlayerLocal)?.playerInput;
            if (input == null) return "(E)";
            return XUiUtils.GetBindingXuiMarkupString(input.Activate) +
                   XUiUtils.GetBindingXuiMarkupString(input.PermanentActions.Activate);
        }

        public override string GetActivationText(
            WorldBase world,
            BlockValue blockValue,
            Vector3i blockPos,
            EntityAlive entityFocusing)
        {
            Vector3i anchorPos = ResolveParentPosition(blockValue, blockPos);
            if (ZiplineLink.TryGetLowerEndpoint(world, anchorPos, out _))
                return string.Format(Localization.Get("DSZiplineRidePrompt"), UseBinding(entityFocusing));
            if (ZiplineLink.HasLinkedAnchor(world, anchorPos))
                return Localization.Get("DSZiplineLowerPrompt");
            return Localization.Get("DSZiplineWirePrompt");
        }

        public override BlockActivationCommand[] GetBlockActivationCommands(
            WorldBase world,
            BlockValue blockValue,
            Vector3i blockPos,
            EntityAlive entityFocusing)
        {
            Vector3i anchorPos = ResolveParentPosition(blockValue, blockPos);
            BlockValue anchorValue = world.GetBlock(anchorPos);
            BlockActivationCommand[] inherited = base.GetBlockActivationCommands(
                world, anchorValue, anchorPos, entityFocusing) ?? BlockActivationCommand.Empty;
            bool canRide = entityFocusing is EntityPlayerLocal &&
                           ZiplineLink.TryGetLowerEndpoint(world, anchorPos, out _);

            var commands = new BlockActivationCommand[inherited.Length + 1];
            commands[0] = new BlockActivationCommand(RideCommand, "run", canRide, false, null);
            Array.Copy(inherited, 0, commands, 1, inherited.Length);
            return commands;
        }

        public override bool OnBlockActivated(
            WorldBase world,
            Vector3i blockPos,
            BlockValue blockValue,
            EntityPlayerLocal player)
        {
            Vector3i anchorPos = ResolveParentPosition(blockValue, blockPos);
            if (ZiplineLink.TryGetLowerEndpoint(world, anchorPos, out _) &&
                ZiplineRideBridge.StartRide != null &&
                ZiplineRideBridge.StartRide(player, world, anchorPos))
                return true;
            return base.OnBlockActivated(world, anchorPos, world.GetBlock(anchorPos), player);
        }

        public override bool OnBlockActivated(
            string commandName,
            WorldBase world,
            Vector3i blockPos,
            BlockValue blockValue,
            EntityPlayerLocal player)
        {
            Vector3i anchorPos = ResolveParentPosition(blockValue, blockPos);
            if (commandName == RideCommand)
            {
                if (ZiplineRideBridge.StartRide != null &&
                    ZiplineRideBridge.StartRide(player, world, anchorPos))
                {
                    return true;
                }

                GameManager.ShowTooltip(player, Localization.Get("DSZiplineNoRoute"), false, false, 0f);
                return true;
            }

            return base.OnBlockActivated(
                commandName, world, anchorPos, world.GetBlock(anchorPos), player);
        }
    }

    /// <summary>Primitive wooden tier: static black cable and quarter-speed travel.</summary>
    [Preserve]
    public class BlockZiplineAnchorWood : BlockZiplineAnchor
    {
        public override bool IsSonicTier => false;
        public override float RideSpeed => 4f;
    }
}
