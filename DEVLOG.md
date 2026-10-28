# Voxelforge - Dev Log

> Our adventure journal. After each session, add an entry at the top: what we did, what we learned, and what's next.
> Checklist status lives in `ROADMAP.md`; the *why* behind big choices lives in `DECISIONS.md`.

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
