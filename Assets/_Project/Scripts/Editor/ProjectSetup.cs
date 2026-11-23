using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShadowFrit.EditorTools
{
    /// Builds the rigged Shadow Sentinel prefab (humanoid avatar, animator with the Meshy clips,
    /// metal + purple-glow materials, sword in the right hand, shield on the left forearm) and a
    /// dark look-dev scene. Menu: ShadowFrit > Build Sentinel Look-Dev Scene. Safe to re-run.
    public static class ProjectSetup
    {
        const string Root = "Assets/_Project";
        const string CharDir = Root + "/Art/Characters/ShadowSentinel";
        const string CharFbx = CharDir + "/ShadowSentinel.fbx";
        const string AnimFbx = CharDir + "/Animations/ShadowSentinel_Anims.fbx";
        const string SwordDir = Root + "/Art/Weapons/Thunderblade";
        const string ShieldDir = Root + "/Art/Weapons/ShatteredAegis";
        const string MatDir = Root + "/Materials";
        const string ControllerPath = Root + "/Animation/AC_ShadowSentinel.controller";
        const string GroundMatPath = MatDir + "/M_Ground.mat";
        const string PrefabPath = Root + "/Prefabs/ShadowSentinel.prefab";
        const string ProfilePath = Root + "/Settings/PP_ShadowLookDev.asset";
        const string ScenePath = Root + "/Scenes/SentinelLookDev.unity";

        static readonly string[] LoopingClips = { "Walking", "Running" };
        static readonly Color Background = new Color(0.05f, 0.055f, 0.08f);
        const float GlowIntensity = 5f;   // HDR multiplier on the emission maps

        [MenuItem("ShadowFrit/Build Sentinel Look-Dev Scene")]
        public static void BuildSentinelScene()
        {
            foreach (var d in new[] { "Materials", "Prefabs", "Settings", "Scenes", "Animation" })
                Directory.CreateDirectory(Path.Combine(Root, d));

            ConfigureImports();
            var controller = CreateController();
            var mats = new Dictionary<string, Material>
            {
                ["body"] = LitMaterial("M_ShadowSentinel", CharDir + "/Textures", "ShadowSentinel"),
                ["sword"] = LitMaterial("M_Thunderblade", SwordDir, "Thunderblade"),
                ["shield"] = LitMaterial("M_ShatteredAegis", ShieldDir, "ShatteredAegis"),
            };
            var prefab = CreateSentinelPrefab(controller, mats);
            var profile = CreatePostProfile();
            CreateScene(prefab, profile);
            AssetDatabase.SaveAssets();
            // URP's material post-processor strips _EMISSION from freshly created materials; put it back.
            foreach (var m in mats.Values)
            {
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ShadowFrit] emission keyword: " + string.Join(", ", mats.Values.Select(m => $"{m.name}={m.IsKeywordEnabled("_EMISSION")}")));
            Debug.Log("[ShadowFrit] Sentinel look-dev scene built: " + ScenePath);
        }

        public static void BatchBuild()
        {
            try { BuildSentinelScene(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // -shot <path.png> [-time <seconds>]: poses every AnimationPreview at its start clip, renders Main Camera.
        public static void BatchCapture()
        {
            try
            {
                string path = ArgAfter("-shot") ?? "lookdev.png";
                float t = float.TryParse(ArgAfter("-time"), System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out var tt) ? tt : 0.5f;
                EditorSceneManager.OpenScene(ScenePath);
                var clips = AssetDatabase.LoadAllAssetsAtPath(AnimFbx).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                foreach (var p in UnityEngine.Object.FindObjectsByType<ShadowFrit.AnimationPreview>(FindObjectsSortMode.None))
                {
                    if (!clips.TryGetValue(p.startState, out var clip)) continue;
                    AnimationMode.SampleAnimationClip(p.gameObject, clip, Mathf.Repeat(t, clip.length));
                }
                AnimationMode.EndSampling();
                string close = ArgAfter("-close");
                if (close != null)
                {
                    var target = GameObject.Find(close).transform;
                    var o = Camera.main.GetComponent<ShadowFrit.LookDevOrbit>();
                    o.target = target.position + Vector3.up * 1.1f; o.distance = 2.6f; o.yaw = -25f; o.pitch = 8f; o.Apply();
                }
                var cam = Camera.main;
                var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                Debug.Log("[ShadowFrit] Captured " + path);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
            finally { if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode(); }
        }

        static string ArgAfter(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // ---------------------------------------------------------------- imports

        static void ConfigureImports()
        {
            var ch = (ModelImporter)AssetImporter.GetAtPath(CharFbx);
            ch.animationType = ModelImporterAnimationType.Human;
            ch.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            ch.materialImportMode = ModelImporterMaterialImportMode.None;
            ch.importAnimation = false;
            ch.importCameras = ch.importLights = false;
            ch.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(CharFbx).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"[ShadowFrit] character avatar human={avatar?.isHuman} valid={avatar?.isValid}");

            var an = (ModelImporter)AssetImporter.GetAtPath(AnimFbx);
            an.animationType = ModelImporterAnimationType.Human;
            an.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            an.materialImportMode = ModelImporterMaterialImportMode.None;
            an.importAnimation = true;
            an.SaveAndReimport();
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in an.defaultClipAnimations)
            {
                var parts = c.takeName.Split('|');
                c.name = parts.Length > 1 ? parts[1] : c.takeName;
                c.loopTime = LoopingClips.Contains(c.name);
                c.lockRootRotation = true;          // bake turning/height into the pose,
                c.lockRootHeightY = true;           // leave forward motion as root motion
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                clips.Add(c);
            }
            an.clipAnimations = clips.ToArray();
            an.SaveAndReimport();
            var anAvatar = AssetDatabase.LoadAllAssetsAtPath(AnimFbx).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"[ShadowFrit] anim avatar human={anAvatar?.isHuman} valid={anAvatar?.isValid}; clips: " +
                      string.Join(", ", clips.Select(c => $"{c.name}{(c.loopTime ? " (loop)" : "")}")));

            foreach (var w in new[] { SwordDir + "/Thunderblade.fbx", ShieldDir + "/ShatteredAegis.fbx" })
            {
                var wi = (ModelImporter)AssetImporter.GetAtPath(w);
                wi.materialImportMode = ModelImporterMaterialImportMode.None;
                wi.animationType = ModelImporterAnimationType.None;
                wi.importAnimation = false;
                wi.SaveAndReimport();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CharDir, SwordDir, ShieldDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                bool linear = p.EndsWith("_MetallicSmoothness.png") || p.EndsWith("_AO.png");
                if (ti.sRGBTexture == !linear && ti.maxTextureSize == 2048) continue;
                ti.sRGBTexture = !linear;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
        }

        static AnimatorController CreateController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = ac.layers[0].stateMachine;
            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimFbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview")).OrderBy(c => c.name).ToList();
            int i = 0;
            foreach (var clip in clips)
            {
                var st = sm.AddState(clip.name, new Vector3(300, 60 * i++, 0));
                st.motion = clip;
                if (clip.name == "Walking") sm.defaultState = st;
            }
            return ac;
        }

        static Texture2D Tex(string dir, string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{file}.png");

        static Material SaveMaterial(Material m, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                existing.shaderKeywords = m.shaderKeywords;
                existing.globalIlluminationFlags = m.globalIlluminationFlags;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material LitMaterial(string name, string dir, string prefix)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", Tex(dir, prefix + "_BaseColor"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", Tex(dir, prefix + "_MetallicSmoothness"));
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness from the metallic map's alpha
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap", Tex(dir, prefix + "_AO"));
            m.SetFloat("_OcclusionStrength", 0.6f);
            m.EnableKeyword("_OCCLUSIONMAP");
            m.SetTexture("_EmissionMap", Tex(dir, prefix + "_Emission"));
            m.SetColor("_EmissionColor", Color.white * GlowIntensity);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return SaveMaterial(m, $"{MatDir}/{name}.mat");
        }

        // ---------------------------------------------------------------- prefab

        static Transform FindDeep(Transform t, string contains)
        {
            if (t.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) return t;
            foreach (Transform c in t) { var r = FindDeep(c, contains); if (r) return r; }
            return null;
        }

        static GameObject CreateSentinelPrefab(AnimatorController controller, Dictionary<string, Material> mats)
        {
            var root = new GameObject("ShadowSentinel");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharFbx));
            body.name = "Body";
            body.transform.SetParent(root.transform, false);

            var anim = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(CharFbx).OfType<Avatar>().First();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;            // clips play in place; movement code drives the transform
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            body.AddComponent<ShadowFrit.AnimationPreview>();

            var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMaterial = mats["body"];
            var b = smr.bounds;
            body.transform.position -= new Vector3(0f, b.min.y, 0f);     // feet on the ground
            Debug.Log($"[ShadowFrit] sentinel height {b.size.y:F2} m");

            // Facing: the rig has a 'headfront' helper bone in front of the head.
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var hf = FindDeep(body.transform, "headfront");
            Vector3 fwd = hf ? Vector3.ProjectOnPlane(hf.position - head.position, Vector3.up).normalized : body.transform.forward;
            if (fwd.sqrMagnitude < 0.5f) fwd = body.transform.forward;
            Debug.Log($"[ShadowFrit] character forward {fwd}");

            // Sword: grip in the right fist (bind T-pose, palms down), blade pointing forward, edges up/down.
            var rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Vector3 rArm = (rHand.position - rFore.position).normalized;
            var swordSocket = new GameObject("Socket_Sword").transform;
            swordSocket.SetPositionAndRotation(rHand.position + rArm * 0.075f - Vector3.up * 0.015f,
                                               Quaternion.LookRotation(Vector3.Cross(Vector3.up, fwd), fwd));
            swordSocket.SetParent(rHand, true);
            AddWeapon(SwordDir + "/Thunderblade.fbx", "Thunderblade", swordSocket, mats["sword"]);

            // Shield: strapped along the outside (back of the hand side) of the left forearm, top towards the elbow.
            var lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var lFore = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Vector3 lArm = (lHand.position - lFore.position).normalized;
            var shieldMesh = AssetDatabase.LoadAllAssetsAtPath(ShieldDir + "/ShatteredAegis.fbx").OfType<Mesh>().First();
            var sb = shieldMesh.bounds;
            int thin = sb.size.x < sb.size.y ? (sb.size.x < sb.size.z ? 0 : 2) : (sb.size.y < sb.size.z ? 1 : 2);
            Debug.Log($"[ShadowFrit] shield mesh bounds {sb.center} size {sb.size} thin axis {thin}");
            float frontSign = Mathf.Sign(sb.center[thin]);
            var shieldSocket = new GameObject("Socket_Shield").transform;
            Vector3 face = Vector3.up;                                   // back of the hand in the T-pose
            shieldSocket.SetPositionAndRotation(Vector3.Lerp(lFore.position, lHand.position, 0.45f) + face * 0.06f,
                                                Quaternion.LookRotation(face * frontSign, -lArm));
            shieldSocket.SetParent(lFore, true);
            AddWeapon(ShieldDir + "/ShatteredAegis.fbx", "ShatteredAegis", shieldSocket, mats["shield"]);

            var swordMesh = AssetDatabase.LoadAllAssetsAtPath(SwordDir + "/Thunderblade.fbx").OfType<Mesh>().First();
            Debug.Log($"[ShadowFrit] sword mesh bounds {swordMesh.bounds.center} size {swordMesh.bounds.size}");

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static void AddWeapon(string fbx, string name, Transform socket, Material mat)
        {
            var w = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            w.name = name;
            w.transform.SetParent(socket, false);
            w.transform.localPosition = Vector3.zero;
            w.transform.localRotation = Quaternion.identity;
            foreach (var r in w.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
        }

        // ---------------------------------------------------------------- scene

        static VolumeProfile CreatePostProfile()
        {
            AssetDatabase.DeleteAsset(ProfilePath);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, ProfilePath);
            p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(1.2f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(0.85f, 0.75f, 1f));
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.32f);
            vig.smoothness.Override(0.5f);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.5f);
            ca.contrast.Override(12f);
            ca.saturation.Override(-10f);
            foreach (var c in p.components) AssetDatabase.AddObjectToAsset(c, p);
            EditorUtility.SetDirty(p);
            return p;
        }

        static void AddLight(string name, LightType type, Color color, float intensity, Vector3 pos, Vector3 lookAt, float range = 10f)
        {
            var go = new GameObject(name);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            if (type == LightType.Spot) { l.spotAngle = 50f; l.innerSpotAngle = 25f; }
            l.shadows = type == LightType.Directional ? LightShadows.Soft : LightShadows.None;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(lookAt - pos);
        }

        static void CreateScene(GameObject prefab, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.36f, 0.45f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.12f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.fogColor = Background;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var orbit = camGo.AddComponent<ShadowFrit.LookDevOrbit>();
            orbit.target = new Vector3(0f, 1.0f, 0f);
            orbit.yaw = 0f; orbit.pitch = 6f; orbit.distance = 6.2f;
            orbit.Apply();

            // soldiers face the camera (-Z); lights: cool key from camera-left, two purple-blue rims behind
            AddLight("Moonlight", LightType.Directional, new Color(0.75f, 0.8f, 1f), 2.2f, new Vector3(-3f, 5f, -4f), new Vector3(0f, 1f, 0f));
            AddLight("Rim_L", LightType.Spot, new Color(0.55f, 0.5f, 1f), 40f, new Vector3(-2.8f, 2.8f, 3f), new Vector3(-1f, 1.2f, 0f));
            AddLight("Rim_R", LightType.Spot, new Color(0.55f, 0.5f, 1f), 40f, new Vector3(2.8f, 2.8f, 3f), new Vector3(1f, 1.2f, 0f));
            AddLight("Fill", LightType.Spot, new Color(0.8f, 0.85f, 1f), 25f, new Vector3(2.5f, 2.2f, -4f), new Vector3(0f, 1.1f, 0f), 12f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.07f));
            gm.SetFloat("_Smoothness", 0.35f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = SaveMaterial(gm, GroundMatPath);

            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var army = new GameObject("ShadowArmy");
            var spots = new[] { (-1.6f, "Reaping_Swing"), (0f, "Walking"), (1.6f, "Running") };
            foreach (var (x, state) in spots)
            {
                var s = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                s.transform.SetParent(army.transform, false);
                s.transform.position = new Vector3(x, 0f, 0f);
                s.name = $"ShadowSentinel_{state}";
                var prev = s.GetComponentInChildren<ShadowFrit.AnimationPreview>();
                prev.startState = state;
                EditorUtility.SetDirty(prev);
            }
            FaceCamera(army.transform, camGo.transform.position);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // Rotate each soldier so its rig forward (headfront bone) points at the camera.
        static void FaceCamera(Transform army, Vector3 camPos)
        {
            foreach (Transform s in army)
            {
                var anim = s.GetComponentInChildren<Animator>();
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                var hf = FindDeep(s, "headfront");
                if (!hf) continue;
                Vector3 fwd = Vector3.ProjectOnPlane(hf.position - head.position, Vector3.up).normalized;
                Vector3 want = Vector3.ProjectOnPlane(camPos - s.position, Vector3.up).normalized;
                s.rotation = Quaternion.FromToRotation(fwd, want) * s.rotation;
            }
        }
    }
}
