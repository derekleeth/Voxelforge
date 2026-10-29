# Voxelforge - Dev Log

> Our adventure journal. After each session, add an entry at the top: what we did, what we learned, and what's next.
> Checklist status lives in `ROADMAP.md`; the *why* behind big choices lives in `DECISIONS.md`.

---

## 2026-10-29 - Session 1 (part 28): KABOOM! Spells That Dig Craters 💥 Roadmap 4.3
**Phase:** 4 - The Warcraft Injection

### What we did
- **Spells blow holes in the world now!** A big spell doesn't just break one block any more; it blasts out a whole crater. Details in DECISIONS.md **D-026**.
- **Two new spells:**
  - 🔥 **Pyroblast** (`2`, P2 `-`, d-pad left): stand still for 2.5 seconds while a fire roar builds in your hands, then hurl a huge fireball that blows a deep, round crater.
  - 🪨 **EarthShatter** (`3`, P2 `=`, d-pad right): instantly slam the ground you're looking at and smash it into a wide, shallow bowl. Aim at the sky and it says "Nothing in range" (and you keep your mana).
  - The **Arcane Orb** (`1`) makes a small crater too.
- **Craters are "realistic":** soft stuff like dirt and sand gets carved out wide, while hard stuff like stone and ore only breaks near the middle and is left cracked further out.
- **Home Totems are blast-proof,** so your partner can't blow up your way home by accident. You can still mine your own totem to move it.
- **No lag from 100 drops:** everything a blast breaks pops out as a few big stacks (like "Dirt ×41"), not a hundred little items. Chopped trees drop a stack or two of logs now too.
- **It feels big:** a flash lights up the crater, chunks of rock (the colour of what you hit) fly everywhere, there are new boom and slam sounds, and **your camera shakes** if you're close. Your partner's camera only shakes if *they're* close too.

### Tests: 306 / 306 passing ✅
- With no blast radius, a spell still only hits one block (the Mana Bolt didn't change).
- A crater never reaches past its radius, gets weaker toward the edge, and "flatten" makes it shallower but just as wide.
- A blast breaks dirt, cracks stone, skips totems and unbreakable blocks, and fells a tree caught in it.
- Nearby broken blocks of the same kind become one stack.
- Camera shake is full inside the crater and zero far away.

### Things we learned
- A crater is just *lots of mining at once*. Because every block goes through the same "damage this block" path, saving, drops, sounds and falling trees all just work.
- The first time some code runs it's slow (18 ms) because the computer is still translating it. After that a whole crater took less than half a millisecond!

### Next up
- [x] Play-test 4.3 in split-screen: Pyroblast and EarthShatter craters, merged drops, debris and shake, a totem surviving a blast, "Nothing in range" ✅
- [ ] Roadmap **4.4 Per-Player Split-Screen HUD** (real action bars replace the temporary 1/2/3 buttons)

---

## 2026-10-29 - Session 1 (part 27): Rules on the Host, Sparkles on Your Screen 🎆 Roadmap 4.2
**Phase:** 4 - The Warcraft Injection

### What we did
- **Spells are split in two.** The host does the *rules* (mana, where the bolt flies, which block it breaks), then just announces what happened: "cast started", "it went off", "it hit stone here". Each PC turns those announcements (we call them **cues**) into sparkles and sounds. Details in DECISIONS.md **D-025**.
- **Every spell has a "look" asset now** (`Data/Abilities/Looks/`): its colour, the shape of its bolt, its sparks and its sounds. A new spell can look totally different without touching any code.
- **The Arcane Orb looks like an orb!** It's a big purple ball now, not a purple Mana Bolt. While you cast it, glowing motes swirl into your hands and a magic hum builds. When it goes off there's a flash and a deep *whump*, and where it lands it makes a big purple burst and a boom.
- If you're not holding the staff, the orb now comes out of your hand instead of your eyes.

### Tests: 293 / 293 passing ✅
- Every kind of cue survives being sent over the network.
- A bolt damages only the block it hits, and a fizzle damages nothing (4.3 will add craters).
- Each spell finds its own look, and a spell with no look gets the default.
- A "guard" test fails if anyone ever puts a colour, sound or particle effect into the rules code.

### Things we learned
- Big games split it the same way: the server decides *what* happened and every screen decides how it *looks*. That way a slow PC, or a server with no screen at all, still plays exactly the same game.

### Next up
- [x] Play-test 4.2 in split-screen: the Mana Bolt looks and sounds the same, the Arcane Orb's hand glow, hum, orb, burst, and cancelling a cast by moving ✅
- [ ] Roadmap **4.3 Destructive Spell Mechanics** (craters!)

---

## 2026-10-28 - Session 1 (part 26): Spells Are Real Now! ✨ Roadmap 4.1
**Phase:** 4 - The Warcraft Injection

### What we did
- **Spells are made of data now.** Each spell is a Unity asset with its mana cost, cooldown, cast time, element (Arcane, Fire, Frost, Earth...), range and how big an area it hits. They live in `Data/Abilities/`. Details in DECISIONS.md **D-024**.
- **Every hero has mana!** A blue bar under your hero: 100 mana, and it fills back up 10 a second.
- **The M.G.R.S. costs mana now:** 8 per bolt. Hold fire for about 16 seconds and you run dry ("Not enough mana"), then wait a moment and keep blasting.
- **Our first spell with a cast time, the Arcane Orb!** Press **1** (Player 2 on the right of the keyboard: **]**, gamepad: **B**). A cast bar fills for 1.5 seconds, then a slow purple orb flies out and smashes blocks. Walk or jump while casting and it's interrupted. Then it needs 6 seconds to recharge.
- The host decides everything: whether you have enough mana, if the spell is ready, and where it flies. Your PC only shows it.
- **Caves are a bit darker.** Each player's camera looks up through the blocks to see how much sky is above it, and dims the sky's light down to 55% deep underground. In split-screen, each view gets its own darkness, so one player can be in a cave while the other is in the sun. It fades in over about a second as you walk in.
- **Holding a torch lights up the cave around you!** A warm, flickering light at the flame, a little softer than a placed torch. Things in your hand also glow stronger now (3.5, was 2.2), so the torch flame and the M.G.R.S. crystals stand out.

### Tests: 277 / 277 passing ✅
- Mana is spent when the spell goes off, never more than full, and refills over time.
- Cooldowns block a spell, then free it at exactly the right time.
- Cast times finish on time; moving cancels them and costs nothing.
- The spell list catches broken spells (two with the same ID, a bolt with no speed).
- The M.G.R.S. still breaks stone in 2 shots.

### Issues found
- **Held items had stopped glowing** (the M.G.R.S. crystals, torch flames in your hand). Unity had quietly switched the glow off in `Props_Held.mat` back in the 3.9 session. The cause: the material's lighting flag was "None", and URP reads "None" as "no glow", so it turned the glow off every time it checked the material. Now the flag is "Realtime Emissive" (in the material and in *Build Props & Items*), and the glow stays on.

### Things we learned
- In *World of Warcraft* you pay for a spell when it **finishes**, not when you start it. That's why getting interrupted doesn't waste mana. We do the same.
- If a file changes when nobody touched it, find out why before you just put it back. Otherwise Unity changes it again.

### Next up
- [x] Play-test 4.1 in split-screen: mana bar, running dry, the Arcane Orb's cast bar, getting interrupted ✅
- [x] Roadmap **4.2 Spell Logic vs. Visual Cue Split** (part 27)

---

## 2026-10-28 - Session 1 (part 25): Forge & Carve, the Real Game! 🏆 Roadmap 3.10 done, Phase 3 complete!
**Phase:** 3 - "Forge & Carve"

### What we did
- **A new Windows game, version 0.3.10:** `Builds/Windows/Voxelforge.exe`, with everything from Phase 3: digging, chopping, building, torches, crafting, the M.G.R.S., Home Totems, Classic split-screen, sound and saving. Details in DECISIONS.md **D-023**.
- **F11 = full screen!** The game had got stuck in a window, because Unity remembers the last window mode. Now F11 switches between full screen and a window, and the game remembers your choice.
- **F4 records more:** the split-screen style (Dynamic or Classic), how big the save is, how many blocks were changed and how long the save took to load.
- Claude's smoke tests now use their own world called `smoketest`, so they never touch our real save.

### Results 🎉
| | Average FPS | 1% low | Worst frame |
|---|---|---|---|
| Dynamic split | 120-134 ✅ | 41-43 ⚠️ | 56-60 ms |
| Classic split | 134-166 ✅ | 42-51 ⚠️ | 26-69 ms |
| Overview (nobody joined) | 280 | 76-82 | 16-24 ms |

- Tested at **2560×1440** this time (2.7 was 1920×1080), so that's a lot more pixels, and the average still beats 120. ✅
- **Build size:** 113.3 MB (was 110 MB). Builds take 15-40 seconds.
- **Startup:** the world is ready in about 1.5 seconds.
- **Saving:** 206 bytes for 12 changed blocks and 2 players. It loads in about 20 ms. We quit, restarted, and **everything came back**. ✅
- **Play-test approved:** "It looks awesome." ✅
- **Phase 3 is complete!** 🏆

### Issues found
- **The "1% low" got worse:** 41-51 FPS, down from 87 in 2.7. That means some frames take 25-70 ms. We didn't feel it while playing, but we'll track it down before Phase 4 adds spells.
- **A 1-second pause at startup** while the world is built. If you press F4 in the first 10 seconds, it shows up as a huge "worst frame". A loading screen (6.2) will hide it.
- F4 said "save 0.0 KB" before the first save of a session. Now it says "not saved yet".

### Things we learned
- The game remembers full screen or windowed *outside* the project (in Windows' registry), so changing the project setting alone doesn't fix a stuck window.
- The average FPS can look great while the 1% low hides stutters. That's why we record both.

### Next up
- [x] Play-test 3.10 ✅
- [ ] ⏸️ **Pending (needs both players):** find the slow frames behind the 1% low. In Classic split, wait 10+ s before each F4:
  1. Both standing still in the open, no torches → F4 (baseline)
  2. Both digging non-stop → F4 (remeshing?)
  3. Both standing still next to 4-6 torches → F4 (lights / sounds?)
  4. F11 to a window, standing still in the open → F4 (the 1440p resolution?)
  - If that doesn't show the cause: a Development build with the Unity profiler.
- [ ] Roadmap **Phase 4: The Warcraft Injection**: classes, races and spells!

---

## 2026-10-28 - Session 1 (part 24): The World Makes Noise! 🔊🔥 Roadmap 3.9
**Phase:** 3 - "Forge & Carve"

### What we did
- **The game has sound now!** Details in DECISIONS.md **D-022**.
  - **Footsteps** that sound like what you walk on: stone, dirt, grass, sand, snow and wood. Plus a whoosh when you jump and a thump when you land from high up.
  - **Mining:** each hit gets higher as the block cracks, so you can *hear* it's about to break. Then a break sound, and a thunk when you place a block.
  - **Mana Crystal Ore** rings like a little bell when you hit it, and plays a magic chord when it breaks.
  - **Chop a tree** and it creaks and crashes down with a rustle of leaves.
  - **Pick up drops:** pop!
  - **Torches** softly roar and crackle when you're near them.
  - **The M.G.R.S.** goes *zap* when it fires and *boom-sparkle* when the bolt lands.
- **Split-screen sound:** each player hears from their own hero. In Classic split, Player 1's sounds come from the left speaker and Player 2's from the right. A torch next to either player sounds loud, however far away the other one is.
- **Every sound is made from code**, like the 3D models: noise, tones and bells mixed together. Rebuild them any time with *Voxelforge → Build Sound Effects*.

### Tests: all passing ✅
- How loudness falls off with distance, and which side of the speakers each player's sounds come from (full screen, Classic halves, the angled split).
- Mining hits get higher crack by crack.
- Blocks without their own sounds get the default ones.

### Play-test results 🎉
- First try: footsteps sounded like **cymbals clashing** and were way too loud, and torches sounded like **crunchy foil**. 😬
- Fixed: footsteps are now a soft heel-and-toe thud (no metal ring), half as loud. Torches are mostly a low flame roar with a few gentle crackles and the odd wood snap.
- Second try: **"That sounds much better."** ✅

### Things we learned
- Unity can only have one "ear" (`AudioListener`), so for split-screen we do the 3D part ourselves, once per player.
- A footstep is played hundreds of times, so it has to be quiet and dull. Anything bright or ringing gets annoying fast.
- Real fire is mostly a soft roar. The crackles are the small part!

### Next up
- [x] Play-test 3.9
- [ ] Roadmap **3.10 Playable Demo Build**

---

## 2026-10-28 - Session 1 (part 23): There's No Place Like Home 🏠✨ Roadmap 3.8
**Phase:** 3 - "Forge & Carve"

### What we did
- **Home Totems!** Each player can set up their own home and teleport back to it. Details in DECISIONS.md **D-021**.
  - **Craft it** at a Workbench: 4 Log + 1 Mana Crystal Ore.
  - **The look:** a carved wooden post on a stone base, with a little house carved on every side that glows green, a glowing rune band and a gem on its roof. It lights up the room with a soft green glow.
  - **Place it** and it's your home. Placing a new one moves your home there, and the old totem pops out so you can pick it up.
  - **Go home:** press **Home** (H, `[` for the right-hand keyboard player, or the gamepad's View/Select button). A "Teleport home?" box opens in your own part of the screen. **Place** = yes, **Home** = no. Only Place teleports you, so you can't go home by accident.
  - **The trip:** 2 seconds of green sparkles spiralling up around you. Move, jump or mine and it's cancelled. Then you pop out right next to your totem with a green flash.
  - If someone breaks your totem, you're told, and your home is gone until you place a new one.
  - Your home is saved with your profile.

### Tests: 261 / 261 passing ✅
- A totem far away still counts (the game remembers it from the edits it saved).
- You land beside the totem facing it, skip blocked sides, and stand on top of it if it's walled in.
- Teleporting waits until the land around the totem has loaded.
- Homes save and load, and old saves from before totems still load.

### Editor test by Claude
- Placed a totem in a tunnel with real button presses: the walls glowed green.
- From 35 m away: Home opened the box, Place teleported Player 1 beside the totem. Walking during the charge cancelled it.
- A second totem moved the home and dropped the first one. Breaking the totem cleared the home. The home came back after a restart.

### Play-test results 🎉
- **The totem looks great** ✅
- Teleporting works and the 2-second timing feels right ✅
- Pressing No, or moving, cancels the teleport ✅

### Next up
- [x] Play-test 3.8
- [ ] Roadmap **3.9 Split-Screen Spatial Audio & Voxel Foley**

---

## 2026-10-28 - Session 1 (part 22): Seeing in Tunnels 🔦 Roadmap 3.7
**Phase:** 3 - "Forge & Carve"

### What we did
- **The camera works in tunnels now!** Details in DECISIONS.md **D-020**.
  - In a **1-wide tunnel**, when the camera is squeezed up behind your hero, your hero and what they're holding turn invisible (their shadow stays). You see straight down the tunnel, or the wall in front of you to put a torch on.
  - In a **2-wide tunnel**, the camera slides over your hero's right shoulder instead, so you still see your hero.
  - In split-screen your hero only disappears in **your** squeezed view. The other player still sees you.
- **Two kinds of split-screen.** Press **F6** or **click the right stick** to switch:
  - **Dynamic** (the one we had): one view when you're close, split by an angled line when you're apart.
  - **Classic**: always split down the middle, each player with their own half. Good when one player's mining and the other's chopping trees.
  - The game remembers which one you picked.
- Added two new steps to the roadmap: **3.7** (this one) and **3.8 Home Totem**. Audio is now 3.9 and the demo build 3.10.

### Tests: 255 / 255 passing ✅
- When a hero hides and shows again (with a gap, so they don't flicker), and a hero in the middle of a close view hides too.
- The over-the-shoulder slide stops before a tunnel wall.
- Classic gives two players the left and right halves.

### Editor test by Claude
- Built a 1-wide and a 2-wide stone tunnel and took screenshots. 2-wide: the view looks over the shoulder. 1-wide: the hero and the held Dirt block disappear and the whole tunnel shows.
- Found and fixed: the held item stayed floating in the view at first. Unity's fast drawing system can't hide something from just one camera, so the held item is now drawn the normal way.
- With 2 players: pressed the right stick on Player 2's gamepad. The screen went Classic, then back to Dynamic, with a message each time.

### Play-test results 🎉
- **The camera in tunnels is fixed and looks great** ✅
- FPS in the editor (bigger window than before):

| Mode | Avg FPS | 1% low |
|---|---|---|
| One player, in a tunnel | 158-161 | 67-71 |
| Dynamic split | 95 | 29 |
| Classic split | 106 | 46 |

- Split-screen is under 120 in the editor, but the editor has always been slower than a real build (2.7: ~109 in the editor, 153 in the build). We'll check it in the 3.10 build.

### Next up
- [x] Play-test 3.7
- [ ] Roadmap **3.8 Home Totem & Hearth Home**

---

## 2026-10-28 - Session 1 (part 21): Kian's M.G.R.S. 🪄💥 Roadmap 3.6
**Phase:** 3 - "Forge & Carve"

### What we did
- **The M.G.R.S. (Magic Gun Rifle Staff) works!** A wizard's staff that shoots glowing Mana Bolts that break blocks from far away. Details in DECISIONS.md **D-019**.
  - **Craft it** at a Workbench: 3 Log + 2 Stone + 5 Mana Crystal Ore.
  - **Fire it:** hold the staff and press Mine. Keep holding to keep firing, one bolt every half second.
  - **Power:** reaches 24 m. Dirt, sand and logs break in 1 shot, stone in 2, Mana Crystal Ore in 4. Bedrock can't be broken.
  - **Looks:** a muzzle flash, a glowing gem-shaped bolt with a trail and its own light, sparks the colour of the block it hits, and the staff kicks back when it fires.
  - The host moves every bolt, so it's fair and ready for online play later.
- **New crafting window!** A proper framed window with a picture of every item, which recipes are ready, and "have / need" for each ingredient in green or red. It always opens in your own part of the screen.

### Tests: 250 / 250 passing ✅
- A fast bolt still hits the first block in its way, fizzles at its range and stops at torches.
- The cooldown works. Dirt breaks in one shot and stone in two, but bedrock never breaks.
- The M.G.R.S. is a ranged item (not a tool) and needs a Workbench.

### Editor test by Claude
- Crafted the M.G.R.S. with real button presses; the host took exactly 3 Log, 2 Stone and 5 Ore.
- A stone 12 m away: "Damaged" on the first shot, "Broken" on the second.
- Player 2, without a staff, couldn't hold one.

### Play-test results 🎉
- **Kian loves it!** Every part of it works and looks good ✅
- The new crafting window makes it much easier to see items and figure out how to make them ✅
- Found: inside tunnels the camera shows mostly your own body, so it's hard to put a torch on a wall. Worse in split-screen. Plan coming next.

### Next up
- [x] Play-test 3.6
- [ ] Roadmap **3.7 Tunnel Camera & Split-Screen Styles** (new: better camera in tunnels, and a button to switch to a plain split-screen)
- [ ] Roadmap **3.8 Home Totem & Hearth Home** (new: a Home Totem you can teleport back to)
- Audio is now 3.9 and the demo build 3.10.

---

## 2026-10-28 - Session 1 (part 20): The Pick of Destiny ⛏️🪓 Roadmap 3.5
**Phase:** 3 - "Forge & Carve"

### What we did
- **Kian's ideas went on the roadmap!**
  - **The Pick of Destiny**, an axe on one side and a pick on the other, is in this step.
  - His **M.G.R.S.** (Magic Gun Rifle Staff) gets its own step, **3.6**, where it'll shoot glowing Mana Bolts that break blocks. Its model is already built.
  - Audio is now 3.7 and the demo build 3.8.
- **Tools!** Hold one and you mine faster. Pickaxes are for stone and ore, axes are for trees and logs. Details in DECISIONS.md **D-018**.

| Tool | Stone | Trees | Dirt & sand |
|---|---|---|---|
| Hands | 1.5 s | 0.75 s | 0.5 s |
| Wooden Pickaxe | 0.75 s | 0.75 s | 0.33 s |
| Stone Pickaxe | 0.5 s | 0.75 s | 0.25 s |
| Stone Axe | 1.5 s | 0.25 s | 0.33 s |
| **The Pick of Destiny** | **0.3 s** | **0.15 s** | 0.17 s |

- **Your hero holds what you picked** in their right hand, and everyone sees it. The tool swings while you mine. Pick items the same way as blocks (wheel, LB / RB, Z / C, . / /).
- **Crafting!** Press **Interact** (F, or X on a gamepad, or `;` for the right-hand keyboard player) to open your crafting list in **your own** part of the screen. Next / Previous choose, Place crafts.
  - Anywhere: **Workbench** (4 Log) and **4 Torches** (1 Log).
  - At a Workbench: **Wooden Pickaxe** (3 Log), **Stone Pickaxe** and **Stone Axe** (2 Log + 3 Stone each), and **The Pick of Destiny** (a Stone Pickaxe + a Stone Axe + 3 Mana Crystal Ore).
- **Torches** stand on the floor, or hang on a wall if there's no floor. They give off **real flickering light**, and their flames glow. Mine the block under one and the torch drops.
- **The Workbench** is a real wooden bench with a hammer, a vice and logs on the shelf.
- Every model and texture was made by code in Blender (`Tools/Blender/build_props.py`), so it's all ours.
- **Our rule for Phase 4 gear:** our own names and looks, *inspired by* classic RPG weapons and items, so no Blizzard stuff.

### Tests: 244 / 244 passing ✅
- Torches need something to rest on (not a ceiling!), can go where a hero stands, and fall when their floor is mined. A torch on a wall stays put.
- Aim rays stop at torches, so you can mine them; the camera looks past them.
- Tool power by block kind, and The Pick of Destiny breaks stone in 0.3 s.
- Crafting takes everything or nothing, refuses when the bag is short or full, and knows which recipes need a workbench.
- Floor torches stand up, wall torches turn to face away from their wall, and workbenches get their model with no grass under them.

### Editor test by Claude
- Joined with a pretend gamepad. With real button presses: crafted a **Workbench**, then a Stone Pickaxe, a Stone Axe and **The Pick of Destiny**. The host took the right items each time.
- Placed a workbench, a floor torch and a wall torch on a stone pillar. All three showed their models, both torches lit the stone, and the light flickered.
- Picked The Pick of Destiny: the host agreed it was in hand, and the hero held it out front, ready to swing.
- Timed it: **stone broke in 0.30 s** (1.5 s by hand), and the tool swung while mining.
- Restarted: the workbench and the crafted Pick of Destiny came back from the save.

### Play-test results 🎉
- **Play-tested: everything worked, and The Pick of Destiny is a hit** ✅

### Next up
- [x] Play-test 3.5
- [ ] Roadmap **3.6 Kian's M.G.R.S.**: build the Mana Bolt

---

## 2026-10-28 - Session 1 (part 19): The World Remembers 💾 Roadmap 3.4
**Phase:** 3 - "Forge & Carve"

### What we did
- **Worlds save now!** Everything you dig and build, and where each player stood with their block bag, comes back next time. Details in DECISIONS.md **D-017**.
- A save is tiny: just the world's **seed** (the number the whole world grows from) plus **only the blocks players changed**. The rest grows back from the seed.
- **When it saves:**
  - Every 2 minutes, by itself
  - When you quit
  - When you press **F5** (a message pops up to say it's done)
- **Each player is remembered.** Your PC gives Player 1 and Player 2 their own secret ID, so you each get your **own** spot and bag back, even if you join in a different order or one of you skips a session.
- If someone built a block where you were standing, you come back on top of it instead of stuck inside.
- **Starting over:** run the game with `-newworld`, or in the editor use *Voxelforge > Saves > Delete Editor Saves*. Editor saves and build saves are kept apart.

### Tests: 233 / 233 passing ✅
- The changed-blocks list saves and loads exactly, even with negative coordinates.
- Seed, both player spots, facing and bags (in the same order) come back.
- Broken files, files cut short and saves from a **newer** version are all caught safely, with no crash.
- Saving again keeps the last save as a backup.
- Each slot on this PC keeps the same player ID after a restart.

### Editor test by Claude
- Two players joined, built a 3-Log pillar and dug a trench. Player 1 stood on top of the pillar facing east, with Dirt 12 and Log 4 in the bag.
- Pressed save (176 bytes!), then added one more Stone to Player 2 and stopped Play: **the quit-save caught that too**.
- Played again: pillar there, Player 1 back on top facing east, both bags right. Loading took **about 1 ms**.
- Session with only Player 2: Player 1's profile was still kept. Walled over Player 1's spot, and next time they landed on top of the wall.
- **10,000 changed blocks save in under 1 KB** (865 bytes).

### Play-test results 🎉
- **Saved in split-screen, quit and came back: both players were in the exact spots we left them** ✅

### Fixed after the play-test: aim up!
- You could only mine and build **at or below eye level**, so building tall pillars was hard. (We forgot to mention it during 3.3.)
- Why: the camera could only tilt from a little bit down to a lot down, so the aim never went more than 15° up.
- Now **move the mouse or stick up** and the camera swings low behind your hero to look up. The aim follows, nearly straight up.
- Everything you could aim at before works exactly the same.
- Claude's check, against a wall one block ahead: the old limit reached the block at **eye level**, and now it reaches **3 blocks higher**, plus the blocks straight above your head.

### Next up
- [ ] Roadmap **3.5 Props & Tools (Creative)**: pickaxes, torches with real lights, and workbenches

---

## 2026-10-28 - Session 1 (part 18): Build It Block by Block 🧱 Roadmap 3.3
**Phase:** 3 - "Forge & Carve"

### What we did
- **Build with what you mined!** Place (right mouse, left trigger, E or Enter) now puts down a block **from your bag**, one at a time. No more free Dirt. Details in DECISIONS.md **D-016**.
- **A ghost block shows where it'll go.** A see-through copy of your block, with a glow in your team colour, snaps to the grid against the side you're aiming at.
  - It turns **red** if the block would go inside a hero (you *or* your partner), and nothing gets placed.
- **Hold Place** to keep building, one block every quarter second. Great for walls.
- **Pillar jump:** jump and place a block under your feet to build a tower.
- **Pick your block:**

| Controls | Previous / next block |
|---|---|
| Mouse | Scroll wheel |
| Gamepad | LB / RB |
| Split keyboard, left player | Z / C |
| Split keyboard, right player | `.` / `/` |

  - The block you're building with is in **[brackets] in your team colour** under your hero: `[Dirt 12] · Stone 5 · Log 4`.
  - When one runs out, it moves on to the next block you have.
- **No cheating:** your PC only says *which* block. The host works out *where* from your controls, checks that it's empty and that nobody's standing in it, and takes the block from your bag. If something goes wrong, you get the block back.

### Tests: 212 / 212 passing ✅
- A block snaps to the open cell against the face you aim at.
- Filled cells (even invisible tree trunks), unloaded chunks and "aiming from inside a block" are refused.
- A block can't go inside a hero, including the *other* player. Just touching is fine (pillar jump!).
- Picking blocks: skips empty ones, wraps round both ways, moves on when one runs out, and an empty bag picks nothing.

### Editor test by Claude
- Two heroes (keyboard + a pretend gamepad). Both ghosts showed up, blue and orange, snapped to the grid, and the HUD showed `[Dirt 5] · Log 3`.
- Two placements on the host: both landed, the bag went 5 → 3 Dirt, and the F3 overlay showed `edits: 2 blocks`.
- The host refused the cells where each hero stands (`BlocksCharacter`) and allowed an empty one. The ghost turned red over Player 2.

### Play-test results 🎉
- **Building walls with the ghost block** looks good ✅
- Mining and building are **easy to pick up** and quick to do ✅
- **Blocks in the bag cycle and place correctly** ✅
- Pillar towers and "can't build inside each other" ✅
- 105 blocks edited on a snowy mountain, with Dirt, Log, Snow and Stone in the bag.
- **FPS (F4, in the editor, 2 players, shared camera):** 166 average, 65 1% low, worst frame 25.4 ms. That's the same as 3.2 (168-179 average, 55-61 1% low), so building costs nothing noticeable.

### Added after the play-test
- **The ghost hides while you mine**, so it's easier to see the block you're cracking.
- **Toggle Ghost:** each player can turn their ghost block off and on. Placing still works with it off.

| Controls | Toggle Ghost |
|---|---|
| Keyboard + mouse | G |
| Gamepad | Y |
| Split keyboard, left player | G |
| Split keyboard, right player | `'` (next to Enter) |

- Checked by Claude in Play Mode with a pretend gamepad:
  - Y turned Player 2's ghost off and on again, and Player 1's stayed on.
  - Holding the trigger to mine hid the ghost, and letting go brought it back.
  - Still 212 / 212 tests passing.

### Next up
- [x] You two: split-screen play-test ✅
- [ ] Try the two ghost changes, then tick off **3.3**
- [ ] Roadmap **3.4 Seed + Delta Save System**: save the world and both players' bags

---

## 2026-10-28 - Session 1 (part 17): Crack, Crumble, Collect 🪨 Roadmap 3.2
**Phase:** 3 - "Forge & Carve"

### What we did
- **Real mining!** Hold **Mine** (left mouse or right trigger) and the block you're aiming at wears down until it breaks. Tough blocks take longer. Details in DECISIONS.md **D-015**.
  - Snow: ¼ second. Dirt and Grass: ½ second. Tree trunk: ¾ second. Stone: 1½ seconds. Mana Crystal Ore: almost 4 seconds!
- **Blocks crack as you mine.** Cracks grow across the block in 8 steps. Stop mining, and after 3 seconds the block heals.
- **Mine together:** if you both hit the same block, your mining adds up.
- **Blocks drop!** A broken block pops out as a little spinning block. Walk close and it flies to you and goes into your **block bag**.
  - Grass drops Dirt.
  - Chopping a tree drops a **Log for every trunk block**. Logs are a brand-new block with bark sides and tree rings on the ends, painted in Blender.
  - Drops lying next to each other join into one pile, and drops nobody collects disappear after 5 minutes.
- **Each player has a mini HUD:**
  - A **crosshair in your team colour** shows exactly where you're aiming. It goes faint when nothing is in reach.
  - Your bag ("Dirt 12 · Stone 5 · Log 4") floats under your hero, in your own part of the screen.
- **No cheating:** the host works out what you're aiming at from your controls. Your PC never tells it which block to break.
- Right mouse / left trigger still places Dirt for now. Building with the blocks in your bag comes in 3.3.
- The F3 overlay shows how many blocks are being mined and how many drops are lying around.

### Tests: 202 / 202 passing ✅
- A block wears down, breaks exactly at its toughness, and the break is a normal logged edit.
- Two players mining together break a block faster, and the one who finishes it gets the credit.
- Mining Stone in small steps (like the game does) takes 1½ seconds.
- Left alone, a block heals. Building over a cracked block clears its cracks.
- Breaking one trunk block fells the whole tree and makes one edit (one Log) per trunk block.
- Air, unloaded chunks, unbreakable blocks, zero damage and "no player" are all refused.
- Cracks pick the right stage. A drop's mini block is a whole cube with the right textures.
- Bag: piles up by type, stops at 999, and taking is all or nothing.
- Drop rules: Grass → Dirt, trunk → Log, Air → nothing, everything else drops itself.

### Editor test by Claude
- A hero held Mine while looking down and **dug 6 blocks straight down in about 7 seconds**. The bag then had **Dirt 4 and Stone 2**, so Grass really does drop Dirt.
- Screenshots show the crack stages on grass tops and dirt sides, and the little dirt drops resting in their holes.

### Play-test results 🎉
- **Walking into drops picks them up** ✅
- **Crosshair, block names and counts** show on each player's own side in split-screen ✅
- **Cracks grow** as you mine, and tougher blocks take longer ✅
- **Chopping a tree** gives you Logs ✅
- **Blocks heal** when you stop mining them ✅
- **FPS (F4, in the editor, 2 players, shared camera):**

| Run | Average FPS | 1% low | Worst frame |
|---|---|---|---|
| 1 | 179 | 55 | 54.2 ms |
| 2 | 168 | 61 | 24.8 ms |

- The averages are well above 120. Both runs used the shared camera, so a split-screen FPS run is still to do. The 3.6 demo build will measure it properly.
- The 1% low is lower than we'd like (55-61). One frame took 54 ms, which you'd feel as a small hitch. The editor is always slower than a real build, but we'll watch for hitches when blocks break in the 3.6 build.
- **Roadmap 3.2 is complete!** 🪨

### Things we learned
- Our first screenshot "lost" the drops, but they were just hidden at the bottom of their holes. A view from straight above found them, so look from more than one angle before deciding something is broken.

### Next up
- [x] You two: split-screen play-test ✅ **3.2 done.**
- [ ] Watch the 1% low FPS when blocks break; measure it in split-screen in the 3.6 build
- [ ] Roadmap **3.3 Grid Snap Placement**: build with the blocks in your bag, with a see-through ghost block showing where it'll go

---

## 2026-10-28 - Session 1 (part 16): Dig, Build, Remember ⛏️ Roadmap 3.1
**Phase:** 3 - "Forge & Carve"

### What we did
- **Phase 3 has a finish line:** a new roadmap step, **3.6 Playable Demo Build**. It's a new .exe where we dig, chop, build and save, then restart the game and check that everything came back.
- **Every block change now goes through one door:** `RequestModifyVoxel(where, which block, which player)`. Details in DECISIONS.md **D-014**.
- **The world remembers what you changed.** The new **delta log** writes down every block that's different from what the world generator made, and who changed it. Put a block back the way it was, and the log forgets it, so it stays small.
- **Digging survives walking away.** When a chunk unloads and loads again, its edits are put back before you can see it.
- **Chop a tree, and the whole trunk goes**, and the tree model goes with it.
- **No cheating, even on the couch:** your PC asks the host, and the host checks that the request came from your own player and is within 8 m of you. Only then does it change the block, in your name.
- **Try it (temporary controls until 3.2):**
  - **Dig:** left mouse or right trigger digs the block in front of you.
  - **Place:** right mouse or left trigger puts down Dirt.
  - Split keyboard: Q / E for the left player, Right Shift / Enter for the right.
- The F3 overlay shows how many blocks have been edited.

### Tests: 188 / 188 passing ✅
- Digging sets the block, logs it with the original block and the player, and reports the change.
- The same block, an unloaded chunk, an unknown block and "no player" are all refused.
- Changing a block back removes it from the log. Editing twice keeps the original block.
- Edits on chunk edges and corners redraw every chunk that touches them (8 on a corner).
- Chopping a trunk clears it across a chunk border, but not the ground under it.
- With the real terrain generator: edits come back after a chunk unloads and generates again, in both a stony underground chunk and an empty sky chunk.

### Play-test results 🎉
- Digging tunnels and building walls in **split-screen** ✅
- Chopping a tree makes the **whole trunk** disappear ✅
- Walked more than 300 m away and back: **every edit was still there** ✅
- The `edits:` count in the F3 overlay goes up as you dig ✅
- **Roadmap 3.1 is complete!** ⛏️

### Added after the play-test
- It was hard to tell which block Dig would hit, so sometimes the wrong one got dug. Now an **outline in your team colour** shows the exact block. In split-screen you can see your partner's outline too, in their colour.
- **Aim with the camera:** you no longer have to turn the whole hero. Look around with the mouse or right stick, and the outline follows where your camera points, up to 5 m away. Tilt the camera up to aim at walls and ceilings, or down to aim at the floor.
- **Place puts the block against the side you're aiming at**, like stacking bricks. It won't place a block inside your own hero.

### Things we learned
- Claude recompiled the scripts while Unity was still in Play Mode from the play-test, and the game threw errors. Nothing was lost, but always stop Play first.

### Next up
- [x] You two: play-test ✅ **3.1 done.**
- [x] You two: try camera aiming ✅ Much more natural for quick digging and building.
- [ ] Roadmap **3.2 Block Durability & Mining**: aim with a crosshair, blocks crack as you mine, and they drop items

---

## 2026-10-28 - Session 1 (part 15): Our First Real Game! 🎮 Roadmap 2.7 done, Phase 2 complete!
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **Voxelforge is now a real Windows game** you can play without Unity: `Builds/Windows/Voxelforge.exe`. Details in DECISIONS.md **D-013**.
- **One-click builds:** *Voxelforge → Build Windows Demo* makes the game and writes `build-report.txt` with how long it took and how big it is. There's also a *Development* version that the Unity profiler can connect to.
- The game is now called **Voxelforge** (it was "VoxelforgeTmp"), version **0.2.7**.
- **New play-test keys:**
  - **F4** records the FPS over the last 10 seconds into `frame-stats.txt`, next to the game.
  - **F12** saves a screenshot into `Screenshots/`.
  - A message at the bottom of the screen confirms each one.
  - **Shift+Esc** quits the game.
- The F3 overlay now shows **average FPS** and **"1% low" FPS**, the speed of the slowest 1% of frames. That's what stutter feels like.
- **Smoke test:** `Voxelforge.exe -smoketest 20` starts the game, records FPS and a screenshot after 20 seconds, then quits, so Claude can check a new build works before we play it.

### Results 🎉
| | Average FPS | 1% low | Worst frame |
|---|---|---|---|
| Overview (nobody joined) | 310 | 141-169 | 13-20 ms |
| One player | 252 | 97 | 15 ms |
| **Two players, split-screen** | **153** ✅ | 87 | 17 ms |

- Tested on an RTX 4060 laptop at 1920×1080. **Split-screen beats the 120 FPS target.** ✅
- **Build size:** 110 MB. The first build took 4 min 15 s (compiling all the shaders), and later builds take about 15 s.
- **The game starts cleanly:** no errors from our code. The terrain loads in about 2 seconds.
- **It looks the same as the editor:** we compared screenshots of terrain, textures, plants, fog, biome colours and shadows.
- **Play-test passed:** both heroes exploring in split-screen. ✅
- **Phase 2 is complete!** 🏆

### Tests: 175 / 175 passing ✅
- New FPS maths tests: average and 1% low, only counting the last 10 seconds, the memory filling up and wrapping around, and ignoring bad frame times.

### Things we learned
- **F4 and F12 worked the first time, but nothing showed it.** The numbers went into a log file hidden in AppData. Now every key press puts a message on screen and saves next to the game, where you'd look.
- **The grey "sky" isn't a bug.** The overview camera looks down, so past the edge of the loaded world you see the bottom half of Unity's default sky, which is grey. It's the same in the editor. A real sky could come later.
- Unity warns that it removed a few post-effect shaders we don't use (depth of field, Panini). That's fine and saves space.
- The split-screen "1% low" (87 FPS) is under 120 even though the average is fine. We'll keep an eye on it as monsters and spells arrive.
- In an earlier run, an Xbox controller dropped out twice and the game stuttered for a moment. It didn't happen again, but if you see a stutter, press F4 right after.

### Next up
- [x] You two: split-screen play-test of the .exe ✅ **2.7 done.**
- [ ] Roadmap **3.1 Message-Driven Voxel Edits**: start digging and building! `RequestModifyVoxel` with a player ID on every change

---

## 2026-10-28 - Session 1 (part 14): Colours, Soft Corners & a Smarter Camera 🎨 Roadmap 2.6 done!
**Phase:** 2 - Dynamic Couch Co-Op & Smooth Heroes

### What we did
- **Every biome has its own colours.** Grass blocks, grass tufts, flower leaves, bushes and trees take their biome's grass or leaf colour. Meadows are fresh green, the desert is dry yellow, the red crags are autumn gold and orange, and the snowy peaks are cool blue-green. Colours fade smoothly where biomes meet. Details in DECISIONS.md **D-012**.
- **Only the green parts change colour.** The dirt on a grass block's side, tree bark and flower petals keep their own colours.
- **Soft corners (corner AO):** block corners tucked against other blocks are darker, so steps, walls and cliff bottoms get soft shading and the world looks solid instead of flat.
- To get the shading right across chunk edges, each chunk now looks at **all 26 chunks around it** (it used to be 6).
- **Camera fix:** the camera could show the inside of a mountain, and make the hero look stuck inside it, when the screen was split. Now it always keeps a clear view from your own hero's head, checks the corners of its view too, and ignores the invisible tree-trunk blocks.

### Results 🎉
- **FPS check passed:** 190-230 FPS with two players on one screen, and **124-139 FPS split-screen** (the target is 120+). ✅
- A busy chunk still meshes in about 2.4 ms on a worker thread, even with the corner shading.
- **Play-test passed:** colours and soft corners look good, and the camera no longer goes inside mountains. ✅
- **Roadmap 2.6 is complete!** 🌳

### Tests: 168 / 168 passing ✅
- Open floors stay unshaded and stay one big piece. A block on a floor shades the floor around it.
- A corner between two walls is fully shaded, and pieces with one dark corner are split through that corner.
- Edges and corners are read from the diagonal chunks, and editing a chunk corner refreshes all 8 chunks that share it.
- Grass faces carry their biome colour; stone stays white.
- The camera's wall check passes through invisible tree trunks, but bodies still bump into them.

### Things we learned
- **Split-screen moves where each camera looks.** It aims a few metres toward the other player, so your hero sits in the middle of your half of the screen. Checking for walls from that point let the camera go inside hills. Checking from the hero's own head fixes it.
- Biome tint and corner shading fit in the colour every block corner already had, so the meshes didn't get any bigger.

### Next up
- [x] You two: FPS check and camera play-test ✅ **2.6 done.**
- [ ] Roadmap **2.7 Playable Demo Build**: a real Windows .exe we can play outside Unity, in split-screen at 120+ FPS

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
