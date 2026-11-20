using System.Collections.Generic;
using System.IO;
using Ashline.City;
using UnityEditor;
using UnityEngine;

namespace Ashline.EditorTools
{
    /// <summary>
    /// Turns the downloaded Poly Haven sets into URP Lit materials and writes Resources/CityArt.asset.
    /// </summary>
    public static class AshlineArt
    {
        const string Root = "Assets/Ashline";
        const string Surfaces = Root + "/Art/Surfaces";
        const string MaterialsDir = Root + "/Art/Materials";
        const string ResourcesDir = Root + "/Resources";
        const string SkyHdr = Root + "/Art/Sky/day_partly_cloudy.hdr";
        public const string SkyMaterial = Root + "/Art/Sky/Sky.mat";

        /// <summary>Real-world size of one texture repeat (meters), from the Poly Haven metadata.</summary>
        static readonly Dictionary<string, float> RepeatMeters = new Dictionary<string, float>
        {
            { "asphalt_02", 3f }, { "asphalt_04", 4.04f }, { "beige_wall_001", 3f }, { "brick_wall_10", 1.9f },
            { "brown_brick_02", 1f }, { "brushed_concrete", 2.5f }, { "clay_roof_tiles_02", 2.5f },
            { "concrete_panels", 8f }, { "concrete_pavement_02", 1.8f }, { "concrete_tile_facade", 2.09f },
            { "corrugated_iron_02", 2.7f }, { "herringbone_pavement_02", 2.48f }, { "leafy_grass", 2f },
            { "plastered_wall_04", 3.2f }, { "rectangular_facade_tiles_02", 2.105f }, { "tarred_gravel", 2.2f },
            { "sand_03", 2f }, { "damp_beach_sand", 2f }, { "seaside_rock", 2f }, { "wood_floor_deck", 1.8f },
            { "red_sandstone_pavement", 2.15f }, { "white_sandstone_bricks", 2f },
        };

        /// <summary>Albedo multipliers: the leafy grass scan is autumn-brown, the islands want a watered lawn.</summary>
        static readonly Dictionary<string, Color> Tints = new Dictionary<string, Color>
        {
            { "leafy_grass", new Color(0.47f, 0.62f, 0.33f) },
        };

        const string GeneratedDir = Root + "/Art/Generated";

        static readonly Color[] AwningColors =
        {
            new Color(0.62f, 0.08f, 0.07f), new Color(0.05f, 0.32f, 0.45f), new Color(0.1f, 0.38f, 0.2f), new Color(0.86f, 0.62f, 0.2f),
            new Color(0.85f, 0.83f, 0.78f), new Color(0.76f, 0.3f, 0.36f), new Color(0.12f, 0.16f, 0.32f), new Color(0.9f, 0.45f, 0.15f),
        };

        [MenuItem("Ashline/Build Art")]
        public static void Build()
        {
            Directory.CreateDirectory(MaterialsDir);
            Directory.CreateDirectory(ResourcesDir);

            var art = AssetDatabase.LoadAssetAtPath<CityArt>(ResourcesDir + "/CityArt.asset");
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<CityArt>();
                AssetDatabase.CreateAsset(art, ResourcesDir + "/CityArt.asset");
            }

            art.surfaces.Clear();
            foreach (var dir in Directory.GetDirectories(Surfaces))
            {
                string id = Path.GetFileName(dir);
                var mat = SurfaceMaterial(id, dir.Replace('\\', '/'));
                if (mat == null) continue;
                float m = RepeatMeters.TryGetValue(id, out var v) ? v : 2f;
                art.surfaces.Add(new CityArt.Surface { id = id, material = mat, size = new Vector2(m, m) });
            }

            art.glass = Interior("Glass", false);
            art.shopGlass = Interior("ShopGlass", true);
            art.windowFrame = Solid("WindowFrame", new Color(0.13f, 0.13f, 0.14f), 0.85f, 0.55f);
            art.roadPaint = Solid("RoadPaint", new Color(0.82f, 0.82f, 0.78f), 0f, 0.3f);
            art.roadPaintYellow = Solid("RoadPaintYellow", new Color(0.86f, 0.62f, 0.12f), 0f, 0.3f);
            art.metalDark = Solid("MetalDark", new Color(0.09f, 0.095f, 0.1f), 0.75f, 0.45f);
            art.lampEmissive = Solid("LampHead", new Color(1f, 0.92f, 0.78f), 0f, 0.8f, new Color(1f, 0.86f, 0.62f) * 4f);
            art.signs = Solid("Signs", new Color(0.07f, 0.09f, 0.12f), 0.35f, 0.62f);

            art.awnings = new Material[AwningColors.Length];
            for (int i = 0; i < AwningColors.Length; i++) art.awnings[i] = Solid($"Awning{i}", AwningColors[i], 0f, 0.18f);
            art.paintWhite = Solid("PaintWhite", new Color(0.86f, 0.86f, 0.84f), 0f, 0.45f);
            art.paintBlue = Solid("PaintBlue", new Color(0.04f, 0.16f, 0.42f), 0.1f, 0.55f);
            art.paintRed = Solid("PaintRed", new Color(0.7f, 0.05f, 0.04f), 0f, 0.5f);
            art.glowWhite = Solid("GlowWhite", new Color(1f, 0.95f, 0.85f), 0f, 0.6f, new Color(1f, 0.9f, 0.7f) * 3f);
            art.pool = Solid("PoolWater", new Color(0.12f, 0.55f, 0.68f), 0f, 0.96f);
            art.pond = Solid("PondWater", new Color(0.04f, 0.11f, 0.1f), 0f, 0.95f);
            art.water = WaterMaterial();
            PalmMaterials(art);

            SkyboxMaterial();

            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log($"Ashline art: {art.surfaces.Count} surfaces built.");
        }

        static Material SurfaceMaterial(string id, string dir)
        {
            string diff = $"{dir}/{id}_diff.jpg", nor = $"{dir}/{id}_nor.jpg", arm = $"{dir}/{id}_arm.jpg";
            if (!File.Exists(diff)) return null;

            Configure(diff, TextureImporterType.Default, true);
            if (File.Exists(nor)) Configure(nor, TextureImporterType.NormalMap, false);

            // URP Lit wants metallic in R, occlusion in G and smoothness in A; pack them into one map.
            string mask = $"{dir}/{id}_mask.png";
            if (File.Exists(arm) && !File.Exists(mask))
            {
                var src = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
                src.LoadImage(File.ReadAllBytes(arm));
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var p = px[i];
                    px[i] = new Color32(p.b, p.r, 0, (byte)(255 - p.g));
                }
                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
                dst.SetPixels32(px);
                File.WriteAllBytes(mask, dst.EncodeToPNG());
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(dst);
                AssetDatabase.ImportAsset(mask);
            }
            if (File.Exists(mask)) Configure(mask, TextureImporterType.Default, false);

            var mat = LitMaterial(id);
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(diff));
            mat.SetColor("_BaseColor", Tints.TryGetValue(id, out var tint) ? tint : Color.white);
            if (File.Exists(nor))
            {
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nor));
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (File.Exists(mask))
            {
                var m = AssetDatabase.LoadAssetAtPath<Texture2D>(mask);
                mat.SetTexture("_MetallicGlossMap", m);
                mat.SetTexture("_OcclusionMap", m);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_OcclusionStrength", 1f);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.EnableKeyword("_OCCLUSIONMAP");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Configure(string path, TextureImporterType type, bool srgb)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            bool changed = ti.textureType != type || ti.sRGBTexture != srgb || ti.maxTextureSize != 2048 || ti.anisoLevel != 8;
            if (!changed) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "iPhone",
                overridden = true,
                maxTextureSize = 2048,
                format = type == TextureImporterType.NormalMap ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6,
            });
            ti.SaveAndReimport();
        }

        static Material LitMaterial(string name)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.enableInstancing = true;
            return mat;
        }

        static Material Interior(string name, bool shop)
        {
            var mat = LitMaterial(name);
            mat.shader = Shader.Find("Ashline/WindowInterior");
            mat.SetColor("_GlassTint", shop ? new Color(0.78f, 0.8f, 0.8f) : new Color(0.52f, 0.57f, 0.6f));
            mat.SetFloat("_RoomDepth", shop ? 8f : 5f);
            mat.SetFloat("_RoomWidth", shop ? 12f : 4.2f);
            mat.SetFloat("_RoomHeight", shop ? 4f : 3f);
            mat.SetFloat("_SillHeight", shop ? 0.45f : 0.9f);
            mat.SetFloat("_LightChance", shop ? 0.85f : 0.2f);
            mat.SetFloat("_LightIntensity", shop ? 0.75f : 0.9f);
            mat.SetFloat("_DayLight", shop ? 0.35f : 0.45f);
            mat.SetFloat("_Reflectivity", shop ? 0.05f : 0.1f);
            mat.SetFloat("_Shop", shop ? 1f : 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Solid(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            var mat = LitMaterial(name);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.SetColor("_EmissionColor", emission.Value);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material WaterMaterial()
        {
            string path = $"{MaterialsDir}/Sea.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Ashline/Water"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("Ashline/Water");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ palms

        static void PalmMaterials(CityArt art)
        {
            Directory.CreateDirectory(GeneratedDir);
            string frondPath = GeneratedDir + "/PalmFrond.png", trunkPath = GeneratedDir + "/PalmTrunk.png";
            if (!File.Exists(frondPath)) File.WriteAllBytes(frondPath, FrondTexture().EncodeToPNG());
            if (!File.Exists(trunkPath)) File.WriteAllBytes(trunkPath, TrunkTexture().EncodeToPNG());
            AssetDatabase.ImportAsset(frondPath);
            AssetDatabase.ImportAsset(trunkPath);
            foreach (var p in new[] { frondPath, trunkPath })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.sRGBTexture = true;
                ti.alphaIsTransparency = p == frondPath;
                ti.mipmapEnabled = true;
                ti.mipMapsPreserveCoverage = p == frondPath;
                ti.alphaTestReferenceValue = 0.45f;
                ti.wrapModeU = p == frondPath ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.wrapModeV = p == frondPath ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.anisoLevel = 4;
                ti.SaveAndReimport();
            }

            var frond = LitMaterial("PalmFrond");
            frond.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(frondPath));
            frond.SetColor("_BaseColor", Color.white);
            frond.SetFloat("_Smoothness", 0.28f);
            frond.SetFloat("_Metallic", 0f);
            frond.SetFloat("_AlphaClip", 1f);
            frond.SetFloat("_Cutoff", 0.45f);
            frond.EnableKeyword("_ALPHATEST_ON");
            frond.SetFloat("_Cull", 0f);
            frond.SetOverrideTag("RenderType", "TransparentCutout");
            frond.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            frond.doubleSidedGI = true;
            EditorUtility.SetDirty(frond);
            art.palmFrond = frond;

            var trunk = LitMaterial("PalmTrunk");
            trunk.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(trunkPath));
            trunk.SetColor("_BaseColor", Color.white);
            trunk.SetFloat("_Smoothness", 0.12f);
            trunk.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(trunk);
            art.palmTrunk = trunk;
        }

        static float Hash(int x, int y) => Mathf.Repeat(Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f, 1f);

        /// <summary>Frond: u runs along the rib (x), v across it (y, rib in the middle); leaflets slant toward the tip.</summary>
        static Texture2D FrondTexture()
        {
            const int W = 512, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1), v = Mathf.Abs(y / (float)(H - 1) - 0.5f) * 2f;
                float maxV = 0.97f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.04f))), 0.45f);
                float phase = u * 46f + v * 4.2f;
                int leaf = Mathf.FloorToInt(phase);
                float f = phase - leaf;
                float len = maxV * (0.82f + 0.18f * Hash(leaf, y > H / 2 ? 1 : 0));
                float widthAtV = 0.55f * (1f - Mathf.Pow(v / Mathf.Max(len, 0.01f), 2.2f));
                bool isLeaf = v < len && f < widthAtV;
                bool rib = v < 0.035f + 0.02f * (1 - u);
                var c = new Color(0, 0, 0, 0);
                if (isLeaf || rib)
                {
                    float shade = 0.82f + 0.25f * Hash(leaf, 7) - 0.18f * Mathf.Abs(f / Mathf.Max(widthAtV, 0.01f) - 0.5f);
                    var green = Color.Lerp(new Color(0.12f, 0.24f, 0.05f), new Color(0.3f, 0.42f, 0.1f), Hash(leaf, 3));
                    // Sun-bleached, slightly brown tips.
                    green = Color.Lerp(green, new Color(0.45f, 0.42f, 0.18f), Mathf.Pow(v / Mathf.Max(len, 0.01f), 4f) * 0.6f);
                    c = rib ? new Color(0.42f, 0.4f, 0.2f) : green * shade;
                    c.a = 1f;
                }
                px[y * W + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Texture2D TrunkTexture()
        {
            const int W = 256, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (float)H;
                // Six leaf-scar rings per tile with a fibrous vertical grain.
                float ring = Mathf.Repeat(v * 6f + Hash(x / 16, 0) * 0.08f, 1f);
                float groove = Mathf.SmoothStep(0f, 0.12f, ring) * Mathf.SmoothStep(1f, 0.75f, ring);
                float grain = 0.85f + 0.15f * Hash(x, y / 6) + 0.1f * Mathf.Sin(x * 0.9f);
                var bark = Color.Lerp(new Color(0.2f, 0.16f, 0.12f), new Color(0.52f, 0.46f, 0.38f), groove) * grain;
                bark.a = 1;
                px[y * W + x] = bark;
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static void SkyboxMaterial()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(SkyHdr);
            if (ti != null && ti.textureShape != TextureImporterShape.TextureCube)
            {
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                ti.maxTextureSize = 2048;
                ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterial);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Cubemap"));
                AssetDatabase.CreateAsset(mat, SkyMaterial);
            }
            mat.SetTexture("_Tex", AssetDatabase.LoadAssetAtPath<Cubemap>(SkyHdr));
            mat.SetFloat("_Exposure", 1f);
            mat.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(mat);
        }
    }
}
