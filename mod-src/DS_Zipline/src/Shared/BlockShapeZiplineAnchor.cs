using UnityEngine;
using UnityEngine.Scripting;

namespace DSZipline
{
    /// <summary>
    /// Interaction/collision bounds for the custom three-block-tall anchor.
    /// MultiBlockDim repeats this one-voxel shaft bound for the parent and both
    /// children, while child activation is redirected to the powered parent by
    /// the game's normal multiblock handling.
    /// </summary>
    [Preserve]
    public class BlockShapeZiplineAnchor : BlockShapeModelEntity
    {
        // The rendered shaft center after the model's socket-alignment shift.
        private static readonly Vector3 ShaftOffsetFromBlockCenter =
            new Vector3(0.37599f, 0f, 0.01229f);

        public override void Init(Block block)
        {
            base.Init(block);
            Log.Out("[DSZipline] Custom 1x3 anchor interaction shape initialized.");
        }

        public override Bounds[] GetBounds(BlockValue blockValue)
        {
            if (blockValue.Block is BlockZiplineAnchorWood)
            {
                // The primitive tier has a centered timber, wide crossbeam, and
                // two braces. Rotate its rectangular per-voxel footprint with the
                // placed block so native multiblock child targeting remains exact.
                Vector3 size = GetRotation(blockValue) * new Vector3(0.78f, 1f, 0.40f);
                size = new Vector3(Mathf.Abs(size.x), 1f, Mathf.Abs(size.z));
                return new[] { new Bounds(new Vector3(0.5f, 0.5f, 0.5f), size) };
            }

            Vector3 offset = GetRotation(blockValue) * ShaftOffsetFromBlockCenter;
            var center = new Vector3(0.5f + offset.x, 0.5f, 0.5f + offset.z);

            // Slightly pad the approximately 0.10 m square Sonic post so aiming
            // at an edge remains comfortable without activating nearby empty space.
            return new[] { new Bounds(center, new Vector3(0.16f, 1f, 0.16f)) };
        }
    }
}
