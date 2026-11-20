using Ashline.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashline.EditorTools
{
    /// <summary>One-click project configuration and City scene generation.</summary>
    public static class AshlineSetup
    {
        const string ScenePath = "Assets/Ashline/Scenes/City.unity";
        const string VolumePath = "Assets/Ashline/Settings/CityVolume.asset";
        const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const string CarModelPath = "Assets/Ashline/Art/Cars/CarConcept/CarConcept.glb";
        const string CharacterPath = "Assets/Ashline/Art/Characters/Soldier/Soldier.glb";

        [MenuItem("Ashline/Setup Everything")]
        public static void All()
        {
            AshlineArt.Build();
            Project();
            Pipeline();
            Scene();
        }

        [MenuItem("Ashline/Configure Project")]
        public static void Project()
        {
            PlayerSettings.companyName = "Skogarmaor";
            PlayerSettings.productName = "Ashline";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.skogarmaor.ashline");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.iOS.targetOSVersionString = "17.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Time.fixedDeltaTime = 0.01f;

            var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = quality.FindProperty("m_QualitySettings");
            var pc = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelinePath);
            for (int i = 0; i < levels.arraySize; i++)
                levels.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline").objectReferenceValue = pc;
            quality.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pc;
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Ashline/Configure Pipeline")]
        public static void Pipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var so = new SerializedObject(asset);
            void Set(string name, float v)
            {
                var p = so.FindProperty(name);
                if (p == null) { Debug.LogWarning($"URP asset has no {name}"); return; }
                if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
                else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v > 0.5f;
                else p.intValue = (int)v;
            }
            Set("m_SupportsHDR", 1);
            Set("m_MSAA", 4);
            Set("m_RenderScale", 0.85f);
            Set("m_UpscalingFilter", 3);
            Set("m_MainLightShadowmapResolution", 2048);
            Set("m_ShadowDistance", 110);
            Set("m_ShadowCascadeCount", 2);
            Set("m_SoftShadowsSupported", 1);
            Set("m_SoftShadowQuality", 2);
            Set("m_ColorGradingMode", 1);
            Set("m_AdditionalLightsPerObjectLimit", 4);
            // The sea shader reads scene depth for shoreline foam and shallow-water colour.
            Set("m_RequireDepthTexture", 1);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            foreach (var f in data.rendererFeatures)
                if (f is ScreenSpaceAmbientOcclusion ssao)
                {
                    var fs = new SerializedObject(ssao);
                    fs.FindProperty("m_Settings.Intensity").floatValue = 0.6f;
                    fs.FindProperty("m_Settings.Radius").floatValue = 0.35f;
                    fs.ApplyModifiedPropertiesWithoutUndo();
                }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Ashline/Build City Scene")]
        public static void Scene()
        {
            System.IO.Directory.CreateDirectory("Assets/Ashline/Scenes");
            System.IO.Directory.CreateDirectory("Assets/Ashline/Settings");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(AshlineArt.SkyMaterial);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.35f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0011f;
            RenderSettings.fogColor = new Color(0.68f, 0.76f, 0.86f);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 2.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            sun.transform.rotation = Quaternion.Euler(38f, -128f, 0f);
            RenderSettings.sun = sun;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 1800f;
            cam.fieldOfView = 58f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = true;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0, 40, -120);

            var volume = new GameObject("PostFX").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = VolumeProfileAsset();

            var cityGo = new GameObject("City");
            var city = cityGo.AddComponent<CityGenerator>();

            var boot = new GameObject("Bootstrap").AddComponent<GameBootstrap>();
            boot.city = city;
            boot.carModel = AssetDatabase.LoadAssetAtPath<GameObject>(CarModelPath);
            boot.characterModel = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(CharacterPath))
            {
                if (!(o is AnimationClip clip)) continue;
                if (clip.name == "Idle") boot.idleClip = clip;
                else if (clip.name == "Walk") boot.walkClip = clip;
                else if (clip.name == "Run") boot.runClip = clip;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Ashline: City scene built.");
        }

        static VolumeProfile VolumeProfileAsset()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet<T>(out var c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                return c;
            }

            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.68f);
            bloom.highQualityFiltering.Override(false);

            var color = Get<ColorAdjustments>();
            color.postExposure.Override(0.45f);
            color.contrast.Override(14f);
            color.saturation.Override(6f);

            var wb = Get<WhiteBalance>();
            wb.temperature.Override(6f);

            var smh = Get<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.96f, 0.99f, 1.06f, 0f));
            smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.95f, 0f));

            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            var blur = Get<MotionBlur>();
            blur.mode.Override(MotionBlurMode.CameraOnly);
            blur.quality.Override(MotionBlurQuality.Low);
            blur.intensity.Override(0.25f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
