# Voxelforge - Dev Log

> Our adventure journal. After each session, add an entry at the top: what we did, what we learned, and what's next.
> Checklist status lives in `ROADMAP.md`; the *why* behind big choices lives in `DECISIONS.md`.

---

## 2026-10-28 - Session 1 (part 13): Real Plants! 🌼 Roadmap 2.6, part B
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **The stand-in boxes are gone.** A new Blender script, `Tools/Blender/build_foliage.py`, paints all our plant art by code and builds the models. It's our own art, with no photos, and running the script again rebuilds everything. Details in DECISIONS.md **D-012**.
- **10 plants**, each with a cheaper version for far away:
  - Short grass, and tall grass with seed heads
  - Yellow buttercups, blue cornflowers and red poppies
  - A leafy bush, and a twiggy dry shrub for the desert
  - **Oak trees** with chunky trunks and lumpy, leafy crowns (meadows)
  - **Pine trees** with droopy layered skirts (snowy peaks)
  - **Scrub trees:** new! Wind-bent and flat-topped, for the desert and red crags
- **All the plants share one painted texture**, laid out like a sticker sheet.
- **Tree crowns are shaded like one soft ball**, so a tree looks round and leafy instead of like a pile of flat cards.
- ***Voxelforge → Build Foliage Decorations*** makes the Unity materials and plant settings from the Blender files. Unity imports the models and texture with the right settings automatically.
- Grass and flowers were made bigger after the first look, so they don't get lost next to 1-block steps.

### Results 🎉
- About 27,000 plants around spawn, with all 10 kinds in the right biomes.
- Trees cost 470-800 triangles up close and about 200 far away. A grass tuft is just 12.
- **Play-test passed:** plants, grass and trees look great! ✅

### Tests: 157 / 157 passing ✅

### Things we learned
- **Bug: bright white orbs in the sky!** They weren't the sun. On the back of the desert shrub's leaf cards, the shader blended "facing away" and "facing toward you" exactly 50/50, which made a zero-length direction. Turning that into a proper direction gave an invalid number (NaN), and the **bloom** glow effect spread every bad pixel into a big white ball. Fixed: the back of a card now only flips its sideways direction, so it can never become zero.
- Unity treats a model part named `_LOD1` as a level of detail and wants a matching `_LOD0`, so our models are named `Name_LOD0` (close) and `Name_LOD1` (far).
- Recompiling scripts while the game is running breaks the play session. Claude now checks for Play Mode first.

### Next up
- [x] You two: play-test part B. Looks great, orbs gone! ✅
- [ ] Roadmap **2.6 part C**: biome colours, soft shadows in block corners (corner AO), and the split-screen 120 FPS check

---

## 2026-10-28 - Session 1 (part 12): Trees and Grass 🌳 Roadmap 2.6, part A
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **The world grows plants!** Grass tufts, flowers, bushes and trees now cover the terrain. The world seed picks where they go, so the same seed always grows the same forest. Details in DECISIONS.md **D-012**.
- **Trees you can bump into.** Each trunk is filled with a new invisible but solid **Tree Trunk** block, so heroes bump into trunks but walk straight through leaves. Later (3.1) you'll chop them down.
- **Trees never touch:** at most one tree grows in each 6×6 patch of ground. Oaks grow in meadows and red crags, pines on snowy mountains, and only a few in the desert.
- **Plants follow the blocks.** They're worked out at the same time as each chunk's mesh, on the worker threads. Dig out the block under a flower and the flower goes too, with no save data or network messages needed.
- **Cheap to draw:** there's no Unity object per plant. Before each camera draws, the graphics card itself picks the plants that camera can see, so each split-screen view gets its own list.
- **A wind shader** sways the tips of grass and leaves, and plants fade in softly as you walk toward them.
- **No more spawning on a tree:** if the spawn point lands on a trunk, the hero moves one block over.
- The debug corner (F3) shows how many decorations are loaded.
- The plants are **simple stand-in shapes for now** (green cards and boxes). Part B builds the real art in Blender.

### Results 🎉
- About **25,000 decorations** around spawn, including **766 trees**.
- Overview camera at **~181 FPS** in the editor.
- **Play-test passed:** trunks block the hero, and the wind sways the plants. ✅

### Tests: 157 / 157 passing ✅
- Trees never sit next to each other, and their heights stay in range.
- Plants only grow on open grass tops, and the same seed always gives the same plants.
- The slot bookkeeping for the graphics card's plant lists hands out, frees and reuses space correctly.
- Spawning next to a tree trunk moves you one column over.
- Removing the block under a plant removes exactly that plant, and unloading a chunk removes its plants.

### Things we learned
- Adding trees changed the world on purpose, so we bumped the **terrain version to 2** and updated the recorded terrain fingerprint that the tests check against.
- A screen-door fade (dropping some pixels in a pattern) can poke tiny holes in fully faded-in plants. The fix: only drop pixels while a plant is actually fading.

### Next up
- [x] You two: play-test part A. Trunks block, wind works! ✅
- [ ] Roadmap **2.6 part B**: real grass, flower, bush and tree models built by a Blender script
- [ ] Roadmap **2.6 part C**: biome colours, soft shadows in block corners (corner AO), and the split-screen 120 FPS check

---

## 2026-10-28 - Session 1 (part 11): Meet the Wayfarer 🧙 Roadmap 2.5
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **Real heroes!** The capsules are gone. Meet **the Wayfarer**: our own hooded, bearded adventurer with shoulder armour, a cape and big chunky boots. Details in DECISIONS.md **D-011**.
- **Built in Blender by a script**, then painted with soft hand-painted shading baked into one texture, just like our blocks.
- **Team colours:** the tunic, hood and cape take your player colour (P1 blue, P2 orange, P3 green, P4 purple).
- **Rigged and animated with Mixamo:** idle, walk, run, jump, fall and land.
- **Walk up stairs without jumping!** Any 1-block step is climbed automatically, and walking down a step keeps your feet on the ground. 2-block walls still need a jump.
- **Heroes face where they walk** and turn smoothly.
- **Feet stay on the blocks:** foot IK plants each foot on the block under it, and pulls a foot back onto the edge instead of standing on air.
- **The camera no longer goes inside hills.** It slides in front of them.

### Results 🎉
- The hero ran up a natural staircase of 1-block steps with no jumps (y 77 → 87). He faced his running direction, the Animator moved Idle → Run → Jump → Land → Idle, and he landed with both boots flat on the grass.
- About **212 FPS** standing in the editor with one player.

### Tests: 140 / 140 passing ✅
- Climbs a 1-block step without jumping, but not a 2-block wall, not without head room, and not while in the air.
- Stays grounded walking down a 1-block ledge; falls off a 2-block ledge.
- Jump still works next to a step.
- Ground finder and block raycast hit the right block and face, including negative coordinates.
- Turning takes the short way round and respects the turn speed.

### Things we learned
- **Mixamo needs one closed body.** Our hero is ~60 overlapping pieces (head inside hood, belt through tunic), so the auto-rigger failed. Fix: melt a copy into one shell, rig *that*, then copy the bones and weights back onto the real hero in Blender.
- Mixamo's clips are skeleton-only motion, so they work on our hero even though Mixamo never saw it.
- After restarting Unity, Claude Code needs **`/mcp` → Reconnect** to talk to it again.
- **Play-test bug: "he jumps a block to the left and back while walking."** The Walk and Run clips came without "In Place", so the body ran up to 0.9 m ahead of the player (Mixamo's run also curves left) and snapped back each loop. Fixed in the import settings: the travel is now thrown away, and the hips stay within 0.08 m of the player.

### Next up
- [x] You two: play-test! Run up and down hills and stairs, jump, and check it all looks right. Found the sideways lurch, fixed it, and it looks great! ✅ **2.5 done.**
- [ ] Roadmap **2.6 Foliage & Decoration Layer**: grass tufts, flowers, bushes and trees

---

## 2026-10-27 - Session 1 (part 10): A World With No Edge 🌍 Roadmap 2.4
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **The world grows around you!** It used to be a fixed 512 m box that you could walk off. Now terrain loads in a 256 m circle around **each** player, closest ground first, and unloads again when nobody is near. Details in DECISIONS.md **D-010**.
- **Walk apart as far as you like.** Each of you gets your own circle of world, so you can explore in opposite directions.
- **No flickering on borders:** terrain only unloads once you're 2 chunks past the edge of your circle, so stepping back and forth doesn't reload it.
- **Fog** now softly hides the far edge of the world, so you don't see chunks popping in.
- Chunk objects are **recycled** instead of thrown away and rebuilt.
- **Cheaper second camera:** Unity's **GPU Resident Drawer** now draws the chunks, and the graphics card itself skips chunks hidden behind hills. The block shader was updated to support it.
- The debug corner (F3) shows chunks loaded and drawn, columns, and how many viewers the world is loading around.

### Results 🎉
- Before anyone joins: 113 columns (904 chunks) around the spawn point, 0 errors.
- Teleported the loading point 1 km away: the old area unloaded, the new one loaded, and the recycled chunk objects were reused.
- Terrain, textures and the new fog look right with the GPU Resident Drawer on.

### Tests: 121 / 121 passing ✅
- Terrain loads around *every* viewer, in full columns, nearest first.
- The unload margin keeps nearby terrain loaded, and it all unloads when everyone leaves.
- Unloading is spread over several frames.
- A mesh being built for a chunk that unloads is thrown away, and recycled chunk objects get reused.

### Things we learned
- Unity shows things in **cyan** while it's still compiling new shader versions. It isn't a bug; wait a few seconds!
- The host loads the world around *every* player, because it runs everyone's physics. That's what will let online friends explore too.

### Next up
- [x] You two: play-test split-screen with the Game view focused. Walk far apart and check the FPS (target **120+**), that the ground is always there under you, and that the fog hides the edge. Everything looked good! ✅ **2.4 done.**
- [ ] Roadmap **2.5 Smooth Character Integration**: real heroes with animations, auto-stepping and feet that plant on blocky stairs

---

## 2026-10-27 - Session 1 (part 9): Your Own Camera, and a Screen That Splits 🎥 Roadmap 2.3
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **Every hero has their own camera now!** Details in DECISIONS.md **D-009**.
- **Stick together** (closer than 15 m): you share one camera that floats over both of you and zooms out as you spread apart.
- **Walk apart** (more than 15 m): the screen **splits along a slanted line**, and the line always points between you. If your friend is up and to the right, their half is up and to the right. Walk back together and the halves melt into one picture again.
- **Turn the camera** with the **mouse** (keyboard player) or the **right stick** (gamepad). When you're together, either of you can turn the shared camera. When you're apart, you each turn your own.
- **W / stick-up now means "away from my camera"**, not "north". The host still decides where you actually go.
- **3 or 4 players** get a quarter of the screen each. With 3 players, the spare quarter shows the whole world from above.
- Before anyone joins, the **Overview Camera** shows the whole world floating in space.
- The mouse is locked to the game while you play. Press **Esc** to get it back, and click the game to lock it again.
- The debug corner (F3) shows the camera mode and how far apart you are.

### Results 🎉
- Split exactly at 15 m: the two halves lined up perfectly, with no jump, and the divider line faded in.
- Far apart: a slanted line with P1 on one side and P2 on the other.
- 3 players: three quarters plus the overview.
- **FPS (editor):** about 143 merged and about 100–111 split. Drawing the world twice costs about 2 ms per camera, so we made the split cheaper (the second camera's picture is painted on top of the first instead of both being combined by a third camera). The real FPS test is the 2.7 build.

### Tests: 109 / 109 passing ✅
- The split math: merged under 15 m, split over it, and both cameras look at the midpoint at exactly 15 m (that's what makes it seamless).
- Angles turn the short way round (350° → 10° is +20°, not −340°).
- The split line points from P1 to P2 on screen, even when the camera is turned.
- Grid quarters cover the whole screen.
- Camera-relative walking: facing east, W walks east and D walks south.

### Things we learned
- A "Voronoi" split means each player owns the part of the screen that's closer to them. With two players that's just a straight line halfway between them.
- Making the split invisible at the moment it happens is all about both cameras seeing *the same thing* at that instant.
- Two cameras cost twice the *work*, not twice the *pixels*. Making the pictures smaller didn't help at all, which told us where the cost really is.

### Play-test round 1: two problems found 🔍
- **Your hero could disappear** after turning sharply while split. Each camera aimed at a spot in the *world* between you, so turning around could swing your hero over to your friend's half. Now, the more you're allowed to turn your own view, the more your camera aims to keep your hero **in the middle of your own side of the screen**. Tested by turning P1's camera right round: both heroes stayed on their own sides. 3 new tests (112 / 112 passing).
- **Split-screen ran at 72–81 FPS.** We measured it properly by clicking into Unity while it ran an automatic test that switched effects off one at a time:
  - The split costs **exactly twice** the single view: 183 FPS merged vs 102 split. Each camera draws all ~900 chunks and their shadows again.
  - **Ambient occlusion** (the soft shading in corners) was drawing every chunk an *extra* time per camera. We switched it to a cheaper mode that looks the same: **102 → 109 FPS**.
  - We also turned off a full-screen copy that nothing was using.
  - The big fix (letting the GPU keep the chunk list itself and skip chunks hidden behind hills) is now part of **2.4**.

### Things we learned (round 1)
- Unity only runs at full speed while its window is focused, so measuring FPS needs a human clicking in!
- To find what's slow, switch one thing off at a time and measure. Our guess (pixels) was wrong: a bigger window didn't change the time at all.

### Next up
- [x] You two: play-test the hero-on-screen fix, then we tick 2.3 off. Walked apart and turned sharply: the camera corrects and your hero stays in view! ✅ **2.3 done.**
- [ ] Roadmap **2.4 Multi-Viewer Chunk Loader**: load terrain around *both* players so you can really explore apart, and make extra cameras cheap (GPU Resident Drawer) so split-screen reaches 120+ FPS

---

## 2026-10-27 - Session 1 (part 8): Grab a Controller and Press Start 🕹️ Roadmap 2.2
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **Press to join!** The world starts empty. Press **Start** (or **A**) on a gamepad, or **Enter** (or **Space**) on the keyboard, and a hero drops in for you. Details in DECISIONS.md **D-008**.
- **Everyone has their own controls.** Each player gets their own copy of the controls, tied to their own device. Your gamepad only moves *your* hero.
  - Keyboard: **WASD** or **arrow keys** to walk, **Space** to jump (keyboard and mouse join together as one player).
  - Gamepad: **left stick** to walk, **A** to jump.
- **Unplugged controller?** That hero stands still and the debug corner turns orange: "disconnected". Plug it back in to carry on, or press Start on a different controller to take over the same hero.
- **Up to 4 players per PC.** The host checks every join request, just like it will for online friends later.
- **Split-keyboard dev mode** (off by default): tick *Allow Split Keyboard* on the `Session` object's **Local Player Manager**. Then Space joins a WASD player and Enter joins an arrow-keys player.
- The debug corner (F3) now shows each slot's device, plus "press Start / Enter to join" for the next free slot.

### Results 🎉
- Virtual gamepad pressed Start → **P1** spawned. Keyboard pressed Enter → **P2** spawned.
- The stick moved only P1 (10 blocks east), and W moved only P2.
- Pulled the virtual gamepad: P1 went "disconnected". Plugged it back: P1 was back in control.
- About **137 FPS** in the editor.

### Tests: 92 / 92 passing ✅
- Slot rules: first device gets slot 0, a lost player's slot is reused first, and nobody joins a full roster.
- With virtual devices: Start joins, keyboard brings the mouse along, the same controller can't join twice, each player only hears their own gamepad, and unplugging/replugging works. A new controller takes over a lost slot without spawning a second hero, and split keyboard keeps WASD and arrows apart.

### Things we learned
- The Input System's **InputUser** is the "local player" system: pair a device to a user, give the user its own copy of the controls, and that player only hears their device.
- **Don't press Play while Claude is changing scripts!** Unity reloads the code mid-game and the world falls apart (lots of red errors). Wait until Claude says it's ready.

### Next up
- [x] You two: play-test 2.2 (P1 on keyboard + mouse, P2 on your gamepad), then we tick it off in the roadmap. Worked with keyboard and an Xbox controller!
- [ ] Roadmap **2.3 Voronoi Dynamic Split-Screen**: a camera for each player, splitting the screen when you wander apart

---

## 2026-10-27 - Session 1 (part 7): Two Heroes Join the World 🎮 Roadmap 2.1
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **The game is now a Host.** Pressing Play starts a tiny server *and* a player connection in the same program, just like an online game, except everything stays on this PC. Details in DECISIONS.md **D-007**.
- **Two players spawn**: P1 (blue capsule) and P2 (orange capsule). They wait for the ground to generate, then land on the surface near the middle of the world.
- **The host is the boss.** Your keys don't move your hero directly: they send a message to the host, the host moves the hero, and then tells everyone where it is. That's what lets online play slot in later.
- **Heroes bump into blocks** with our own box-vs-block collision: walls stop you, you land on the ground, ceilings bonk your head, and you can't fall out of the world.
- **Temporary controls** (2.2 replaces them):
  - P1: **WASD** to walk, **Space** to jump
  - P2: **Arrow keys** to walk, **Right Ctrl** to jump
- **A debug corner** shows Host status, FPS and each player's position and state. Press **F3** to hide it.
- The camera follows the middle of both players and pulls back when they split up (2.3 will give each player their own view).

### Results 🎉
- Host starts, P1 and P2 spawn and land on the grass at about y = 75.
- P1 walked about 10 blocks east down a slope; P2 jumped up a hill about 10 blocks high.
- About **144 FPS** in the editor while both players move.

### Tests: 80 / 80 passing ✅
- Bodies fall and land, walls block, ceilings stop jumps, and jumps reach the right height.
- A super-fast fall still can't tunnel through a 1-block floor.
- An ungenerated chunk acts like solid ground, and a player in an unloaded chunk waits instead of falling.
- The spawn finder picks the surface (not a cave) and skips spots without enough head room.
- `PlayerId` equality and names (P1, P2).

### Things we learned
- In this netcode, one program can only have **one** network manager, so both couch players belong to the same connection and get told apart by their **slot** number.
- Without auto-step, a single block is a wall: you have to **jump** up every 1-block step. Auto-step is coming in 2.5!
- Unity once "forgot" a new script file (it wasn't being compiled at all). Deleting and re-creating the file fixed it.

### Next up
- [x] You two: play-test 2.1 (both players on one keyboard), then we tick it off in the roadmap
- [ ] Roadmap **2.2 Dual Input Binding**: gamepads, and a proper device for each player

---

## 2026-10-27 - Session 1 (part 6): Painted Blocks & Torchlight 🎨 Roadmap 1.5
**Phase:** 1 - The Voxel Foundation

### What we did
- **Every block has painted textures now**: hand-painted WoW style, 128 × 128 per face, with color, bumps (normal + height maps), shine and glow. Details in DECISIONS.md **D-006**.
- **Made in Blender with procedural nodes:** 9 node materials, and a bake script that turns each into 4 texture maps in about 40 seconds. Every texture tiles perfectly.
- **Grass is special:** grassy top, a dirt side with a wavy, dripping grass overhang, and dirt on the bottom.
- **Mana Crystal Ore glows!** Purple-blue crystal clusters, with Bloom making them shine.
- **Real lighting:** sun and soft shadows, ambient occlusion, and **point lights with shadows**. We put a test torch light in a cave to prove it, ready for real torches in Roadmap 3.5.
- **Parallax depth:** up close, cracks and cobbles look carved in, not painted on.
- **One menu click** (*Voxelforge → Rebuild Block Textures*) packs the Blender textures into the game.

### How to change a texture yourselves 🖌️
1. Open `Tools/Blender/BlockMaterials.blend` in Blender and switch to the **BlockBake** scene.
2. Pick a material on the plane (e.g. `VF_RedRock`) and change colors or numbers in the Shader Editor.
3. Open the **Text Editor**, choose `bake_block_textures.py`, and press **Run Script**.
4. In Unity: **Voxelforge → Rebuild Block Textures**. Press Play!

### Results 🎉
- About **120 FPS** in the editor on the full test world.
- 9 texture layers × 3 arrays = only about 0.6 MB of GPU texture memory.

### Tests: 67 / 67 passing ✅
- Every solid block has textures for its top, sides and bottom; Grass has a different side; Grass's bottom is Dirt.
- The texture arrays are up to date (right number of layers, 128 px, 8 mip levels).
- The mesher gives top, side and bottom faces the right texture, and faces without one fall back to their flat color.

### Things we learned
- Our first sand looked like **wooden planks**: ripples that line up block after block look like wood grain. Fading ripples in and out fixed it.
- Grass made of round bumps looked like **bubble wrap**. Swirly "brush stroke" noise looks much more painted.
- **Too much parallax makes stone look puffy.** A little depth goes a long way.
- Don't save a `.blend` inside `Assets/`: Unity tries to import it as a 3D model.
- Running Unity's tests reloads the scene, so **save the scene before running tests**, or unsaved changes vanish.

### Next up
- [ ] Phase 2! Roadmap **2.1 Local Listen-Server Setup**: start the game in Host mode for couch co-op

---

## 2026-10-27 - Session 1 (part 5): A Real World Grows From a Seed ✅ Roadmap 1.4
**Phase:** 1 - The Voxel Foundation

### What we did
- **Real terrain** replaced the test pattern. Details in DECISIONS.md **D-005**.
- **4 biomes, each like a classic WoW zone:**
  - 🌾 **Meadows** (Elwynn Forest): green rolling hills
  - 🏜️ **Desert** (Tanaris): sand dunes over sandstone
  - 🪨 **Red Crags** (Redridge): stepped red-rock cliffs with grassy tops
  - 🏔️ **Snowy Peaks** (Dun Morogh): tall snowy mountains

  Biomes blend into each other smoothly, and anything above y = 165 gets a snow cap.
- **Caves!** Winding tunnels (some open up to the surface as cave entrances) and big caverns deep down. The bottom layer is never dug out.
- **Mana Crystal Ore** now grows in glowing clusters, with more under the mountains.
- **4 new blocks:** Sand (5), Snow (6), Sandstone (7) and Red Rock (8).
- **The `WorldSeed`:** one number grows the whole world. Same seed = the exact same world, every time. Turn on "Randomize Seed On Play" to explore new worlds; the seed it picked shows in the Inspector, so you can keep one you like.
- **Everything is tweakable** in `Data/Terrain/TerrainSettings.asset`: biome heights, hill sizes, blocks, cave sizes, ore amounts.
- **No freezing:** chunks are built on worker threads and appear as they finish, middle first.

### Results 🎉
- A 512 × 256 × 512-block world (2,048 chunks) **generates in about 1 second**, and meshing finishes at the same time.
- **About 122 FPS** with about 628,000 block faces in view.
- 0 meshes thrown away, because chunks wait for their neighbours before meshing.

### Tests: 64 / 64 passing ✅
- Same seed → identical chunk; different seed → different chunk.
- Chunks come out the same whatever order they're built in.
- A "fingerprint" of one chunk catches anyone changing the terrain by accident.
- Biome weights always add up to 1, and all 4 biomes show up.
- Sky chunks are pure air, the floor never has holes, and there are caves and ore underground.

### Things we learned
- Our first mountains were **needle-thin spikes**! Tall height + narrow hills = needles. Making the peaks wider and a bit lower turned them into real mountains, and fewer faces raised FPS from 91 to 122.
- Small biome "blobs" didn't feel like zones, so we made biomes about 60% bigger.
- Turning on **Run In Background** means Unity keeps playing even when you're not clicking on it, so no more "click the Game view" while Claude tests.

### Next up
- [ ] Roadmap **1.5 Art & Shaders (Creative)**: real block textures and materials replace the placeholder colors

---

## 2026-10-27 - Session 1 (part 4): We Can SEE the World! ✅ Roadmap 1.3
**Phase:** 1 - The Voxel Foundation

### What we did
- **Greedy meshing.** Neighbouring faces of the same block join into one big rectangle, so a flat 32×32 floor is 1 quad instead of 1,024. Full details are in DECISIONS.md **D-004**.
- **Hidden-face culling.** Faces touching another solid block are never drawn, even across chunk borders. Each row of 32 blocks is packed into one number, so a single bit trick checks the whole row at once.
- **Off the main thread.** Burst jobs on worker threads do all the meshing. The game never waits for them: finished meshes are picked up on the next frame.
- **`VoxelWorld` + `VoxelWorldManager`** hold all the chunks and keep track of which ones need redrawing. They're not singletons, so we can pass them around explicitly for split-screen later.
- **A placeholder shader** colors each block type, adds sun, shadows and ambient light, and draws faint lines so you can still see single blocks on big merged faces.
- **A test pattern:** 16×4×16 chunks (512×128×512 blocks) of hills, tunnels and Mana Crystal Ore specks in the Sandbox scene. Real terrain replaces it in 1.4.

### Results 🎉
- **140–158 FPS** in the editor with the whole test world on screen.
- 1,024 chunks, of which 740 have something to draw (the rest are empty sky or buried). That's about 533,000 quads.
- One busy chunk meshes in about **3 ms** on a worker thread. The test pattern generates in **138 ms**.

### Tests: 53 / 53 passing ✅
- Face counts:
  - 1 block → 6 faces
  - a 2-block bar → still 6 faces (merged)
  - 2 different blocks → 10 faces (different blocks don't merge)
  - full chunk → 6 giant faces
  - buried chunk → 0 faces
- Faces between chunks are hidden. Loading a neighbour or editing an edge block redraws both chunks.
- Every triangle faces outward, and UVs count blocks, so textures will tile properly in 1.5.
- A checkerboard (worst case: 393k vertices) works.
- If a chunk changes while its mesh is being built, the old mesh is thrown away and rebuilt.

### Things we learned
- Burst can quietly skip a job that's a **private struct nested inside a class**, and run it as slow normal C# instead. Moving it to its own struct made world generation 5× faster (664 → 138 ms).
- Unity only runs Play Mode while its window has focus, so click into the Game view when Claude is testing.

### Next up
- [ ] Roadmap **1.4 Procedural Noise Terrain**: real hills, caves and biomes from a `WorldSeed`
- [ ] Roadmap **1.5 Art & Shaders (Creative)**: real block materials replace the placeholder colors

---

## 2026-10-27 - Session 1 (part 3): Blocks Have a Home ✅ Roadmap 1.2
**Phase:** 1 - The Voxel Foundation

### What we did
- Designed how the world remembers every block (full details in DECISIONS.md **D-003**).
- **`Voxel`**: one block in only 4 bytes, holding its type, light level and which way it faces.
- **`VoxelChunk`**: a 32×32×32 cube of blocks. Empty sky chunks barely use any memory.
- **`VoxelMath`**: converts world positions ↔ chunk positions, and works for negative coordinates too.
- **Our first 5 blocks** as Unity assets: Air, Grass, Dirt, Stone and **Mana Crystal Ore** (it glows!). They're in `Assets/_Voxelforge/Data/Blocks/`.
- **`BlockRegistry`**: the master list of blocks. It warns you if two blocks share an ID.
- Renamed the voxel code assembly to `Voxelforge.Voxels`, because a struct called `Voxel` inside a namespace called `Voxel` confuses C#.

### Tests: 36 / 36 passing ✅
- Coordinate math, including negative positions (block -1 is in chunk -1).
- A voxel is exactly 4 bytes, and light/facing pack correctly.
- Chunks: stay small until edited, count versions, free memory when disposed.
- The real block registry asset is valid.
- A **Burst job on a worker thread** read a chunk and counted 1,024 stone blocks, so the data is ready for fast meshing.

### Things we learned
- Tests catch our own mistakes too: one test expected the wrong answer (block 31 is the *last* block of chunk 0, not the first of chunk 1).
- Player scripts *can't* edit chunks directly. The compiler blocks it, which keeps multiplayer safe later.

### Next up
- [ ] Roadmap **1.3 Greedy Meshing & Culling**: turn chunk data into actual visible blocks in the Sandbox!

---

## 2026-10-27 - Session 1 (part 2): Unity Is Alive! ✅ Roadmap 1.1
**Phase:** 1 - The Voxel Foundation

### What we did
- Installed **Unity Hub**, **Unity 6.6** and **Blender 5.2**.
- Created the project from the **Universal 3D (URP)** template and moved it into the repo.
- Set up Git properly: `.gitignore` for Unity, plus **Git LFS** for textures, models and sounds.
- Added the voxel-power packages: **Burst, Collections, Mathematics**, plus Test Framework.
- Built our folder home, `Assets/_Voxelforge/`, with 4 code assemblies (Core, Voxel, Editor, Tests).
- Renamed the starter scene to **Sandbox**. That's where the blocky world will appear first.
- **Connected Claude to Unity and Blender** with MCP. Claude can now create things in our Unity scene and read our Blender scene directly!

### Smoke tests passed
- Unity console: **0 errors, 0 warnings**
- Claude created a cube in the Sandbox scene, then deleted it ✔
- Play Mode: no errors ✔
- Claude read the Blender scene (Cube, Light, Camera) ✔

### Things we learned
- Unity Hub won't create a project inside a folder that already has files, so we made it next door and moved it in.
- Close Unity **before** moving project files around, because it locks its files while open.
- The Unity MCP server has to be started each time Unity opens: *Window → MCP for Unity → Toggle MCP Window → Start Server*.
- Blender's MCP panel says **"Connected on port 9876"** when it's working. There isn't a "Connect to Claude" button anymore.

### Next up
- [x] Roadmap **1.2 Chunk Data Architecture**: plan the `VoxelChunk` data (chunk size, block IDs, durability, light) before coding

---

## 2026-10-27 - Session 1: The Quest Begins
**Phase:** 1 - The Voxel Foundation

### What we did
- Wrote the vision: Minecraft-style mining and building + classic World of Warcraft classes, combat, and gear.
- Set up the project rules (`CLAUDE.md`) and the 6-phase roadmap (`ROADMAP.md`).
- Started the progress docs: this dev log + `DECISIONS.md`.
- Compared engines (Unity 6, Unreal 5, Godot 4). Recommended **Unity 6 + URP**. See `DECISIONS.md` D-001.

### Decisions made
- Local split-screen co-op first, built on a Host/Server setup so online play can slot in later (Phase 6).
- **Engine: Unity 6 + URP** (D-001 accepted).

### Next up
- [x] Roadmap 1.1: Install Unity 6 LTS, create the project, add a Unity `.gitignore`, connect Unity MCP + Blender MCP
