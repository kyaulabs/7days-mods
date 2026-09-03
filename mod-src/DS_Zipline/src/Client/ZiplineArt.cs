using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace DSZipline
{
    /// <summary>
    /// Loads compact Blender-exported geometry and constructs Unity meshes at runtime.
    /// This deliberately avoids Unity AssetBundles: 7DTD V3.2 uses a respin-incompatible
    /// 2022.3.62f2 player even though the editor reports the same public version.
    /// </summary>
    public static class ZiplineArt
    {
        public const string MeshFileName = "dszipline.meshbin";

        private sealed class Model
        {
            public Mesh Mesh;
            public Material[] Materials;
        }

        private static readonly Dictionary<string, Model> Models = new Dictionary<string, Model>();

        public static void Initialize(string modPath)
        {
            string path = Path.Combine(modPath, "Resources", MeshFileName);
            try
            {
                Load(path);
                Log.Out("[DSZipline] Runtime zipline meshes loaded from " + path);
            }
            catch (Exception error)
            {
                Models.Clear();
                Log.Error("[DSZipline] Could not load custom zipline meshes: " + error);
            }
        }

        public static Transform CreateTrolley()
        {
            return CreateVisual("DSZiplineTrolley", "DSZiplineTrolley_Ride")?.transform;
        }

        public static GameObject CreateToolVisual()
        {
            return CreateVisual("DSZiplineTool", "DSZiplineTool_CustomModel");
        }

        public static bool ApplyToolVisual(Transform root, BlockShape.MeshPurpose purpose)
        {
            if (root == null) return false;
            Transform custom = root.Find("DSZiplineTool_CustomModel");
            if (custom == null)
            {
                GameObject visual = CreateToolVisual();
                if (visual == null) return false;
                custom = visual.transform;
                custom.SetParent(root, false);
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool belongsToCustom = renderer.transform == custom || renderer.transform.IsChildOf(custom);
                renderer.enabled = belongsToCustom;
            }

            // The pivot is in one black grip and source -X points toward the jaws.
            // Local (first person) and Hold (third person/world) inherit different
            // wire-tool coordinate frames, so each needs its own presentation tilt.
            bool firstPerson = purpose == BlockShape.MeshPurpose.Local;
            // The inherited wire-tool model origins sit just above the rendered palm
            // in both rigs. Offset the grip itself into the closed right hand; their
            // opposite root frames require opposite local-Z signs.
            custom.localPosition = firstPerson
                ? new Vector3(0f, 0f, -0.065f)
                : new Vector3(0f, 0f, 0.065f);
            Quaternion meshForward = Quaternion.Euler(0f, 90f, 0f);
            // The cutters are carried upright: black grips start in the hand and the
            // jaws rise above it. Local's root maps +Z upward; Hold maps +Z downward,
            // so the world model needs a 180° X reversal rather than a lateral yaw.
            custom.localRotation = firstPerson
                ? meshForward
                : Quaternion.AngleAxis(180f, Vector3.right) * meshForward;
            custom.localScale = Vector3.one;
            custom.gameObject.SetActive(true);
            SetLayerRecursively(custom, root.gameObject.layer);
            return true;
        }

        public static GameObject CreateAnchorVisual(bool sonicTier)
        {
            return sonicTier
                ? CreateVisual("DSZiplineAnchor", "DSZiplineAnchor_CustomModel")
                : CreateVisual("DSZiplineWoodAnchor", "DSZiplineWoodAnchor_CustomModel");
        }

        public static bool ApplyAnchorVisual(
            GameObject root,
            bool sonicTier,
            bool configureInteractionCollider = true)
        {
            if (root == null) return false;

            string modelName = sonicTier ? "DSZiplineAnchor" : "DSZiplineWoodAnchor";
            string objectName = sonicTier
                ? "DSZiplineAnchor_CustomModel"
                : "DSZiplineWoodAnchor_CustomModel";
            string otherObjectName = sonicTier
                ? "DSZiplineWoodAnchor_CustomModel"
                : "DSZiplineAnchor_CustomModel";

            // The fallback electric-fence prefab carries an LODGroup which can
            // re-enable its renderer when nearby geometry (notably an opening door)
            // invalidates culling state. Disable every inherited LOD controller;
            // the one-mesh runtime anchors do not need prefab LOD switching.
            foreach (LODGroup lodGroup in root.GetComponentsInChildren<LODGroup>(true))
                lodGroup.enabled = false;

            Transform custom = root.transform.Find(objectName);
            Transform otherCustom = root.transform.Find(otherObjectName);
            if (otherCustom != null) otherCustom.gameObject.SetActive(false);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = custom != null &&
                                   (renderer.transform == custom || renderer.transform.IsChildOf(custom));
            }

            if (custom == null)
            {
                GameObject visual = CreateAnchorVisual(sonicTier);
                if (visual == null) return false;
                custom = visual.transform;
                custom.SetParent(root.transform, false);
            }

            // ModelEntity roots are centered half a block above the supporting
            // surface. Both meshes are grounded at y=0. The Sonic source needs its
            // proven socket-centering correction; the purpose-built wooden eye is
            // already centered exactly on the procedural endpoint.
            custom.localPosition = sonicTier
                ? new Vector3(0.07513f, -0.5f, 0.01125f)
                : new Vector3(0f, -0.5f, 0f);
            custom.localRotation = sonicTier
                ? Quaternion.Euler(0f, 90f, 0f)
                : Quaternion.identity;
            custom.localScale = Vector3.one;
            custom.gameObject.SetActive(true);

            // Reassert shared runtime assets after every block-value change. The
            // vanilla ModelEntity damage path is written for the fallback prefab
            // and may otherwise leave its damage material/state on our renderer.
            if (Models.TryGetValue(modelName, out Model anchorModel))
            {
                MeshFilter filter = custom.GetComponent<MeshFilter>();
                MeshRenderer meshRenderer = custom.GetComponent<MeshRenderer>();
                if (filter != null) filter.sharedMesh = anchorModel.Mesh;
                if (meshRenderer != null && !HasIntactMaterials(meshRenderer, anchorModel))
                    meshRenderer.sharedMaterials = CreateMaterialInstances(anchorModel);
            }

            if (configureInteractionCollider)
            {
                // Physics raycasts take precedence over BlockShape bounds. Preserve
                // the root collider's proven T_Block/layer/master-block association,
                // but replace its electric-fence geometry with the visible anchor.
                BoxCollider interactionCollider = root.GetComponent<BoxCollider>();
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    collider.enabled = collider == interactionCollider;
                if (interactionCollider == null)
                    interactionCollider = root.AddComponent<BoxCollider>();

                if (sonicTier)
                {
                    interactionCollider.center = new Vector3(0.37599f, 0.85918f, 0.01229f);
                    interactionCollider.size = new Vector3(0.18f, 2.71836f, 0.18f);
                }
                else
                {
                    interactionCollider.center = new Vector3(0f, 0.825f, 0f);
                    interactionCollider.size = new Vector3(0.78f, 2.65f, 0.40f);
                }
                interactionCollider.isTrigger = false;
                interactionCollider.enabled = true;
            }

            // BlockShapeModelEntity applies tint/damage values through a
            // MaterialPropertyBlock. Clear them after final activation; actual
            // model colors live in textures and these models have no damage shader.
            foreach (Renderer renderer in custom.GetComponentsInChildren<Renderer>(true))
            {
                renderer.SetPropertyBlock(null);
                renderer.enabled = true;
            }
            return true;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private static GameObject CreateVisual(string modelName, string objectName)
        {
            if (!Models.TryGetValue(modelName, out Model model)) return null;

            var visual = new GameObject(objectName);
            visual.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterials = CreateMaterialInstances(model);
            return visual;
        }

        private static Material[] CreateMaterialInstances(Model model)
        {
            var instances = new Material[model.Materials.Length];
            for (int i = 0; i < instances.Length; i++)
            {
                instances[i] = new Material(model.Materials[i])
                {
                    name = model.Materials[i].name + "_Instance"
                };
            }
            return instances;
        }

        private static bool HasIntactMaterials(MeshRenderer renderer, Model model)
        {
            Material[] current = renderer.sharedMaterials;
            if (current == null || current.Length != model.Materials.Length) return false;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] == null || current[i].shader != model.Materials[i].shader ||
                    current[i].mainTexture != model.Materials[i].mainTexture)
                    return false;
            }
            return true;
        }

        private static void Load(string path)
        {
            Models.Clear();
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                string magic = new string(reader.ReadChars(4));
                if (magic != "DSZM") throw new InvalidDataException("invalid mesh file magic");
                int version = reader.ReadInt32();
                if (version != 1) throw new InvalidDataException("unsupported mesh file version " + version);
                int modelCount = CheckedCount(reader.ReadInt32(), 16, "model");

                for (int modelIndex = 0; modelIndex < modelCount; modelIndex++)
                {
                    string name = ReadString(reader);
                    int vertexCount = CheckedCount(reader.ReadInt32(), 1_000_000, "vertex");
                    var vertices = new Vector3[vertexCount];
                    var normals = new Vector3[vertexCount];
                    var uvs = new Vector2[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    }

                    int materialCount = CheckedCount(reader.ReadInt32(), 128, "material");
                    var materials = new Material[materialCount];
                    for (int i = 0; i < materialCount; i++)
                    {
                        Color color = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        float metallic = reader.ReadSingle();
                        float smoothness = reader.ReadSingle();
                        materials[i] = CreateMaterial(name + "_" + i, color, metallic, smoothness);
                    }

                    var triangles = new int[materialCount][];
                    for (int submesh = 0; submesh < materialCount; submesh++)
                    {
                        int indexCount = CheckedCount(reader.ReadInt32(), 3_000_000, "index");
                        if (indexCount % 3 != 0) throw new InvalidDataException("triangle index count is not divisible by three");
                        triangles[submesh] = new int[indexCount];
                        for (int i = 0; i < indexCount; i++)
                        {
                            int index = reader.ReadInt32();
                            if ((uint)index >= (uint)vertexCount) throw new InvalidDataException("vertex index is out of range");
                            triangles[submesh][i] = index;
                        }
                    }

                    var mesh = new Mesh
                    {
                        name = name + "_RuntimeMesh",
                        indexFormat = vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                        subMeshCount = materialCount
                    };
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.uv = uvs;
                    for (int submesh = 0; submesh < materialCount; submesh++)
                        mesh.SetTriangles(triangles[submesh], submesh, false);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    Models.Add(name, new Model { Mesh = mesh, Materials = materials });
                    Log.Out("[DSZipline] Loaded " + name + " mesh: " + vertexCount + " vertices, bounds " + mesh.bounds.size);
                }

                if (stream.Position != stream.Length)
                    throw new InvalidDataException("unexpected trailing mesh data");
            }

            ConfigureToolMaterials(Path.GetDirectoryName(path));
            ConfigureWoodAnchorMaterials();
        }

        private static void ConfigureToolMaterials(string resources)
        {
            if (!Models.TryGetValue("DSZiplineTool", out Model tool))
                throw new InvalidDataException("runtime payload is missing DSZiplineTool");

            string directory = Path.Combine(resources, "tool");
            Texture2D albedo = LoadTexture(Path.Combine(directory, "tool_albedo.jpg"), false);
            Texture2D normal = LoadTexture(Path.Combine(directory, "tool_normal_dxt.png"), true);
            Texture2D metallic = LoadTexture(Path.Combine(directory, "tool_metallic_smoothness.png"), true);
            Texture2D occlusion = LoadTexture(Path.Combine(directory, "tool_ao.jpg"), true);

            foreach (Material material in tool.Materials)
            {
                material.color = Color.white;
                material.mainTexture = albedo;
                if (material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", 1f);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (material.HasProperty("_MetallicGlossMap"))
                {
                    material.SetTexture("_MetallicGlossMap", metallic);
                    material.SetFloat("_Metallic", 1f);
                    material.SetFloat("_GlossMapScale", 1f);
                    material.EnableKeyword("_METALLICGLOSSMAP");
                }
                if (material.HasProperty("_OcclusionMap"))
                {
                    material.SetTexture("_OcclusionMap", occlusion);
                    material.SetFloat("_OcclusionStrength", 1f);
                }
            }
            Log.Out("[DSZipline] Loaded Zipline Tool PBR textures from " + directory);
        }

        private static void ConfigureWoodAnchorMaterials()
        {
            if (!Models.TryGetValue("DSZiplineWoodAnchor", out Model wood) ||
                wood.Materials == null || wood.Materials.Length < 1)
                throw new InvalidDataException("runtime payload is missing DSZiplineWoodAnchor materials");

            const int width = 64;
            const int height = 256;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Deterministic weathered grain: long vertical waves, fine
                    // saw marks, dark cracks, and occasional knots. Keeping this
                    // runtime-generated avoids shipping a flat brown reskin while
                    // remaining reproducible and independent of vanilla assets.
                    uint hash = (uint)(x * 374761393 + y * 668265263);
                    hash = (hash ^ (hash >> 13)) * 1274126177u;
                    float noise = ((hash >> 24) / 255f - 0.5f) * 16f;
                    float wave = Mathf.Sin(x * 0.42f + Mathf.Sin(y * 0.055f) * 2.6f) * 12f;
                    float fine = Mathf.Sin(x * 1.35f + y * 0.025f) * 4f;
                    float knotDx = x - 21f - Mathf.Sin(y * 0.031f) * 5f;
                    float knotDy = (y % 117) - 58f;
                    float knot = Mathf.Exp(-(knotDx * knotDx * 0.035f + knotDy * knotDy * 0.004f)) * -28f;
                    float crack = ((hash & 1023u) < 5u && Mathf.Abs(wave) > 7f) ? -30f : 0f;
                    float value = noise + wave + fine + knot + crack;
                    float red = Mathf.Clamp(104f + value, 38f, 145f);
                    float green = Mathf.Clamp(67f + value * 0.72f, 28f, 105f);
                    float blue = Mathf.Clamp(36f + value * 0.42f, 16f, 72f);
                    // Broad dried-mud smears plus sparse embedded grit keep the
                    // procedural grain from reading like newly milled lumber.
                    float smear = Mathf.Clamp01(
                        0.18f + Mathf.Sin(x * 0.19f + y * 0.041f) * 0.16f +
                        Mathf.Sin(x * 0.057f - y * 0.083f) * 0.14f);
                    float grit = ((hash >> 11) & 255u) > 248u ? 0.42f : 0f;
                    float dirt = Mathf.Clamp01(smear + grit);
                    pixels[y * width + x] = new Color32(
                        (byte)Mathf.Lerp(red, 42f, dirt),
                        (byte)Mathf.Lerp(green, 30f, dirt),
                        (byte)Mathf.Lerp(blue, 18f, dirt),
                        255);
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGB24, true, false)
            {
                name = "DSZiplineWeatheredWood",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            wood.Materials[0].color = Color.white;
            wood.Materials[0].mainTexture = texture;
            if (wood.Materials[0].HasProperty("_Glossiness"))
                wood.Materials[0].SetFloat("_Glossiness", 0.18f);
            Log.Out("[DSZipline] Generated weathered wood anchor material.");
        }

        private static Texture2D LoadTexture(string path, bool linear)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("missing runtime texture", path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear)
            {
                name = Path.GetFileNameWithoutExtension(path),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            if (!texture.LoadImage(File.ReadAllBytes(path), true))
                throw new InvalidDataException("could not decode runtime texture " + path);
            return texture;
        }

        private static Material CreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) throw new InvalidOperationException("no compatible built-in mesh shader found");

            // Block models receive a white MaterialPropertyBlock _Color which
            // overrides material.color. Keep _Color white and carry the palette in
            // a texture. Anchor textures add deterministic soot, mud, and oxidized
            // speckling so neither tier looks freshly manufactured.
            bool anchorMaterial = name.StartsWith("DSZiplineAnchor_", StringComparison.Ordinal) ||
                                  name.StartsWith("DSZiplineWoodAnchor_", StringComparison.Ordinal);
            Texture2D texture = anchorMaterial
                ? CreateWeatheredAnchorTexture(name, color, metallic)
                : CreateSolidColorTexture(name, color);

            var material = new Material(shader) { name = name, color = Color.white, mainTexture = texture };
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
            return material;
        }

        private static Texture2D CreateSolidColorTexture(string name, Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
            {
                name = name + "_Color",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateWeatheredAnchorTexture(string name, Color color, float metallic)
        {
            const int size = 64;
            uint seed = 2166136261u;
            for (int i = 0; i < name.Length; i++)
                seed = (seed ^ name[i]) * 16777619u;

            var pixels = new Color32[size * size];
            Color dirtColor = metallic > 0.35f
                ? new Color(0.24f, 0.075f, 0.018f, 1f)
                : new Color(0.095f, 0.058f, 0.025f, 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    uint hash = Hash(seed ^ (uint)(x * 374761393 + y * 668265263));
                    float random = (hash >> 24) / 255f;
                    float broad = 0.5f +
                        Mathf.Sin(x * 0.21f + y * 0.087f) * 0.24f +
                        Mathf.Sin(x * 0.061f - y * 0.17f) * 0.18f;
                    float grime = Mathf.Clamp01((broad - 0.42f) * 0.72f +
                                                (random > 0.90f ? (random - 0.90f) * 5f : 0f));
                    float abrasion = 0.78f + random * 0.22f;
                    Color worn = new Color(
                        color.r * abrasion, color.g * abrasion,
                        color.b * abrasion, color.a);
                    Color pixel = Color.Lerp(worn, dirtColor, grime * 0.62f);

                    // Rare warm flecks suggest exposed oxidation on the metallic
                    // hardware without turning the red Sonic panels uniformly brown.
                    if (metallic > 0.35f && (hash & 1023u) < 13u)
                        pixel = Color.Lerp(pixel, new Color(0.42f, 0.11f, 0.025f, 1f), 0.68f);
                    pixels[y * size + x] = pixel;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGB24, true, true)
            {
                name = name + "_Weathered",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = CheckedCount(reader.ReadInt32(), 1024, "string byte");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static int CheckedCount(int value, int maximum, string label)
        {
            if (value < 0 || value > maximum) throw new InvalidDataException(label + " count is invalid: " + value);
            return value;
        }
    }

    /// <summary>Replace the inherited wire-tool hand meshes after cloning.</summary>
    [HarmonyPatch(typeof(ItemClass), nameof(ItemClass.CloneModel), new Type[]
    {
        typeof(World), typeof(ItemValue), typeof(Vector3), typeof(Transform),
        typeof(BlockShape.MeshPurpose), typeof(TextureFullArray)
    })]
    public static class ZiplineToolModelPatch
    {
        private static bool loggedFirstReplacement;

        public static void Postfix(ItemClass __instance, BlockShape.MeshPurpose _purpose, Transform __result)
        {
            if (__instance == null || __instance.GetItemName() != "DSZiplineTool" ||
                (_purpose != BlockShape.MeshPurpose.Hold && _purpose != BlockShape.MeshPurpose.Local) ||
                !ZiplineArt.ApplyToolVisual(__result, _purpose))
                return;

            if (!loggedFirstReplacement)
            {
                loggedFirstReplacement = true;
                Log.Out("[DSZipline] Applied custom held Zipline Tool model (purpose " + _purpose + ").");
            }
        }
    }

    /// <summary>
    /// The custom runtime material has no vanilla block-damage shader contract.
    /// Suppress the fallback's _Damage property update; the value can turn all
    /// renderers sharing the runtime materials into Unity's magenta error state.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityData), nameof(BlockEntityData.SetMaterialValue))]
    public static class ZiplineAnchorDamageMaterialPatch
    {
        private static readonly System.Reflection.FieldInfo BlockValueField =
            AccessTools.Field(typeof(BlockEntityData), "blockValue");

        public static bool Prefix(BlockEntityData __instance, string name)
        {
            if (name != "_Damage" || __instance == null || BlockValueField == null)
                return true;
            return !(BlockValueField.GetValue(__instance) is BlockValue value) ||
                   !(value.Block is BlockZiplineAnchor);
        }
    }

    /// <summary>
    /// Placement previews never reach final BlockEntity activation. Replace the
    /// fallback immediately after CloneModel, but leave interaction-collider setup
    /// to the final activation hook for actual placed blocks.
    /// </summary>
    [HarmonyPatch(typeof(BlockShapeModelEntity), nameof(BlockShapeModelEntity.CloneModel))]
    public static class ZiplineAnchorPreviewModelPatch
    {
        public static void Postfix(BlockValue _blockValue, Transform __result)
        {
            if (_blockValue.Block is BlockZiplineAnchor anchor && __result != null)
                ZiplineArt.ApplyAnchorVisual(__result.gameObject, anchor.IsSonicTier, false);
        }
    }

    /// <summary>
    /// Prepare the actual block entity while it is still inactive. A later postfix
    /// handles the activation boundary where vanilla can alter renderer state.
    /// </summary>
    [HarmonyPatch(typeof(BlockShapeModelEntity), nameof(BlockShapeModelEntity.OnBlockEntityTransformBeforeActivated))]
    public static class ZiplineAnchorActivatedModelPatch
    {
        private static readonly System.Reflection.FieldInfo TransformField =
            AccessTools.Field(typeof(BlockEntityData), "transform");
        private static bool loggedFirstReplacement;

        public static void Postfix(Vector3i _blockPos, BlockValue _blockValue, BlockEntityData _ebcd)
        {
            if (!(_blockValue.Block is BlockZiplineAnchor anchor) || _ebcd == null || TransformField == null)
                return;

            Transform root = TransformField.GetValue(_ebcd) as Transform;
            if (root == null || !ZiplineArt.ApplyAnchorVisual(root.gameObject, anchor.IsSonicTier)) return;

            _ebcd.Cleanup();
            if (!loggedFirstReplacement)
            {
                loggedFirstReplacement = true;
                Log.Out("[DSZipline] Applied custom anchor renderer at " + _blockPos);
            }
        }
    }

    /// <summary>
    /// Block entities are activated after the shape's BeforeActivated callback.
    /// Reassert the visual after that boundary so prefab OnEnable/LOD behavior
    /// cannot restore the electric-fence renderer.
    /// </summary>
    [HarmonyPatch(typeof(BlockPowered), nameof(BlockPowered.OnBlockEntityTransformAfterActivated))]
    public static class ZiplineAnchorAfterActivatedPatch
    {
        public static void Postfix(BlockValue _blockValue, BlockEntityData _ebcd)
        {
            if (!(_blockValue.Block is BlockZiplineAnchor anchor) || _ebcd?.transform == null)
                return;
            if (ZiplineArt.ApplyAnchorVisual(_ebcd.transform.gameObject, anchor.IsSonicTier))
                _ebcd.Cleanup();
        }
    }

    /// <summary>
    /// Chunk occlusion/render toggles blindly set every child MeshRenderer enabled.
    /// Opening a nearby door triggers this path and used to reveal the inherited
    /// electric-fence mesh. Restore the selected runtime renderer immediately.
    /// </summary>
    [HarmonyPatch(typeof(Chunk), nameof(Chunk.SetBlockEntityRendering))]
    public static class ZiplineAnchorChunkRenderingPatch
    {
        public static void Postfix(BlockEntityData _bed, bool _bOn)
        {
            if (!_bOn || !(_bed?.blockValue.Block is BlockZiplineAnchor anchor) ||
                _bed.transform == null)
                return;
            if (ZiplineArt.ApplyAnchorVisual(_bed.transform.gameObject, anchor.IsSonicTier))
                _bed.Cleanup();
        }
    }

    /// <summary>
    /// Damage changes run the vanilla fallback's ModelEntity update again. Reapply
    /// the runtime model afterward so fallback renderers/colliders stay disabled,
    /// custom materials are restored, and stale property blocks are removed.
    /// </summary>
    [HarmonyPatch(typeof(BlockShapeModelEntity), nameof(BlockShapeModelEntity.OnBlockValueChanged))]
    public static class ZiplineAnchorValueChangedPatch
    {
        private static readonly System.Reflection.FieldInfo TransformField =
            AccessTools.Field(typeof(BlockEntityData), "transform");
        private static bool loggedFirstRepair;

        public static void Postfix(WorldBase _world, Vector3i _blockPos, BlockValue _newBlockValue)
        {
            if (!(_newBlockValue.Block is BlockZiplineAnchor anchor) || _world?.ChunkCache == null)
                return;

            Vector3i entityPos = _blockPos;
            if (_newBlockValue.ischild && anchor.multiBlockPos != null)
                entityPos = anchor.multiBlockPos.GetParentPos(_blockPos, _newBlockValue);

            Chunk chunk = _world.ChunkCache.GetChunkFromWorldPos(
                entityPos.x, entityPos.y, entityPos.z) as Chunk;
            BlockEntityData data = chunk?.GetBlockEntity(entityPos);
            Transform root = data != null && TransformField != null
                ? TransformField.GetValue(data) as Transform
                : null;
            if (root == null || !ZiplineArt.ApplyAnchorVisual(root.gameObject, anchor.IsSonicTier)) return;
            data.Cleanup();
            if (!loggedFirstRepair)
            {
                loggedFirstRepair = true;
                Log.Out("[DSZipline] Reapplied custom anchor after damage at " + entityPos);
            }
        }
    }
}
