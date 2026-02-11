using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace FontPatcher.Editor
{
    [Serializable]
    internal sealed class JobManifest
    {
        public string fontAssetPath = "";
        public string unityOutputDirAssetPath = "Assets/Generated";
        public string absoluteBundleOutputDir = "";
        public string assetBundleName = "fontbundle";
        public string tmpAssetName = "TMP_Font";
        public string buildTarget = "StandaloneWindows64";
        public int[] atlasSizes = new[] { 1024, 2048, 4096 };
        public int samplingPointSize = 90;
        public int padding = 8;
        public int scanUpperBound = 1114111;
        public bool forceDynamic;
        public bool forceStatic;
        public bool includeControlCharacters;
        public int dynamicWarmupLimit = 20000;
        public int dynamicWarmupBatchSize = 1024;
    }

    internal sealed class BuildPlan
    {
        public bool UseDynamic;
        public int AtlasSize;
        public int SamplingPointSize;
    }

    public static class FontBundleBuilder
    {
        public static void Run()
        {
            try
            {
                string manifestPath = GetArgument("--job-manifest");
                if (string.IsNullOrEmpty(manifestPath))
                {
                    throw new InvalidOperationException("Missing --job-manifest argument.");
                }

                if (!File.Exists(manifestPath))
                {
                    throw new FileNotFoundException("Job manifest not found.", manifestPath);
                }

                string json = File.ReadAllText(manifestPath);
                JobManifest job = JsonUtility.FromJson<JobManifest>(json);
                if (job == null)
                {
                    throw new InvalidOperationException("Unable to parse job manifest.");
                }

                Execute(job);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void Execute(JobManifest job)
        {
            EnsureTextMeshProResources();
            EnsureAssetFolder(job.unityOutputDirAssetPath);
            Directory.CreateDirectory(job.absoluteBundleOutputDir);

            Font font = AssetDatabase.LoadAssetAtPath<Font>(NormalizeAssetPath(job.fontAssetPath));
            if (font == null)
            {
                throw new InvalidOperationException("Cannot load font asset: " + job.fontAssetPath);
            }

            List<uint> codePoints = ScanAvailableCodePoints(font, job);
            if (codePoints.Count == 0)
            {
                throw new InvalidOperationException("No glyphs were discovered in the source font.");
            }

            BuildPlan plan = CreatePlan(job, codePoints.Count);
            Debug.Log(string.Format(
                "FontPatcher: glyphs={0}, mode={1}, atlas={2}, pointSize={3}",
                codePoints.Count,
                plan.UseDynamic ? "dynamic" : "static",
                plan.AtlasSize,
                plan.SamplingPointSize));

            TMP_FontAsset tmpAsset = plan.UseDynamic
                ? CreateDynamicAsset(font, plan, job)
                : CreateStaticAsset(font, plan, job);

            if (!plan.UseDynamic)
            {
                uint[] missing;
                bool staticSuccess = tmpAsset.TryAddCharacters(codePoints.ToArray(), out missing);
                if (!staticSuccess || (missing != null && missing.Length > 0))
                {
                    Debug.LogWarning("Static atlas did not fit all glyphs. Falling back to dynamic multi-atlas.");
                    tmpAsset = CreateDynamicAsset(font, plan, job);
                    WarmupDynamicAsset(tmpAsset, codePoints, job.dynamicWarmupLimit, job.dynamicWarmupBatchSize);
                }
            }
            else
            {
                WarmupDynamicAsset(tmpAsset, codePoints, job.dynamicWarmupLimit, job.dynamicWarmupBatchSize);
            }

            tmpAsset.name = job.tmpAssetName;
            string tmpAssetPath = NormalizeAssetPath(job.unityOutputDirAssetPath) + "/" + job.tmpAssetName + ".asset";
            PersistTmpAsset(tmpAsset, tmpAssetPath);

            BuildAssetBundle(job, tmpAssetPath);
            Debug.Log("FontPatcher: success");
        }

        private static void EnsureTextMeshProResources()
        {
            bool hasSettings = File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset");
            if (!hasSettings)
            {
                Debug.Log("FontPatcher: importing TMP Essential Resources.");
                TryImportTmpResources();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            Shader sdfShader = ResolveShader(
                new[]
                {
                    "TextMeshPro/Distance Field",
                    "TextMeshPro/Mobile/Distance Field",
                    "TextMeshPro/Distance Field SSD",
                    "TextMeshPro/Distance Field Overlay"
                },
                new[]
                {
                    "Assets/TextMesh Pro/Shaders/TMP_SDF.shader",
                    "Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader",
                    "Assets/TextMesh Pro/Shaders/TMP_SDF SSD.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_SDF.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_SDF-Mobile.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_SDF SSD.shader"
                },
                "TMP_SDF");
            Shader bitmapShader = ResolveShader(
                new[]
                {
                    "TextMeshPro/Bitmap",
                    "TextMeshPro/Mobile/Bitmap"
                },
                new[]
                {
                    "Assets/TextMesh Pro/Shaders/TMP_Bitmap.shader",
                    "Assets/TextMesh Pro/Shaders/TMP_Bitmap-Mobile.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_Bitmap.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_Bitmap-Mobile.shader"
                },
                "TMP_Bitmap");

            if (sdfShader == null)
            {
                throw new InvalidOperationException(
                    "TMP SDF shader is missing after importing TMP resources.");
            }

            // In batchmode on some Unity versions, Shader.Find can stay null until TMP internal refs are bound explicitly.
            SetShaderReference(
                new[] { "k_ShaderRef_MobileSDF", "k_ShaderRef_SDF", "k_ShaderRef_Properties" },
                sdfShader);

            if (bitmapShader != null)
            {
                SetShaderReference(
                    new[] { "k_ShaderRef_MobileBitmap", "k_ShaderRef_Bitmap", "k_ShaderRef_Sprite" },
                    bitmapShader);
            }

            ForceInitializeShaderUtilities();
        }

        private static void TryImportTmpResources()
        {
            Type importerType = FindTypeByFullName("TMPro.TMP_PackageResourceImporter");
            if (importerType == null)
            {
                return;
            }

            MethodInfo importMethod = importerType.GetMethod(
                "ImportResources",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (importMethod == null)
            {
                return;
            }

            ParameterInfo[] parameters = importMethod.GetParameters();
            object[] args = parameters.Length switch
            {
                3 => new object[] { true, false, false },
                2 => new object[] { true, false },
                1 => new object[] { true },
                _ => Array.Empty<object>()
            };

            importMethod.Invoke(null, args);
        }

        private static Type FindTypeByFullName(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Shader ResolveShader(string[] shaderNames, string[] assetPaths, string searchHint)
        {
            foreach (string shaderName in shaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    return shader;
                }
            }

            foreach (string assetPath in assetPaths)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader != null)
                {
                    return shader;
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:Shader " + searchHint);
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static void SetShaderReference(string[] fieldNames, Shader shader)
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string fieldName in fieldNames)
            {
                FieldInfo field = typeof(ShaderUtilities).GetField(fieldName, flags);
                if (field == null)
                {
                    continue;
                }

                if (field.FieldType != typeof(Shader))
                {
                    continue;
                }

                field.SetValue(null, shader);
            }
        }

        private static void ForceInitializeShaderUtilities()
        {
            MethodInfo method = typeof(ShaderUtilities).GetMethod(
                "GetShaderPropertyIDs",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                return;
            }

            method.Invoke(null, null);
        }

        private static void PersistTmpAsset(TMP_FontAsset tmpAsset, string tmpAssetPath)
        {
            AssetDatabase.CreateAsset(tmpAsset, tmpAssetPath);
            EnsureRuntimeMaterial(tmpAsset);
            AddSubAssetIfNeeded(tmpAsset.material, tmpAssetPath, tmpAsset.name + " Material");

            Texture2D[] atlasTextures = tmpAsset.atlasTextures;
            if (atlasTextures != null)
            {
                for (int i = 0; i < atlasTextures.Length; i++)
                {
                    Texture2D atlasTexture = atlasTextures[i];
                    AddSubAssetIfNeeded(atlasTexture, tmpAssetPath, tmpAsset.name + " Atlas " + i);
                }
            }

            AddSubAssetIfNeeded(tmpAsset.atlasTexture, tmpAssetPath, tmpAsset.name + " Atlas");

            tmpAsset.ReadFontAssetDefinition();
            EditorUtility.SetDirty(tmpAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(tmpAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        private static void EnsureRuntimeMaterial(TMP_FontAsset tmpAsset)
        {
            Shader sdfShader = ResolveShader(
                new[]
                {
                    "TextMeshPro/Distance Field",
                    "TextMeshPro/Mobile/Distance Field",
                    "TextMeshPro/Distance Field SSD",
                    "TextMeshPro/Distance Field Overlay"
                },
                new[]
                {
                    "Assets/TextMesh Pro/Shaders/TMP_SDF.shader",
                    "Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader",
                    "Assets/TextMesh Pro/Shaders/TMP_SDF SSD.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_SDF.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_SDF-Mobile.shader",
                    "Packages/com.unity.textmeshpro/Shaders/TMP_SDF SSD.shader"
                },
                "TMP_SDF");
            if (sdfShader == null)
            {
                return;
            }

            Material material = tmpAsset.material;
            if (material == null)
            {
                material = new Material(sdfShader);
                material.name = tmpAsset.name + " Material";
                tmpAsset.material = material;
                return;
            }

            if (material.shader == null)
            {
                material.shader = sdfShader;
            }
        }

        private static void AddSubAssetIfNeeded(UnityEngine.Object obj, string assetPath, string defaultName)
        {
            if (obj == null)
            {
                return;
            }

            string existingPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(existingPath))
            {
                return;
            }

            if (string.IsNullOrEmpty(obj.name))
            {
                obj.name = defaultName;
            }

            obj.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(obj, assetPath);
            EditorUtility.SetDirty(obj);
        }

        private static void BuildAssetBundle(JobManifest job, string tmpAssetPath)
        {
            BuildTarget target;
            if (!Enum.TryParse(job.buildTarget, true, out target))
            {
                throw new InvalidOperationException("Unknown BuildTarget: " + job.buildTarget);
            }

            AssetBundleBuild build = new AssetBundleBuild();
            build.assetBundleName = job.assetBundleName;
            build.assetNames = new[] { tmpAssetPath };

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                job.absoluteBundleOutputDir,
                new[] { build },
                BuildAssetBundleOptions.StrictMode,
                target);

            if (manifest == null)
            {
                throw new InvalidOperationException("BuildPipeline.BuildAssetBundles returned null.");
            }
        }

        private static TMP_FontAsset CreateStaticAsset(Font font, BuildPlan plan, JobManifest job)
        {
            return CreateFontAssetWithRetry(
                font,
                plan,
                job,
                AtlasPopulationMode.Static,
                false);
        }

        private static TMP_FontAsset CreateDynamicAsset(Font font, BuildPlan plan, JobManifest job)
        {
            TMP_FontAsset asset = CreateFontAssetWithRetry(
                font,
                plan,
                job,
                AtlasPopulationMode.Dynamic,
                true);

            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            asset.isMultiAtlasTexturesEnabled = true;
            return asset;
        }

        private static TMP_FontAsset CreateFontAssetWithRetry(
            Font font,
            BuildPlan plan,
            JobManifest job,
            AtlasPopulationMode atlasMode,
            bool multiAtlas)
        {
            try
            {
                return TMP_FontAsset.CreateFontAsset(
                    font,
                    plan.SamplingPointSize,
                    job.padding,
                    GlyphRenderMode.SDFAA,
                    plan.AtlasSize,
                    plan.AtlasSize,
                    atlasMode,
                    multiAtlas);
            }
            catch (ArgumentNullException ex) when (
                string.Equals(ex.ParamName, "shader", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    "FontPatcher: TMP shader reference was null during CreateFontAsset; retrying after rebind.");
                EnsureTextMeshProResources();
                return TMP_FontAsset.CreateFontAsset(
                    font,
                    plan.SamplingPointSize,
                    job.padding,
                    GlyphRenderMode.SDFAA,
                    plan.AtlasSize,
                    plan.AtlasSize,
                    atlasMode,
                    multiAtlas);
            }
        }

        private static void WarmupDynamicAsset(
            TMP_FontAsset asset,
            List<uint> codePoints,
            int warmupLimit,
            int batchSize)
        {
            if (warmupLimit <= 0 || batchSize <= 0)
            {
                return;
            }

            int limit = Mathf.Min(warmupLimit, codePoints.Count);
            for (int offset = 0; offset < limit; offset += batchSize)
            {
                int count = Mathf.Min(batchSize, limit - offset);
                uint[] batch = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    batch[i] = codePoints[offset + i];
                }

                uint[] ignored;
                asset.TryAddCharacters(batch, out ignored);
            }
        }

        private static List<uint> ScanAvailableCodePoints(Font font, JobManifest job)
        {
            FontEngine.InitializeFontEngine();
            FontEngineError loadError = FontEngine.LoadFontFace(font, job.samplingPointSize);
            if (loadError != FontEngineError.Success)
            {
                throw new InvalidOperationException("FontEngine.LoadFontFace failed: " + loadError);
            }

            int upperBound = Mathf.Clamp(job.scanUpperBound, 127, 1114111);
            List<uint> result = new List<uint>(16384);
            for (uint codePoint = 0; codePoint <= (uint)upperBound; codePoint++)
            {
                if (codePoint >= 0xD800 && codePoint <= 0xDFFF)
                {
                    continue;
                }

                if (!job.includeControlCharacters && codePoint < 0x20)
                {
                    continue;
                }

                uint glyphIndex;
                if (FontEngine.TryGetGlyphIndex(codePoint, out glyphIndex) && glyphIndex != 0)
                {
                    result.Add(codePoint);
                }
            }

            return result;
        }

        private static BuildPlan CreatePlan(JobManifest job, int glyphCount)
        {
            List<int> atlasSizes = NormalizeAtlasSizes(job.atlasSizes);
            List<int> candidates = new List<int>();
            candidates.Add(Mathf.Clamp(job.samplingPointSize, 16, 256));
            candidates.Add(72);
            candidates.Add(64);
            candidates.Add(56);
            candidates.Add(48);
            candidates.Add(40);
            candidates.Add(32);
            candidates = candidates.Distinct().OrderByDescending(x => x).ToList();

            bool canChooseStatic = !job.forceDynamic;
            bool canChooseDynamic = !job.forceStatic;

            if (canChooseStatic)
            {
                foreach (int pointSize in candidates)
                {
                    foreach (int atlasSize in atlasSizes)
                    {
                        if (glyphCount <= EstimateCapacity(atlasSize, pointSize, job.padding))
                        {
                            return new BuildPlan
                            {
                                UseDynamic = false,
                                AtlasSize = atlasSize,
                                SamplingPointSize = pointSize
                            };
                        }
                    }
                }
            }

            if (canChooseDynamic)
            {
                int dynamicAtlas = atlasSizes.Max();
                int dynamicPointSize = Mathf.Clamp(job.samplingPointSize, 32, 128);
                return new BuildPlan
                {
                    UseDynamic = true,
                    AtlasSize = dynamicAtlas,
                    SamplingPointSize = dynamicPointSize
                };
            }

            int forcedAtlas = atlasSizes.Max();
            return new BuildPlan
            {
                UseDynamic = false,
                AtlasSize = forcedAtlas,
                SamplingPointSize = candidates.Min()
            };
        }

        private static int EstimateCapacity(int atlasSize, int pointSize, int padding)
        {
            float cell = Mathf.Max(8f, pointSize + (padding * 2f) + 6f);
            float usableArea = (atlasSize * atlasSize) * 0.74f;
            int estimate = Mathf.FloorToInt(usableArea / (cell * cell));
            return Mathf.Max(1, estimate);
        }

        private static List<int> NormalizeAtlasSizes(int[] source)
        {
            if (source == null || source.Length == 0)
            {
                return new List<int> { 1024, 2048, 4096 };
            }

            return source
                .Where(x => x >= 256 && x <= 8192)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                throw new InvalidOperationException("Asset folder must start with Assets/: " + assetPath);
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace('\\', '/').Trim();
        }

        private static string GetArgument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        return args[i + 1].Trim('"');
                    }
                }
                else if (arg.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(key.Length + 1).Trim('"');
                }
            }

            return null;
        }
    }
}