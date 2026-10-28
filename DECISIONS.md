# Voxelforge - Decision Log

> A record of the big choices we make and *why*, so future-us (and Claude) don't re-argue them.
> Newest decisions go at the top. Status is one of: **Proposed**, **Accepted**, **Replaced**.

---

## D-019: Kian's M.G.R.S. and the Crafting Window
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **A ranged item is an item with `ProjectileStats`** (damage, cooldown, speed, range, muzzle point, glow colour) on its `ItemDefinition`. Damage 0 means not ranged. `ItemRegistry.TryGetProjectile` reads it, and the registry check flags a ranged item without a cooldown, speed and range above 0. In Phase 4 these numbers move into the 4.1 ability templates (mana cost, damage to creatures).
  - **The M.G.R.S. (`ItemIds.Mgrs`):** 45 damage, 0.5 s cooldown, 40 m/s, 24 m. Dirt, sand and logs break in 1 shot, stone in 2, Mana Crystal Ore in 4; bedrock never. Recipe: 3 Log + 2 Stone + 5 Mana Crystal Ore at a Workbench, added **at the end** of the `RecipeBook` (recipes are sent by index, D-018).
- **Mine fires it.** While the held item is ranged, `PlayerMiner` does nothing and `PlayerStaff` fires instead, so no new button and no new input field.
- **The host owns every bolt (Laws #1-#3).** `PlayerStaff.FixedUpdate` runs on the host only: it fires only while `PlayerHands` agrees the staff is in the bag, from the hero's eyes along the aim the host received (`PlayerAim`), the same as mining. No position or direction is ever sent from a PC.
  - `ManaBoltFlight` (plain C#, tested without a scene) keeps the cooldown and moves each bolt. Each step sweeps a voxel ray (`VoxelQueries.Raycast`) over the distance moved, so a fast bolt can't skip through a block, and it stops at the same blocks aiming does (solid blocks and props like torches).
  - A hit calls `VoxelWorldManager.RequestDamageVoxel(block, damage, shooter)`, so cracks, drops, falling trees and torches losing support all work exactly as with mining. Damage is fixed when the bolt leaves.
  - Two RPCs to every PC: `BoltFiredRpc` (id, start, direction, item) and `BoltEndedRpc` (id, end point, face normal, hit, block it hit).
- **Looks (`ManaBoltView`, every PC, presentation only).** Muzzle flash, a gem-shaped glowing bolt with a trail and a point light, and sparks tinted by the hit block's colour. The drawn bolt starts at the staff's muzzle and eases onto the host's line, then ends wherever the host says. Each hero has its own small pool: nothing global, and Forward+ only shades the bolt lights each split-screen camera sees.
  - `HeldItemView` holds a ranged item aimed forward instead of like a pickaxe, with a kick-back on each shot (`PlayerStaff.LastShotTime`).
  - The reticle reaches the full 24 m while the staff is picked, with a small recharge bar under it (`PlayerAim.TryAim(…, maxDistance, …)`).
- **The crafting window (`CraftingWindow`).** D-018's text list became a framed panel: every recipe on the left with a picture and a status ("Ready", "Needs a Workbench", "Missing items"), the chosen recipe large on the right with each input as have / need in green or red. It's laid out inside **that player's own view rect** (Law #5), so two players never get a menu in the same place, whatever the camera does.
  - **`ItemIcons`:** a hidden camera on an unnamed layer (31), far below the world, draws each item's model or block cube once into a small texture and keeps it. Nothing per player, so one cache serves every view.

### Deferred on purpose
- Damage to creatures, mana cost and a real cast animation: 4.1 / 4.2.
- Firing sounds and impact sounds: 3.7.

---

## D-018: Items, Tools, Torches and the Workbench (Props & Crafting)
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **Items beyond blocks, without a new save format.** A block's item ID is its block ID; other items start at `ItemIds.FirstNonBlock` (1000). `BlockStack`/`BlockStacks` became `ItemStack`/`ItemStacks` (field `ItemId`). A bag slot was already an ID plus a count, so 3.4 saves load unchanged.
  - `ItemDefinition` (Gameplay): ID, name, model + lit material, held pose in the hand bone, drop size and `ToolPower`. `ItemRegistry` lists them. A definition with a **block's** ID just gives that block a model when held or dropped (torch, workbench).
  - Tools stack like blocks for now: there's no tool wear yet.
- **Tool power.** Each block has a `HarvestKind` (Pick: Stone, Ore, Sandstone, Red Rock; Axe: trunks, Logs, Workbench; Any: the rest). `ToolPower` multiplies mining power (40/s) per kind. Bare hands are 1×.

| Tool | Pick | Axe | Any |
|---|---|---|---|
| Wooden Pickaxe | 2 | 1 | 1.5 |
| Stone Pickaxe | 3 | 1 | 2 |
| Stone Axe | 1 | 3 | 1.5 |
| **The Pick of Destiny** (Kian's) | 5 | 5 | 3 |

- **Holding things (`PlayerHands`).** D-016's block picker now picks any item (`ItemSelection`). The owner sends it in every input frame (`PlayerInputFrame.HeldItem`). **The host** only accepts it if the bag really has one, and shares the result as a `NetworkVariable`. `PlayerMiner` reads the host's value, so a PC can't claim a tool it doesn't own (Law #2). `PlayerBuilder` places only items that are blocks.
  - `HeldItemView` puts the model on the Wayfarer's humanoid **right hand bone** on every PC, and swings it while `PlayerMiner.IsMining` (a synced bool). Poses were measured on the idle pose (`PropsBuilder`).
- **Prop blocks.** New `BlockFlags.Prop` (drawn as a model by the 2.6 decoration layer, never as a cube; aim rays stop at it even when it isn't solid) and `BlockFlags.NeedsSupport`.
  - **Torch (11):** not solid, needs support, breaks in one hit, drops itself. **Workbench (12):** solid (an invisible full block under the model, like tree trunks), Axe.
  - A new `DecorationKind.Prop` draws every prop block's model, placed in the `DecorateChunkJob` beside meshing. Props come back from the delta log like any block, so they need no extra save data or sync.
  - **Floor or wall** comes from the blocks around the torch, not from stored facing. `VoxelSupport`: the solid block below wins, then north, east, south, west. Wall models have their wall on +Z and are turned to face it. Torches never hang from ceilings. Workbenches are turned to a seeded quarter.
  - **Support:** `VoxelPlacement.Check(…, blockId, …)` refuses a torch with nothing to rest on (`NoSupport`) and lets one go where a hero stands, since it isn't solid. The editor breaks unsupported torches whenever a solid block next to them goes, and that break is a normal edit, so the torch drops.
- **Torch lights (`PropLights`, scene, presentation only).** A `DecorationLight` on a decoration (colour, intensity, range, offset, flicker). A fixed pool of **32** point lights goes each frame to the torches nearest **any** viewer within 48 m, with Perlin flicker and no shadows. Forward+ shades only the lights each split-screen camera sees. Flames glow through a new emission map on `Voxelforge/Foliage`; only flame tips have wind sway painted in.
- **Crafting (`PlayerCrafter`).** **Interact** (F, gamepad X, `;` for the right split-keyboard player) opens a recipe list in the **top-right of that player's view** (Law #5). Next / Previous choose a recipe, and Place crafts it.
  - The PC sends only the recipe's index (`RequestCraftRpc(byte)`). The host re-checks it (`RecipeBook.Check`): a Workbench block within 4 blocks of the hero's middle, then the inputs. `ItemStacks.TryExchange` swaps inputs for the output all at once, or changes nothing. Crafts are at least 0.2 s apart.
  - `RecipeDefinition` (inputs, output, `needsWorkbench`) stays small so 5.6 can add stations and skill. Recipes are sent by **index, so new ones go at the end** of the `RecipeBook`.
  - Recipes: Workbench = 4 Log (anywhere), 4 Torches = 1 Log (anywhere), Wooden Pickaxe = 3 Log, Stone Pickaxe / Stone Axe = 2 Log + 3 Stone, **The Pick of Destiny = Stone Pickaxe + Stone Axe + 3 Mana Crystal Ore**.
- **Art (`Tools/Blender/build_props.py` → `Voxelforge > Build Props & Items`).** One 1024² painted atlas plus a glow map, all drawn by code. Models: 3 tools, The Pick of Destiny, torches (floor + wall), the workbench, and the **M.G.R.S.** for 3.6. Each is 97-384 triangles.
  - `FoliageImportSettings` now covers `Art/*/Props/` too. Without it, Unity turns FBX models half way round instead of baking the axes.
  - Re-running *Build Foliage Decorations* keeps the prop decorations.
- **Ghost block:** props show their model (the `_PROP_MODEL` variant of `Voxelforge/Block Ghost`) standing on the cell's floor. **Drops:** models are fitted to drop size and spin around their middle.
- **Phase 4 weapons and items are our own, inspired by classic RPG kinds and rarities, with no Blizzard IP** (names, looks or lore), same as the races in 4.7 (asked for 2026-10-28).

### Deferred on purpose
- Wall torches pick their wall by the rule above, not by the face you aimed at: the mesh gather job only copies block IDs. Storing facing in the voxel state would fix it later.
- The swing is a simple turn of the tool in the hand, not an arm animation. A real swing animation comes with 4.x combat.
- A wooden axe, tool wear, and the M.G.R.S. itself (3.6).
- Torch light inside the voxel lighting (`Voxel` light bits, `lightEmission` 14 is already set): the real point lights do the job for now.

---

## D-017: Saving the World (Seed + Deltas + Player Profiles)
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **A save is only what can't grow back:** the `WorldSeed`, `TerrainSampler.GeneratorVersion`, the `VoxelDeltaLog` (D-014) and a `PlayerProfile` per player. Everything else regenerates from the seed.
- **One binary file per world:** `persistentDataPath/Saves/<world>/world.vfsave`. The editor uses `Saves-Editor/`, so play-testing in the editor never touches a build's save.
  - Layout: `VFSV`, an `int` format version (1), then a **GZip** body: generator version, seed, save time, deltas, profiles and an `END!` marker that catches files cut short. The version sits outside the compressed part, so a newer save is refused before decoding.
  - **Safe writes:** into `world.vfsave.tmp`, then `File.Replace` swaps it in and keeps the old save as `world.vfsave.bak`. A damaged save falls back to the `.bak`; if that fails too, the bad file is moved aside (`.damaged-<time>`) and a new world starts. A save from a **newer** version is never overwritten: the game plays without saving.
- **Deltas (`DeltaLogCodec`, Voxels assembly, so 7.2 can send it to joining players):** per chunk, its coord and its blocks sorted by local index, each as a varint index gap + `Current` (id, state) + `Generated` (id, state).
  - Saving `Generated` too means the log is rebuilt exactly at load (`VoxelDeltaLog.Restore`) without generating anything, and net-change logic keeps working (digging a loaded block back to what was generated removes it).
  - When a chunk generates, `ReapplyDeltas` compares the generated block with the saved `Generated`. A mismatch means the generator changed: one warning, `ChunkGenerationScheduler.MismatchedDeltaCount` counts them, and the edits go back anyway.
  - Light isn't saved (it'll be recomputed when lighting lands). The instigator isn't saved: `PlayerId` is per-session and nothing reads it yet.
  - **Size:** 10,000 edited blocks (a 1,100-block tunnel and a wall) = **865 bytes** compressed, 1.2 ms to encode.
- **Persistent identity: `PlayerUuid`** (Core, a GUID as two `ulong`s). `PlayerId` stays the 1-byte session ID handed out in join order.
  - Each PC keeps one UUID per local slot in `persistentDataPath/local-players.json` (`LocalPlayerIdentities`). Player 1 on this couch is always the same player, in every world.
  - `PlayerJoinService.RequestJoinRpc` carries it, so a Phase 7 remote PC brings its own UUIDs through the same request. The host refuses a missing UUID or one already playing. (The host trusts the UUID; fine for friends' co-op, and Steam/EOS IDs can replace it in 7.1.)
- **Profiles:** UUID, name, feet position, yaw and the bag's slots in order (empty slots too, so the block picker order stays). Phase 4 adds class, race and stats.
  - A returning player's spawn column is their saved spot. Once its terrain generates they land **exactly** there, facing the same way, unless something now fills that space; then they land on top of that column.
  - `PlayerBlockBag.ServerRestore` fills the bag after the player spawns (a `NetworkList` only syncs once spawned).
  - Profiles of players who aren't playing are kept, so Player 2 skipping a session doesn't lose their stuff.
- **`WorldSaveManager`** (scene, host only, not a singleton) runs at execution order **-200**, before `VoxelWorldManager` (-100), and hands it the saved seed and deltas with `UseSavedWorld`. A saved seed beats the inspector seed and `randomizeSeedOnPlay`.
  - **When it saves:** every 2 minutes, on quit (including leaving Play in the editor), and **F5**. The overlay shows the save state and a toast when it's done.
  - Encoding happens on the main thread, where the world can't change underneath it (about 1 ms); compressing and writing happen on a worker `Task`. Quitting waits for writes to finish.
  - Live players' positions and bags are copied into their profiles every second, so the save on quit has them even if the session is already shutting down.
  - `-newworld` (or the inspector toggle) starts fresh; the old save is replaced, and kept as `.bak`, at the first save. `-world <name>` picks another save.
  - *Voxelforge > Saves > Open Save Folder* and *Delete Editor Saves*.

### Deferred on purpose
- Save slots and a menu to pick or delete worlds: the 6.1 world picker. A pause-menu save button is 6.5; until then, F5.
- Mining damage and drops lying on the ground aren't saved: they're short-lived by design (D-015).
- Who changed each block across sessions (would map `PlayerId` → `PlayerUuid` in the deltas).
- Sending the save to remote players (7.2 reuses `DeltaLogCodec`).

---

## D-016: Building With the Block Bag (Ghost Block and Placement Checks)
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **The host aims for building, like mining (D-015).** `PlayerBuilder` (on the player, beside `PlayerMiner`) sends `RequestPlaceRpc(blockId)` with **only the block type**. The host aims with `PlayerController.ServerInput` and `PlayerAim` and works out the cell itself. **No block position ever comes from a PC.**
  - The reliable RPC goes out after that frame's `SubmitInputRpc`, so the host aims with the frame the player saw.
  - Host steps: owner check, a **0.15 s** minimum between placements, aim, `VoxelPlacement.Check`, the 8 m `InReach`, `PlayerBlockBag.ServerTryTake(block, 1)`, then `RequestModifyVoxel`. If the edit isn't `Applied`, the block goes **back in the bag**.
  - Holding Place puts down another block every **0.25 s**, for quick walls.
- **`VoxelPlacement.Check`** (Voxels assembly, plain C#, tested) returns a `PlacementResult`. The host, the ghost and tests all use it, and 4.5 *Earthen Wall* can too.
  - **Grid snap:** the cell is `hit.Block + hit.Normal`, the open cell against the face you aim at.
  - `NoFace` (the aim started inside a block), `ChunkNotLoaded`, `Occupied` (anything but air, including invisible tree trunks), `BlocksCharacter`, `Ok`.
  - **Characters:** `BodyBounds` boxes for **every player** in `PlayerRegistry`, which `PlayerController` now gets in `ServerInitialize` (no singleton, Law #1). `PlayerController.Bounds` uses the motor's own body on the host, not the smoothed transform.
  - Boxes that only touch don't count (1 mm skin), so you can **pillar-jump**: jump and place a block under your feet.
- **Choosing a block:** `BlockSelection` steps through the bag's **non-empty** stacks in slot order. Slots never move, so the order stays put. When the selected block runs out, it moves to the next one. An empty bag selects nothing.
  - The selection lives on the owner's PC. The host only checks that the bag has the block.
  - Controls: mouse wheel, LB / RB, split keyboard Z / C (left player) and `.` / `/` (right player).
  - `DevPlace` became **`Place`** (same bindings: right mouse, left trigger, E, Enter). New `PlayerButtons.SelectNext` / `SelectPrevious`.
- **Aiming above the head (added 2026-10-28, after the 3.4 play-test):** the orbit camera's `minPitch` went from **10° to −35°**, so the camera can swing under the hero and look up. The `PlayerAim` mapping (`× 1.5 − 30`) is unchanged, so aim now runs from **82.5° up** to 75° down, where it used to stop at 15° up (eye level). Everything reachable before is reached exactly the same way. Camera collision already pulls the camera in when it would dip into the ground.
- **Ghost block:** `BlockGhost` draws the selected block's `BlockItemMesh` in the snapped cell with the new `Voxelforge/Block Ghost` shader (`Materials/BlockGhost.mat`).
  - The shader is unlit and see-through, uses the real block textures, pulses gently and has a team-coloured rim.
  - It turns **red** where a block would go inside a character. Nothing to build on, or an empty bag, hides it.
  - Presentation only, on the owning PC. The team outline still marks the block you'd mine, so both show at once.
  - **Hidden while Mine is held** (asked for in the play-test), so the cracks and the outline are easy to see.
  - **Toggle Ghost** (`PlayerButtons.ToggleGhost`: G, gamepad Y, `'` for the right split-keyboard player) turns it off and on per player. `PlayerBuilder.ghostOnAtStart` sets where it starts. Placing works either way.
- **HUD:** the bag line brackets the selected block in your team colour: `[Dirt 12]  ·  Stone 5`.
- `PlayerVoxelEditor` is now just the target outline. Its dev placement and `RequestModifyVoxelRpc` are gone, and every edit still goes through `VoxelWorldManager.RequestModifyVoxel` (Law #3).
- Blocks placed on top of a drop push it out (`BlockDrop` already did this), and grass tufts under a new block disappear on remesh (decorations need air above them).

### Deferred on purpose
- Turning blocks (Logs lying sideways). `Voxel` has facing bits, but no block uses them yet.
- Tree crowns are meshes, not voxels, so a block placed in leaves clips through them.
- A real hotbar (4.4), and the ghost on remote clients (Phase 6, no world there yet). A remote PC's ghost would only check its own hero; the host checks everyone anyway.

---

## D-015: Mining, Cracks, Block Drops and the Block Bag
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **The host aims for mining.** `PlayerAim` (on `PlayerController.Aim`) holds the aim settings from D-014: a voxel ray from the eyes, 5 m, pitch `× 1.5 − 30`, plus the 8 m host reach check. The owning PC uses it for the outline and reticle. The host runs the same ray with the controls it received (`PlayerController.ServerInput`), so **no block position ever comes from a PC for mining.**
  - **Spherecast:** the voxel ray is exact for one-block targets, so mining doesn't use a sphere. Spherecast targeting arrives with 4.x combat, where targets are creatures.
- **`Mine` replaces `DevDig`** (held, same bindings: left mouse, right trigger, Q / Right Shift in split-keyboard). `PlayerMiner` (host, `FixedUpdate`) calls `VoxelWorldManager.RequestDamageVoxel(coord, miningPower × dt, playerId)` while it's held.
  - `miningPower` = **40/s bare-handed**: Snow 0.25 s, Dirt and Grass 0.5 s, Tree trunk 0.75 s, Stone and Red Rock 1.5 s, Mana Ore 3.75 s. 3.5 tools will multiply it.
- **Sparse damage (as planned in D-003):** `BlockDamageMap` holds `int3 → { Damage, LastHitTime }`, on the host only, and isn't saved or sent.
  - A block nobody hits for **3 s** heals completely (`damageHealSeconds`).
  - Any edit to a block forgets its damage.
  - Damage from several players adds up, and whoever finishes the block is the instigator of the edit.
- **`RequestDamageVoxel`** sits beside `RequestModifyVoxel` in `VoxelWorldEditor`, with results instead of exceptions: `Damaged`, `Broken`, `Unbreakable` (durability 0), `NotSolid`, `NoDamage`, `ChunkNotLoaded`, `NoInstigator`.
  - Breaking calls `RequestModifyVoxel(coord, Air)`, so the delta log, tree felling and remeshing are all the 3.1 code.
- **Cracks:** `BlockCrackRenderer` (created by `VoxelWorldManager` when `crackMaterial` is set) draws a 1.004-scale cube over each damaged block.
  - The shader is `Voxelforge/Block Crack`: unlit, multiply blend, depth offset, fades into fog.
  - **8 stages side by side in one strip** (`Art/Textures/Cracks/BlockCracks.png`), not a texture array. Each stage gets its own material and one `RenderMeshInstanced` call, which keeps the shader tiny. The strip has no mipmaps so stages don't bleed into each other.
  - *Voxelforge > Rebuild Crack Textures* draws seeded, branching cracks that spread further each stage.
- **Drops:** `BlockDefinition` has `drop` (empty = itself) and `dropCount`, copied into `BlockInfo.DropId/DropCount`. `BlockRegistry.Validate` checks the dropped block exists.
  - Air drops nothing, Grass drops Dirt, Tree trunk drops **Log**, and everything else drops itself (ore items come in 5.5).
  - `BlockDropSpawner` (scene, host) listens to `EditApplied`. Each block that turns into air pops out one `BlockDrop`, so a felled tree drops one Log per trunk block.
- **`BlockDrop`** is a NetworkObject with a position-only `NetworkTransform`.
  - **The host moves it:** a small hop, gravity, per-axis voxel collision, and sliding to a stop.
  - The spinning, bobbing 0.25 m mini block is drawn on every PC. Its mesh comes from `BlockItemMesh`, which runs the real `GreedyMesher` on one block, so it uses the block material and looks exactly like the block.
  - **The spawner (host)** merges resting same-type drops within 1 m and pulls drops to the nearest hero within 2 m. It puts them in that hero's bag at 0.6 m and clears drops after 5 min. A full stack leaves the drop for someone else.
- **Block bag:** `PlayerBlockBag` on the player. The host keeps a `BlockStacks` (plain C#, tested) and mirrors only the changed slot into a `NetworkList<BlockStack>`.
  - Slots never move (an empty slot stays at 0), and each type stops at 999.
  - `ServerTryTake` is ready for 3.3 placement, and 3.4 saves the bag in the player profile.
- **HUD (CLAUDE.md Law #5):** `PlayerHud` draws, for each local player, a reticle in their team colour at their aim point (faint when nothing is in reach) and their bag under their hero. Both go through **that player's own camera and view**, so they work merged, split and in the grid. Real HUD windows replace it in 4.4.
- **Log block (ID 10):** bark sides, ring top and bottom, durability 30. Baked by the 1.5 Blender pipeline (`LogBark` and `LogTop` recipes in `Tools/Blender/bake_block_textures.py`).
- The F3 overlay adds `being mined: N   drops: M`.

### Deferred on purpose
- **Remote clients** see drops (NGO spawns and moves them) but not cracks or block meshes, because they have no world yet. Phase 6, same as D-014.
- Tools and mining speed (3.5), ore items and depth tiers (5.5), a mining swing animation, and placing from the bag (3.3).

---

## D-014: How Blocks Change (Message-Driven Edits and the Delta Log)
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **One way in:** `VoxelWorldManager.RequestModifyVoxel(int3 coord, ushort blockId, PlayerId instigator)` is the only public way to change a block (CLAUDE.md Law #3). It forwards to `VoxelWorldEditor`, which is plain C# so tests can drive it. `VoxelWorld.SetVoxel` stays `internal`.
- **Results, not exceptions:** `VoxelEditResult` is `Applied`, `Unchanged`, `ChunkNotLoaded`, `UnknownBlock` or `NoInstigator`.
  - Every edit needs a valid `PlayerId` (Law #1).
  - Edits to unloaded chunks are refused, because we can't know what the generator put there. That also covers the space above and below the world.
- **Edits apply straight away**, after `ChunkMeshScheduler.CompleteGatherJobs` (a gather job may be reading the chunk). One edit costs very little. A batch call for explosive spells (4.3) can come later.
- **`VoxelDeltaLog`:** for each chunk, a sparse map from local index to `VoxelDelta { Current, Generated, Instigator }`.
  - It holds the **net change**, not a history. Editing a block twice keeps one entry with the original `Generated` block, and changing a block back to what was generated deletes the entry. The log only grows with blocks that really differ.
  - The world is `WorldSeed` + this log. Saves (3.4) write it out, and joining players (6.2) receive it.
- **Regeneration puts edits back:** `ChunkGenerationScheduler.DeltaLog` makes every freshly generated chunk replay its deltas before anyone can see it. Digging survives a chunk streaming out and back in (D-010). This resolves the `TODO(3.1)` in `ChunkStreamer`.
- **Chopping trees:** replacing a `TreeTrunk` block with anything else also clears the trunk blocks above and below it, across chunk borders, up to 32. Each cleared block is logged and raised as its own edit. The tree model sits on the bottom trunk block (D-012), so it disappears when its chunk remeshes. Wood drops come in 3.2.
- **`EditApplied` event** on `VoxelWorldManager` (host only), raised once per changed block with before, after and instigator. It's the hook for drops (3.2) and for sending edits to clients (6.3).
- **Networking: `PlayerVoxelEditor`** is a `NetworkBehaviour` on the player prefab.
  - The owning PC calls `RequestModifyVoxel(coord, blockId)`, which sends `RequestModifyVoxelRpc(SendTo.Server)` with a `Vector3Int` and the block ID.
  - The host drops requests from anyone but the owner and anything more than **8 m** (`maxReach`) from the hero's chest, then calls the world with **this player object's** `PlayerId`. The ID never travels in the message, so a PC can't edit in another player's name.
- **Temporary dev controls (until 3.2 and 3.3):** `DevDig` and `DevPlace` actions.
  - **Bindings:** left/right mouse, right/left trigger; in split-keyboard mode Q/E for the left player and Right Shift/Enter for the right.
  - **Aiming (changed after the play-test, so the target follows the camera):** a voxel ray (`VoxelQueries.Raycast`, 5 m) from the hero's eyes (1.5 m) along their camera's yaw and pitch. The orbit camera only looks 10-70° down, so the aim pitch is `camera pitch × 1.5 − 30`: from 15° up (walls and ceilings) to 75° down (the floor at your feet).
  - `PlayerInputFrame.ViewPitch` carries the pitch next to `ViewYaw`, set by `SplitScreenDirector`. In the merged view both players share one camera, so they aim in the same direction from their own heroes.
  - **Dig** removes the block the ray hits. **Place** puts Dirt against the face it hit, unless that block would overlap your own hero. Overlap with other players is 3.3.
  - The owner reads its frame from `PlayerController.OwnerInput`.
- The F3 overlay shows `edits: N blocks in M chunks`.
- **Target outline (asked for in the play-test):** `BlockHighlight` draws 12 thin bars around the block the aim ray hits, in the player's `TeamColor`, using the unlit, double-sided `Materials/BlockHighlight.mat`. It's a world object, so in split-screen each player also sees their partner's outline in the partner's colour. It's presentation only, drawn on the owning PC, and hidden on a PC that has no blocks (remote clients until Phase 6). 3.2 keeps it and points it at the crosshair target.

### Deferred on purpose
- **Sending edits to remote clients** (6.2/6.3). On loopback the host and the couch share one world, so there's nothing to send yet. A client-side handler now would be code nobody runs.
- **A generator change vs. old deltas:** if `GeneratorVersion` changes, a delta's `Generated` block may no longer match. Saves (3.4) record the generator version and decide what to do.
- **Placement validation** (inside other players, floating blocks): 3.3.

---

## D-013: The Playable Demo Build
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **Windows x86_64, Mono scripting backend.** Only the Mono player module is installed. The heavy work (noise, meshing, decoration placement) is Burst-compiled native code either way, so IL2CPP would mostly speed up gameplay scripts. We revisit IL2CPP when we ship to other people or when gameplay scripts show up in the profiler.
- **Graphics: Direct3D 12** (Unity 6's default on Windows), so the GPU Resident Drawer and GPU occlusion culling work the same as in the editor (D-010).
- **Output:** `Builds/Windows/Voxelforge.exe`, git-ignored. It's built from **Voxelforge → Build Windows Demo** (release) or **… (Development)** (profiler attaches). `DemoBuilder.BuildFromCommandLine` does the same in batch mode. Each build writes `Builds/Windows/build-report.txt` with the result, time, size and errors.
- **Player settings:** product and company are both `Voxelforge`, version `0.2.7`, Fullscreen Window at native resolution, PC quality level (vsync off). Logs go to `%USERPROFILE%\AppData\LocalLow\Voxelforge\Voxelforge\Player.log`.
- **Measuring FPS:** `FrameTimeStats` keeps the last 8,192 frame times in a ring buffer. The F3 overlay shows the average and **1% low** (the average FPS of the slowest 1% of frames) over the last 10 s. **F4** saves a snapshot, tagged with camera mode, player count and resolution, to `Player.log` and to `frame-stats.txt`. We judge "120+ FPS" by the average and keep an eye on the 1% low for stutter.
- **Demo hotkeys:** **F12** saves a screenshot to `Screenshots/`. F4 and F12 both show a confirmation along the bottom of the screen for 4 s, even while the overlay is hidden. The screenshot message waits one frame so it isn't in the picture. **Shift+Esc quits** a build (plain Esc still frees the mouse). A real pause menu comes with the 4.4 UI.
- **Where demo files go:** builds write next to the `.exe` (`Builds/Windows/frame-stats.txt`, `Builds/Windows/Screenshots/`). The editor writes to `Builds/Editor/`. Both are git-ignored. `build-report.txt` is only written by the build itself.
- **Smoke test:** `Voxelforge.exe -smoketest <seconds>` logs a stats snapshot and a screenshot after that long, then quits, so Claude can check a fresh build starts cleanly without anyone at the controls.
- **Results (RTX 4060 Laptop GPU, 1920×1080):** split-screen **153 FPS average** (87 FPS 1% low, worst frame 17 ms); one player 252 average (97 1% low); overview 310 average. The average passes the 120 target. The 1% low in split-screen is below 120, so we watch it as more gameplay is added.
- **Build-time settings Unity writes:** URP records which shader features the build uses in `PC_RPAsset.asset` and `UniversalRenderPipelineGlobalSettings.asset`, and the Input System adds our input actions to `preloadedAssets`. We commit these with the build changes.
- **Expected build log noise:** URP strips the post-effect shaders we don't use (depth of field, Panini), and Unity strips the GPU-occlusion *debug* shaders. Both print warnings that are harmless.

---

## D-012: Foliage, Trees and the Decoration Layer
- **Date:** 2026-10-28
- **Status:** Accepted (parts A engine, B art, C tint + corner AO + FPS; all play-tested 2026-10-28)

### Decisions
- **Trees live in the terrain; everything else lives in the mesh job.**
  - Trunks must be real voxels to block and collide, so tree placement happens during **generation** (`TerrainSampler`).
  - Grass, flowers and bushes are placed during **meshing**, from the gathered blocks only. They rebuild whenever their chunk remeshes, so they need **no save data and no network sync**.
- **`TreeTrunk` block (ID 9):** `Solid` + `Transparent`, with no texture. The greedy mesher already skips non-opaque blocks, so the trunk is invisible, yet `VoxelBodyMotor.IsSolid` collides with it. No physics changes were needed. Trees collide on the **trunk only**; heroes walk through leaves.
- **`TreePlacement`** (Burst-safe static, used by the generator and the decorator so they always agree):
  - A **jittered grid**, one candidate per `treeCellSize` (6) cell. Offsets stay inside `cellSize - 1`, so trees are never adjacent.
  - Per biome `treeChance` (Meadows 0.35, Desert 0.03, Red Crags 0.2, Snowy 0.3) decides whether a candidate grows.
  - A tree needs the biome's own surface block on top (not snow above the snow line) and ground that a cave hasn't carved.
  - Trunk height (4-6) comes from `hash(seed, x, z)`.
  - **`GeneratorVersion` -> 2**, and the golden terrain hash was updated on purpose.
- **`SpawnPointFinder`** only accepts opaque solid ground. If the column holds a trunk, it tries up to 3 columns east.
- **`DecorateChunkJob`** (Burst, strict floats) runs after the gather job, **in parallel with `GreedyMeshJob`**; both only read the padded block buffer.
  - **Ground decorations:** one hash roll per open block top (Air above) picks at most one type, weighted by density × biome weight.
  - **Trees:** a tree instance goes on each trunk's bottom voxel, scaled by trunk height ÷ the model's trunk height (5).
  - The biome per column comes from `TerrainSampler.SurfaceBiome(x, z)` (2D climate noise only, cheap).
  - The existing stale-version check throws away decorations together with the mesh (D-004).
- **Data-driven:** `DecorationDefinition` (ScriptableObject) holds the kind, LOD0/LOD1 meshes, material, allowed surface blocks, density, biome weights, scale range, LOD/max distance, shadows and tint source. `DecorationSet` lists them, is validated at startup (every biome that grows trees must have a tree type), and builds the Burst lookups.
- **Instances are 32 bytes** (`DecorationInstance`: position, yaw, scale, type, RGBA8 tint, random). Scale 0 marks an empty slot.
- **`DecorationLayer`** keeps one CPU array per type. `InstanceRangeAllocator` (first-fit, merges freed ranges, grows by doubling) gives each chunk a range. Only dirty ranges are uploaded, or the whole buffer after it grows.
- **`DecorationRenderer`** (plain C#, owned by `VoxelWorldManager`, no `Camera.main`):
  - One `GraphicsBuffer` per type.
  - On `RenderPipelineManager.beginCameraRendering`, **for each camera**, `DecorationCull.compute` tests distance and frustum and appends visible indices into near (LOD0) and far (LOD1) lists. `CopyCount` writes the counts into indirect args, and `Graphics.RenderMeshIndirect` draws them for that camera only.
  - Shadow-casting types (bushes, trees) use a 12 m frustum margin so shadows from just off-screen still land.
  - Streaming (D-010) comes for free: chunks add and remove their ranges when they mesh or are released.
- **`Voxelforge/Foliage` shader** (URP): PBR lighting, alpha cut-out, double-sided, optional up-facing normals for grass cards.
  - **Wind:** global `_VF_Wind` (direction, strength, speed, set on `VoxelWorldManager`). Each vertex bends by its sway weight from vertex colour alpha (0 at the root, 1 at the tips), with a per-plant phase so fields ripple. Presentation only.
  - **Distance fade:** a dithered screen-door fade from 80% to 100% of the type's max distance, which also works in shadows. It only drops pixels while actually fading.
  - Passes: ForwardLit, ShadowCaster, DepthOnly (SSAO reads depth, D-009).
- **Placeholder art** (part A only) came from a placeholder builder; part B replaced it.
- **Foliage art (part B)** is built by `Tools/Blender/build_foliage.py` (`build_all()`) into `Tools/Blender/Foliage.blend`:
  - **One 2048² atlas** (`Art/Textures/Foliage/Foliage_Atlas.png`), painted by code with numpy brush strokes: grass and flower cards, leaf/needle/dry-leaf clumps with alpha, opaque leaf "mass" areas, and two barks that wrap round a trunk. Every plant uses one material and one texture, because the renderer draws one submesh per type.
  - **10 models**, one FBX each in `Art/Models/Foliage/`, holding `<Name>_LOD0` and `<Name>_LOD1` (Unity's LOD naming needs both): GrassShort, GrassTall, FlowerYellow/Blue/Red, Bush, DryShrub, OakTree, PineTree, and **ScrubTree** (new, for Desert 1 / Crags 0.6, replacing oaks in the desert).
  - **Crowns** are noise-bumped ellipsoid blobs plus leaf cards tilted every which way. Blob UVs project the direction onto a disc, so they have no seam. **Custom normals point out of the whole crown**, so each tree lights like one soft ball. Pine tiers are ragged cones mapped from above with radial needles, plus hanging needle cards.
  - The export bakes Unity's Y-up into the vertices (`bake_space_transform`), because the renderer uses raw meshes with no GameObject transform. Vertex colours are exported linear: RGB = shading, A = sway.
- **`FoliageImportSettings`** (AssetPostprocessor) keeps the imported normals and colours, imports no materials or animation, and gives the atlas alpha-is-transparency plus **mip maps that preserve alpha coverage**, so far leaves don't thin out.
- **`FoliageDecorationBuilder`** (*Voxelforge -> Build Foliage Decorations*, renamed from the part A placeholder builder) makes 5 materials (Grass, Flowers, Shrub, Bush, Tree) on the atlas, the 10 decoration assets and the set. Ground densities add up per block top (0.4 short grass, 0.07 tall grass, 0.045 flowers, 0.02 bushes).
- **Back-face normals:** new `_BackFaceFlip` (0 for crowns, which keep their outward normals on both sides). A card's back face mirrors only the sideways part of its normal. **Never blend a normal toward its own opposite:** at 50% it is zero, `normalize` gives NaN, and bloom turns each NaN pixel into a white orb. That happened with the dry shrub (`_NormalUp` 0.5) in the play-test. Normals now use `SafeNormalize`.

- **Biome tint (part C):**
  - `BiomeSettings` gains `grassTint` and `foliageTint` (Meadows white, Desert dry yellow, Red Crags autumn gold/orange, Snowy cool blue-green). Each colour is 0-1, so a tint can only darken.
  - `BlockDefinition.biomeTint` (`BiomeTint`: None / Grass / Foliage, which replaces `DecorationTint`). Only Grass uses it for now.
  - `TerrainSampler.TintAt(xz, tint)` blends the biome colours with the same climate weights that blend the terrain's shape, so colours fade smoothly across borders.
  - **Blocks:** `ChunkTints` caches the colour at a chunk's 33×33 corner columns, worked out only when a tinted face needs it. The mesher writes it into vertex colour RGB of textured faces; untextured faces keep their MapColor. `ChunkMeshScheduler.SetBiomeTint` turns it on.
  - **Decorations:** the decorator writes the tint at each plant into `DecorationInstance.Tint`. Flowers use Grass tint and the dry shrub uses Foliage.
  - **Shaders** (`BiomeTint.hlsl`) multiply the tint only into the green parts of a texture: `smoothstep(0, 0.06, g - max(r, b))`. Dirt on grass block sides, bark, sand, snow and petals are untouched, so no separate masks are needed.
- **Corner AO (part C):**
  - The gather job reads all **27 chunks** (`GatherSource` holds a read-only pointer, because a job can't hold 27 NativeArrays), so the padding's edges and corners are filled. The scheduler's promise doesn't change: chunks are only edited or freed after `CompleteGatherJobs`.
  - `VoxelWorld.CreateChunk` dirties all 26 neighbours. `SetVoxel` on a chunk edge or corner also dirties the diagonal chunks (up to 8 in all). The stale check and the wait-for-incoming-neighbours check cover all 27.
  - `GreedyMesher.CornerOcclusion`: the classic 0-3 per corner (two sides + diagonal; two sides blocked = 0), packed 2 bits per corner. Faces merge only when block **and** all 4 corner values match. When the 0-2 diagonal joins the brighter corners, the quad starts one corner later so it splits through the dark one.
  - AO goes in vertex colour alpha (0, 85, 170, 255), so the 40-byte vertex didn't change. `VoxelBlock.shader` darkens albedo by `_VertexAOStrength` (0.5): albedo rather than just ambient occlusion, for the classic soft-block look in sunlight too.
- **Camera collision fix (found in the part C play-test, extends D-011):** split-screen's `SplitFocus` moves each camera's look-at point towards the other player, so it can sit inside or behind a hill. `PlayerCameraRig.SetPose` now takes a **pivot** in open air (the player's head; for the shared view, the midpoint only if both players can see it). It keeps the line from pivot to camera clear at the centre and the 4 near-plane corners (5 rays), snaps in and eases out along that line. `VoxelQueries.Raycast(..., opaqueOnly: true)` lets cameras look through invisible tree trunks.

### Measured
- **Part A:** ~25k decorations in 193 chunks around spawn, 766 trees. Overview camera ~181 FPS.
- **Part C, play-tested by the users with the Game view focused:** two players on one screen 190-230 FPS; **split-screen 124-139 FPS (target 120+, passed)**.
- A busy chunk greedy-meshes in ~2.4 ms with AO (was ~3 ms). 168/168 EditMode tests pass.

### Deferred on purpose
- **Chopping trees (3.1/3.2):** removing a trunk voxel through `RequestModifyVoxel` should clear the whole trunk column and its tree visual, and drop wood.
- **Herb nodes (5.5)** build on `DecorationDefinition`.
- Water and other transparent blocks aren't part of 2.6.

---

## D-011: How Heroes Move and Look
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested 2026-10-28)

### Decisions
- **Auto-step lives in the host's motor** (`VoxelBodyMotor`, `MotorSettings.StepHeight = 1`).
  - **Up:** a body that was on the ground at the start of the tick, walking into a block whose top is ≤ 1 block above its feet, climbs onto it if the box fits there (head room is checked). A 2-block wall still blocks, and a jumping or falling body never steps.
  - **Down:** a body that was on the ground and walks off a ledge of ≤ 1 block snaps straight down and stays `Grounded`, so stairs never flicker into a fall animation. Taller drops fall normally.
  - `VoxelBody.StepOffset` reports each tick's step for tests and debugging.
- **Heroes face where they walk:** the host turns the player's yaw toward the world move direction at 720°/s, the short way round (`MoveDirection.TurnToward`), and blends it between ticks like position. The existing `NetworkTransform` already syncs yaw.
- **Animation is presentation only.** No `NetworkAnimator`. Every PC works it out from the synced transform plus one new `NetworkVariable<bool>` (`Grounded`, written only when it changes).
  - `HeroAnimator`, on the `Visual` child, feeds the Animator `Speed`, `VerticalSpeed` and `Grounded`.
  - It also **glides over steps:** while grounded, it soaks up any height change of the root and eases it out (about 0.12 s). Cameras follow `PlayerController.VisualRoot`, so they glide too.
- **Foot IK on blocks** (`HeroFootIK`, `OnAnimatorIK`, IK Pass on): block ground comes in whole metres, so each foot either plants on ground within 0.6 m, or, over a drop it can't reach, **slides back toward the body until it's on the block edge** instead of standing on air. The pelvis drops when a foot has to reach down. Weights fade while swinging, running fast or airborne.
  - It reads blocks through `PlayerController.World`. Only the host has one for now; a remote PC skips IK until it builds its own world (6.2).
- **`VoxelQueries`** (new, plain C#, unit-tested): `TryGetGroundHeight` for foot IK, and a grid DDA `Raycast` that returns the block, face normal and distance. **3.2 mining reuses the raycast.**
- **Camera collision** (D-009 deferred it here): each camera raycasts from its focus back to its wanted spot. It snaps in to 0.3 m in front of any block right away, then eases back out.
- **The hero art pipeline** (Roadmap 2.5 Creative):
  - **"The Wayfarer"**, our own design: a hooded, bearded traveller with pauldrons, short cape, chunky boots and gloves, ~10.8k triangles, 1.8 m tall.
  - Built by `Tools/Blender/build_hero.py` into `Tools/Blender/Hero.blend`, so it can be rebuilt and tweaked.
  - One 1024² albedo with painted shading baked in (AO, light from above, brush noise, worn edges), like D-006's blocks. No normal map: classic-MMO characters read fine without one.
  - **Two material slots**, both URP Lit with the same texture. `Hero_TeamCloth` (tunic, hood, cape) is painted pale, and each player gets their own material copy in their slot colour. That's a copy rather than a property block, so the SRP Batcher keeps working.
- **Mixamo through a stand-in.** Mixamo's auto-rigger fails on the real hero ("Unknown error while generating motion"), because it's ~60 overlapping pieces.
  - `build_rig_proxy()` voxel-remeshes it into one closed shell, which Mixamo rigs (Skeleton LOD: No Fingers).
  - `apply_mixamo_rig()` copies each vertex's weights from the nearest point on the rigged stand-in (4 bones max, normalized) onto the real hero, and `export_hero_fbx()` writes `Art/Models/Hero/Hero.fbx`.
  - Mixamo clips are downloaded as `Animations/Hero@Idle|Walk|Run|Jump|Fall|Land.fbx`, all In Place. Mixamo content is royalty-free for games.
- ***Voxelforge → Rebuild Hero Animator*** (`HeroAnimatorBuilder`) sets everything up from those files:
  - **Imports:** Humanoid everywhere. Each animation has its **own** avatar, because copying the hero's fails: Blender's export roots the bones differently. Turning and height are baked into the pose, but forward travel is **extracted as root motion and ignored** (`applyRootMotion` off), because the motor moves the player, not the clip. Baking XZ into the pose would let a clip that wasn't downloaded In Place carry the body away and snap it back each loop. That happened in the first play-test: Mixamo's Run travels 2.5 m per cycle and drifts left.
  - The controller asset is emptied and refilled, never deleted, so the prefab's link to it survives a rebuild.
  - **Materials** and their remaps.
  - **`Hero.controller`:** a Locomotion blend tree on `Speed` (Idle 0, Walk 2, Run 5 m/s), plus Jump / Fall / Land.

### Deferred on purpose
- Cape and hood are skinned to the spine like the rest. Cloth physics or secondary motion can come with the 2.6 wind work.
- A walk/run toggle: the motor only has one speed (5 m/s), so heroes always run unless the stick is half-pushed.
- Races with bigger rigs (Thunderhoof, 4.6) reuse this pipeline: build, stand-in, Mixamo, weight copy.

---

## D-010: Streaming Terrain Around Every Player, and Cheap Extra Cameras
- **Date:** 2026-10-27
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **Viewers, not cameras.** A `ChunkViewer` is a point the world must exist around plus a radius. Anything implementing `IChunkViewerSource` can be one. The **host** loads terrain around every player (local or, in Phase 6, remote), because its physics needs the blocks (CLAUDE.md Law #2). Client-side streaming of what a remote PC *draws* comes with 6.2 world sync.
- **`ChunkLoader`** (scene component on `VoxelWorld`, not a singleton) holds the registered sources. `PlayerRegistry` registers each player it spawns. Destroyed sources are dropped automatically.
  - Before landing, a player reports its **spawn column**, so the ground under the spawn point generates first. The spawn no longer has to be inside a fixed box.
  - **Fallback viewers** are only used while no other source has a position. The `Spawn Anchor` (radius 6) is one, so the overview camera shows a world before anyone joins.
- **`ChunkStreamer`** (plain C#, unit-tested) works in whole **columns** (all 8 layers, 256 blocks):
  - **Load:** columns within a viewer's radius (default **8 chunks = 256 m**, a circle), **nearest first, top layer down**.
  - **Unload:** once a column is more than radius + **2** chunks from *every* viewer. The margin stops terrain flickering when someone walks back and forth over a border. At most **4 columns per frame** unload, so a teleport can't spike a frame.
  - It only re-plans when a viewer enters another column, so standing still costs nothing. Each re-plan clears the generator's queue (`CancelQueued`) and re-requests in the new nearest-first order. Jobs already running still finish; their columns unload afterwards if no longer wanted.
- **Unloading a chunk** (`VoxelWorld.RemoveChunk`) frees its memory and does **not** remesh its neighbours. Their faces toward the gap stay hidden, which is invisible at the fog line and saves a ring of remeshes every step. Mesh jobs for removed chunks are thrown away by the existing stale check (D-004).
- **Chunk GameObjects are pooled** (D-004 deferred this): an unloaded chunk's renderer is hidden and later moved to a new chunk.
- **Linear fog, 150 → 235 m**, sky-tinted, so the streaming edge fades out instead of popping. The overview camera moved in to ~185 m from spawn so it isn't fogged out.
- **GPU Resident Drawer** (Instanced Drawing) with **GPU occlusion culling** on the PC render pipeline asset. Chunks are submitted once and culled on the GPU for every camera, instead of each split-screen camera culling and submitting ~900 renderers on the CPU. The block shader got DOTS instancing in all 4 passes. Graphics settings keep **all BatchRendererGroup variants**, or builds would strip them (needed for 2.7).

### Measured so far (editor, not focused, so FPS isn't meaningful yet)
- Spawn area: 113 columns / 904 chunks, 0 errors. Teleporting the anchor 1 km: the old area unloaded completely, the new one loaded, and pooled renderers were reused.
- All 121 EditMode tests pass.
- Each chunk has its own mesh, so the GRD can't merge chunks into fewer draw calls. Its win is GPU culling (frustum + occlusion) per camera. **Play-test (2026-10-28): split-screen passed.** If FPS ever falls short later, the next suspects are per-camera shadow cascades, then our own `BatchRendererGroup`.

### Deferred on purpose
- **Edits + unloading (3.1/3.4):** an unloaded chunk regenerates from the seed. When edits exist it must re-apply its `VoxelDeltaLog` entries on load (`TODO(3.1)` in `ChunkStreamer.RequestColumn`).
- Fog colour is hand-picked to match the procedural sky. Tune it with the 2.6 art pass.

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
