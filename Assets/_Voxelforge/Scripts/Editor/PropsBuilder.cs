using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Props &amp; Items</i> (Roadmap 3.5): makes everything the tools, torches and workbench need
    /// from the Blender art that <c>Tools/Blender/build_props.py</c> exports. Re-running it updates the same assets in place.
    /// <list type="bullet">
    /// <item>Materials: <c>Props_World</c> (Voxelforge/Foliage, for torches and workbenches in the world) and
    /// <c>Props_Held</c> (URP Lit, for hands and drops), both with the glow map.</item>
    /// <item>Blocks: Torch (11) and Workbench (12) in the <see cref="BlockRegistry"/>, and every block's harvest kind.</item>
    /// <item>Prop decorations (floor torch with a light, wall torch with a light, workbench) added to the <see cref="DecorationSet"/>.</item>
    /// <item>Items (the tools, plus models for held and dropped torches and workbenches), the <see cref="ItemRegistry"/>,
    /// the recipes and the <see cref="RecipeBook"/>.</item>
    /// <item>Kian's M.G.R.S. (Roadmap 3.6): its item, pointing at the Mana Bolt ability (<see cref="AbilityBuilder"/>, 4.1),
    /// its recipe, and <c>ManaBolt.mat</c>, the additive glow for its bolts, trails and sparks.</item>
    /// <item>The Home Totem (Roadmap 3.8): block 13, its prop with a soft green light, its item and its recipe.</item>
    /// <item>The wearable bags (Roadmap 4.5): Woven Satchel, Traveler's Pack and Delver's Rucksack, Gear items with their
    /// extra slots, and workbench recipes for now (5.6 Leatherworking will make them from hides).</item>
    /// <item>The Storage Chest (Roadmap 4.5): block 14, its body prop (the lid is drawn by <see cref="ChestLids"/>), its
    /// item and its recipe (8 Logs, anywhere).</item>
    /// </list>
    /// </summary>
    public static class PropsBuilder
    {
        const string ModelDir = "Assets/_Voxelforge/Art/Models/Props";
        const string AtlasPath = "Assets/_Voxelforge/Art/Textures/Props/Props_Atlas.png";
        const string GlowPath = "Assets/_Voxelforge/Art/Textures/Props/Props_Glow.png";
        const string MaterialDir = "Assets/_Voxelforge/Art/Materials/Props";
        const string BlockDir = "Assets/_Voxelforge/Data/Blocks";
        const string DecorationDir = "Assets/_Voxelforge/Data/Decorations";
        const string ItemDir = "Assets/_Voxelforge/Data/Items";
        const string RecipeDir = "Assets/_Voxelforge/Data/Items/Recipes";

        static readonly Color Glow = new Color(2.2f, 2.2f, 2.2f);
        // Held and dropped items glow harder than placed props: in the hand they're small, and a torch flame at 2.2 was
        // barely noticeable (4.1 play-test).
        static readonly Color HeldGlow = new Color(3.5f, 3.5f, 3.5f);
        // A held torch lights the cave around the hero: like a placed torch (2.4 over 10 m), a little softer.
        static readonly DecorationLight HeldTorchLight = new DecorationLight
        {
            enabled = true, color = new Color(1f, 0.64f, 0.32f), intensity = 1.8f, range = 8f,
            offset = new Vector3(0f, 0.73f, 0f), flicker = 0.35f,
        };

        // Held poses in the Wayfarer's right hand bone (2.5), measured on its idle pose: tools held out front, ready
        // to swing with the head pointing down at the block; torches and workbenches upright. The grip sits in the palm.
        static readonly Vector3 Grip = new Vector3(0f, 0.08f, 0f);
        static readonly Vector3 ToolPose = new Vector3(352f, 314f, 70.8f);
        static readonly Vector3 UprightPose = new Vector3(359.5f, 323.9f, 148f);
        // The M.G.R.S. (3.6) is aimed, not swung: its barrel points ahead of the hero and a little up.
        static readonly Vector3 StaffPose = new Vector3(72.3f, 233.3f, 350.7f);
        static readonly Color TorchLight = new Color(1f, 0.64f, 0.32f);
        static readonly Color HomeGlow = new Color(0.35f, 1f, 0.45f);

        // The M.G.R.S.'s muzzle (3.6) is the top of the gold barrel crown in build_props.py (z 1.15 in Blender, +Y in
        // Unity). What it shoots is the Mana Bolt ability (4.1, AbilityBuilder).
        static readonly Vector3 StaffMuzzle = new Vector3(0f, 1.16f, 0f);

        [MenuItem("Voxelforge/Build Props & Items")]
        public static void Build()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var glow = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowPath);
            if (atlas == null || glow == null)
            {
                Debug.LogError($"[Voxelforge] Missing {AtlasPath} or {GlowPath}. Run build_all() in Tools/Blender/build_props.py first.");
                return;
            }
            EnsureFolder(MaterialDir);
            EnsureFolder(RecipeDir);

            Material world = MakeWorldMaterial(atlas, glow);
            Material held = MakeHeldMaterial(atlas, glow);

            // ---- Blocks ----
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>($"{BlockDir}/BlockRegistry.asset");
            BlockDefinition torch = MakeBlock("Torch", BlockIds.Torch, durability: 1, solid: false, needsSupport: true,
                light: 14, HarvestKind.Any, new Color32(255, 170, 70, 255));
            BlockDefinition workbench = MakeBlock("Workbench", BlockIds.Workbench, durability: 60, solid: true, needsSupport: false,
                light: 0, HarvestKind.Axe, new Color32(150, 100, 60, 255));
            BlockDefinition homeTotem = MakeBlock("HomeTotem", BlockIds.HomeTotem, durability: 60, solid: true, needsSupport: false,
                light: 0, HarvestKind.Axe, new Color32(90, 220, 110, 255), blastProof: true);
            // Blast-proof like the totem: a partner's spell shouldn't scatter a base's storage.
            BlockDefinition chest = MakeBlock("Chest", BlockIds.Chest, durability: 60, solid: true, needsSupport: false,
                light: 0, HarvestKind.Axe, new Color32(160, 110, 60, 255), blastProof: true, displayName: "Storage Chest");
            AddToList(registry, "blocks", torch, workbench, homeTotem, chest);
            SetHarvestKinds(registry);

            // ---- Prop decorations ----
            Mesh torchFloorMesh = LoadMesh("TorchFloor");
            Mesh torchWallMesh = LoadMesh("TorchWall");
            Mesh workbenchMesh = LoadMesh("Workbench");
            Mesh totemMesh = LoadMesh("HomeTotem");
            Mesh chestBodyMesh = LoadMesh("ChestBody");
            if (torchFloorMesh == null || torchWallMesh == null || workbenchMesh == null || totemMesh == null || chestBodyMesh == null)
                return;
            var floorLight = new DecorationLight
            {
                enabled = true, color = TorchLight, intensity = 2.4f, range = 10f, offset = FlameOf(torchFloorMesh), flicker = 0.35f,
            };
            // The wall torch leans out of its wall (+Z): its flame sits forward of the cell's middle and lower.
            DecorationLight wallLight = floorLight;
            wallLight.offset = FlameOf(torchWallMesh);
            var decorationSet = AssetDatabase.LoadAssetAtPath<DecorationSet>($"{DecorationDir}/DecorationSet.asset");
            AddToList(decorationSet, "decorations",
                MakeProp("TorchFloor", torchFloorMesh, world, torch, PropAttach.Floor, floorLight, castShadows: false),
                MakeProp("TorchWall", torchWallMesh, world, torch, PropAttach.Wall, wallLight, castShadows: false),
                MakeProp("Workbench", workbenchMesh, world, workbench, PropAttach.Any, default, castShadows: true),
                // A soft green glow at the carved houses, with only a little shimmer.
                MakeProp("HomeTotem", totemMesh, world, homeTotem, PropAttach.Any,
                    new DecorationLight { enabled = true, color = HomeGlow, intensity = 1.6f, range = 7f, offset = new Vector3(0f, 0.6f, 0f), flicker = 0.12f },
                    castShadows: true),
                // Just the body: ChestLids draws the lid so it can open (Roadmap 4.5).
                MakeProp("Chest", chestBodyMesh, world, chest, PropAttach.Any, default, castShadows: true));

            // ---- Items ----
            ItemDefinition woodenPickaxe = MakeItem("WoodenPickaxe", ItemIds.WoodenPickaxe, "Wooden Pickaxe", held, new ToolPower(2f, 1f, 1.5f));
            ItemDefinition stonePickaxe = MakeItem("StonePickaxe", ItemIds.StonePickaxe, "Stone Pickaxe", held, new ToolPower(3f, 1f, 2f));
            ItemDefinition stoneAxe = MakeItem("StoneAxe", ItemIds.StoneAxe, "Stone Axe", held, new ToolPower(1f, 3f, 1.5f));
            ItemDefinition destiny = MakeItem("PickOfDestiny", ItemIds.PickOfDestiny, "The Pick of Destiny", held, new ToolPower(5f, 5f, 3f));
            AbilityDefinition manaBolt = AbilityBuilder.Build();
            ItemDefinition mgrs = MakeItem("Mgrs", ItemIds.Mgrs, "M.G.R.S.", held, default, ability: manaBolt, muzzle: StaffMuzzle,
                pose: StaffPose);
            ItemDefinition torchItem = MakeItem("TorchFloor", BlockIds.Torch, "Torch", held, default, "Torch", dropSize: 1.6f,
                heldScale: 0.7f, upright: true, heldLight: HeldTorchLight);
            ItemDefinition benchItem = MakeItem("Workbench", BlockIds.Workbench, "Workbench", held, default, "WorkbenchItem", dropSize: 1.3f,
                heldScale: 0.3f, upright: true);
            ItemDefinition totemItem = MakeItem("HomeTotem", BlockIds.HomeTotem, "Home Totem", held, default, "HomeTotemItem", dropSize: 1.4f,
                heldScale: 0.35f, upright: true);
            ItemDefinition satchel = MakeItem("BagSatchel", ItemIds.WovenSatchel, "Woven Satchel", held, default, "WovenSatchel",
                dropSize: 1.4f, heldScale: 0.6f, upright: true, bagSlots: 8);
            ItemDefinition pack = MakeItem("BagPack", ItemIds.TravelersPack, "Traveler's Pack", held, default, "TravelersPack",
                dropSize: 1.6f, heldScale: 0.5f, upright: true, bagSlots: 12);
            ItemDefinition rucksack = MakeItem("BagRucksack", ItemIds.DelversRucksack, "Delver's Rucksack", held, default,
                "DelversRucksack", dropSize: 1.8f, heldScale: 0.45f, upright: true, bagSlots: 16);
            ItemDefinition chestItem = MakeItem("Chest", BlockIds.Chest, "Storage Chest", held, default, "ChestItem", dropSize: 1.3f,
                heldScale: 0.3f, upright: true);
            var items = LoadOrCreate<ItemRegistry>($"{ItemDir}/ItemRegistry.asset");
            AddToList(items, "items", woodenPickaxe, stonePickaxe, stoneAxe, destiny, torchItem, benchItem, mgrs, totemItem,
                satchel, pack, rucksack, chestItem);

            // ---- Recipes (add new ones at the end: a recipe's place in the book is sent over the network) ----
            registry.TryGet(BlockIds.Log, out BlockDefinition log);
            registry.TryGet(BlockIds.Stone, out BlockDefinition stone);
            registry.TryGet(BlockIds.ManaCrystalOre, out BlockDefinition mana);
            var book = LoadOrCreate<RecipeBook>($"{ItemDir}/RecipeBook.asset");
            SetList(book, "recipes",
                MakeRecipe("Workbench", new ItemAmount(workbench, 1), false, new ItemAmount(log, 4)),
                MakeRecipe("Torches", new ItemAmount(torch, 4), false, new ItemAmount(log, 1)),
                MakeRecipe("WoodenPickaxe", new ItemAmount(woodenPickaxe, 1), true, new ItemAmount(log, 3)),
                MakeRecipe("StonePickaxe", new ItemAmount(stonePickaxe, 1), true, new ItemAmount(log, 2), new ItemAmount(stone, 3)),
                MakeRecipe("StoneAxe", new ItemAmount(stoneAxe, 1), true, new ItemAmount(log, 2), new ItemAmount(stone, 3)),
                MakeRecipe("PickOfDestiny", new ItemAmount(destiny, 1), true,
                    new ItemAmount(stonePickaxe, 1), new ItemAmount(stoneAxe, 1), new ItemAmount(mana, 3)),
                MakeRecipe("Mgrs", new ItemAmount(mgrs, 1), true,
                    new ItemAmount(log, 3), new ItemAmount(stone, 2), new ItemAmount(mana, 5)),
                MakeRecipe("HomeTotem", new ItemAmount(homeTotem, 1), true, new ItemAmount(log, 4), new ItemAmount(mana, 1)),
                // Roadmap 4.5. Bark and stone for now; Leatherworking (5.6) replaces these with hides.
                MakeRecipe("WovenSatchel", new ItemAmount(satchel, 1), true, new ItemAmount(log, 6)),
                MakeRecipe("TravelersPack", new ItemAmount(pack, 1), true, new ItemAmount(log, 8), new ItemAmount(stone, 4)),
                MakeRecipe("DelversRucksack", new ItemAmount(rucksack, 1), true,
                    new ItemAmount(log, 10), new ItemAmount(stone, 6), new ItemAmount(mana, 3)),
                MakeRecipe("Chest", new ItemAmount(chest, 1), false, new ItemAmount(log, 8)));
            MakeBoltMaterial();

            AssetDatabase.SaveAssets();

            var errors = new List<string>();
            errors.AddRange(registry.Validate());
            errors.AddRange(decorationSet.Validate(null, registry));
            errors.AddRange(items.Validate(registry));
            errors.AddRange(book.Validate());
            if (errors.Count > 0)
                Debug.LogError("[Voxelforge] Props & items have problems:\n" + string.Join("\n", errors));
            else
                Debug.Log("[Voxelforge] Built props, items and recipes.", book);
        }

        /// <summary>The middle of a torch model's flame: the top quarter of the mesh, where the light goes.</summary>
        static Vector3 FlameOf(Mesh mesh)
        {
            Bounds b = mesh.bounds;
            return new Vector3(b.center.x, b.max.y - b.size.y * 0.12f, b.center.z) + FlameLean(mesh);
        }

        /// <summary>For a leaning torch, the flame is at the far end of the lean, not over the middle of the box.</summary>
        static Vector3 FlameLean(Mesh mesh)
        {
            Bounds b = mesh.bounds;
            // The wall side is the +Z face; the flame leans away from it, to the -Z end of the box.
            return mesh.name == "TorchWall" ? new Vector3(0f, 0f, b.min.z + 0.07f - b.center.z) : Vector3.zero;
        }

        static Material MakeWorldMaterial(Texture2D atlas, Texture2D glow)
        {
            Material material = LoadOrCreateMaterial($"{MaterialDir}/Props_World.mat", Shader.Find("Voxelforge/Foliage"));
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_NormalUp", 0f);
            material.SetFloat("_BackFaceFlip", 0f);
            material.SetFloat("_WindScale", 0.6f); // Only flame tips have sway painted in.
            material.SetTexture("_EmissionMap", glow);
            material.SetColor("_EmissionColor", Glow);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material MakeHeldMaterial(Texture2D atlas, Texture2D glow)
        {
            Material material = LoadOrCreateMaterial($"{MaterialDir}/Props_Held.mat", Shader.Find("Universal Render Pipeline/Lit"));
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.3f);
            material.SetTexture("_EmissionMap", glow);
            material.SetColor("_EmissionColor", HeldGlow);
            material.EnableKeyword("_EMISSION");
            // Not None: URP's keyword check (run on import and in the Inspector) reads None as "emission off" and strips
            // _EMISSION, so held items stop glowing. We have no realtime GI, so this flag changes nothing else.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>URP Particles/Unlit, additive: the Mana Bolt's core, trail and sparks (tinted per renderer and particle).</summary>
        static Material MakeBoltMaterial()
        {
            Material material = LoadOrCreateMaterial($"{MaterialDir}/ManaBolt.mat", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 2f); // Additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }

        static BlockDefinition MakeBlock(string name, ushort id, int durability, bool solid, bool needsSupport, int light,
            HarvestKind harvest, Color32 mapColor, bool blastProof = false, string displayName = null)
        {
            var block = LoadOrCreate<BlockDefinition>($"{BlockDir}/{name}.asset");
            var serialized = new SerializedObject(block);
            serialized.FindProperty("id").intValue = id;
            serialized.FindProperty("displayName").stringValue = displayName ?? name;
            serialized.FindProperty("blastProof").boolValue = blastProof;
            serialized.FindProperty("maxDurability").intValue = durability;
            serialized.FindProperty("drop").objectReferenceValue = null; // Drops itself.
            serialized.FindProperty("dropCount").intValue = 1;
            serialized.FindProperty("harvestKind").enumValueIndex = (int)harvest;
            serialized.FindProperty("isSolid").boolValue = solid;
            serialized.FindProperty("isTransparent").boolValue = true; // Props have no cube faces.
            serialized.FindProperty("isProp").boolValue = true;
            serialized.FindProperty("needsSupport").boolValue = needsSupport;
            serialized.FindProperty("lightEmission").intValue = light;
            serialized.FindProperty("mapColor").colorValue = mapColor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return block;
        }

        /// <summary>Rock and ore want a pickaxe, wood wants an axe, the rest is soft (Roadmap 3.5).</summary>
        static void SetHarvestKinds(BlockRegistry registry)
        {
            foreach (BlockDefinition block in registry.Blocks)
            {
                if (block == null)
                    continue;
                HarvestKind kind;
                switch (block.Id)
                {
                    case BlockIds.Stone:
                    case BlockIds.ManaCrystalOre:
                    case BlockIds.Sandstone:
                    case BlockIds.RedRock:
                        kind = HarvestKind.Pick;
                        break;
                    case BlockIds.TreeTrunk:
                    case BlockIds.Log:
                    case BlockIds.Workbench:
                    case BlockIds.HomeTotem:
                    case BlockIds.Chest:
                        kind = HarvestKind.Axe;
                        break;
                    default:
                        kind = HarvestKind.Any;
                        break;
                }
                var serialized = new SerializedObject(block);
                serialized.FindProperty("harvestKind").enumValueIndex = (int)kind;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static Mesh LoadMesh(string model)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath($"{ModelDir}/{model}.fbx"))
            {
                if (asset is Mesh mesh && mesh.name == model)
                    return mesh;
            }
            Debug.LogError($"[Voxelforge] {ModelDir}/{model}.fbx needs a mesh called '{model}'. Run build_props.py first.");
            return null;
        }

        static DecorationDefinition MakeProp(string name, Mesh mesh, Material material, BlockDefinition block, PropAttach attach,
            DecorationLight light, bool castShadows)
        {
            var decoration = LoadOrCreate<DecorationDefinition>($"{DecorationDir}/{name}.asset");
            var serialized = new SerializedObject(decoration);
            serialized.FindProperty("kind").enumValueIndex = (int)DecorationKind.Prop;
            serialized.FindProperty("mesh").objectReferenceValue = mesh;
            serialized.FindProperty("lodMesh").objectReferenceValue = null;
            serialized.FindProperty("material").objectReferenceValue = material;
            serialized.FindProperty("tint").enumValueIndex = (int)BiomeTint.None;
            serialized.FindProperty("castShadows").boolValue = castShadows;
            serialized.FindProperty("lodDistance").floatValue = 200f;
            serialized.FindProperty("maxDistance").floatValue = 90f;
            serialized.FindProperty("propBlock").objectReferenceValue = block;
            serialized.FindProperty("attach").enumValueIndex = (int)attach;
            serialized.FindProperty("minScale").floatValue = 1f;
            serialized.FindProperty("maxScale").floatValue = 1f;
            WriteLight(serialized.FindProperty("light"), light);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return decoration;
        }

        static ItemDefinition MakeItem(string model, ushort id, string displayName, Material material, ToolPower power,
            string assetName = null, float dropSize = 2f, float heldScale = 1f, bool upright = false, AbilityDefinition ability = null,
            Vector3 muzzle = default, Vector3? pose = null, DecorationLight heldLight = default, int bagSlots = 0)
        {
            Mesh mesh = LoadMesh(model);
            var item = LoadOrCreate<ItemDefinition>($"{ItemDir}/{assetName ?? model}.asset");
            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").intValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("mesh").objectReferenceValue = mesh;
            serialized.FindProperty("material").objectReferenceValue = material;
            serialized.FindProperty("heldPosition").vector3Value = Grip;
            serialized.FindProperty("heldRotation").vector3Value = pose ?? (upright ? UprightPose : ToolPose);
            serialized.FindProperty("heldScale").floatValue = heldScale;
            serialized.FindProperty("dropSize").floatValue = dropSize;
            SerializedProperty tool = serialized.FindProperty("toolPower");
            tool.FindPropertyRelative("pick").floatValue = power.pick;
            tool.FindPropertyRelative("axe").floatValue = power.axe;
            tool.FindPropertyRelative("any").floatValue = power.any;
            serialized.FindProperty("ability").objectReferenceValue = ability;
            serialized.FindProperty("muzzle").vector3Value = muzzle;
            WriteLight(serialized.FindProperty("heldLight"), heldLight);
            serialized.FindProperty("bagSlots").intValue = bagSlots;
            if (bagSlots > 0)
                serialized.FindProperty("category").enumValueIndex = (int)ItemCategory.Gear;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        static RecipeDefinition MakeRecipe(string name, ItemAmount output, bool needsWorkbench, params ItemAmount[] inputs)
        {
            var recipe = LoadOrCreate<RecipeDefinition>($"{RecipeDir}/{name}.asset");
            var serialized = new SerializedObject(recipe);
            WriteAmount(serialized.FindProperty("output"), output);
            serialized.FindProperty("needsWorkbench").boolValue = needsWorkbench;
            SerializedProperty list = serialized.FindProperty("inputs");
            list.arraySize = inputs.Length;
            for (int i = 0; i < inputs.Length; i++)
                WriteAmount(list.GetArrayElementAtIndex(i), inputs[i]);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return recipe;
        }

        static void WriteLight(SerializedProperty property, DecorationLight light)
        {
            property.FindPropertyRelative("enabled").boolValue = light.enabled;
            property.FindPropertyRelative("color").colorValue = light.color;
            property.FindPropertyRelative("intensity").floatValue = light.intensity;
            property.FindPropertyRelative("range").floatValue = Mathf.Max(light.range, 0.1f);
            property.FindPropertyRelative("offset").vector3Value = light.offset;
            property.FindPropertyRelative("flicker").floatValue = light.flicker;
        }

        static void WriteAmount(SerializedProperty property, ItemAmount amount)
        {
            property.FindPropertyRelative("block").objectReferenceValue = amount.block;
            property.FindPropertyRelative("item").objectReferenceValue = amount.item;
            property.FindPropertyRelative("count").intValue = amount.count;
        }

        /// <summary>Adds each of <paramref name="entries"/> to a list field unless it's already there.</summary>
        static void AddToList(Object owner, string field, params Object[] entries)
        {
            var serialized = new SerializedObject(owner);
            SerializedProperty list = serialized.FindProperty(field);
            foreach (Object entry in entries)
            {
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    present |= list.GetArrayElementAtIndex(i).objectReferenceValue == entry;
                if (present)
                    continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = entry;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
        }

        /// <summary>Replaces a list field with exactly <paramref name="entries"/>, in order.</summary>
        static void SetList(Object owner, string field, params Object[] entries)
        {
            var serialized = new SerializedObject(owner);
            SerializedProperty list = serialized.FindProperty(field);
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
