# Voxelforge - Development Roadmap & Milestone Tracker

> **Instructions for Claude Code:** 
> 1. Always check this file to identify the active phase and current task before writing code.
> 2. Follow all architectural guardrails in `CLAUDE.md` (Host-Authority, No Singletons, Message-Driven Voxels).
> 3. When a milestone task is implemented and verified by the user, update `[ ]` to `[x]` in this file.

---

## Phase 1: The Voxel Foundation ("Hello, World of Blocks")
**Goal:** Generate a high-performance, 3D blocky terrain with modern lighting and PBR textures.
- [x] **1.1 Engine & MCP Setup:** Initialize repository, configure `.gitignore`, and verify Engine MCP + Blender MCP connections.
- [ ] **1.2 Chunk Data Architecture:** Create `VoxelChunk` data structures (`16x16x16` or `32x32x32`) storing Block ID, Durability, and Light/State bytes.
- [ ] **1.3 Greedy Meshing & Culling:** Implement background-threaded Greedy Meshing and hidden-face culling so chunks render at 120+ FPS.
- [ ] **1.4 Procedural Noise Terrain:** Generate rolling hills, caves, and biomes from a deterministic `WorldSeed` integer.
- [ ] **1.5 Art & Shaders (Creative):** Create PBR Triplanar/Block materials (Grass, Dirt, Stone, Mana Crystal Ore) with Normal/Height maps and dynamic lighting.

## Phase 2: Dynamic Couch Co-Op & Smooth Heroes
**Goal:** Get two smooth RPG characters running around the blocky world together on one PC.
- [ ] **2.1 Local Listen-Server Setup:** Configure game to launch in local `Host Mode` (Server + 2 Local Clients).
- [ ] **2.2 Dual Input Binding:** Map Player 1 (Keyboard/Mouse or Gamepad 1) and Player 2 (Gamepad 2) via native local player subsystems.
- [ ] **2.3 Voronoi Dynamic Split-Screen:** Build midpoint shared camera when players are `< 15m` apart; smoothly split via angled screen-space render targets when `> 15m` apart.
- [ ] **2.4 Multi-Viewer Chunk Loader:** Update Chunk Manager to stream/unload terrain around a `List<ViewerTransform>` so both players can explore independently.
- [ ] **2.5 Smooth Character Integration (Creative):** Import stylized 3D characters with Mixamo animations, 1m auto-stepping, and IK foot placement on blocky stairs.

## Phase 3: "Forge & Carve" (Digging, Building & Saving)
**Goal:** Let players mine tunnels, build castles, and save their world in a network-ready format.
- [ ] **3.1 Message-Driven Voxel Edits:** Implement `RequestModifyVoxel(Coord, BlockID, InstigatorID)`. Never mutate chunk arrays directly from client input.
- [ ] **3.2 Block Durability & Mining:** Add raycast/spherecast targeting, block cracking decals, and block item drops.
- [ ] **3.3 Grid Snap Placement:** Add ghost-block preview and placement validation (prevent placing blocks inside characters).
- [ ] **3.4 Seed + Delta Save System:** Save world using only `WorldSeed` + a compressed dictionary of modified chunk deltas and `PlayerUUID` profiles.
- [ ] **3.5 Props & Tools (Creative):** Model 3D pickaxes, torches (with real point lights), and interactive workbenches.

## Phase 4: The Warcraft Injection (Classes, Spells & RPG UI)
**Goal:** Layer *World of Warcraft* combat, mana, cooldowns, and talent trees onto the voxel world.
- [ ] **4.1 Data-Driven Ability Framework:** Build ScriptableObject/DataAsset spell templates (Mana Cost, Cooldown, Cast Time, Element, Range, AoE Radius).
- [ ] **4.2 Spell Logic vs. Visual Cue Split:** Separate authoritative damage/terrain destruction math from client-side particle VFX and audio.
- [ ] **4.3 Destructive Spell Mechanics:** Allow explosive spells (e.g., *Pyroblast*, *EarthShatter*) to batch-modify voxel chunks and carve real craters.
- [ ] **4.4 Per-Player Split-Screen HUD:** Build independent Action Bars (1-9 hotkeys/d-pad), cast bars, floating combat text, and inventory windows confined to each player's viewport.
- [ ] **4.5 Class & Spell Design (Creative):** Design first 2 playable classes (e.g., *Runeforged Paladin*, *Geomancer Mage*) with 3 signature abilities each.

## Phase 5: Monsters, Dungeons & Bosses
**Goal:** Populate the world with intelligent RPG creatures that can navigate dynamic blocky terrain.
- [ ] **5.1 Dynamic Voxel NavMesh:** Configure runtime NavMesh rebuilding so monsters recalculate paths when players dig trenches or build walls.
- [ ] **5.2 RPG Threat & Aggro System:** Implement a *WoW*-style threat table so tank characters can hold boss aggro while ranged characters cast spells.
- [ ] **5.3 Procedural Dungeon Stamps:** Stamp pre-built boss arenas and ruins into the procedural voxel world during chunk generation.
- [ ] **5.4 Loot & Equipment System:** Implement armor slots, weapon attachment sockets, and rarity tiers (Common, Rare, Epic, Legendary).
- [ ] **5.5 Monster & Boss Creation (Creative):** Model and animate 3 overland mobs and 1 multi-phase Dungeon Boss with telegraph attacks.

## Phase 6: Opening the Portal (Internet Multiplayer)
**Goal:** Enable online co-op with friends while preserving local Dynamic Split-Screen.
- [ ] **6.1 Relay Transport Swap:** Integrate Steamworks P2P or Epic Online Services (EOS) transport layer (no port-forwarding required).
- [ ] **6.2 Initial World Sync:** Send `WorldSeed` + compressed `VoxelDeltaLog` to joining remote players upon connection.
- [ ] **6.3 Bandwidth Optimization:** Compress chunk delta broadcasts and test mixed mode (2 local split-screen players + 1-2 remote online players).