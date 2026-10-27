# Voxelforge - Dev Log

> Our adventure journal. After each session, add an entry at the top: what we did, what we learned, and what's next.
> Checklist status lives in `ROADMAP.md`; the *why* behind big choices lives in `DECISIONS.md`.

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
- [ ] Roadmap **1.2 Chunk Data Architecture**: plan the `VoxelChunk` data (chunk size, block IDs, durability, light) before coding

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
