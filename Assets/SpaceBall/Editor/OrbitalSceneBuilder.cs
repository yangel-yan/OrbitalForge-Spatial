using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ByteDance.PICO.SpatialAdapter;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace SpaceBall.Editor
{
    public static class OrbitalSceneBuilder
    {
        const string RootFolder = "Assets/SpaceBall";
        const string ScenePath = RootFolder + "/Scenes/OrbitalCourier_Prototype.unity";
        const string FontPath = RootFolder + "/Fonts/Orbital_Noto_SDF.asset";
        const string SourceFontPath = RootFolder + "/Fonts/NotoSansCJKsc-Regular.otf";
        const string CameraConfigurationPath = RootFolder + "/Scenes/Orbital_SharedSpace.asset";

        static readonly string[] MaterialNames =
        {
            "Floor", "Rail", "Accent", "Dark", "White", "Moon", "Inactive", "Success",
            "Ceramic", "Titanium", "GuideLight", "Energy", "Panel", "Deck", "TrackGuard"
        };

        static readonly string[] MaterialColors =
        {
            "#355362", "#70BDCB", "#D8AE66", "#132536", "#EDF3F2", "#B6C4CC", "#435B69", "#79DDB5",
            "#DFE9EB", "#3D5A6B", "#59B8C5", "#FFD28A", "#213949", "#293947", "#819AA8"
        };

        [MenuItem("SpaceBall/Create First Chapter")]
        public static void CreateFromMenu()
        {
            string path = CreateAndSave();
            Debug.Log("Created the six-mission Orbital Courier chapter: " + path);
        }

        public static string CreateAndSave()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before creating the Orbital Courier scene.");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var openScene = SceneManager.GetSceneAt(i);
                if (openScene.isDirty)
                    throw new InvalidOperationException("Save the open scene before creating the prototype: " +
                                                        openScene.name + ". No open scene was replaced.");
            }

            EnsureFolder(RootFolder);
            EnsureFolder(RootFolder + "/Scenes");
            EnsureFolder(RootFolder + "/Materials");
            EnsureFolder(RootFolder + "/Fonts");
            EnsureFolder(RootFolder + "/Art/Meshes");

            // Prepare persistent resources before replacing even a clean open scene.
            var font = CreateOrReuseFont();
            var materials = CreateOrReusePalette();
            PrepareDisplayMaterials();
            var stationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RootFolder + "/Art/Generated/OrbitalDock.prefab");
            ValidateStationAsset(stationPrefab);
            var configuration = CreateOrReuseCameraConfiguration();
            AlignDefaultCameraConfiguration(configuration);
            if (PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) == "com.pico.spatial.template")
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.bits.spaceball.prototype");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Orbital Courier Prototype");

            var spatialCameraObject = CreateRoot("SpatialCamera");
            var spatialCamera = Undo.AddComponent<SpatialCamera>(spatialCameraObject);
            Undo.AddComponent<OrbitalSpatialPreviewGuard>(spatialCameraObject);
            spatialCamera.Dimensions = Vector3.one * 1.5f;
            spatialCamera.OutputConfiguration = configuration;
            spatialCamera.OpenWindowOnLoad = true;

            var cameraObject = CreateRoot("Prototype Preview Camera");
            var previewCamera = Undo.AddComponent<Camera>(cameraObject);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(.77f, .92f, -1.46f);
            cameraObject.transform.LookAt(new Vector3(0f, .19f, .02f));
            previewCamera.fieldOfView = 51f;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 20f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = ParseColor("#07101A");
            Undo.AddComponent<AudioListener>(cameraObject);

            var lightObject = CreateRoot("Prototype Key Light");
            var keyLight = Undo.AddComponent<Light>(lightObject);
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.4f;
            keyLight.color = new Color(.96f,.98f,1f);
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = .70f;
            keyLight.shadowBias = .03f;
            keyLight.shadowNormalBias = .12f;
            lightObject.transform.rotation = Quaternion.Euler(38f, 58f, 0f);
            RenderSettings.sun = keyLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.20f,.24f,.30f);
            var fill=CreateRoot("Model Fill Light").AddComponent<Light>();
            fill.type=LightType.Directional;fill.intensity=.28f;
            fill.color=new Color(.73f,.85f,1);fill.shadows=LightShadows.None;
            fill.transform.rotation=Quaternion.Euler(32f,145f,0);
            // A metre-scale exhibit needs a local shadow range, not the template's
            // 50 metre atlas. Native Shared Space output is reviewed separately.
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline!=null)
            {
                pipeline.renderScale=1f;pipeline.msaaSampleCount=4;
                pipeline.shadowDistance=3f;pipeline.mainLightShadowmapResolution=2048;
                var settings = new SerializedObject(pipeline);
                settings.FindProperty("m_SoftShadowsSupported").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
            }

            var prototypeObject = CreateRoot("Orbital Courier Prototype");
            var prototype = Undo.AddComponent<OrbitalPrototype>(prototypeObject);
            prototype.uiFont = font;
            prototype.palette = materials;
            prototype.moonSurface = AssetDatabase.LoadAssetAtPath<Material>(RootFolder + "/Materials/MoonSurface.mat");
            prototype.moonCrater = AssetDatabase.LoadAssetAtPath<Material>(RootFolder + "/Materials/MoonCrater.mat");
            prototype.stationVisualPrefab = stationPrefab;
            prototype.previewCamera = previewCamera;
            prototype.eventSounds = new[] { "Assembly", "RelayA", "RelayB", "Delivery" }
                .Select(name => AssetDatabase.LoadAssetAtPath<AudioClip>(RootFolder + "/Audio/" + name + ".wav")).ToArray();
            if (prototype.eventSounds.Any(clip => clip == null))
                throw new InvalidOperationException("Missing original game cues. Run Tools/generate_orbital_audio.py before rebuilding.");
            var feedback = Undo.AddComponent<OrbitalFeedbackAudio>(prototypeObject);
            feedback.cues = Enum.GetNames(typeof(OrbitalFeedbackCue))
                .Select(name => AssetDatabase.LoadAssetAtPath<AudioClip>(RootFolder + "/Audio/Interaction/" + name + ".wav")).ToArray();
            if (feedback.cues.Any(clip => clip == null))
                throw new InvalidOperationException("Missing interaction cues in Assets/SpaceBall/Audio/Interaction.");
            prototype.BuildPrototype();
            OrbitalNativeShadows.Apply(prototype);
            var effectMaterials = CreateOrReuseEffectMaterials();
            foreach (var presentation in prototype.GetComponentsInChildren<OrbitalPresentation>(true))
            {
                var ball = presentation.ballVisual.GetComponentInParent<Rigidbody>(true);
                var targets = Enumerable.Range(0, 5)
                    .Select(index => index < presentation.pulseRings.Length ? presentation.pulseRings[index] : null).ToArray();
                presentation.energyEffects = OrbitalEnergyEffects.Create(prototype, presentation, ball,
                    presentation.transform, targets, effectMaterials);
            }
            // Export valid glyph geometry even for the initially hidden layouts.
            foreach (var text in prototypeObject.GetComponentsInChildren<TMP_Text>(true))
                text.ForceMeshUpdate(true, true);
            OrbitalSpatialPlacement.FitToContent(spatialCamera, prototypeObject.transform);
            AssetDatabase.StartAssetEditing();
            try { BakeGeneratedMeshes(prototypeObject); }
            finally { AssetDatabase.StopAssetEditing(); }

            EditorUtility.SetDirty(prototype);
            EditorUtility.SetDirty(spatialCamera);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unity could not save the prototype scene to " + ScenePath + ".");

            var buildScenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            buildScenes.AddRange(EditorBuildSettings.scenes.Where(entry => entry.path != ScenePath));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = prototypeObject;
            return ScenePath;
        }

        static GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(root, "Create " + name);
            return root;
        }

        static void ValidateStationAsset(GameObject prefab)
        {
            if (prefab == null) throw new InvalidOperationException("Missing OrbitalDock.prefab art resource.");
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0 || filters.Any(filter => filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0))
                throw new InvalidOperationException("OrbitalDock.prefab must contain valid display meshes.");
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 || prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                throw new InvalidOperationException("OrbitalDock.prefab is decorative and must not contain physics components.");
        }

        static SpatialCameraConfiguration CreateOrReuseCameraConfiguration()
        {
            var configuration = AssetDatabase.LoadAssetAtPath<SpatialCameraConfiguration>(CameraConfigurationPath);
            if (configuration == null)
            {
                configuration = ScriptableObject.CreateInstance<SpatialCameraConfiguration>();
                configuration.name = "Orbital Shared Space";
                AssetDatabase.CreateAsset(configuration, CameraConfigurationPath);
            }

            configuration.mode = SpatialCamera.CameraMode.Constrained;
            configuration.outputDimensions = Vector3.one * 1.5f;
            EditorUtility.SetDirty(configuration);
            return configuration;
        }

        static void AlignDefaultCameraConfiguration(SpatialCameraConfiguration configuration)
        {
            const string path = "Assets/Settings/SpatialAdapterRuntimeSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (settings == null) throw new InvalidOperationException("Missing PICO Spatial runtime settings: " + path);
            var serializedSettings = new SerializedObject(settings);
            serializedSettings.Update();
            var property = serializedSettings.FindProperty("m_defaultSpatialCameraConfiguration");
            if (property == null) throw new InvalidOperationException("Installed Spatial settings have no default camera configuration field.");
            if (property.objectReferenceValue == configuration) return;
            Undo.RecordObject(settings, "Align Orbital Shared Space Configuration");
            property.objectReferenceValue = configuration;
            serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        static Material[] CreateOrReusePalette()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("The prototype requires the installed URP/Lit shader.");

            var palette = new Material[MaterialNames.Length];
            for (int i = 0; i < palette.Length; i++)
            {
                string path = RootFolder + "/Materials/" + MaterialNames[i] + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = MaterialNames[i] };
                    AssetDatabase.CreateAsset(material, path);
                }

                material.shader = shader;
                material.SetColor("_BaseColor", ParseColor(MaterialColors[i]));
                // Preserve authored surface maps. Rebuilding the chapter must not
                // flatten its PBR assets back to the original blockout palette.
                material.SetFloat("_Metallic", i == 9 ? .35f : i == 2 ? .45f : i == 3 ? .12f : 0f);
                material.SetFloat("_Smoothness", i == 5 ? .24f : i == 8 ? .36f : .29f);
                bool emissive = i == 7 || i == 10 || i == 11;
                if (emissive)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", ParseColor(MaterialColors[i]).linear * (i == 11 ? .6f : .18f));
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else { material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black); }
                EditorUtility.SetDirty(material);
                palette[i] = material;
            }

            OrbitalSurfaceMaterials.Configure(palette);
            return palette;
        }

        static void PrepareDisplayMaterials()
        {
            OrbitalSurfaceMaterials.ConfigureLunarMaterials();
        }

        static Material[] CreateOrReuseEffectMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("The energy effects need the URP Lit shader.");
            string[] names = { "VFX_WhiteCore", "VFX_Cyan", "VFX_DeepCyan", "VFX_Gold", "VFX_DimGold" };
            string[] colors = { "#F3FCFF", "#54E7FF", "#187B9B", "#FFD47C", "#94692B" };
            var materials = new Material[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                string path = RootFolder + "/Materials/" + names[index] + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = names[index] };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.SetColor("_BaseColor", ParseColor(colors[index]));
                material.SetTexture("_BaseMap", null);
                material.SetFloat("_Metallic", 0);
                material.SetFloat("_Smoothness", .1f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", ParseColor(colors[index]).linear * (index == 2 || index == 4 ? .15f : .65f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetFloat("_Surface", 0);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Geometry;
                EditorUtility.SetDirty(material);
                materials[index] = material;
            }
            return materials;
        }

        static void BakeGeneratedMeshes(GameObject root)
        {
            int index=0;
            var saved=new Dictionary<Mesh,Mesh>();
            var temporary=new List<Mesh>();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                // TMP owns and replaces its dynamic mesh; never persist that mesh as an asset.
                if(filter.GetComponent<TMP_Text>()!=null || filter.GetComponent<TMP_SubMesh>()!=null) continue;
                var mesh=filter.sharedMesh;
                if(mesh==null || AssetDatabase.Contains(mesh)) continue;
                if(saved.TryGetValue(mesh,out var reused)) { filter.sharedMesh=reused; continue; }
                string path=RootFolder+"/Art/Meshes/Orbital_"+(index++).ToString("D3")+".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing!=null)
                {
                    // Explicit mesh setters refresh GPU buffers when a prior bake
                    // had different topology; CopySerialized alone can leave stale buffers.
                    existing.Clear();existing.indexFormat=mesh.indexFormat;
                    existing.name=mesh.name;existing.vertices=mesh.vertices;
                    existing.normals=mesh.normals;existing.uv=mesh.uv;existing.colors=mesh.colors;
                    existing.tangents=mesh.tangents;
                    existing.subMeshCount=mesh.subMeshCount;
                    for(int sub=0;sub<mesh.subMeshCount;sub++)existing.SetTriangles(mesh.GetTriangles(sub),sub);
                    existing.bounds=mesh.bounds;existing.UploadMeshData(false);
                    filter.sharedMesh=existing;
                    saved.Add(mesh,existing);
                    temporary.Add(mesh);
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    var copy=UnityEngine.Object.Instantiate(mesh);copy.name=mesh.name;
                    AssetDatabase.CreateAsset(copy,path);filter.sharedMesh=copy;
                    saved.Add(mesh,copy);temporary.Add(mesh);
                }
            }
            foreach(var mesh in temporary) UnityEngine.Object.DestroyImmediate(mesh);
        }

        static TMP_FontAsset CreateOrReuseFont()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (fontAsset == null)
            {
                var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
                if (sourceFont == null)
                {
                    if (!File.Exists(SourceFontPath))
                        throw new FileNotFoundException("Missing bundled OFL font. Restore NotoSansCJKsc-Regular.otf and its license.", SourceFontPath);

                    AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceSynchronousImport |
                                                               ImportAssetOptions.ForceUpdate);
                    sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
                    if (sourceFont == null)
                        throw new InvalidOperationException("Unity could not import the Chinese font at " +
                                                            SourceFontPath + ". Check its Font Import settings.");
                }

                fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont, 64, 6, GlyphRenderMode.SDFAA,
                    2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (fontAsset == null)
                    throw new InvalidOperationException("Unable to create the Chinese TMP font. Enable Include Font " +
                                                        "Data on " + SourceFontPath + " and try again.");

                fontAsset.name = "Orbital Noto SDF";
                AssetDatabase.CreateAsset(fontAsset, FontPath);
            }

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            // Keep the prewarmed primary glyphs for native Spatial scene export.
            var fontSerialization = new SerializedObject(fontAsset);
            var clearOnBuild = fontSerialization.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuild != null)
            {
                clearOnBuild.boolValue = false;
                fontSerialization.ApplyModifiedPropertiesWithoutUndo();
            }
            if (!fontAsset.TryAddCharacters(CollectSourceCharacters(), out string missingCharacters))
            {
                Debug.LogWarning("The prototype font does not contain these source characters: " + missingCharacters,
                    fontAsset);
            }

            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
            {
                fontAsset.material.name = "Orbital Noto Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                var texture = fontAsset.atlasTextures[i];
                if (texture == null)
                    continue;

                if (!AssetDatabase.Contains(texture))
                {
                    texture.name = "Orbital Noto Atlas " + i;
                    AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }

                EditorUtility.SetDirty(texture);
            }

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            const string licenseSource = RootFolder + "/Fonts/NotoSansCJK-OFL.txt";
            Directory.CreateDirectory("Assets/StreamingAssets/Licenses");
            File.Copy(licenseSource, "Assets/StreamingAssets/Licenses/NotoSansCJK-OFL.txt", true);
            AssetDatabase.ImportAsset("Assets/StreamingAssets/Licenses/NotoSansCJK-OFL.txt");
            EditorUtility.SetDirty(fontAsset.material);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        static string CollectSourceCharacters()
        {
            var characters = new HashSet<char>();
            // TMP requests these for overflow/fallback even when they are not
            // literal game copy. Keep them in the native font's own atlas.
            characters.Add('\u2026');
            characters.Add('\u25A1');
            for (char character = ' '; character <= '~'; character++)
                characters.Add(character);

            foreach (string file in Directory.EnumerateFiles(RootFolder, "*.cs", SearchOption.AllDirectories))
            {
                foreach (char character in File.ReadAllText(file))
                {
                    if (!char.IsControl(character) && !char.IsSurrogate(character))
                        characters.Add(character);
                }
            }

            return new string(characters.OrderBy(character => character).ToArray());
        }

        static Color ParseColor(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
                throw new ArgumentException("Invalid prototype palette color: " + value);

            return color;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                throw new ArgumentException("Invalid project asset folder: " + path);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
