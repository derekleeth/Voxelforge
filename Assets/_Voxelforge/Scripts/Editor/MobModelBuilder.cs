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
    /// <item><b>Material:</b> <c>Name_Body</c>, URP Lit with the baked albedo.</item>
    /// <item><b>Animator:</b> <c>Name.controller</c>: a Locomotion blend tree on <c>Speed</c> (Idle, then Walk and Run
    /// at the speeds its <see cref="MobDefinition"/> moves at), and one state per other clip, which
    /// <see cref="MobAnimator"/> cross-fades to by name.</item>
    /// <item><b>Prefab:</b> swaps the blocky stand-in body for the model, and <see cref="MobStandInView"/> for
    /// <see cref="MobAnimator"/>.</item>
    /// </list>
    /// </summary>
    public static class MobModelBuilder
    {
        const string ModelRoot = "Assets/_Voxelforge/Art/Models/Mobs";

        /// <summary>Each mob with a model: its Blender name, its definition and its prefab.</summary>
        static readonly (string Name, string Definition, string Prefab)[] Models =
        {
            ("Boar", "Assets/_Voxelforge/Data/Mobs/BristlebackBoar.asset", "Assets/_Voxelforge/Prefabs/Mobs/BristlebackBoar.prefab"),
            ("Wolf", "Assets/_Voxelforge/Data/Mobs/FrostfangWolf.asset", "Assets/_Voxelforge/Prefabs/Mobs/FrostfangWolf.prefab"),
        };

        static readonly HashSet<string> Looping = new HashSet<string> { "Idle", "Walk", "Run", "Paw", "Charge" };

        [MenuItem("Voxelforge/Rebuild Mob Models")]
        public static void Rebuild()
        {
            foreach ((string name, string definitionPath, string prefabPath) in Models)
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
                ImportModel(modelPath);
                BuildMaterial(folder, name, modelPath);
                AnimatorController controller = BuildController(folder, name, modelPath, definition);
                UpdatePrefab(prefabPath, modelPath, controller);
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

        static void BuildMaterial(string folder, string name, string modelPath)
        {
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Textures/{name}_Albedo.png");
            string materialFolder = $"{folder}/Materials";
            Directory.CreateDirectory(materialFolder);
            string path = $"{materialFolder}/{name}_Body.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"{name}_Body" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.12f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);

            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            importer.SaveAndReimport();
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
            tree.AddChild(Clip(clips, "Idle"), 0f);
            tree.AddChild(Clip(clips, "Walk"), walk);
            tree.AddChild(Clip(clips, "Run"), walk * definition.RunSpeed);
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

        static void UpdatePrefab(string prefabPath, string modelPath, AnimatorController controller)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform body = root.transform.Find("Body") ?? throw new InvalidOperationException($"{prefabPath} has no Body.");
                for (int i = body.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(body.GetChild(i).gameObject);
                body.localPosition = Vector3.zero;
                body.localRotation = Quaternion.identity;
                body.localScale = Vector3.one;

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
