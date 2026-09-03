using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DSZipline
{
    /// <summary>
    /// Owns cable meshes for one loaded anchor. Vanilla FastWireNode refuses spans
    /// over 256 m and only draws when the far endpoint chunk is loaded; this renderer
    /// uses the persisted endpoint coordinates and the exact rider curve instead.
    /// </summary>
    public sealed class ZiplineCableSet : MonoBehaviour
    {
        private readonly Dictionary<Vector3i, ZiplineCableVisual> visuals =
            new Dictionary<Vector3i, ZiplineCableVisual>();

        public void Sync(TileEntityPowered tile, BlockZiplineAnchor anchor)
        {
            var expected = new HashSet<Vector3i>();
            Vector3i source = tile.ToWorldPos();
            float maximum = ZiplineLink.MaximumLengthFor(anchor);

            // The child endpoint owns the normal renderer because parentPosition
            // is serialized with its TileEntity even while the parent is remote.
            if (tile.HasParent())
                AddIfValid(tile.GetParent(), false);

            // A loaded parent still needs a fallback when its child endpoint is
            // outside the chunk radius. The visual self-hides when that child loads.
            if (tile.wireDataList != null)
                foreach (Vector3i child in tile.wireDataList)
                    AddIfValid(child, true);

            var stale = new List<Vector3i>();
            foreach (var pair in visuals)
                if (!expected.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (Vector3i endpoint in stale)
            {
                ZiplineCableVisual visual = visuals[endpoint];
                visuals.Remove(endpoint);
                if (visual != null) Destroy(visual.gameObject);
            }

            void AddIfValid(Vector3i endpoint, bool parentFallback)
            {
                float distance = Vector3.Distance(
                    ZiplineLink.AnchorPoint(source), ZiplineLink.AnchorPoint(endpoint));
                if (distance < 0.01f || distance > maximum) return;

                World world = GameManager.Instance?.World;
                if (world != null &&
                    world.GetChunkFromWorldPos(endpoint.x, endpoint.y, endpoint.z) is Chunk &&
                    (!ZiplineLink.TryGetAnchor(world, endpoint, out BlockZiplineAnchor other) ||
                     other.IsSonicTier != anchor.IsSonicTier))
                    return;

                expected.Add(endpoint);
                if (!visuals.TryGetValue(endpoint, out ZiplineCableVisual visual) || visual == null)
                {
                    var child = new GameObject("DSZiplineCable_" + endpoint);
                    child.transform.SetParent(transform, false);
                    visual = child.AddComponent<ZiplineCableVisual>();
                    visuals[endpoint] = visual;
                }
                visual.Configure(source, endpoint, anchor.IsSonicTier, parentFallback);
            }
        }
    }

    public sealed class ZiplineCableVisual : MonoBehaviour
    {
        private const int Sides = 6;
        private static bool loggedFirstCable;
        private static bool loggedFirstLongCable;
        private Mesh cableMesh;
        private MeshRenderer cableRenderer;
        private Material cableMaterial;
        private Vector3i remoteEndpoint;
        private bool parentFallback;
        private Vector3i lastStart = Vector3i.invalid;
        private Vector3i lastEnd = Vector3i.invalid;
        private bool lastSonicTier;

        public void Configure(
            Vector3i start,
            Vector3i end,
            bool sonicTier,
            bool isParentFallback)
        {
            remoteEndpoint = end;
            parentFallback = isParentFallback;
            EnsureComponents(sonicTier);
            if (start != lastStart || end != lastEnd || sonicTier != lastSonicTier)
            {
                BuildCableMesh(start, end);
                lastStart = start;
                lastEnd = end;
                lastSonicTier = sonicTier;
            }
            WireRenderPatch.ConfigureCableMaterial(cableMaterial, sonicTier);
            UpdateVisibility();
        }

        private void Update()
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (cableRenderer == null) return;
            if (!parentFallback)
            {
                cableRenderer.enabled = true;
                return;
            }

            World world = GameManager.Instance?.World;
            bool childOwnsRenderer = world != null &&
                world.GetChunkFromWorldPos(
                    remoteEndpoint.x, remoteEndpoint.y, remoteEndpoint.z) is Chunk &&
                world.GetTileEntity(remoteEndpoint) is TileEntityPowered child &&
                child.BlockTransform != null;
            cableRenderer.enabled = !childOwnsRenderer;
        }

        private void EnsureComponents(bool sonicTier)
        {
            MeshFilter filter = gameObject.GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
            cableRenderer = gameObject.GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
            cableRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cableRenderer.receiveShadows = false;

            if (cableMesh == null)
            {
                cableMesh = new Mesh { name = "DSZipline_LongCableMesh" };
                cableMesh.MarkDynamic();
                filter.sharedMesh = cableMesh;
            }
            if (cableMaterial == null)
            {
                Material template = FastWireNode.BaseMaterial ??
                                    Resources.Load<Material>("Materials/WireMaterial");
                if (template == null)
                    throw new System.InvalidOperationException("vanilla WireMaterial is unavailable");
                cableMaterial = new Material(template) { name = "DSZipline_LongCableMaterial" };
                cableRenderer.sharedMaterial = cableMaterial;
            }
            WireRenderPatch.ConfigureCableMaterial(cableMaterial, sonicTier);
            gameObject.layer = transform.parent != null ? transform.parent.gameObject.layer : gameObject.layer;
        }

        private void BuildCableMesh(Vector3i startBlock, Vector3i endBlock)
        {
            Vector3 start = ZiplineLink.AnchorPoint(startBlock);
            Vector3 end = ZiplineLink.AnchorPoint(endBlock);
            float length = ZiplineLink.ApproximateLength(start, end);
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / 4f), 16, 160);
            int rings = segments + 1;
            var vertices = new Vector3[rings * Sides];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var uv2 = new Vector2[vertices.Length];
            var triangles = new int[segments * Sides * 6];
            Transform owner = transform.parent;

            for (int ring = 0; ring < rings; ring++)
            {
                float t = ring / (float)segments;
                Vector3 point = ZiplineLink.Point(start, end, t);
                Vector3 tangent = ZiplineLink.Tangent(start, end, t);
                Vector3 right = Vector3.Cross(tangent, Vector3.up);
                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.Cross(tangent, Vector3.forward);
                right.Normalize();
                Vector3 up = Vector3.Cross(right, tangent).normalized;

                for (int side = 0; side < Sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / Sides;
                    Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    int index = ring * Sides + side;
                    vertices[index] = owner.InverseTransformPoint(point + radial * 0.025f);
                    normals[index] = owner.InverseTransformDirection(radial).normalized;
                    uvs[index] = new Vector2(t * length * 0.25f, side / (float)Sides);
                    uv2[index] = Vector2.zero;
                }
            }

            int cursor = 0;
            for (int segment = 0; segment < segments; segment++)
            {
                for (int side = 0; side < Sides; side++)
                {
                    int nextSide = (side + 1) % Sides;
                    int a = segment * Sides + side;
                    int b = (segment + 1) * Sides + side;
                    int c = segment * Sides + nextSide;
                    int d = (segment + 1) * Sides + nextSide;
                    triangles[cursor++] = a;
                    triangles[cursor++] = b;
                    triangles[cursor++] = c;
                    triangles[cursor++] = c;
                    triangles[cursor++] = b;
                    triangles[cursor++] = d;
                }
            }

            cableMesh.Clear();
            cableMesh.vertices = vertices;
            cableMesh.normals = normals;
            cableMesh.uv = uvs;
            cableMesh.uv2 = uv2;
            cableMesh.triangles = triangles;
            cableMesh.RecalculateBounds();

            if (!loggedFirstCable)
            {
                loggedFirstCable = true;
                Log.Out("[DSZipline] Built scalable cable mesh (" +
                        length.ToString("0.0") + " m, " + segments + " sections).");
            }
            if (length > 256f && !loggedFirstLongCable)
            {
                loggedFirstLongCable = true;
                Log.Out("[DSZipline] Built over-256 m cable mesh (" +
                        length.ToString("0.0") + " m); vanilla render limit bypassed.");
            }
        }

        private void OnDestroy()
        {
            if (cableMesh != null) Destroy(cableMesh);
            if (cableMaterial != null) Destroy(cableMaterial);
        }
    }
}
