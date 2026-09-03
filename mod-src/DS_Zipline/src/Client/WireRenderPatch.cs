using HarmonyLib;
using UnityEngine;

namespace DSZipline
{
    /// <summary>Marks pooled FastWireNodes whose vanilla material state was changed for a zipline.</summary>
    public sealed class ZiplineWireMarker : MonoBehaviour
    {
    }

    /// <summary>Makes zipline wires permanently visible and gives rendering the same curve used by the rider.</summary>
    [HarmonyPatch(typeof(TileEntityPowered), nameof(TileEntityPowered.DrawWires))]
    public static class WireRenderPatch
    {
        // Match the original Sonic Zipline material: cyan emission color
        // (0, 1, 0.91748) at strength 3.1. The wooden tier retains the original
        // subdued black cable, but neither tier inherits the electrical pulse.
        private static readonly Color SonicCableGlow = new Color(0f, 3.1f, 2.8442f, 1f);
        private static readonly Color WoodenCableColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color VanillaWireColor = new Color(0f, 0f, 0f, 0.9019608f);
        private const float VanillaPulseSpeed = 360f;

        public static void Postfix(TileEntityPowered __instance)
        {
            if (!(__instance.block is BlockZiplineAnchor sourceAnchor) ||
                __instance.BlockTransform == null)
                return;

            // Never expose the vanilla mesh for an anchor route. Besides its hard
            // 256 m early return, it has only 16 sections and requires the far
            // endpoint chunk to be loaded. Keep its pooled node for vanilla graph
            // bookkeeping, but render every zipline through our scalable mesh.
            if (__instance.currentWireNodes != null)
            {
                foreach (IWireNode node in __instance.currentWireNodes)
                {
                    if (!(node is FastWireNode fastNode)) continue;
                    if (fastNode.GetComponent<ZiplineWireMarker>() == null)
                        fastNode.gameObject.AddComponent<ZiplineWireMarker>();
                    ConfigureCableMaterial(fastNode, sourceAnchor.IsSonicTier);
                    if (fastNode.meshRenderer != null) fastNode.meshRenderer.enabled = false;
                    if (fastNode.meshCollider != null) fastNode.meshCollider.enabled = false;
                }
            }

            ZiplineCableSet cableSet =
                __instance.BlockTransform.GetComponent<ZiplineCableSet>() ??
                __instance.BlockTransform.gameObject.AddComponent<ZiplineCableSet>();
            cableSet.Sync(__instance, sourceAnchor);
        }

        private static void ConfigureCableMaterial(FastWireNode node, bool sonicTier)
        {
            Color cableColor = sonicTier ? SonicCableGlow : WoodenCableColor;
            node.wireColor = cableColor;
            node.SetWireColor(cableColor);
            node.SetPulseColor(cableColor);
            node.SetPulseSpeed(0f);
            node.TogglePulse(false);
            ConfigureCableMaterial(
                node.meshRenderer != null ? node.meshRenderer.material : null,
                sonicTier);
        }

        internal static void ConfigureCableMaterial(Material material, bool sonicTier)
        {
            if (material == null) return;
            Color cableColor = sonicTier ? SonicCableGlow : WoodenCableColor;
            material.SetColor("_Color", cableColor);
            if (material.HasProperty("_WireColor"))
                material.SetColor("_WireColor", cableColor);
            if (material.HasProperty("_PulseColor"))
                material.SetColor("_PulseColor", cableColor);
            if (material.HasProperty("_PulseSpeed"))
                material.SetFloat("_PulseSpeed", 0f);
        }

        internal static void RestorePooledWire(FastWireNode node)
        {
            // FastWireNode.Reset does not reset colors or pulse speed. Restore
            // every marked node before WireManager can reuse it for electricity.
            if (node == null || node.GetComponent<ZiplineWireMarker>() == null) return;
            if (node.meshRenderer != null) node.meshRenderer.enabled = true;
            if (node.meshCollider != null) node.meshCollider.enabled = true;
            node.wireColor = Color.black;
            node.prevWireColor = Color.white;
            node.SetWireColor(Color.black);
            node.SetPulseColor(Color.yellow);
            node.SetPulseSpeed(VanillaPulseSpeed);
            node.TogglePulse(false);

            Material material = node.meshRenderer != null ? node.meshRenderer.material : null;
            if (material != null && material.HasProperty("_WireColor"))
                material.SetColor("_WireColor", VanillaWireColor);
        }
    }

    [HarmonyPatch(typeof(FastWireNode), nameof(FastWireNode.Reset))]
    public static class ZiplineCablePoolResetPatch
    {
        public static void Postfix(FastWireNode __instance)
        {
            WireRenderPatch.RestorePooledWire(__instance);
        }
    }
}
