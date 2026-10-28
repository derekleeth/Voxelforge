# Voxelforge - Decision Log

> A record of the big choices we make and *why*, so future-us (and Claude) don't re-argue them.
> Newest decisions go at the top. Status is one of: **Proposed**, **Accepted**, **Replaced**.

---

## D-009: Cameras and Voronoi Split-Screen
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **One camera rig per local player**, made at runtime by `SplitScreenDirector` (a scene component, not a singleton) as a copy of the **Overview Camera**, so every view shares its render settings. The overview camera shows the world before anyone joins. Nothing uses `Camera.main` (the `MainCamera` tag is gone).
- **How the screen is shared** (`SplitMode`):

  | Players | Layout |
  |---|---|
  | 0 | Overview camera |
  | 1 | One full-screen camera |
  | 2, within **15 m** | **Merged:** one shared camera on the midpoint. It pulls back as they spread (`7 m + 0.6 × separation`) |
  | 2, beyond 15 m | **Voronoi:** an angled split line at right angles to the on-screen direction between them |
  | 3–4 | **Grid** of quarters (top-left, top-right, bottom-left, bottom-right). With 3 players the 4th quarter shows the overview |

- **Seamless split.** While split, each camera looks at `player + toward-midpoint × min(half the separation, 7.5 m)` from the distance it had at 15 m. Exactly at 15 m that point *is* the midpoint, so the two split views equal the shared view and the line appears with no jump. The divider fades in over the first metre. The math lives in `SplitScreenMath` and is unit-tested.
- **Turning the camera:** mouse (0.15°/px, while the cursor is locked) or right stick (180°/s).
  - **Together**, either player turns the shared camera.
  - **Apart**, each player turns their own view.
  - From 15 to 22 m, each view eases between the shared angle and the player's own (`smoothstep`). While split, the shared angle drifts to the middle of the two views, so they meet halfway when the players come back together.
  - Pitch is clamped to 10°–70°.
- **How it's drawn (cheapest way we found):** the first player's camera draws straight to the screen, and the second player's camera draws into a `RenderTexture`. The **Composite Camera** is a URP *overlay* camera stacked on the first player's camera. It draws only a full-screen quad (on the `Compositor` layer) with `SplitScreenComposite.shader`, which paints the texture over the second player's side of the line and draws the divider. This replaced a first version where both views went into textures and a separate base camera combined them. That version cost about 1.3 ms more per frame, plus a second full-screen texture.
- **Camera-relative movement, still host-authoritative.** `PlayerInputFrame` carries `ViewYaw` (which way the player's camera faces). The host turns `Move` by it (`MoveDirection.ToWorld`) before running the motor. The host never sees cameras, only the yaw, and Phase 4 aiming can reuse it.
- **`PlayerController.HasLanded`** is a `NetworkVariable`, so cameras on any PC know when a hero is on the ground. The old `IsWaitingForGround` is host-only.
- **HUDs (4.4) ask `SplitScreenDirector.TryGetView(slot)`** for their player's camera, screen rectangle and Voronoi side, never the whole screen (CLAUDE.md Law #5).
- **Mouse lock:** the cursor locks while a keyboard + mouse player is in. Esc frees it, and a click takes it back.
- **One `AudioListener`** follows the middle of the players.

- **Keeping your hero on your side while free-looking** (fixed after play-testing). Aiming at a fixed world point only keeps a hero on the correct side while the camera faces the shared angle. After a sharp turn, a hero could end up under the other player's half or off screen. Split cameras now blend, by look freedom, from that world point to an **on-screen** point that keeps the hero halfway between the screen center and the edge of their own side (`SplitScreenMath.SplitFocus`). It's still the world point at 15 m, so the split stays seamless.

### Performance (play-tested in the editor with the Game view focused)
- Before tuning, you saw **72–81 FPS** split.
- Measured (650×839 Game view): **merged 183 FPS** (GPU 3.9 ms), **split 102 FPS** (GPU 7.8 ms). **The split costs almost exactly twice the merged view.** Every camera pays full price to cull and draw ~900 chunk renderers, 4 shadow cascades and the post effects. Doubling the pixels (1304×847) didn't change GPU time, so pixels aren't the bottleneck.
- **Free wins we kept (same look):**
  - SSAO uses **Source = Depth + After Opaque** instead of Depth Normals. That removes a whole extra geometry pass (the depth-normals prepass) per camera: 102 → 109 FPS split. Normals rebuilt from depth are exact on flat block faces, and screenshots before and after looked the same.
  - **Camera opaque texture off.** Nothing samples `_CameraOpaqueTexture`, and it was a full-screen copy per camera per frame.
- **Small wins we did *not* take:** half-resolution SSAO (+5%) and 2 shadow cascades (+3%) cost a little softness for little gain.
- **The real fix goes in Roadmap 2.4:** Unity 6's **GPU Resident Drawer** with **GPU occlusion culling**, so a second camera doesn't re-submit every chunk and chunks behind hills are skipped. It needs the block shader to support DOTS instancing. Then we confirm 120+ split in the 2.7 build.

### Deferred on purpose
- Voronoi for 3–4 players (merging in groups).
- Per-player audio listeners.
- Camera collision with terrain (the camera passing through hills): 2.5, with the character work.
- Heroes turning to face where they walk: 2.5.

---

## D-008: How Each Player Gets Their Controller
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **Press to join.** Nobody spawns when Play starts. Pressing **Start or A** on a gamepad, or **Enter or Space** on the keyboard, claims the next local slot for that device, and the host spawns a player for it. Keyboard + mouse always join together as one player.
- **Each player only hears their own devices.** Every slot gets a `LocalPlayerInput`: its own copy of `VoxelforgeControls.inputactions` plus an Input System **`InputUser`** paired to that player's devices, with that player's **control scheme** as the binding mask. Two gamepads can't steer each other's heroes.
- **Our own actions asset** (`Assets/_Voxelforge/Input/VoxelforgeControls.inputactions`): `Move`, `Jump`, and `Look` (defined for the 2.3 cameras, not sent yet). Control schemes: `KeyboardMouse`, `Gamepad`, and the dev-only `KeyboardLeft` / `KeyboardRight`. Unity's template `InputSystem_Actions` is unused.
- **Slots go Free → Joining → Active ⇄ Disconnected.** `LocalSlotAllocator` (pure, tested) picks the slot for a new device:
  1. A **disconnected** player first, so a new controller takes over a lost one.
  2. Otherwise the lowest free slot.
  3. Otherwise nobody: there are 4 slots per PC.
- **Unplugged controller:** `InputUser` reports `DeviceLost`, and that player stands still (sends zero input) until `DeviceRegained`, or until another unclaimed controller presses Start and takes the slot over. Taking over keeps the same hero; it doesn't spawn a new one.
- **One input object per slot for the whole session.** Taking over a slot re-pairs the devices *inside* the same `LocalPlayerInput`, so the `PlayerController` never needs re-wiring.
- **Split keyboard is a dev toggle** (`LocalPlayerManager → Allow Split Keyboard`, off by default): Space joins with WASD + Space, and Enter joins with Arrows + Right Ctrl. Both players are paired to the same keyboard but use different binding masks.
- **Joining goes through the host.** The roster calls `PlayerJoinService.RequestJoin`, which sends `RequestJoinRpc(localSlot)` to the host. The host refuses a slot above the per-connection limit, a slot that connection already has (`PlayerRegistry.IsSlotTaken`), or a full session (4 players). Remote couches in Phase 6 use the same RPC. `PlayerJoinService` lives on its own `Session Network` scene object, because NGO doesn't allow a `NetworkObject` on the NetworkManager.
- **Finding your own players without a global search:** `PlayerController` raises static `OwnerSpawned` / `OwnerDespawned` events on the owning PC. `LocalPlayerManager` listens and hands the player its slot's `LocalPlayerInput` (`SetInputSource`). These are events, not a player singleton, so CLAUDE.md Law #1 still holds.
- **Code split:** the join rules live in the plain-C# `LocalPlayerRoster`, so tests drive it with the Input System's virtual devices (`InputTestFixture`). `LocalPlayerManager` is the thin scene component around it.

### Deferred on purpose
- **Leaving the game** (a "drop out" button that despawns your hero): not needed yet.
- **Join refused by the host** leaves the slot showing "joining…": only possible with 4+ players, so there's no reply message yet.
- **Rumble, button-prompt icons per device, and rebinding:** Phase 4 UI.

---

## D-007: How Couch Co-Op Runs on a Host
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **Netcode for GameObjects 2.13.3** + Unity Transport. Pressing Play starts a **Host** (server + local client in one program) listening on `127.0.0.1:7777`. `GameSession` starts it; nothing else calls `StartHost`.
- **One connection, many local players.** NGO allows one `NetworkManager` per program, so the couch can't be "2 clients". Instead the host's own connection **owns several `PlayerController` objects**, told apart by their **local slot** (0 = Player 1, 1 = Player 2). In Phase 6, a remote couch is just another connection with its own slots.
- **`PlayerId` ≠ connection.** `PlayerId` (in `Voxelforge.Core`, 1 byte, `P1`, `P2`…) is who did something. It's the `InstigatorPlayerID` for voxel edits, damage and the delta log. `PlayerRegistry` (host only, not a singleton) maps each `PlayerId` to its `OwnerClientId` + `LocalSlot`.
- **NGO's automatic player prefab is off.** `PlayerRegistry.SpawnLocalPlayer` spawns players with `SpawnWithOwnership`. The ID and slot are `NetworkVariable`s, written in `OnNetworkSpawn` so clients get them with the spawn.
- **Server-authoritative movement.** Every frame, the owning client reads controls from an `IPlayerInputSource` and sends a `PlayerInputFrame` (move, buttons, sequence number) with `SubmitInputRpc`. The host ignores frames from anyone but the owner, and frames older than the newest one it has. The host runs the motor at the fixed rate (50 Hz), blends between ticks for smooth 120+ FPS visuals, and a server-authority `NetworkTransform` sends the position to clients.
- **Voxel box collision, no Unity physics.** `VoxelBodyMotor` moves a box (0.6 × 1.8 × 0.6 m) through the block grid one axis at a time, in sub-steps under half a block, so fast falls can't skip a floor. It reads blocks straight from `VoxelWorld`:
  - Block edits (Phase 3) change collision on the very next tick, with no colliders to rebuild.
  - It's plain C#, cheap enough for the host to run for every player, and unit-tested.
  - **Chunks that aren't generated yet count as solid**, and a body whose own chunk isn't loaded is held still. Nobody falls out of the world while terrain streams in (2.4).
- **Spawning waits for terrain.** `SpawnPointFinder` scans a column from the sky down for the first solid block with 2 blocks of head room, and keeps trying each tick until the chunks exist. Scanning from the top finds the surface, never a cave.
- **New assembly `Voxelforge.Gameplay`** (Core + Voxels + Netcode + Transport + Input System). Voxels and Core still know nothing about networking.

### Temporary on purpose
- `TempKeyboardInputSource`: P1 = WASD + Space, P2 = Arrows + Right Ctrl on one keyboard. **Replaced in 2.2** by per-device binding (D-008).
- `TempMidpointCamera`: one camera on the players' midpoint. **2.3** replaces it with per-player Voronoi split-screen.
- Movement is world-aligned (W = north), not camera-relative, until the cameras exist (2.3).

### Deferred on purpose
- **1 m auto-step** (walk up one block without jumping): **2.5**. For now, you jump.
- **Client-side prediction / reconciliation:** not needed on loopback (zero lag). Phase 6, when real network lag appears.
- **Per-slot authority check:** the host can only check *which connection* sent an input, not which slot. A connection could steer its own other couch players, which is harmless. Phase 6 should check that a connection only sends for its own slots (it already can't steer other connections' players).
- `Assets/DefaultNetworkPrefabs.asset` is created and kept up to date by NGO itself, so it stays where NGO puts it, outside `_Voxelforge/`.

---

## D-006: How Blocks Get Their Look
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **Style:** stylized, hand-painted vanilla-WoW look at **128 × 128 per block face** ("Painted, high detail"). Shading is painted into the colors (light on bumps, dark in cracks), like WoW's textures, on top of real lighting.
- **All textures are Blender procedural.** No AI image generation, so no outside accounts or credits.
  - `Tools/Blender/BlockMaterials.blend` holds one node material per texture (`VF_Stone`, `VF_GrassTop`…).
  - `Tools/Blender/bake_block_textures.py` bakes each one at 512 px into `Art/Textures/Blocks/<Layer>_{Albedo,Height,Normal,Mask}.png`.
  - Both live **outside `Assets/`**, so Unity doesn't try to import the `.blend` as a model.
- **Seamless by construction:** noise is sampled on a 4D torus wrapped around the texture square, so every texture tiles on both axes. Grass Side is the exception: it has grass at the top and dirt at the bottom, so it only tiles sideways.
- **Normal and AO maps come from the height map** (numpy, in the bake script), so all four maps always agree.
- **Tweak, then re-bake.** The baker keeps hand-edited node graphs (`REBUILD = False`). Set `REBUILD = True` to reset every material to the recipes in the script.
- **9 texture layers:** GrassTop, GrassSide, Dirt, Stone, ManaCrystalOre, Sand, Snow, Sandstone, RedRock.
  - Blocks pick a `BlockTexture` asset for their top, sides and bottom. Side falls back to top, and bottom falls back to side.
  - Grass is GrassTop / GrassSide / Dirt.
  - A `BlockTexture` asset holds no image references: its **name** links it to the PNGs. That keeps the 512 px sources out of game builds.
- **Three `Texture2DArray`s**, built by *Voxelforge → Rebuild Block Textures* (`BlockTextureArrayBuilder`):

  | Array | Channels | Format |
  |---|---|---|
  | AlbedoHeight | color (sRGB) + height | BC7 |
  | Normal | X, Y (shader rebuilds Z) | BC5 |
  | Mask | AO, smoothness, emission | BC7 |

  All three use 8 mip levels, built with a box filter: color is averaged in linear space, and normals are re-normalized. The layer order comes from `BlockRegistry.GetTextureLayers()`, the same function the block lookup uses, so the two can't disagree. A test fails if the arrays are out of date.
- **Arrays, not an atlas.** UVs are measured in blocks and wrap with Repeat, so one greedy-merged quad shows one texture copy per block. No atlas bleeding, and mipmaps just work.
- **Vertex format unchanged (40 bytes); `UV1.x` changed meaning.** In D-004 it held the block ID; now it's the face's **texture layer**, picked by the mesher from the facing (top/side/bottom), or −1 for "no texture" (then the block's flat `MapColor` is drawn).
- **No tangents in the mesh.** Faces are axis-aligned and the mesher lays UVs along fixed axes per facing, so the shader works out the tangent frame from the normal.
- **Shader `Voxelforge/Voxel Block`:** URP physically based lighting.
  - Sun + soft shadows, **Forward+ point/spot lights with shadows** (ready for 3.5 torches), SSAO (which also sees the normal maps), fog, ambient light and emission.
  - **Parallax occlusion** from the height map gives close-up depth: 6–16 steps, 0.03 blocks deep, fading out with distance (32 m) and at grazing angles.
  - Mana Crystal Ore glows through the emission mask, and the scene's Bloom makes it shine.
- **Performance:** about 8 ms per frame (~120 FPS in the editor) on the full 512 × 256 × 512 test world. Parallax costs nothing measurable at overview distance, because it has faded out by then.

### Deferred on purpose
- **Biome color tinting** (e.g. greener grass in Meadows, drier in Desert borders): vertex color is still free for it.
- **Per-face random rotation/variants** to hide repetition on big flat areas.
- **Smooth lighting / per-corner AO:** still deferred (see D-004). SSAO + painted AO cover it for now.
- **Block light from Mana Crystal Ore** (it glows but doesn't light its surroundings yet). Comes with the block-light system.

---

## D-005: How the World Grows From a Seed
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **One `WorldSeed` integer builds the whole world.** `VoxelWorldManager` owns it (Inspector: `worldSeed`, or "randomize every Play"), so saves (3.4) and joining players (6.2) need only the seed + the blocks players changed.
- **Terrain is a pure function of seed + settings + position** (`TerrainSampler`). Any chunk can be generated alone, in any order, on any thread, and comes out the same. A test generates chunks in two orders and compares them.
- **Seeding noise:** `noise.snoise` has no seed, so each noise layer (temperature, ruggedness, each biome, tunnels, caverns, ore) is shifted by its own offset made from `hash(seed, salt)` using integer math only.
- **4 biomes on a climate map** (temperature × ruggedness), styled after vanilla WoW zones:

  | Biome | Inspired by | Shape | Surface / under |
  |---|---|---|---|
  | Meadows | Elwynn Forest | gentle rolling hills | Grass / Dirt |
  | Desert | Tanaris | ridged dunes | Sand / Sandstone |
  | Red Crags | Redridge Mountains | stepped (terraced) cliffs | Grass / Red Rock |
  | Snowy Peaks | Dun Morogh | tall ridged mountains | Snow / Stone |

  Any ground above the **snowline (y = 165)** gets snow, whatever the biome.
- **Smooth borders:** the nearest biome always has full weight; others fade in as they get within `biomeBlend` of being nearest. Heights are blended by weight, so biomes meet without cliffs. Surface blocks use the nearest biome with a little per-column jitter, so borders look ragged instead of ruler-straight.
- **Caves:** "spaghetti" tunnels where two 3D noises both cross zero, which narrow near the surface so only some break through as entrances, plus big "cheese" caverns below y = 56. Nothing is carved below `caveFloorY` (1), so the world always has a floor.
- **Mana Crystal Ore** grows in 3D-noise clusters below y = 80, and is richer under mountains (`oreRichness` per biome).
- **All tuning lives in `Data/Terrain/TerrainSettings.asset`**, built like the block registry: Inspector-friendly ScriptableObject → blittable `TerrainParams` + `NativeArray<BiomeInfo>` for jobs.
- **Generation never blocks the main thread.** `ChunkGenerationScheduler` works like the mesh scheduler: `Request(coord)`, then each frame it collects finished `GenerateChunkJob`s and schedules more (32 in flight). Roadmap 2.4's streaming loader will just call `Request` around each player. Chunks wait to mesh until their neighbours that are still generating arrive, so nothing is meshed twice.
- **Determinism safeguards:**
  - Burst `FloatMode.Strict` + `FloatPrecision.High` + synchronous compilation.
  - No sin/cos/exp/pow in terrain code (only +, ×, floor, sqrt and simplex noise).
  - A **golden-hash test** fingerprints one chunk. If it fails, terrain output changed: on purpose → update the hash and bump `TerrainSampler.GeneratorVersion`; otherwise it's a bug.
- **Editing `TerrainSettings` changes every world made from it.** Fine for now. Once saves exist (3.4), bump `GeneratorVersion` when you do.
- **Run In Background is on** (Player Settings), so Play Mode keeps running when Unity isn't the focused window. That's needed for testing via MCP, and it suits couch co-op too.

### Honest caveat
Every x64 PC should build identical worlds from the same seed. Bit-identical floats across *different CPU families* (e.g. ARM consoles/phones) aren't guaranteed. If that matters in Phase 6, the host can compare chunk hashes and send any chunk that differs.

### Deferred on purpose
- **Trees, grass tufts, water/lakes:** need transparent block rendering (see D-004).
- **Bedrock / world bottom and height limits:** the test area is 16×8×16 chunks (512×256×512 blocks), fixed until 2.4's streaming loader.
- **Dungeon stamps:** Roadmap 5.3.

---

## D-004: How Chunks Become Meshes
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **Two jobs per chunk.**
  1. `ChunkGatherJob` copies the chunk's block IDs, plus a 1-block border from its 6 neighbours, into a padded 34³ buffer.
  2. `GreedyMeshJob` (Burst) builds the mesh from that copy only.

  Chunks are only locked during the short gather step. Before editing a chunk, call `ChunkMeshScheduler.CompleteGatherJobs()`.
- **Binary culling.** Each row of opaque blocks is packed into a `ulong` (34 bits including the border). `col & ~(col >> 1)` finds every visible face in the row at once.
- **Greedy merge per block type.** Faces only merge with the same block ID, so each block keeps its own color and texture.
- **"Opaque" = registered and not transparent** (`BlockInfo.IsOpaque`). Transparent blocks (glass, water) aren't drawn yet. They'll need their own pass when we add one.
- **Unloaded neighbours count as Air.** The edge of the loaded world shows its faces. When a neighbour loads, both chunks are remeshed.
- **Vertex format (40 bytes):**

  | Field | Format | Notes |
  |---|---|---|
  | Position | float3 | chunk-local |
  | Normal | float3 | |
  | Color | Color32 | placeholder `MapColor` |
  | UV0 | float2 | **in blocks**, so textures repeat once per block |
  | UV1.x | float | block ID, for 1.5 texture arrays |

  Indices are always 32-bit, because the worst case is 393k vertices.
- **Meshes are written straight from jobs** with `Mesh.MeshData`, so the main thread doesn't copy vertex arrays.
- **Never wait on jobs.** Each frame picks up finished jobs and schedules new ones, with at most 32 chunks meshing at once (adjustable on `VoxelWorldManager`).
- **Stale results are thrown away.** Each job remembers the version of its chunk and its 6 neighbours. If any changed before the job finished, the mesh is discarded and rebuilt.
- **Shortcuts:** all-air chunks, and solid chunks buried in solid chunks, skip meshing entirely and get no GameObject.

### Deferred on purpose
- **Ambient occlusion / smooth lighting.** Per-corner shading stops some faces from merging. We'll decide on it alongside the 1.5 art.
- **Mesh colliders.** Needed when characters arrive in Phase 2.
- **Unloading chunks and object pooling.** Comes with the multi-viewer loader in 2.4.

---

## D-003: How the World Stores Blocks
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **Chunks are 32 × 32 × 32.** One row of blocks fits in a 32-bit integer, so the 1.3 mesher can find faces with fast bit tricks. That's also 8× fewer chunks than 16³, and coordinate math is just shifts (`>> 5`, `& 31`), which works correctly for negative coordinates.
- **One voxel = 4 bytes** (`Voxel` struct): `BlockId` (ushort), `Light` (4 bits sunlight + 4 bits block light, 0–15 each), `State` (3 bits facing + 5 reserved). A full chunk is 128 KB.
- **Durability is not stored per voxel.** `BlockDefinition.MaxDurability` says how tough a block *type* is. In-progress mining damage goes in a small sparse map, built in Roadmap 3.2. Almost no blocks are damaged at once, so a per-voxel byte would waste memory and bloat saves and network syncs.
- **Uniform chunks:** a chunk that's all one block (sky, deep stone) stores a single voxel and no array. It allocates its array on the first different write; `Compact()` frees it again.
- **Stable block IDs:** every `BlockDefinition` asset has an explicit ID that must never change or be reused, mirrored in `BlockIds.cs`. `BlockRegistry` validates them (no duplicates, Air = 0 and not solid) and builds a `NativeArray<BlockInfo>` that jobs index by ID. A test checks the real registry asset.
- **Edits are compiler-enforced:** `VoxelChunk.SetVoxel/Fill/Compact` are `internal` to `Voxelforge.Voxels`, so gameplay code *can't* edit chunks directly (CLAUDE.md Law #3). Each chunk has a `Version` counter so meshing and networking can tell what changed.
- **Block index order** inside a chunk: `x + z·32 + y·1024` (x fastest).
- **Starter blocks:** Air 0, Grass 1, Dirt 2, Stone 3, Mana Crystal Ore 4 (glows at light level 7).

---

## D-002: Project Layout, Packages & MCP Tools
- **Date:** 2026-10-27
- **Status:** Accepted

### Decisions
- **Editor version:** Unity **6.6 (6000.6.7f1)**, a "Supported" release rather than LTS. Plan: upgrade to **Unity 6.7 LTS** when it's out.
- **Unity project lives at the repo root** (`Assets/`, `Packages/`, `ProjectSettings/` next to our docs). One folder, one repo, no nesting.
- **All of our own content goes in `Assets/_Voxelforge/`**, so it's never mixed with imported packages or store assets. The `_` sorts it to the top.
- **Code is split into assembly definitions** for faster compiles and clear boundaries:
  - `Voxelforge.Core`: shared types, no voxel or engine-heavy code
  - `Voxelforge.Voxels`: chunks, meshing, noise (Burst + Collections + Mathematics, unsafe allowed). Plural on purpose: a namespace named `Voxel` would clash with the `Voxel` struct.
  - `Voxelforge.Editor`: editor-only tools
  - `Voxelforge.Tests.EditMode`: unit tests
- **Packages:** Burst 2.0, Collections 6.6, Mathematics 1.4 and Test Framework 1.8 (all built into Unity 6.6). Netcode for GameObjects 2.13.3 was added in Phase 2.1 (see D-007).
- **Removed Unity Version Control (`collab-proxy`)**, because we use Git. Binary art goes through **Git LFS** (see `.gitattributes`).
- **MCP tools** (project `.mcp.json`):
  - **Unity:** "MCP for Unity" by CoplayDev, pinned to **v10.3.0**. It uses the HTTP transport at `http://127.0.0.1:8080/mcp`; start it from *Window → MCP for Unity → Toggle MCP Window → Start Server*.
  - **Blender:** `blender-mcp` add-on 1.8 (port 9876); connect it from the N-panel → *MCP for Blender* tab.
  - Asset libraries and AI model generators in the Blender add-on are **off** for now.

---

## D-001: Game Engine
- **Date:** 2026-10-27
- **Status:** Accepted (2026-10-27)
- **Decision:** Unity 6 (LTS) + Universal Render Pipeline (URP), C#, Netcode for GameObjects

### What we need from an engine
| Need | Where it comes from |
|---|---|
| Fast voxel meshing off the main thread | `CLAUDE.md` Law #4, Roadmap 1.3 |
| Stylized "WoW-look" shaders with modern lighting | Roadmap 1.5 |
| Local split-screen with a host/server running underneath | `CLAUDE.md` Laws #1, #2, #5, Roadmap Phase 2 |
| Easy swap to online play later (Steam / EOS) | Roadmap Phase 6 |
| Text-based code and data Claude can read and edit | How we're building this |

### Options considered
| | Unity 6 (URP) | Unreal Engine 5 | Godot 4 |
|---|---|---|---|
| Voxel meshing | **Jobs + Burst**: very fast and widely used for Minecraft-like games | C++ async tasks + RealtimeMesh/Voxel Plugin; fast but heavier | C# threads; workable, fewer proven voxel examples |
| Stylized graphics | Shader Graph + URP; stylized looks are well documented | Best raw visuals (Lumen); stylized is possible but fights the defaults | Good, but behind the other two |
| Split-screen + host | Input System `PlayerInputManager` handles split-screen; host owns several local players | **Built in**: multiple local players + listen server | SubViewports + high-level multiplayer; more DIY |
| Online later | NGO transports for Steam/EOS exist | Online Subsystem Steam/EOS built in | Community plugins |
| Works with Claude | **All C# and text assets** | Blueprints are binary (Claude can't edit them); slow C++ compiles | All text (GDScript/C#) |
| PC requirements | Moderate | High | Low |
| Cost | Free (Unity Personal) | Free until $1M revenue | Free / open source |

### Why Unity
1. Burst-compiled Jobs make greedy meshing and noise generation fast, and they run off the main thread by design.
2. Everything is C# and text, so Claude can write, read, and fix all of it, and a beginner can follow it.
3. URP + Shader Graph gives the hand-painted, chunky WoW look without needing a monster PC.
4. ScriptableObjects fit the data-driven spell/item design in Phase 4.
5. Netcode for GameObjects runs Host mode locally today, and in Phase 6 we swap the transport for Steam or EOS without rewriting gameplay.

### Trade-offs we accept
- Unreal has better *native* split-screen + replication. In Unity we build the "one host, several local players" layer ourselves in Phase 2.
- Voronoi split-screen is custom work in any engine.
