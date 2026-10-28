# Voxelforge - Development Roadmap & Milestone Tracker

> **Instructions for Claude Code:** 
> 1. Always check this file to identify the active phase and current task before writing code.
> 2. Follow all architectural guardrails in `CLAUDE.md` (Host-Authority, No Singletons, Message-Driven Voxels).
> 3. When a milestone task is implemented and verified by the user, update `[ ]` to `[x]` in this file.

---

## Phase 1: The Voxel Foundation ("Hello, World of Blocks")
**Goal:** Generate a high-performance, 3D blocky terrain with modern lighting and PBR textures.
- [x] **1.1 Engine & MCP Setup:** Initialize repository, configure `.gitignore`, and verify Engine MCP + Blender MCP connections.
- [x] **1.2 Chunk Data Architecture:** Create `VoxelChunk` data structures (`16x16x16` or `32x32x32`) storing Block ID, Durability, and Light/State bytes. *(Built as 32³ with 4-byte voxels; max durability lives on the block type and damage is stored sparsely. See DECISIONS.md D-003.)*
- [x] **1.3 Greedy Meshing & Culling:** Implement background-threaded Greedy Meshing and hidden-face culling so chunks render at 120+ FPS. *(Burst gather + greedy mesh jobs, 140–158 FPS on a 16×4×16-chunk test world. See DECISIONS.md D-004.)*
- [x] **1.4 Procedural Noise Terrain:** Generate rolling hills, caves, and biomes from a deterministic `WorldSeed` integer. *(4 WoW-zone-style biomes, tunnels + caverns, ore clusters; 2,048 chunks in ~1 s at ~122 FPS. See DECISIONS.md D-005.)*
- [x] **1.5 Art & Shaders (Creative):** Create PBR Triplanar/Block materials (Grass, Dirt, Stone, Mana Crystal Ore) with Normal/Height maps and dynamic lighting. *(Blender-baked painted textures for all 8 blocks in Texture2DArrays, URP PBR shader with parallax, Forward+ lights and glowing ore; ~120 FPS. See DECISIONS.md D-006.)*

## Phase 2: Dynamic Couch Co-Op & Smooth Heroes
**Goal:** Get two smooth RPG characters running around the blocky world together on one PC.
- [x] **2.1 Local Listen-Server Setup:** Configure game to launch in local `Host Mode` (Server + 2 Local Clients). *(One host connection owns both couch players, told apart by local slot; server-authoritative input RPCs and voxel box collision. See DECISIONS.md D-007.)*
- [x] **2.2 Dual Input Binding:** Map Player 1 (Keyboard/Mouse or Gamepad 1) and Player 2 (Gamepad 2) via native local player subsystems. *(Press-to-join: each player gets an `InputUser` paired to their own devices; disconnect/reconnect and controller takeover; split-keyboard dev toggle. See DECISIONS.md D-008.)*
- [x] **2.3 Voronoi Dynamic Split-Screen:** Build midpoint shared camera when players are `< 15m` apart; smoothly split via angled screen-space render targets when `> 15m` apart. *(Seamless split at 15 m, free look per player when apart, camera-relative movement, 3–4 player grid. Split is 109 FPS in the editor vs 183 merged; reaching 120+ moves to 2.4. See DECISIONS.md D-009.)*
- [x] **2.4 Multi-Viewer Chunk Loader:** Update Chunk Manager to stream/unload terrain around a `List<ViewerTransform>` so both players can explore independently. Draw chunks with Unity 6's GPU Resident Drawer + GPU occlusion culling (block shader needs DOTS instancing) so each extra split-screen camera is cheap: split-screen must reach 120+ FPS (see D-009). *(Streams whole chunk columns in a 256 m circle around every player, with a 2-chunk unload margin, pooled chunk objects and edge fog; GPU Resident Drawer + GPU occlusion culling with a DOTS-instanced block shader. Play-tested in split-screen. See DECISIONS.md D-010.)*
- [x] **2.5 Smooth Character Integration (Creative):** Import stylized 3D characters with Mixamo animations, 1m auto-stepping, and IK foot placement on blocky stairs. *(Our own hero "the Wayfarer", built in Blender by script and rigged by Mixamo through a closed-shell stand-in; team-colour cloth per player; auto-step up and down 1-block steps, heroes face where they walk, foot IK that keeps feet on block edges, camera collision with terrain. Play-tested. See DECISIONS.md D-011.)*
- [ ] **2.6 Foliage & Decoration Layer (Creative):** Add a non-voxel decoration layer of stylized, hand-painted (WoW-*inspired*, original or CC0) grass tufts, flowers, bushes and trees on top of the blocky terrain. Place decorations deterministically from `WorldSeed` + block position during chunk meshing (off the main thread), draw them with GPU instancing plus a wind-sway shader and distance fade, and stream them with the multi-viewer chunk loader (2.4). Decorations are rebuilt whenever their supporting voxel changes, so they need no save data or network sync. Trees are pretty meshes that fill invisible `TreeTrunk` voxels, so they block building, give collision, and are chopped through `RequestModifyVoxel` (dropping wood blocks once 3.2 lands). Add per-biome color tinting and voxel corner ambient occlusion. FPS must hold at 120+ in split-screen.
- [ ] **2.7 Playable Demo Build:** Build a runnable Windows `.exe` (into a git-ignored `Builds/` folder) and play-test it outside the editor: both players exploring the world in split-screen. Check that it starts cleanly, that terrain, textures, foliage, lighting and glowing ore look the same as in the editor, and that FPS holds at 120+. Record the results (FPS, build size, issues found) in `DEVLOG.md`.

## Phase 3: "Forge & Carve" (Digging, Building & Saving)
**Goal:** Let players mine tunnels, build castles, and save their world in a network-ready format.
- [ ] **3.1 Message-Driven Voxel Edits:** Implement `RequestModifyVoxel(Coord, BlockID, InstigatorID)`. Never mutate chunk arrays directly from client input.
- [ ] **3.2 Block Durability & Mining:** Add raycast/spherecast targeting, block cracking decals, and block item drops. Store in-progress mining damage in a sparse map (see D-003).
- [ ] **3.3 Grid Snap Placement:** Add ghost-block preview and placement validation (prevent placing blocks inside characters).
- [ ] **3.4 Seed + Delta Save System:** Save world using only `WorldSeed` + a compressed dictionary of modified chunk deltas and `PlayerUUID` profiles.
- [ ] **3.5 Props & Tools (Creative):** Model 3D pickaxes, torches (with real point lights), and interactive workbenches.

## Phase 4: The Warcraft Injection (Classes, Races, Spells & RPG UI)
**Goal:** Layer *World of Warcraft* combat, mana, cooldowns, and talent trees onto the voxel world.
- [ ] **4.1 Data-Driven Ability Framework:** Build ScriptableObject/DataAsset spell templates (Mana Cost, Cooldown, Cast Time, Element, Range, AoE Radius).
- [ ] **4.2 Spell Logic vs. Visual Cue Split:** Separate authoritative damage/terrain destruction math from client-side particle VFX and audio.
- [ ] **4.3 Destructive Spell Mechanics:** Allow explosive spells (e.g., *Pyroblast*, *EarthShatter*) to batch-modify voxel chunks and carve real craters.
- [ ] **4.4 Per-Player Split-Screen HUD:** Build independent Action Bars (1-9 hotkeys/d-pad), cast bars, floating combat text, and inventory windows confined to each player's viewport.
- [ ] **4.5 Class & Spell Design (Creative):** Design first 3 playable classes (*Runeforged Paladin*, *Geomancer Mage*, *Shaman*) with 3 signature abilities each. The Shaman plants elemental totems on blocks (server-owned entities that buff, heal or attack nearby) and has at least one earth spell that changes terrain through `RequestModifyVoxel` (e.g. *Earthen Wall* raises a real block wall).
- [ ] **4.6 Playable Races & the Thunderhoof (Creative):** Add a data-driven `RaceDefinition` (model, stats, racial passive, racial ability) and a race choice on character creation. First races: the *Thunderhoof*, our own big, gentle bull-folk of the plains with a deep shaman tradition (original name, look and lore, no Blizzard IP), plus one more race. Thunderhoof racials: *Hoofquake* (short AoE stun around you) and *Herb-Sense* (bonus Herbalism yield, 5.5). Add a *Thunderhoof Plains* homeland biome (windswept grassland, mesas, tall grass) to the seeded terrain. Model the Thunderhoof in Blender with a larger humanoid rig so it can share the 2.5 Mixamo animations.

## Phase 5: Monsters, Dungeons & Bosses
**Goal:** Populate the world with intelligent RPG creatures that can navigate dynamic blocky terrain, and close the gather → craft → gear-up loop (ores, herbs, mob drops → potions, armor, weapons).
- [ ] **5.1 Dynamic Voxel NavMesh:** Configure runtime NavMesh rebuilding so monsters recalculate paths when players dig trenches or build walls.
- [ ] **5.2 RPG Threat & Aggro System:** Implement a *WoW*-style threat table so tank characters can hold boss aggro while ranged characters cast spells.
- [ ] **5.3 Procedural Dungeon Stamps:** Stamp pre-built boss arenas and ruins into the procedural voxel world during chunk generation.
- [ ] **5.4 Loot & Equipment System:** Implement armor slots, weapon attachment sockets, and rarity tiers (Common, Rare, Epic, Legendary). Mobs and bosses use data-driven loot tables, rolled on the server, that drop both gear and crafting materials (hides, fangs, elemental essences). Each couch player gets their own loot so co-op partners never fight over drops.
- [ ] **5.5 Gathering Professions:** Add *Mining* (ore blocks from 3.2 drop ore items; rarer ore tiers by depth and biome), *Herbalism* (special herb nodes from the 2.6 foliage layer that can be harvested, placed by biome, and respawn on a timer kept in a sparse server-side map like D-003's mining damage), and *Skinning/Salvaging* (gather extra materials from defeated mobs). Each profession has a skill level that rises with use and unlocks higher-tier nodes. Every harvest is a server-authoritative request carrying the `InstigatorPlayerID`.
- [ ] **5.6 Crafting Professions:** Add data-driven `RecipeDefinition` assets (inputs, output, required station and skill level) for *Alchemy* (health/mana potions and elixirs, used through the 4.1 ability framework), *Blacksmithing* (plate armor and weapons from ore), and *Leatherworking* (leather armor from hides). Crafting happens at placeable voxel stations (Alchemy Table, Forge + Anvil, Tanning Rack, extending the 3.5 workbench). Each player has their own split-screen crafting window (4.4). Crafted gear plugs into the 5.4 slots and rarity tiers.
- [ ] **5.7 Monster, Boss & Crafting Art (Creative):** Model and animate 3 overland mobs and 1 multi-phase Dungeon Boss with telegraph attacks. Create 3+ herb models, 3 ore tiers, crafting-station props, potion bottles, and one craftable armor set + weapon per class.

## Phase 6: Opening the Portal (Internet Multiplayer)
**Goal:** Enable online co-op with friends while preserving local Dynamic Split-Screen.
- [ ] **6.1 Relay Transport Swap:** Integrate Steamworks P2P or Epic Online Services (EOS) transport layer (no port-forwarding required).
- [ ] **6.2 Initial World Sync:** Send `WorldSeed` + compressed `VoxelDeltaLog` to joining remote players upon connection.
- [ ] **6.3 Bandwidth Optimization:** Compress chunk delta broadcasts and test mixed mode (2 local split-screen players + 1-2 remote online players).