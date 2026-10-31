using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Voxelforge.Editor
{
    /// <summary>
    /// Sets up the Mixamo hero and builds its Animator. Run <i>Voxelforge &gt; Rebuild Hero Animator</i> after
    /// downloading a new hero or animation from Mixamo (see DECISIONS.md D-011).
    /// <list type="bullet">
    /// <item><b>Imports:</b> <c>Hero.fbx</c> and every <c>Animations/Hero@Name.fbx</c> become Humanoids; each clip
    /// loops if it should, and keeps the body in place (root motion is baked into the pose,
    /// because the host's motor moves the player, not the animation).</item>
    /// <item><b>Materials:</b> <c>Hero_Body</c> and <c>Hero_TeamCloth</c>, URP Lit with the baked albedo. The
    /// player tints its own copy of Hero_TeamCloth per slot.</item>
    /// <item><b>Races (Roadmap 4.7):</b> every other race's body in <see cref="RaceModels"/> (the Thunderhoof, from
    /// <c>build_thunderhoof.py</c>) is imported the same way, with its own Humanoid avatar and materials. They all
    /// share <c>Hero.controller</c>: Humanoid retargeting plays the same clips on any of them.</item>
    /// <item><b>Animator:</b> a Locomotion blend tree on <c>Speed</c> (Idle, Walk, Run), and Jump, Fall and Land
    /// driven by <c>Grounded</c> and <c>VerticalSpeed</c>. IK Pass is on for <c>HeroFootIK</c>.</item>
    /// </list>
    /// </summary>
    public static class HeroAnimatorBuilder
    {
        public const string Folder = "Assets/_Voxelforge/Art/Models/Hero";
        public const string ModelPath = Folder + "/Hero.fbx";
        public const string AnimationFolder = Folder + "/Animations";
        public const string AlbedoPath = Folder + "/Textures/Hero_Albedo.png";
        public const string MaterialFolder = Folder + "/Materials";
        public const string ControllerPath = Folder + "/Hero.controller";

        /// <summary>
        /// Speed (m/s) each locomotion clip plays at. The motor's walk speed is 5 m/s, which is a run, and a sprint
        /// (Roadmap 4.8) is 8 m/s: the Run clip again, played faster so the feet keep up.
        /// </summary>
        const float WalkSpeed = 2f, RunSpeed = 5f, SprintSpeed = 8f;

        static readonly (string Name, bool Loop)[] Clips =
        {
            ("Idle", true), ("Walk", true), ("Run", true), ("Jump", false), ("Fall", true), ("Land", false),
        };

        /// <summary>
        /// Every race's body (Roadmap 4.7): its folder and name. Each folder holds <c>Name.fbx</c>,
        /// <c>Textures/Name_Albedo.png</c>, and gets <c>Materials/Name_Body</c> and <c>Name_TeamCloth</c>.
        /// </summary>
        public static readonly (string Folder, string Name)[] RaceModels =
        {
            (Folder, "Hero"),
            ("Assets/_Voxelforge/Art/Models/Thunderhoof", "Thunderhoof"),
        };

        [MenuItem("Voxelforge/Rebuild Hero Animator")]
        public static void Rebuild()
        {
            foreach ((string folder, string name) in RaceModels)
            {
                // The Wayfarer must be there; a race whose Blender build hasn't been run yet is skipped.
                if (name != "Hero" && AssetImporter.GetAtPath($"{folder}/{name}.fbx") == null)
                {
                    Debug.LogWarning($"[Voxelforge] No {folder}/{name}.fbx yet: run its Blender build script.");
                    continue;
                }
                ImportModel($"{folder}/{name}.fbx");
                BuildMaterials(folder, name);
            }
            foreach ((string name, bool loop) in Clips)
                ImportAnimation(name, loop);
            BuildController();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Voxelforge] Rebuilt the hero animator at {ControllerPath}.");
        }

        static Avatar ImportModel(string modelPath)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath)
                ?? throw new FileNotFoundException($"Download the rigged hero from Mixamo to {modelPath}.");
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            // Keep the Blender slots so BuildMaterials can remap them to our own materials by name.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.importAnimation = false;
            importer.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman)
                throw new InvalidOperationException($"{modelPath} didn't make a valid Humanoid avatar. Check its rig in the Inspector.");
            return avatar;
        }

        static void ImportAnimation(string name, bool loop)
        {
            string path = $"{AnimationFolder}/Hero@{name}.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path)
                ?? throw new FileNotFoundException($"Download the {name} animation from Mixamo to {path}.");
            importer.animationType = ModelImporterAnimationType.Human;
            // Each Mixamo file gets its own Humanoid avatar; Humanoid retargeting carries the motion over to the
            // hero. (Copying the hero's avatar fails: Blender's export roots the bones differently from Mixamo's.)
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;

            // Read the clips Mixamo put in the file with this avatar setup before tuning them.
            importer.clipAnimations = null;
            importer.SaveAndReimport();
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = name;
                clip.loopTime = loop;
                clip.loopPose = loop;
                // The motor moves the player, so the body must stay over it. Turning and height stay in the pose;
                // forward travel (Mixamo's walk and run move 1.4 m and 2.5 m per cycle unless downloaded In Place)
                // is pulled out as root motion, which the Animator then ignores (applyRootMotion is off). Baking XZ
                // into the pose instead would carry the body away from the player and snap it back every loop.
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = false;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = false;
                clip.heightFromFeet = true;
                clip.lockRootPositionXZ = false;
                clip.keepOriginalPositionXZ = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static AnimationClip LoadClip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath($"{AnimationFolder}/Hero@{name}.fbx")
                .OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));

        static void BuildMaterials(string folder, string name)
        {
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Textures/{name}_Albedo.png");
            string materialFolder = $"{folder}/Materials";
            Directory.CreateDirectory(materialFolder);
            var materials = new[]
            {
                MakeMaterial(materialFolder, $"{name}_Body", albedo), MakeMaterial(materialFolder, $"{name}_TeamCloth", albedo),
            };

            // Hand the materials to the model by the names Blender gave its slots.
            var importer = (ModelImporter)AssetImporter.GetAtPath($"{folder}/{name}.fbx");
            foreach (Material material in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            importer.SaveAndReimport();
        }

        static Material MakeMaterial(string folder, string name, Texture2D albedo)
        {
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.15f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildController()
        {
            AnimatorController controller = LoadEmptiedController();
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.parameters = controller.parameters.Select(p =>
            {
                if (p.name == "Grounded")
                    p.defaultBool = true;
                return p;
            }).ToArray();

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(LoadClip("Idle"), 0f);
            tree.AddChild(LoadClip("Walk"), WalkSpeed);
            tree.AddChild(LoadClip("Run"), RunSpeed);
            tree.AddChild(LoadClip("Run"), SprintSpeed);
            ChildMotion[] children = tree.children;
            children[^1].timeScale = SprintSpeed / RunSpeed;
            tree.children = children;
            locomotion.iKOnFeet = true;
            machine.defaultState = locomotion;

            AnimatorState jump = AddState(machine, "Jump", new Vector3(300, 0, 0));
            AnimatorState fall = AddState(machine, "Fall", new Vector3(300, 120, 0));
            AnimatorState land = AddState(machine, "Land", new Vector3(0, 200, 0));

            // Leaving the ground: going up is a jump, going down (off a ledge too tall to step down) is a fall.
            Transition(locomotion, jump, 0.1f, (AnimatorConditionMode.IfNot, "Grounded", 0f), (AnimatorConditionMode.Greater, "VerticalSpeed", 1f));
            Transition(locomotion, fall, 0.2f, (AnimatorConditionMode.IfNot, "Grounded", 0f), (AnimatorConditionMode.Less, "VerticalSpeed", -2f));
            Transition(jump, fall, 0.25f, (AnimatorConditionMode.Less, "VerticalSpeed", 0f));
            Transition(jump, land, 0.1f, (AnimatorConditionMode.If, "Grounded", 0f));
            Transition(fall, land, 0.1f, (AnimatorConditionMode.If, "Grounded", 0f));
            // Landing plays briefly, and walking off cuts it short.
            AnimatorStateTransition settle = Transition(land, locomotion, 0.2f);
            settle.hasExitTime = true;
            settle.exitTime = 0.5f;
            Transition(land, locomotion, 0.15f, (AnimatorConditionMode.Greater, "Speed", 1f));
            Transition(land, jump, 0.1f, (AnimatorConditionMode.IfNot, "Grounded", 0f), (AnimatorConditionMode.Greater, "VerticalSpeed", 1f));
        }

        /// <summary>
        /// The controller asset with everything removed. It's emptied rather than deleted and recreated, so it keeps
        /// its GUID and the player prefab's Animator stays linked to it.
        /// </summary>
        static AnimatorController LoadEmptiedController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                return AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states)
                machine.RemoveState(child.state);
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
                machine.RemoveAnyStateTransition(transition);
            foreach (BlendTree tree in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>())
                UnityEngine.Object.DestroyImmediate(tree, true);
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            return controller;
        }

        static AnimatorState AddState(AnimatorStateMachine machine, string clip, Vector3 position)
        {
            AnimatorState state = machine.AddState(clip, position);
            state.motion = LoadClip(clip);
            state.iKOnFeet = true;
            return state;
        }

        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration,
            params (AnimatorConditionMode Mode, string Parameter, float Threshold)[] conditions)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            foreach ((AnimatorConditionMode mode, string parameter, float threshold) in conditions)
                transition.AddCondition(mode, threshold, parameter);
            return transition;
        }
    }
}
