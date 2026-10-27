# Voxelforge - Dev Log

> Our adventure journal. After each session, add an entry at the top: what we did, what we learned, and what's next.
> Checklist status lives in `ROADMAP.md`; the *why* behind big choices lives in `DECISIONS.md`.

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
