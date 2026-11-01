using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Editor
{
    /// <summary>
    /// Puts the Blender-built mobs (Roadmap 5.2 step E, <c>Tools/Blender/build_mobs.py</c>) into the game. Run
    /// <i>Voxelforge &gt; Rebuild Mob Models</i> after exporting from Blender. For each mob in <see cref="Models"/>:
    /// <list type="bullet">
    /// <item><b>Import:</b> <c>Art/Models/Mobs/Name/Name.fbx</c> as a Generic rig; each Blender action (<c>Name|Clip</c>)
    /// becomes a clip called <c>Clip</c>, looping if it's a cycle. The host's motor moves the mob, so no root motion.</item>
    /// <item><b>Materials:</b> <c>Name_Body</c>, URP Lit with the baked albedo; or, for a model with no albedo (the
    /// Slime, step F), its own slots from <see cref="PlainMaterials"/>: see-through jelly, a glowing core.</item>
    /// <item><b>Animator:</b> <c>Name.controller</c>: a Locomotion blend tree on <c>Speed</c> (Idle, then Walk and Run
    /// at the speeds its <see cref="MobDefinition"/> moves at), and one state per other clip, which
    /// <see cref="MobAnimator"/> cross-fades to by name.</item>
    /// <item><b>Prefab:</b> swaps the blocky stand-in body for the model at its scale (one Slime model serves all three
    /// sizes), and <see cref="MobStandInView"/> for <see cref="MobAnimator"/>.</item>
    /// <item><b>Effects (step G):</b> gives each prefab a <see cref="MobEffects"/> with its dust and splash colours, and
    /// makes their materials in <c>Art/Materials/MobFx</c>.</item>
    /// </list>
    /// </summary>
    public static class MobModelBuilder
    {
        const string ModelRoot = "Assets/_Voxelforge/Art/Models/Mobs";

        const string Data = "Assets/_Voxelforge/Data/Mobs/", Prefabs = "Assets/_Voxelforge/Prefabs/Mobs/";

        /// <summary>
        /// Each mob with a model: its Blender name, its definition, its prefab and how big the model is in it. A model
        /// used by several mobs gets its animator from the first.
        /// </summary>
        static readonly (string Name, string Definition, string Prefab, float Scale)[] Models =
        {
            ("Boar", Data + "BristlebackBoar.asset", Prefabs + "BristlebackBoar.prefab", 1f),
            ("Wolf", Data + "FrostfangWolf.asset", Prefabs + "FrostfangWolf.prefab", 1f),
            ("Rhino", Data + "ThunderRhino.asset", Prefabs + "ThunderRhino.prefab", 1f),
            ("Slime", Data + "CrystalSlime.asset", Prefabs + "CrystalSlime.prefab", 0.9f),
            ("Slime", Data + "CrystalSlimeMedium.asset", Prefabs + "CrystalSlimeMedium.prefab", 0.64f),
            ("Slime", Data + "CrystalSlimeSmall.asset", Prefabs + "CrystalSlimeSmall.prefab", 0.4f),
        };

        /// <summary>A model's own material slots, for one with no baked albedo: colour, see-through, glow.</summary>
        static readonly Dictionary<string, (Color Color, bool Clear, float Glow, float Smoothness)> PlainMaterials =
            new Dictionary<string, (Color, bool, float, float)>
            {
                ["Slime_Shell"] = (new Color(0.35f, 0.85f, 1f, 0.5f), true, 0.25f, 0.85f),
                ["Slime_Core"] = (new Color(0.62f, 0.30f, 1f), false, 1.1f, 0.6f),
                ["Slime_Eye"] = (new Color(0.03f, 0.03f, 0.06f), false, 0f, 0.9f),
            };

        const string FxFolder = "Assets/_Voxelforge/Art/Materials/MobFx";

        /// <summary>The colour of each model's dust, and of its splash when it splits or dies (clear for none).</summary>
        static readonly Dictionary<string, (Color Dust, Color Splash)> FxColours = new Dictionary<string, (Color, Color)>
        {
            ["Boar"] = (new Color(0.55f, 0.42f, 0.28f, 0.9f), Color.clear),
            ["Wolf"] = (new Color(0.86f, 0.9f, 0.96f, 0.9f), Color.clear),
            ["Rhino"] = (new Color(0.66f, 0.58f, 0.44f, 0.9f), Color.clear),
            ["Slime"] = (new Color(0.5f, 0.85f, 1f, 0.8f), new Color(0.4f, 0.85f, 1f, 0.85f)),
        };

        static readonly HashSet<string> Looping = new HashSet<string> { "Idle", "Walk", "Run", "Paw", "Charge" };

        [MenuItem("Voxelforge/Rebuild Mob Models")]
        public static void Rebuild()
        {
            var built = new Dictionary<string, AnimatorController>();
            (Material dust, Material mark) = BuildFxMaterials();
            foreach ((string name, string definitionPath, string prefabPath, float scale) in Models)
            {
                string folder = $"{ModelRoot}/{name}";
                string modelPath = $"{folder}/{name}.fbx";
                if (AssetImporter.GetAtPath(modelPath) == null)
                {
                    Debug.LogWarning($"[Voxelforge] No {modelPath} yet: run build_mobs.py in Blender.");
                    continue;
                }
                var definition = AssetDatabase.LoadAssetAtPath<MobDefinition>(definitionPath)
                    ?? throw new FileNotFoundException(definitionPath);
                if (!built.TryGetValue(name, out AnimatorController controller))
                {
                    ImportModel(modelPath);
                    BuildMaterials(folder, name, modelPath);
                    controller = built[name] = BuildController(folder, name, modelPath, definition);
                }
                UpdatePrefab(prefabPath, modelPath, controller, scale, definition.Hops);
                AddEffects(prefabPath, name, dust, mark);
                Debug.Log($"[Voxelforge] Rebuilt the {definition.DisplayName}'s model and animator.");
            }
            AssetDatabase.SaveAssets();
        }

        static void ImportModel(string modelPath)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            // Read the takes Blender wrote before naming them.
            importer.clipAnimations = null;
            importer.SaveAndReimport();

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = ClipName(clip.takeName);
                clip.loopTime = Looping.Contains(clip.name);
                clip.loopPose = false; // Blender's cycles already end where they start.
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>"BoarRig|Boar|Walk" or "Boar|Walk" is "Walk".</summary>
        static string ClipName(string take)
        {
            int bar = take.LastIndexOf('|');
            return bar >= 0 ? take.Substring(bar + 1) : take;
        }

        static void BuildMaterials(string folder, string name, string modelPath)
        {
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Textures/{name}_Albedo.png");
            string materialFolder = $"{folder}/Materials";
            Directory.CreateDirectory(materialFolder);
            var materials = new List<Material>();
            if (albedo != null)
            {
                Material body = LoadOrCreate(materialFolder, $"{name}_Body");
                body.SetTexture("_BaseMap", albedo);
                body.SetColor("_BaseColor", Color.white);
                body.SetFloat("_Smoothness", 0.12f);
                materials.Add(body);
            }
            else
            {
                foreach (KeyValuePair<string, (Color Color, bool Clear, float Glow, float Smoothness)> plain in PlainMaterials)
                {
                    if (!plain.Key.StartsWith(name + "_", StringComparison.Ordinal))
                        continue;
                    Material material = LoadOrCreate(materialFolder, plain.Key);
                    Paint(material, plain.Value.Color, plain.Value.Clear, plain.Value.Glow, plain.Value.Smoothness);
                    materials.Add(material);
                }
            }

            // Hand the materials to the model by the names Blender gave its slots.
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            foreach (Material material in materials)
            {
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            }
            importer.SaveAndReimport();
        }

        static Material LoadOrCreate(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        /// <summary>A plain URP Lit colour; see-through (alpha-blended) and glowing if asked.</summary>
        static void Paint(Material material, Color color, bool clear, float glow, float smoothness)
        {
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", clear ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", clear ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1f);
            material.SetFloat("_DstBlend", clear ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 0f);
            material.SetFloat("_ZWrite", clear ? 0f : 1f);
            material.SetOverrideTag("RenderType", clear ? "Transparent" : "Opaque");
            material.renderQueue = clear ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
            if (clear)
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (glow > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(color.r * glow, color.g * glow, color.b * glow, 1f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
        }

        static AnimatorController BuildController(string folder, string name, string modelPath, MobDefinition definition)
        {
            Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToDictionary(c => c.name);

            string path = $"{folder}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = controller.CreateBlendTreeInController(MobAnimator.Locomotion, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            float walk = definition.Motor.WalkSpeed;
            // A mob with no walk or run (a slime gets about in hops) wobbles in place, whatever its speed.
            AnimationClip idle = Clip(clips, "Idle");
            tree.AddChild(idle, 0f);
            tree.AddChild(clips.TryGetValue("Walk", out AnimationClip walking) ? walking : idle, walk);
            tree.AddChild(clips.TryGetValue("Run", out AnimationClip running) ? running : idle, walk * definition.RunSpeed);
            machine.defaultState = locomotion;

            int row = 0;
            foreach (string state in new[] { MobAnimator.Attack, MobAnimator.Paw, MobAnimator.Charge, MobAnimator.Howl,
                         MobAnimator.Hit, MobAnimator.Death })
            {
                if (!clips.TryGetValue(state, out AnimationClip clip))
                    continue;
                AnimatorState added = machine.AddState(state, new Vector3(320f, 60f * row++, 0f));
                added.motion = clip;
                added.writeDefaultValues = true;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string name) =>
            clips.TryGetValue(name, out AnimationClip clip)
                ? clip
                : throw new InvalidOperationException($"The model has no {name} clip; check build_mobs.py.");

        /// <summary>The dust particles (alpha-blended, from the spells' particle shader) and the charge strip's colour.</summary>
        static (Material Dust, Material Mark) BuildFxMaterials()
        {
            Directory.CreateDirectory(FxFolder);
            var glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Voxelforge/Art/Materials/Props/ManaBolt.mat");
            string dustPath = $"{FxFolder}/MobDust.mat";
            var dust = AssetDatabase.LoadAssetAtPath<Material>(dustPath);
            if (dust == null)
            {
                dust = glow != null ? new Material(glow) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                dust.name = "MobDust";
                AssetDatabase.CreateAsset(dust, dustPath);
            }
            dust.SetFloat("_Surface", 1f);
            dust.SetFloat("_Blend", 0f);
            dust.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            dust.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            dust.SetFloat("_ZWrite", 0f);
            dust.SetColor("_BaseColor", Color.white);
            dust.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            dust.DisableKeyword("_ALPHAMODULATE_ON");
            dust.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dust.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(dust);

            string markPath = $"{FxFolder}/ChargeMark.mat";
            var mark = AssetDatabase.LoadAssetAtPath<Material>(markPath);
            if (mark == null)
            {
                mark = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ChargeMark" };
                AssetDatabase.CreateAsset(mark, markPath);
            }
            mark.SetFloat("_Surface", 1f);
            mark.SetFloat("_Blend", 0f);
            mark.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mark.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mark.SetFloat("_ZWrite", 0f);
            mark.SetFloat("_Cull", 0f);
            mark.SetOverrideTag("RenderType", "Transparent");
            mark.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mark.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mark.SetColor("_BaseColor", new Color(1f, 0.12f, 0.08f, 0.5f));
            EditorUtility.SetDirty(mark);
            return (dust, mark);
        }

        static void AddEffects(string prefabPath, string name, Material dust, Material mark)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                MobEffects effects = root.GetComponent<MobEffects>() ?? root.AddComponent<MobEffects>();
                var serialized = new SerializedObject(effects);
                serialized.FindProperty("mob").objectReferenceValue = root.GetComponent<Mob>();
                serialized.FindProperty("effectMaterial").objectReferenceValue = dust;
                serialized.FindProperty("markMaterial").objectReferenceValue = mark;
                if (FxColours.TryGetValue(name, out (Color Dust, Color Splash) colours))
                {
                    serialized.FindProperty("dust").colorValue = colours.Dust;
                    serialized.FindProperty("splash").colorValue = colours.Splash;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void UpdatePrefab(string prefabPath, string modelPath, AnimatorController controller, float scale, bool hops)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform body = root.transform.Find("Body") ?? throw new InvalidOperationException($"{prefabPath} has no Body.");
                for (int i = body.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(body.GetChild(i).gameObject);
                body.localPosition = Vector3.zero;
                body.localRotation = Quaternion.identity;
                body.localScale = Vector3.one * scale;

                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), body);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                if (root.TryGetComponent(out MobStandInView standIn))
                    UnityEngine.Object.DestroyImmediate(standIn);
                MobAnimator view = root.GetComponent<MobAnimator>() ?? root.AddComponent<MobAnimator>();
                var serialized = new SerializedObject(view);
                serialized.FindProperty("mob").objectReferenceValue = root.GetComponent<Mob>();
                serialized.FindProperty("animator").objectReferenceValue = animator;
                serialized.FindProperty("squash").objectReferenceValue = hops ? body : null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
