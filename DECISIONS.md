# Voxelforge - Decision Log

> A record of the big choices we make and *why*, so future-us (and Claude) don't re-argue them.
> Newest decisions go at the top. Status is one of: **Proposed**, **Accepted**, **Replaced**.

---

## D-030: Races, the Thunderhoof, Racials & the Thunderhoof Plains
- **Date:** 2026-10-31
- **Status:** Accepted (play-tested 2026-10-31: Thunderhoof locomotion, Hoofquake stun, Second Wind, Hearthbound and the Plains mesas)

### Decisions
- **Races are data, like classes.** `RaceDefinition` holds the rules: bonus health, mana and regen, a racial passive and a racial ability. `RaceBook` lists them, with permanent `RaceId`s.
  - Presentation lives in `RaceLook` / `RaceLookBook` (Roadmap 4.2): the body model and avatar, the team-cloth slot, how the worn bag and held item sit, and footstep pitch and volume. Crests and portraits live in `HudSkin`, like class crests.

  | Race | Bonus | Passive | Racial (slot 9) |
  |---|---|---|---|
  | Wayfarer (the 2.5 hero) | +10 mana | *Hearthbound*: Home hearth in 1 s instead of 2 | *Second Wind*: heal 25% of max health, 2 min |
  | Thunderhoof | +20 health | *Herb-Sense*: +50% herb yield (read by 5.5) | *Hoofquake*: 6 m stomp, 20 damage, 2 s stun, 45 s |

- **Class and race add up.** `RaceRules` (plain C#, tested) combines them: pools are class plus race, and the bar is the class loadout with the racial in **slot 9** (pad: B tap).
  - `PlayerClass.ServerReapply` runs when the race changes.
  - `RaceRules.HerbYield` is written and tested now, so Roadmap 5.5 only has to call it.
- **Race is chosen once, on first join.** There's no character-creation screen until Phase 6.
  - `PlayerRace` (host-authoritative) syncs the race and a `HasChosen` flag. The host accepts one `ChooseRpc` while unchosen, then locks it.
  - `PlayerRaceMenu` opens "Choose Your People" (`RacePickerView`) in the player's own view (Law #5) as soon as they land. It can't be closed until the host agrees, and it keeps the inventory and class windows shut meanwhile (`PlayerController.ChoosingRace`).
  - *Voxelforge → Debug → Reset Race Choice* lets everyone choose again in Play. It calls `RaceDebug`, because the editor assembly can't see Netcode types.
- **Save format 5:** each profile gets its race and chosen flag. **v1-v4 saves load unchosen**, so existing heroes pick once on their next join (Wayfarer until then).
- **Two new deliveries.**
  - `Quake` (`PlayerStaff.ServerQuake`): hits targets round the hero's middle with falloff and stuns them. No crater.
  - `SelfHeal` (`PlayerSupport.ServerSelfHeal`): Heal Amount is read as a share of max health. That's a documented meaning of the existing field rather than the separate flag the plan had.
- **Stuns** stop a `SparringDummy` sparring until they wear off (plus 0.5 s), through `CombatTargets.ServerStun`, where Phase 5 creatures will hook in.
  - Heroes are never stunned: no friendly crowd control.
  - A new `TargetStunned` cue shows "STUNNED!" combat text. A stomp's impact cue shows no block-damage number.
- **Every race keeps the 1.8 m collision box.** The Thunderhoof only *looks* bigger (about 2.05 m, horns to about 2.17 m), so all races fit 2-block tunnels. Its horns poke into the ceiling there; the 3.7 camera fade covers close views.
- **The Thunderhoof is rigged without Mixamo** (D-011 had planned to reuse the Mixamo pipeline).
  - `build_thunderhoof.py` appends the Wayfarer's Mixamo skeleton from `Hero.blend` and moves its joints to the Thunderhoof's proportions: hips 0.90 m, shoulders ±0.46 m, a 2.5 m arm span. Bone names and hierarchy stay the same, which is all Humanoid retargeting needs.
  - The body is modelled *around those joints*: horns, nose ring, hump and mane, a team-cloth kilt and mantle, hooves and tail.
  - It's weighted by melting it into one shell (voxel remesh plus decimate), letting Blender's automatic weights skin that, and copying each vertex's nearest weights back (4 bones, normalized). Horns, eyes and nose are rigid on the Head; the tail on the Hips.
  - It's baked with `build_hero`'s painted-shading nodes and exported to `Art/Models/Thunderhoof/`. 14.2k triangles: over the 12k planned, and the Wayfarer is 10.8k.
  - *Rebuild Hero Animator* now loops over `RaceModels`, giving each body its own Humanoid avatar and materials. They all share `Hero.controller`.
- **`HeroBody` swaps bodies on every PC.** The Animator and `HeroFootIK` stay put; only their children (skinned mesh and skeleton) are replaced.
  - Before the swap, anything hung on a bone that isn't itself a bone (held item pivot, worn bag) is lifted off. The old body is unparented before `Destroy`, so `Rebind` can't find it.
  - Afterwards everything is re-hung on the same Humanoid bone, the team cloth is re-tinted (`PlayerController.SetTeamColorRenderer`), and `BodyChanged` fires. `HeldItemView`, `BackpackView` and `CastingView` re-find their bones and apply the race's fit; `PlayerFoley` scales footsteps.
- **The Thunderhoof Plains** are a fifth biome at climate (0.3, −0.05): base 70, ±4 rolling ground, a golden grass tint, rare scrub trees.
  - **Mesas** are a new per-biome option: a second noise lifts flat tops by `mesaHeight` (14 here) with a smoothstep edge.
  - `TerrainColumn.Mesa` (0-1) puts **level RedRock/Sandstone bands** (3 blocks each) in the cliffs and on cliff tops. Only a mesa's flat top (≥ 0.9) keeps grass; the play-test check found grass on every cliff step looked like stairs.
  - With mesas off, terrain was byte-identical: the golden chunk hash held before the biome was added.
  - **`GeneratorVersion` 3**, new golden hash. Unedited ground near the new climate point looks different in old worlds; edits replay as usual.
  - Plains weights for tufts, tall grass, yellow flowers, bushes and scrub trees now live in `FoliageDecorationBuilder`. Step D had set them on the assets, where a rebuild would wipe them.
  - A new **PrairieGrass**: the tall grass's seed-head cards, 1.3 blocks tall, the whole tuft bent one way. It needs no new atlas space, since the gold comes from the biome tint.
  - The debug overlay shows the biome under each player (`VoxelWorldManager.BiomeNameAt`).
- **Art and sound:**
  - Hoofquake and Second Wind icons and the two race crests in `build_spell_icons.py`.
  - Race portraits from `build_race_portraits.py`: the real bodies, arms lowered, comic ink hull, team cloth in a neutral red. The rigs' leftover Mixamo action has to be cleared, or the render puts the T-pose back.
  - Hoofquake (two stamps, rolling boom) and Second Wind (deep breath, warm chime) synthesised in `SfxBuilder`.
  - Thunderhoof footsteps at 0.78 pitch, 1.3 volume.

### Measured
- Choosing Thunderhoof as a Geomancer took max health 90 → 110 and put Hoofquake in slot 9; the window closed when the host agreed.
- The Plains cover about 14% of the world; the nearest mesa top to World1's spawn is at (456, 83, 216).
- Tests: 401 → 422.

### Deferred on purpose
- The race window's "WAYFARER" shows a blank box for its "W". The Bangers SDF font asset has the glyph, so it's likely its dynamic atlas, which had uncommitted changes from before this work. Not touched here.
- `build_hero.py`'s `limb()` can squash a tube along the wrong axis when it points straight down (an arbitrary half-turn). The Thunderhoof builds its kilt bottom-up to avoid it. The Wayfarer's tunic skirt may be slightly affected; it hasn't been rebuilt.
- Herb-Sense does nothing until Herbalism (5.5); a race-specific starting area or spawn; changing race after choosing (a barber or rebirth feature, later).
- Hoofquake can't stun Phase 5 creatures yet (there are none); it can only stun dummies.

---

## D-029: Classes, Health, Training Dummies, Zones, Totems & Earth Magic
- **Date:** 2026-10-30
- **Status:** Accepted (steps A-E play-tested 2026-10-30 by both players; step F's icons and sounds waiting on their play-test)

### Decisions
- **Heroes have health, but no fall damage.** `HeroHealth` (plain C#, tested) holds health, a shield that soaks up damage first, and 2/s regeneration after 5 s out of combat. `PlayerHealth` runs it on the host and tells every PC what happened as `HealthEvent`s (Damaged, Healed, Shielded, Absorbed, Incoming).
  - Kian jumps off mountains on purpose, so falling never hurts.
  - Nothing in 4.6 can knock a hero out: every hit passes a floor (10% for sparring). Knock-outs come with Phase 5 monsters.
  - A red health bar sits over the mana bar, with the shield as a gold strip behind it.
- **Training Dummy (block 15)** gives spells something to hit before Phase 5. It's a blast-proof Axe prop (3 Log + 2 Dirt at a Workbench).
  - `CombatTargets` (host, scene) is where Phase 5 creatures will join. In 4.6 it finds dummies by scanning nearby blocks, only when something needs a target.
  - The plan had a separate `TrainingDummies` component. It was folded into `CombatTargets`, since dummies are the only target for now.
  - `SparringDummy` (plain C#, tested): 500 health. Emptying it topples it ("TIMBERRR!") and it springs back to full. Left alone for 10 s, it's forgotten.
  - While being hit it lobs a hay sack every 3 s at whoever hit it last (8 damage, within 20 m), so heals and shields have something to undo.
- **Spells hit a target's whole body.** Play-test: aiming at the dummy's chest flew over its one block and dug a pit around it.
  - Flights and slams now test a 1 × 1.8 m box per target (`CombatTarget.RayHitsBody`) and report it as a hit on the target's block.
  - A spell that hits a target spends itself on it: no crater. The target struck takes full damage; others in a blast take less with distance.
  - Craters were also made smaller: Arcane Orb 3 → 2 m, Pyroblast 4 → 3 m, EarthShatter 4.5 → 3.5 m.
  - Each target hurt gets a `TargetHit` cue (damage and health left). `TargetHealthBook` and `TargetBarsView` draw a bar over each dummy being fought, in each player's own view (Law #5).
- **Classes are data.** `ClassDefinition` (health, mana, regen, loadout) is listed in a `ClassBook`, with permanent `HeroClassId`s:

  | Class | Health / Mana / Regen | Bar |
  |---|---|---|
  | Adventurer | 100 / 100 / 10 | Arcane Orb, Pyroblast, EarthShatter |
  | Runeforged Paladin | 150 / 80 / 8 | Rune Hammer, Bulwark Rune, Hallowed Forge |
  | Geomancer Mage | 90 / 130 / 12 | Stone Spear, EarthShatter, Rising Pillar |
  | Shaman | 110 / 110 / 10 | Earthen Wall, Wellspring Totem, Thunderhead Totem |

  - `PlayerClass` is host-authoritative: the owner asks, and the host swaps the bar, pool and health, keeping the same share of each. Changing class is free for now.
  - Colours and crests are looks, so they live in `HudSkin`.
- **A class window per player** (`ClassPickerModel`/`View`, `PlayerClassMenu`), with a card per class showing its crest, colour, pools, blurb and three spells.
  - Open it with `K` (`]` on the right-hand keyboard). Gamepads have no free button, so they press LT in the inventory window.
  - `PlayerController.WindowOpen` now covers both windows for the hero freeze, free cursor and HUD.
- **Save format 4:** each profile's class, plus Earthen Rock timers as seconds left. v1-v3 saves load as Adventurers.
- **New deliveries:** `Shield`, `Zone`, `Pillar`, `Wall`, `Totem`. Walls and totems need ground in range before any mana is spent (`NoTarget`).
  - **Shield** (`PlayerSupport`): on the partner nearest the aim (20 m, 12° cone, `SupportRules`), else the caster. `ShieldView` draws a gold bubble with rune rings.
  - **Zone and Totem** (`AbilityZone`, a server-owned network object): `ZoneRules` pulses once at once, then every interval. A pulse can:
    - heal heroes in it;
    - boost their mana regen (`PlayerCaster.ServerBoostRegen`, renewed each pulse);
    - strike the nearest target (reported on the caster's cue channel).
  - Totems stand on a block (`EarthRules.TryFindTotemSpot` drops from a cliff face to the ground), end if that block goes, and are one of each kind per caster. `AbilityZoneView` draws the ring, motes, light, the totem model rising and lightning.
  - **Pillar and Wall** (`EarthWorks`, host, scene): every block goes through `RequestModifyVoxel` (Law #3), skipping full cells and heroes.
- **Earthen Rock (block 16)** is raised earth: packed slabs with glowing amber cracks.
  - It drops nothing, and craters break it.
  - It crumbles back to air after 30 s in the name of the new **`PlayerId.World`** (255), since its caster may have left. The registry never hands that ID out.
  - Mined earth is forgotten, and crumbled earth leaves nothing in the save.
- **Looks:** `AbilityLook` gained a core mesh and spin (the tumbling Rune Hammer, the Stone Spear), a zone model and ring brightness (totems), and a pulse sound.
  - Blender models in `build_props.py`: Training Dummy, Rune Hammer, Stone Spear, two totems.
  - New icons and four class crests in `build_spell_icons.py`; *Build Abilities* now sets new icons to import as sprites.
  - The Earthen Rock texture is in `bake_block_textures.py`.
  - 15 new synthesised sounds in `SfxBuilder`, including the Hallowed Forge hum loop, lightning, a crumble and a hay thwack. `WorldFoley` plays zone and crumble sounds, `PlayerFoley` the shield sounds.
  - The Chest, Training Dummy and Earthen Rock got surface sounds; the Chest had none since 4.5.

### Measured
- Hallowed Forge healed a Paladin 70 → 100 over its 6 pulses. Wellspring healed 60 → 97 in about 8 s and raised a Shaman's mana regen from 10 to 15 a second.
- Thunderhead hit a dummy for 25 every 2 s. A Rune Hammer at chest height did 45 and broke no blocks.
- A Rising Pillar saved mid-way came back with about 17 s left and crumbled on time. Its blocks left the edit log.
- Tests: 345 → 401.

### Deferred on purpose
- Knock-outs, respawning and hero-vs-hero damage: Phase 5.
- Talents, and rearranging or saving custom action bars.
- Remote PCs seeing Earthen Rock and Training Dummies: like every block, they only live on the host until Phase 7. Zones, shields, health and dummy bars already reach every PC.
- A dust puff for crumbling earth (it has a sound, but no particles yet).

---

## D-028: The Generous Bag, Wearable Bags, Loot Bags & Storage Chests
- **Date:** 2026-10-30
- **Status:** Accepted (play-tested 2026-10-30, both players sharing a chest)

### Decisions
- **The bag is real slots, owned by the host.** `ItemStacks` (plain C#, tested) is a fixed list of slots, each empty or one stack.
  - Every player starts with **40 slots**. Up to **4 worn bags** (`EquippedBags`, memcpy, a `NetworkVariable`) add more, up to 255 slots, because slot numbers go over the network as a byte.
  - Stack sizes come from the item's category (`ItemRegistry` / `IItemRules`): **999** for blocks and materials, **20** for consumables, **1** for gear.
  - New items top up matching stacks first, then fill the first empty slot, so nothing jumps around until **Auto-Sort**. Auto-Sort merges stacks and orders them by tab, then by item ID.
  - `PlayerBlockBag` syncs the slots through a `NetworkList<ItemStack>`, sending only the slots that changed. The owner's PC only sends requests (move, sort, equip, unequip, drop, send); the host checks each one and limits how fast they can come.
  - Taking off a bag is refused when its items would have nowhere to go. Crafting swaps a recipe's inputs for its output against the real slots, all at once or not at all.
- **Save format 3** stores each profile's worn bags and adds a Storage Chest section. v1 and v2 saves still load. Saved items that no longer fit are dropped as a loot bag where the player comes back, never lost.
- **One inventory window per player, in their own view (Law #5).**
  - `InventoryModel` (plain C#, tested) holds the tabs (All / Blocks / Gear / Consumables / Materials), the cursor, scrolling, and stick repeat (`NavRepeat`).
  - `InventoryView` draws it in uGUI/TMP like the D-027 HUD: an 8-wide grid that scrolls, 5 rows tall (exactly the starting 40), the four bag sockets, the name of the item under the cursor and hints for that player's controls.
  - While it's open the hero stands still and the window has the controls; buttons still held when it closes are ignored until released, so closing it doesn't also jump. The mouse player gets a free cursor and can point and click.
- **Wearable bags:** the Woven Satchel (+8 slots), Traveler's Pack (+12) and Delver's Rucksack (+16) are Gear items with workbench recipes for now; 5.6 Leatherworking replaces those recipes. `BackpackView` shows the **biggest** worn bag on the upper chest bone on every PC.
- **Send to Partner** gives a stack to the **nearest other hero, wherever they are**. Every PC sees a sack lob across (looks only). What the partner's bag can't take lands at their feet as a loot bag. A target picker by name waits for 6.1 named heroes.
- **Loot bags** (`LootBag` + `LootBagSpawner`, host) hold up to 8 stacks.
  - They share landing physics with block drops (`DropPhysics`), are picked up by walking over them and vanish after 10 minutes.
  - Whoever dropped a sack can't take it back until they've walked away from it, so it doesn't fly straight back in.
- **Storage Chests (block 14):** crafted from 8 Logs anywhere, broken with an axe, **blast-proof** like Home Totems so a partner's spell can't scatter a base's storage. 27 slots, in rows of 9.
  - **Contents live on the host** in `StorageChests`, keyed by the chest block. A chest gets its slots the first time someone opens it. Only chests with something in them are saved, and they load back at startup.
  - **Opening:** Interact while aiming at a chest opens it, else crafting as before (`PlayerCrafter` asks `PlayerChest.OwnerTryOpenAimed` first). The owner sends the chest's position; the host checks a chest is there and in reach with `PlayerAim.InReach`, the same check as block edits. Walking away, or the chest breaking, closes it.
  - **Per hero, `PlayerChest`:** a `ChestLink` `NetworkVariable` says which chest is open, so every PC can open its lid. A `NetworkList` copies that chest's slots to the owner. Every change goes to everyone looking into the same chest, so two players can share one.
  - **Moving stacks:** the same window shows the chest on its own parchment above the bag. Pick up / Put works across both (`ItemStacks.MoveTo`: into, onto or swap). With a chest open, Use becomes **Take / Store**, which moves a whole stack across (`MoveAllTo`). Sort sorts whichever side the cursor is on.
  - **Breaking it:** the host listens to `VoxelWorldManager.EditApplied`, so a chest broken any way spills its contents as loot bags.
- **Chest lids are drawn apart from the body.** The body is a prop decoration like the Workbench, but GPU-instanced props can't move one part of one instance. `ChestLids` (every PC) reads the decoration layer like `PropLights`, gives the nearest chests a pooled lid (URP Lit, `Props_Held`) on the hinge, and swings it open while anyone has that chest open, with a creak and a thump.
- **Art:** `build_props.py` adds `Chest` (whole, for the item, icon and ghost), `ChestBody` and `ChestLid` (origin on the hinge), plus the three bags and the loot sack. Each addition left the atlas's existing areas pixel-identical.

### Trade-offs we accept
- Chests face a random quarter turn, like Workbenches, rather than toward whoever placed them.
- Like every prop and the world itself, chests can only be seen and opened on the host PC until remote players get the world (Phase 7).
- Lids use URP Lit while chest bodies use the props' world shader, so a lid can look slightly brighter than its body in dark caves.

---

## D-027: The Per-Player Comic-Book HUD (Action Bars, Cast Bars, Combat Text)
- **Date:** 2026-10-29
- **Status:** Accepted (play-tested 2026-10-29)

### Decisions
- **uGUI + TextMeshPro for the new HUD; IMGUI stays for the rest.**
  - TMP gives real ink outlines and drop shadows on lettering, and `Image` fill modes give cooldown sweeps for free.
  - One Screen Space Overlay canvas (`CombatHud`) holds a `PlayerHudPanel` per local player. Each panel is fitted every frame to that player's own area (Law #5): their grid cell, their Voronoi side, or their half of a merged view.
  - The area maths moved out of `PlayerHud` into `PlayerScreenArea`, so IMGUI and uGUI always agree.
  - Each panel's bar and combat text sit on their own nested canvases, so ticking cooldowns and floating numbers don't rebuild the frames around them.
  - The reticle, bag line, crafting window and hearth prompt stay in IMGUI `PlayerHud` until 4.5.
- **Action bar slots are host-authoritative.**
  - `PlayerActionBar` (on the Player prefab) shares `ActionBarSlots` (9 ability IDs, memcpy) through a `NetworkVariable`. The host fills it from `Data/Abilities/DefaultLoadout.asset`: Arcane Orb, Pyroblast, EarthShatter; slots 4-9 are empty until 4.6 classes.
  - `PlayerCaster` casts the slot whose button was pressed, looked up on the host. `testAbilities` and the Ability1-3 buttons are gone.
  - The loadout isn't saved yet; that comes with 4.6.
- **Input:** `PlayerButtons` grew to `uint`, with `ActionSlot1-9` in bits 8-16 (`ActionSlotButtons.For(slot)`).

  | Controls | Slots |
  |---|---|
  | KeyboardMouse and KeyboardLeft | `1`-`9` |
  | KeyboardRight | Numpad 1-9 (frees `]`, `-`, `=`) |
  | Gamepad | D-pad up/right/down/left = 1-4; hold B + d-pad = 5-8; tap B alone = 9 |

  - The gamepad chord is the pure `PadSlotChord`. A B tap is held as slot 9 for 0.1 s, because input frames go out every frame but the host reads them on its fixed step.
- **Combat text is built on each PC from what the host already said.** It's never networked (D-025's split). `PlayerCombatText` raises a `CombatTextEvent` for:
  - **spell hits:** the `Impact` cue plus the definition's block damage;
  - **craters:** the `Impact` cue gained `BlocksBroken`, which `PlayerStaff` fills from `VoxelBatchResult`;
  - **mana spent:** the `Released` cue plus the mana cost. Spells under 10 mana stay quiet, so the M.G.R.S. doesn't flood the view;
  - **refusals:** `PlayerCaster.Refused`, an owner-only event that replaced the red IMGUI message;
  - **pickups:** a new owner-only `PlayerBlockBag.PickedUpRpc`.

  Only the local player it's about sees it, in their own panel. Merged views clip to the whole shared picture.
- `CombatTextFeed` (plain C#) owns lifetimes, a 24-label pool per player (the oldest line gives way) and merging pickups of the same item within 0.3 s.
- **Looks stay out of rules:** the spell icon moved from `AbilityDefinition` to `AbilityLook.icon` (Sprite). `HudSkin` holds every sprite, font and colour. The guard test now also covers `PlayerActionBar` and `ActionBarLoadout`, and bans sprites, textures and `HudSkin` in rules classes.
- **Comic-book fantasy art, all rebuildable:**
  - *Voxelforge > Build HUD Art* (`HudArtBuilder`) paints the HUD sprites in code: a parchment panel, wooden ink-outlined slot and bar frames with brass rivets, halftone shading, a glow flash and a starburst.
  - It also makes the TMP font assets with the ink outline and hard drop shadow baked into the material, plus a "Crisp" preset (thin outline, no shadow) for small numbers, which the big style smeared.
  - Fonts: **Bangers** for comic numbers and pop-ups, **MedievalSharp** for spell names. Both are OFL, with their licence files beside them.
  - Spell icons come from `Tools/Blender/build_spell_icons.py` (`SpellIcons.blend`): toon-shaded models with inverted-hull ink outlines over halftone action rays. *Build Abilities* hooks them up by name.
  - TMP Essential Resources were imported (`Assets/TextMesh Pro/`).
- **Crater pop-ups** say a comic word by element: Fire "KA-BOOM!", Earth "KRAKA-THOOM!", Arcane "ZA-POW!" and so on, on a pale starburst.
- **Reticle fix:** the aim ray starts at the hero's eyes, and the reticle used to stop at mining reach (5 m). Past that it floated in mid-air, and the camera above and behind the hero showed it below where spells really land. It now follows the ray out to the longest range of the spells on the bar, or the held item. Mining still uses 5 m and the block outline.

### Deferred on purpose
- Rearranging the action bar and saving loadouts: 4.6 (classes and talents).
- Tooltips, out-of-range tinting and a global cooldown sweep: when there are enough spells to need them.
- Moving the bag, crafting and hearth windows to uGUI: 4.5 (inventory) and 6.3 (settings).

---

## D-026: Craters and Area Spells (Destructive Spell Mechanics)
- **Date:** 2026-10-29
- **Status:** Accepted (play-tested in split-screen 2026-10-29)

### Decisions
- **A crater is just many damage requests.** As D-025 planned, `AbilityImpact.Resolve` turns a hit into a list of `VoxelDamageRequest`s:
  - With no `aoeRadius`, it's the one block that was hit, as before. The Mana Bolt is unchanged.
  - With a radius, it's every block whose middle is inside an ellipsoid around the hit point. `craterFlatten` squashes the ellipsoid: 1 is a round bowl, 0.45 a wide, shallow one.
  - Damage is `blockDamage` at the middle and falls to `craterFalloff` of it at the edge.
  - Radius is capped at 8 m.
- **Durability shapes the hole.** Every block in a crater is damaged the normal way, so its durability decides whether it breaks. Dirt carves wide, while stone and ore only go near the middle and are left cracked further out (those heal, 3.2). Unbreakable blocks are never touched.
- **One batch call (Law #3).** `VoxelWorldManager.RequestDamageVoxels(requests, instigator, area)`:
  - Finishes the gather jobs once, then damages each block through the same path as `RequestDamageVoxel`.
  - Every break is still logged, raised and remeshed one by one, so saves, drops, foley, falling trees and torches dropping all keep working.
  - `VoxelDamageRequest` moved into the Voxels assembly so the batch can take it.
  - It returns `VoxelBatchResult` (broken / damaged / skipped).
- **`BlockFlags.BlastProof`:** blasts (`area: true`) pass blast-proof blocks by. Home Totems are blast-proof, so a partner's spell can't destroy your way home. Mining still breaks them, so you can move your totem.
- **Two spells and a new delivery:**
  - **Pyroblast** (ID 3): Fire, 2.5 s cast, 45 mana, 10 s cooldown. A 22 m/s fireball out to 40 m. Crater radius 4, round, 240 damage at the middle.
  - **EarthShatter** (ID 4): Earth, instant, 35 mana, 8 s cooldown. Crater radius 4.5, flatten 0.45, 150 damage.
  - EarthShatter is the first **`AbilityDelivery.GroundSlam`**: nothing flies; it goes off on the block you aim at, within range.
  - With no block in range, the cast is refused with **`CastCheck.NoTarget`** ("Nothing in range") before any mana is spent.
  - The **Arcane Orb** now makes a small crater (radius 3, falloff 0.15, flatten 0.8).
- **A ground slam reuses the `Impact` cue** with `ShotId` 0 (`AbilityCue.Blast`). Projectile IDs now start at 1 so a slam never matches a flying bolt. Clients read the crater size from the shared `AbilityDefinition`, so nothing new goes over the network.
- **Temporary buttons until the 4.4 action bar:** `PlayerCaster.testAbilities[3]` on Ability 1/2/3. Ability 1 is still Arcane Orb.
  - Ability 2 (Pyroblast): key `2`, P2 `-`, pad d-pad left.
  - Ability 3 (EarthShatter): key `3`, P2 `=`, pad d-pad right.
- **Drops are merged per frame.** `BlockDropSpawner` collects every block broken in a frame in a `DropBatcher` and spawns one stack per drop type per ~3 m area.
  - Each stack spawns from the broken block nearest the middle of its group, which is always air.
  - A crater of about 130 blocks gave 18 stacks instead of 130 network objects.
  - Side effect: a felled tree now drops a stack or two of logs instead of one per block. A single mined block drops as before, one frame later.
- **Looks (each PC, D-025 split):** `AbilityLook` has new crater settings: `debrisChunks`, `debrisSpeed`, `debrisTint`, `shakeStrength` and `blastLight`.
  - `ManaBoltView` flashes a light over the crater and throws lit cubes tinted like the block that was hit (`Art/Materials/Props/BlastDebris.mat`).
  - New synthesised sounds: `FireRoar` (cast loop), `PyroRelease`, `PyroImpact` and `EarthSlam`.
- **Camera shake per viewport (Law #5).** `BlastShake`, owned by `SplitScreenDirector` like `CaveLight`, hears every hero's cues through `PlayerController.AnySpawned`, so it works on remote PCs too.
  - It shakes each local player's own rig by how near their hero is: full strength inside the crater, nothing past 6× the radius.
  - The rig only turns (`PlayerCameraRig.Shake`), so the camera never pokes into blocks.

### Measured
- Live host, warm: a whole Pyroblast crater (112 blocks broken) took **0.42 ms** on the main thread. The first call ever took 18 ms (one-time JIT). Remeshing stays on worker threads (Law #4).

### Deferred on purpose
- Craters hurting creatures and heroes: Phase 5 (health).
- Debris bouncing on blocks: chunks fade out before sinking far, and particle collision against voxels isn't worth it yet.
- Remote PCs seeing the blocks change: Phase 6 block sync.

---

## D-025: Spell Rules vs. Spell Cues (Logic/Visual Split)
- **Date:** 2026-10-29
- **Status:** Accepted (play-tested in split-screen 2026-10-29)

### Decisions
- **The host only runs rules and says what happened.** `AbilityCaster`, `ManaBoltFlight` and the new **`AbilityImpact`** (plain C#: a hit becomes a list of `VoxelDamageRequest`s) decide everything. `PlayerStaff` applies the requests through `VoxelWorldManager.RequestDamageVoxel` (Law #3). The world's editor stays hidden, so the math never touches the world directly.
  - 4.3's craters go into `AbilityImpact.Resolve` (more requests, using the AoE radius). Nothing else needs to change.
- **One cue channel per hero.** `PlayerAbilityCues` (a NetworkBehaviour on the Player prefab) sends `AbilityCue` structs to every PC with one reliable RPC. There are six kinds: `CastBegan`, `CastStopped`, `Released`, `Launched`, `Impact` and `Fizzled`. Only the fields a kind uses are sent.
  - It replaced `PlayerCaster`'s three cast RPCs and `PlayerStaff`'s two bolt RPCs. The owner-only "Not enough mana" message stays its own RPC, because it's a message, not something that happened.
  - `PlayerCaster` keeps the cast bar and cooldowns in step from the cues. Its public API didn't change, so `PlayerHud` didn't either.
- **How a spell looks and sounds is an `AbilityLook` asset**, never part of the `AbilityDefinition`:
  - colour, core shape (gem or orb) and size, trail, light
  - muzzle and impact sparks
  - cast motes and cast light
  - sounds: a cast loop, release, impact and fizzle
  - The looks are listed in an `AbilityLookBook` with a fallback look, the same pattern as `SoundBook` (D-022). `glow` left `AbilityDefinition`. `boltFire` / `boltImpact` left `SoundBook` and moved into the Mana Bolt's look.
- **Views only listen.**
  - `ManaBoltView` draws any projectile from its look. With nothing held, it starts at the right hand.
  - The new `CastingView` swirls motes into both hands and grows a light during a cast, flashes when the spell goes off, and puffs when it's stopped.
  - `PlayerFoley` plays a hum during a cast and the release, impact and fizzle sounds, from cues and looks. It no longer listens to the VFX component.
- **A test guards the line:** `AbilityCueTests.Rules_HoldNothingYouSeeOrHear` fails if a rules class (definitions, caster, flight, impact, cue, `PlayerCaster`, `PlayerStaff`, `PlayerAbilityCues`) gets a field holding a colour, material, sound, particle system, light, renderer, look or view.
- **New look for the Arcane Orb:** a 0.45 m purple orb with a longer trail and a 70-spark burst. While casting it has hand motes and the synthesised `CastHum` loop; when it goes off and lands it plays `OrbRelease` and `OrbImpact`. The Mana Bolt looks and sounds as before.

### Deferred on purpose
- Hearthing (`PlayerHearth`/`HearthView`) keeps its own RPCs. It could move onto cues later.
- Creature damage and health: Phase 5. Cue kinds for them get added when they're needed.
- An `AbilityElement` default look (for example, every Fire spell glowing orange) waits until 4.6 has enough spells to need it.

---

## D-024: The Ability Framework (Spells, Mana, Cooldowns, Cast Times)
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-29)

### Decisions
- **An ability is an `AbilityDefinition` asset** (*Create > Voxelforge > Ability*) in `Data/Abilities/`: permanent `id` (`AbilityIds`, sent over the network), name, description, icon (for 4.4), `AbilityElement` (Physical, Arcane, Fire, Frost, Earth, Nature, Lightning, Holy), **mana cost, cooldown, cast time** (0 = instant), cast-while-moving, **range, AoE radius**, delivery, projectile speed, block damage, creature damage and glow colour. `AbilityBook` lists them by ID and validates them; *Voxelforge > Build Abilities* writes them.
  - **AoE radius and creature damage are stored but not used yet:** craters come in 4.3, creatures with health in Phase 5.
  - Only one delivery so far: **Projectile** (flies from the eyes along the aim). Self, ground and totem deliveries come with 4.3/4.6.
- **The rules live in `AbilityCaster`** (plain C#, tested without a scene): mana with regen, per-ability cooldowns, one cast at a time.
  - **WoW-style timing:** mana is spent and the cooldown starts when the cast *finishes*, and both are checked again then. Instant abilities finish the moment they start.
  - Moving, jumping or drifting 0.35 m cancels a cast (same rule as hearthing, 3.8) unless the ability can be cast on the move. A cancelled cast costs nothing.
- **The host runs it (Laws #1-#2).** `PlayerCaster` (on the Player prefab) owns one `AbilityCaster` per hero on the host and reads only the input the host received. A finished projectile goes to `PlayerStaff.ServerLaunch`, which still launches from the eyes along the host's aim, so no position comes from a PC.
  - Network: mana and max mana are `NetworkVariable`s (sent when mana moves 0.5 or reaches full); `CastStartedRpc` / `CastFinishedRpc` / `CastStoppedRpc` go to every PC, which time cast bars and cooldowns on their own clock (like `PlayerHearth`); refusals ("Not enough mana") go only to the owner.
  - Mana isn't saved: heroes start full.
- **The M.G.R.S. now casts the Mana Bolt ability.** D-019's `ProjectileStats` is gone: the item points at an `AbilityDefinition` and keeps only its muzzle point (a look). Same numbers (45 damage, 0.5 s, 40 m/s, 24 m), now **8 mana** from a **100 mana** pool that regenerates **10 a second**: about 16 s of nonstop fire. Holding Mine keeps firing; "Not enough mana" is said once per press.
- **A temporary Ability 1 button** (keyboard `1`, right-side keyboard `]`, gamepad **B**) casts the **Arcane Orb** test spell: 30 mana, 1.5 s cast, 6 s cooldown, 18 m/s, 30 m, 120 block damage. `PlayerButtons` grew from 8 to 16 bits for it, which 4.4's action bar needs anyway.
- **HUD (in each player's own view, Law #5):** a blue mana bar under the hero, a cast bar with the spell's name low in their area, and a short red message above it. The reticle's recharge bar now reads the cooldown from `PlayerCaster`. 4.4 replaces these with real action and cast bars.

### Deferred on purpose
- A global cooldown, the 4.4 action bar and real hotkeys (Ability 1 is a stand-in).
- Health, creature damage and healing: Phase 5 (or 4.2 if we need it sooner).
- Splitting the effects into logic and visual cues: 4.2. AoE craters: 4.3.

---

## D-023: The 0.3.10 Demo Build (Forge & Carve)
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested in split-screen 2026-10-28)

### Decisions
- **Same pipeline as D-013:** `DemoBuilder`, Windows x86_64, Mono, Direct3D 12, into the git-ignored `Builds/Windows/`. Version **`0.3.10`** (phase 3, step 10).
- **Full screen: F11** switches a build between a borderless full-screen window at the monitor's own resolution and a window (at most 1600×900). Unity remembers the window mode in the registry (`HKCU\Software\Voxelforge\Voxelforge`, `Screenmanager Fullscreen mode`) and that beats the project default, which is how an earlier run got stuck windowed. Alt+Enter still works. The 6.3 settings screen takes this over.
- **F4 records more:** each `frame-stats.txt` line now has the split **style** next to the camera mode, the last save's size (or "not saved yet"), the edited-block count and the load time, so one line holds what the DEVLOG needs.
- **Smoke tests use their own world:** `Voxelforge.exe -smoketest 20 -world smoketest -newworld`, so a smoke test never touches the play-test save.

### Results (RTX 4060 Laptop GPU, 2560×1440, release build)
| Camera | Average FPS | 1% low | Worst frame |
|---|---|---|---|
| Dynamic split (Voronoi) | 120-134 | 41-43 | 56-60 ms |
| Classic split | 134-166 | 42-51 | 26-69 ms |
| Overview (smoke test, 1920×1080) | 280 | 76-82 | 16-24 ms |

- **Averages pass 120+**, at 78% more pixels than 2.7's 1080p. Dynamic split is right on the line.
- **The 1% low roughly halved** since 2.7 (87 → 41-51), with regular 25-70 ms frames. It wasn't noticed in play. Suspects to check with a Development build and the profiler: remeshing while both players edit, GC from Mono, the 3.9 sound mixer, prop lights, and the higher resolution.
- **A 1-1.3 s frame at startup** (terrain generation and first meshing) shows up in any F4 snapshot taken in the first ~10 s. It's a loading pause, not gameplay.
- **Build:** 113.3 MB (110.3 at 2.7), 14-37 s, 0 errors. The two warnings on a full build are the stripped GPU-occlusion debug shaders (D-013).
- **Startup:** terrain (904 chunks) generates in 1.5-1.6 s, first meshing in 0.36 s.
- **Saving:** 206 bytes for 12 edited blocks and 2 profiles; loaded in 15.8-21.1 ms; encode 4-7 ms on the main thread, write 7-25 ms on a worker. Edits and both player profiles came back after a restart.

### Deferred on purpose
- Finding the 1% low drop: before Phase 4 adds spells and UI.
- A loading screen to hide the startup pause: 6.2.

---

## D-022: Split-Screen Sound and Voxel Foley
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested 2026-10-28)

### Decisions
- **One mixer that hears through every local player (`SoundRouter`, scene component, not a singleton).** Unity allows one `AudioListener`, so per-player listeners are out (deferred in D-009). Instead every sound is a **2D** pooled `AudioSource` (`spatialBlend = 0`, 32 voices) and the router does the 3D part itself:
  - **Ears:** one per local player with a view (`LocalPlayerManager.TryGetLocalPlayer` + `SplitScreenDirector.TryGetView`): the hero's `VisualRoot` + 1.6 m, turned like that view's camera. No `Camera.main` or player 0 (Law #1).
  - **Loudness:** the loudest ear wins: full inside 2 m, then `(1 - t)²` to silence at the cue's max distance (`SoundMath.Gain`). A torch next to Player 2 is loud however far away Player 1 is.
  - **Left/right:** from the loudest ear, inside **that player's part of the screen** (`SoundMath.RegionFor`): the whole stereo field for a full-screen or merged view, the left or right half in Classic (from `ScreenRect`), and in the Voronoi split a half centred at `VoronoiSide × VoronoiNormal.x × 0.5`. Then `pan = centre + halfWidth × dot(direction, camera right)`.
  - Voices keep their world position and are **re-mixed every `LateUpdate`** (order 100, after the director), so loops and moving players stay right. When full, a new sound replaces the quietest one-off if it's louder; loops are never replaced.
  - `SfxVolume` / `AmbienceVolume` are ready for the 6.3 settings sliders. The director's `AudioListener` stays only because Unity needs one; its position no longer matters.
- **Data-driven sounds (`SoundBook`, `SurfaceSound`, in Gameplay).** `AudioClip`s stay out of the Voxels assembly, so `BlockDefinition` doesn't change. A `SurfaceSound` has steps, hits, breaks, places, optional layered extras (the Mana Crystal's chime and chord) and the hit pitch at the first and last crack stage. 7 surfaces: Stone (Stone, Red Rock, Sandstone), Dirt, Grass (the default, for blocks not listed and PCs without blocks), Sand, Snow, Wood (Tree Trunk, Log, Torch, Workbench, Home Totem), Crystal (stone + bells). Landing reuses steps, lower and louder.
- **Who plays what:**
  - **`WorldFoley` (host):** listens to `VoxelWorldManager.EditApplied` and a new `BlockDamaged` event (`VoxelHitDamage`: block, crack stage, instigator). Hits: at most one per block every 0.22 s, but always on a new crack stage, pitch rising across the 8 stages. Breaks: at most 3 per frame. **Tree falls:** ≥2 trunk blocks cleared in one frame = one creak at the trunk's middle + a leaf rustle at the top, and the rest of the trunk's breaks are muted. Pickups from a new `BlockDropSpawner.PickedUp` event. Remote PCs don't see blocks until Phase 6, so they don't hear these yet.
  - **`PlayerFoley` (every PC):** follows every hero through new `PlayerController.AnySpawned` / `AnyDespawned` events (raised on every PC, like `BlockDrop.Spawned`). Footsteps from `HeroFootIK.StepCount`, which counts a step when the animation lifts a foot over 0.08 m and puts it back under 0.03 m while walking. It needs no blocks, so remote heroes have footsteps too. Polled, not an event, so nothing has to unsubscribe. At least 0.18 s between one hero's steps. Jump (once the animator sees upward speed), landing (falls of 0.3 s or more, heavier with fall speed). The M.G.R.S. through the existing `ManaBoltView.Fired` / `Impacted`.
  - **`AmbientLoops`:** like `PropLights`, a fixed number of loops (6) go to the props nearest any ear, re-chosen every 0.2 s. Only decorations listed in `SoundBook.Loops` (both torch models) loop, so the Home Totem is silent.
- **Sounds made from code (`SfxBuilder`, *Voxelforge > Build Sound Effects*), not recorded or AI-generated:** free, the same every build, and tweakable by numbers, like `build_props.py`. Filtered noise bursts, gliding sine thuds, partials for wooden knocks and stone rings, inharmonic bells for the crystal, a resonated click train for the tree creak. 70 mono 16-bit WAVs (3.2 MB; ADPCM, decompressed on load; the torch loop Vorbis, compressed in memory).
  - **After play-testing:** footsteps sounded like "cymbals clashing" and torches like "crunchy foil". Fixes: footsteps lost the stone ring (only wood keeps its low knock), became a heel thud + softer toe below ~2.5 kHz, and dropped to volume 0.25; the noise filter got a second low-pass stage (one let too much hiss through); the torch became mostly a breathing low flame roar with ~3 quiet crackles a second and an occasional woody snap, a 6 s loop at volume 0.35.

### Deferred on purpose
- Recorded footsteps (CC0 packs or our own recordings) if we want more realism: they drop straight into the `SurfaceSound` assets.
- Block sounds on remote PCs: with the block sync (Phase 6 / 7.2).
- An `AudioMixer`, music and the volume sliders: 6.3. Occlusion (muffled sounds behind walls): later, if tunnels need it.

---

## D-021: Home Totems and Hearthing Home
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested 2026-10-28)

### Decisions
- **Block `HomeTotem = 13`:** a solid prop like the Workbench (invisible full block under the model, Axe harvest kind, drops itself, 60 durability). Not `NeedsSupport`: one model that stands anywhere is simpler than a floor-only rule, and a 1-block-tall totem looks right on a ledge or a floor. It's drawn by the decoration layer and comes back from the delta log like any block, so it needs no new sync.
  - **Art (`build_props.py`, `PropsBuilder`):** a carved post on a stone base, a little house glowing green on all four sides (the model is turned to a seeded quarter, so every side gets one), a green rune band and a roof cap with a gem, all inside one block (266 triangles). Two new atlas areas, `totem` and `hearth`, painted **after** the older ones so their random numbers don't change. A `DecorationLight` (green, 1.6 intensity, 7 m, gentle flicker) from the 32-light torch pool.
  - **Recipe:** 4 Log + 1 Mana Crystal Ore at a Workbench, added at the end of the `RecipeBook`.
- **One home per player, owned by the host (`PlayerHearth`).** The block doesn't store an owner; the home is the player's own state: `int3` on the host, plus a synced `HasHome`.
  - `PlayerBuilder` calls `ServerSetHome(cell)` after a totem placement succeeds. If the old totem still stands, the host breaks it through `RequestModifyVoxel(old, Air, owner)`. That's an ordinary edit, so it drops (Law #3).
  - **Is it still there?** `HomeTotems.IsStanding` reads the block when its chunk is loaded, otherwise the `VoxelDeltaLog` (a totem is always a player edit, so the log knows far-away ones). The host re-checks once a second and tells the owner "Your Home Totem was broken!".
  - Saved in `PlayerProfile` (`HasHome`, `HomeBlock`). **Save format 2** adds them at the end of each profile; format 1 saves still load, with no home.
- **Hearthing home.** New `PlayerButtons.Home` (the last free bit of the byte; H, `[`, gamepad View/Select).
  - **Owner PC:** Home opens a prompt in the player's own view (`HearthPrompt`, Law #5). **Place** confirms and sends `RequestHearthRpc` (no data); Home or Interact closes it, and so does 6 s or opening the crafting window. While the prompt is open, and until Place is let go, `PlayerBuilder` won't build (`IsUsingPlace`), so the confirming press can't also place a block.
  - **Host:** re-checks the home and the totem, then a **2 s cast**. Moving the stick, jumping, mining, or drifting 0.35 m cancels it. During the cast `PlayerHearth` is a chunk viewer (2-chunk radius) at the home, so far-away terrain streams in. The host waits up to 10 s more for it, then gives up.
  - **Landing (`HomeTotems.TryFindLanding`):** the first of the 8 cells around the totem (sides first) at its level, one up or one down, with room for the body and ground underneath, facing the totem; failing that, on top of the totem's column. `PlayerController.ServerTeleport` moves the body and calls `NetworkTransform.Teleport`, so other PCs don't see the hero slide.
  - **Every PC:** `CastStartedRpc` / `CastEndedRpc` drive `HearthView`: green motes spiralling up a tightening ring, a growing green light, then a burst and a flash where they land (a small puff if cancelled). The owner gets a "Going home..." bar and messages.

### Deferred on purpose
- A cooldown on hearthing (WoW-style): not needed for couch play yet; it can come with the 4.1 ability framework.
- Showing whose totem is whose (a team-colour trim): every totem looks the same for now.
- Home Totems in the 6.x world picker or map.

---

## D-020: Tunnel Camera and Split-Screen Styles
- **Date:** 2026-10-28
- **Status:** Accepted (play-tested 2026-10-28)

### Decisions
- **Hide the hero from a camera that's on top of them, per camera (`HeroCameraFade`, owned by `SplitScreenDirector`, presentation only).** In a tunnel `PlayerCameraRig` slides up to the hero's head, and the body filled the view.
  - A hero is hidden from a camera **closer than 1.1 m** to their body (hips to head), **or within 2.6 m with the body less than 0.45 m from the camera's forward ray** (they'd block what you aim at, as in a 1-wide tunnel). Hysteresis: they show again past 1.5 m, or past 3.0 m / 0.6 m when hidden for blocking the view, so there's no flicker.
  - "Hidden" = `ShadowCastingMode.ShadowsOnly` (or `forceRenderingOff` if it casts no shadow), set in `RenderPipelineManager.beginCameraRendering` and restored in `endCameraRendering`. So only the squeezed view hides that hero; split-screen partners still see them. Renderers are looked up under `VisualRoot` each time, since the held item comes and goes.
  - **The GPU Resident Drawer catch:** it only picks up renderer changes once per frame for every camera, so the per-camera change didn't hide the held item (the hero is a `SkinnedMeshRenderer`, which the GRD doesn't draw). `HeldItemView` gives its one renderer a `MaterialPropertyBlock`, which keeps it on the classic draw path. (`DisallowGPUDrivenRendering` is internal, so it can't be used.)
- **Over the shoulder in tight spots.** When the view straight behind is squeezed below 55 % of its wanted distance, the pivot and look-at point slide up to 0.55 m to the camera's right; they slide back above 85 %. The slide stops `WallGap` short of any visible block (`PlayerCameraRig.ClearSideways`), so the pivot stays in open air. The decision uses the unshifted view, so the slide can't switch itself off. In a 2-wide tunnel you look past the shoulder with the hero in view; in a 1-wide tunnel there's no room, and the hide rule does the work.
- **Split-screen styles.** `SplitStyle.Dynamic` (D-009's merge and Voronoi split) or `SplitStyle.Classic` (2 players always get a fixed half each, side by side, through the same `UpdateOwnView` path as the grid; new `SplitMode.Classic`, `SplitScreenMath.ClassicRect`). 3-4 players use the grid either way.
  - **One setting per couch, on this PC only** (cameras never go to the host). A new `SplitStyle` action (F6 for the keyboard players, right-stick click on gamepads); `LocalPlayerInput.SplitStyleTriggered`. The overlay shows a toast. Kept in `PlayerPrefs` (`Voxelforge.SplitStyle`) until 6.3's settings screen.
  - Switching back to Dynamic starts the shared angle between the two players' own angles.
  - HUDs, the bag line and the crafting window already lay out from `PlayerView.ScreenRect` (Law #5), so they needed no change.
- The controls asset also gained the **Home** action (H, `[`, gamepad View/Select) for 3.8.

### Measured (editor, 1906×861)
- Single player in a tunnel: 158-161 FPS avg. Dynamic split: 95 avg / 29 1% low. Classic split: 106 avg / 46 1% low (two half-size views instead of two full-size ones).
- Editor split-screen has run well under the build before (2.7: ~109 editor vs 153 build), and this window is ~55 % more pixels than earlier today's 116 FPS run. **The 120+ FPS check, especially Dynamic's 1% low, is re-done in the 3.10 build.**

### Deferred on purpose
- A real dithered fade instead of hiding all at once: needs our own hero shader.
- Top/bottom Classic halves: side by side suits 16:9; it could be a 6.3 setting.

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
