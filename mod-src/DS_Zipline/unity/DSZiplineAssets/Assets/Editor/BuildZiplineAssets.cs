using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BuildZiplineAssets
{
    private const string ModelFolder = "Assets/Models";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string MaterialFolder = "Assets/Materials";
    private const string BundleName = "dsziplineassets.unity3d";

    private sealed class PrefabSpec
    {
        public string Name;
        public string ModelPath;
        public bool AddAnchorCollider;
    }

    private static readonly PrefabSpec[] Specs =
    {
        new PrefabSpec { Name = "DSZiplineAnchor", ModelPath = ModelFolder + "/DSZiplineAnchor.fbx", AddAnchorCollider = true },
        new PrefabSpec { Name = "DSZiplineWoodAnchor", ModelPath = ModelFolder + "/DSZiplineWoodAnchor.fbx", AddAnchorCollider = true },
        new PrefabSpec { Name = "DSZiplineTrolley", ModelPath = ModelFolder + "/DSZiplineTrolley.fbx" },
        new PrefabSpec { Name = "DSZiplineCableReference", ModelPath = ModelFolder + "/DSZiplineCableReference.fbx" },
    };

    [MenuItem("AfterHours/Build Zipline Assets")]
    public static void BuildAll()
    {
        PreparePrefabs();

        string dist = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "art", "dist"));
        RecreateDirectory(dist);
        BuildForTarget(dist, "windows", BuildTarget.StandaloneWindows64);
        BuildForTarget(dist, "linux", BuildTarget.StandaloneLinux64);
        ValidateManifest(Path.Combine(dist, "linux", BundleName + ".manifest"));

        Debug.Log("DSZIPLINE_ASSETS_BUILT=" + dist);
    }

    public static void PrepareOnly()
    {
        PreparePrefabs();
        Debug.Log("DSZIPLINE_PREFABS_PREPARED");
    }

    private static void PreparePrefabs()
    {
        ConfigureModelImporters();
        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);

        foreach (PrefabSpec spec in Specs)
            CreatePrefab(spec);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void ConfigureModelImporters()
    {
        foreach (PrefabSpec spec in Specs)
        {
            var importer = AssetImporter.GetAtPath(spec.ModelPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("Missing FBX model: " + spec.ModelPath);

            importer.globalScale = 1f;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.bakeAxisConversion = true;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
        }
    }

    private static void CreatePrefab(PrefabSpec spec)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
        if (modelAsset == null)
            throw new InvalidOperationException("Unity could not load " + spec.ModelPath);

        GameObject root = new GameObject(spec.Name);
        GameObject model = UnityEngine.Object.Instantiate(modelAsset, root.transform, false);
        model.name = spec.Name + "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        ReplaceEmbeddedMaterials(model, spec.Name);

        if (spec.AddAnchorCollider)
        {
            // The post is 2.718 m high after Blender normalization. A modestly
            // widened box is easier to interact with than its 11 cm steel beam.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 1.35918f, 0f);
            collider.size = new Vector3(0.35f, 2.71836f, 0.70f);
        }

        string prefabPath = PrefabFolder + "/" + spec.Name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Bounds bounds = CalculateRendererBounds(saved);
        Debug.Log(string.Format("DSZIPLINE_PREFAB={0} bounds={1} center={2}", spec.Name, bounds.size, bounds.center));
    }

    private static void ReplaceEmbeddedMaterials(GameObject model, string prefix)
    {
        var created = new Dictionary<Material, Material>();
        int index = 0;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] replacements = renderer.sharedMaterials;
            for (int i = 0; i < replacements.Length; i++)
            {
                Material source = replacements[i];
                if (source == null)
                    continue;
                if (!created.TryGetValue(source, out Material replacement))
                {
                    string safeName = Sanitize(source.name);
                    string path = string.Format("{0}/{1}_{2:D2}_{3}.mat", MaterialFolder, prefix, index++, safeName);
                    replacement = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (replacement == null)
                    {
                        replacement = new Material(Shader.Find("Standard"));
                        replacement.name = Path.GetFileNameWithoutExtension(path);
                        AssetDatabase.CreateAsset(replacement, path);
                    }
                    else
                    {
                        replacement.shader = Shader.Find("Standard");
                    }
                    if (source.HasProperty("_Color")) replacement.color = source.color;
                    if (source.HasProperty("_Metallic")) replacement.SetFloat("_Metallic", source.GetFloat("_Metallic"));
                    if (source.HasProperty("_Glossiness")) replacement.SetFloat("_Glossiness", source.GetFloat("_Glossiness"));
                    EditorUtility.SetDirty(replacement);
                    created.Add(source, replacement);
                }
                replacements[i] = replacement;
            }
            renderer.sharedMaterials = replacements;
        }
    }

    private static void BuildForTarget(string dist, string platform, BuildTarget target)
    {
        string output = Path.Combine(dist, platform);
        RecreateDirectory(output);

        string[] assetNames = Specs.Select(s => PrefabFolder + "/" + s.Name + ".prefab").ToArray();
        string[] addressableNames = Specs.Select(s => s.Name + ".prefab").ToArray();
        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = assetNames,
            addressableNames = addressableNames,
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            output,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle,
            target);
        if (manifest == null)
            throw new InvalidOperationException("AssetBundle build failed for " + target);

        string bundlePath = Path.Combine(output, BundleName);
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException("Expected bundle was not produced", bundlePath);
        Debug.Log(string.Format("DSZIPLINE_BUNDLE={0} bytes={1}", bundlePath, new FileInfo(bundlePath).Length));
    }

    private static void ValidateManifest(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Bundle manifest was not produced", path);
        string text = File.ReadAllText(path);
        foreach (PrefabSpec spec in Specs)
        {
            string expected = PrefabFolder + "/" + spec.Name + ".prefab";
            if (!text.Contains(expected))
                throw new InvalidOperationException("Bundle manifest is missing: " + expected);
        }
        Debug.Log("DSZIPLINE_BUNDLE_ASSETS=" + string.Join(",", Specs.Select(s => s.Name + ".prefab")));
    }

    private static Bounds CalculateRendererBounds(GameObject prefab)
    {
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds();
            Bounds result = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) result.Encapsulate(renderer.bounds);
            return result;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Replace(' ', '_').Replace('.', '_');
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;
        string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        string name = Path.GetFileName(assetPath);
        if (!AssetDatabase.IsValidFolder(parent)) Directory.CreateDirectory(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }
}
