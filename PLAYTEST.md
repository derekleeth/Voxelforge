# Play-Test List: Sunday 2026-11-01 (Derek + Kian)

Everything built today that still needs trying in the real game. Tick each box as you go, and jot down anything odd or not fun under **Notes**.

**Before you start:** close any running built copy of Voxelforge (it holds the network port, so the editor can't host). Then press Play, and both players join.

**Handy keys:** **F10** spawns the next mob kind near every hero (Shift+F10 clears them). **F6** takes the whole party to the nearest ruin someone hasn't found (**Shift+F6**: the nearest boss arena). **Tab** picks a target. **K** opens the class window. **M** opens the World Map. **B** opens the bag. **F1** gives every hero a level (Shift+F1: back to 1). **F2** gives every hero a loot kit (one of every piece of gear, 5 of each material). **F4** shows FPS.

---

## Roadmap 5.3: Threat & Aggro (Steps A-D)

Best set-up: one player is a **Runeforged Paladin** (the tank). The other is a **Geomancer Mage** or **Shaman**.

### Class window
- [x] Press **K**. The Paladin's card lists all 4 spells (Rune Hammer, Bulwark Rune, Hallowed Forge, **Oathbrand**), and no text spills off the taller cards.
- [x] Under the Paladin's description it says **Runeforged Oath: ...**
- [x] The other classes show their 3 spells, with no empty rows.
- [x] Still readable when the screen is split for 2 players.

### Holding aggro (the tank's job)
- [x] F10 a **Bristleback Boar**. The Paladin hits it with Rune Hammer, then the mage blasts it. The boar should **stay on the Paladin**.
- [x] The mage keeps nuking hard. Eventually the boar **turns on the mage** (they out-threatened the tank).
- [x] A boar that's winding up a charge finishes it the way it was aiming before it switches.
- [x] Healing the tank (Hallowed Forge, Wellspring Totem) makes the mobs fighting the tank a bit angry at the healer, but much less than damage does.
- [x] If the tank gets knocked out, the mob goes after the next hero.
- [x] A mob dragged too far from home still gives up and runs home (leash).

### Oathbrand (Paladin taunt, slot 4)
- [x] Mage pulls the boar → Paladin presses **Tab**, then **4** → a **gold burst** and an **anvil ring** on the boar, **TAUNTED!** in the Paladin's view → the boar comes back to the Paladin.
- [x] With no target in range, it says **No target** and costs no mana.
- [x] Aiming at a mob without Tab-targeting it also works.
- [x] Taunt a mob that's just wandering (not fighting) → it comes for the Paladin.
- [x] Taunt a **Training Dummy** → its hay bales go to the Paladin.
- [x] **Wolf pack:** taunt a wolf that's circling → it comes straight in, not waiting its turn.
- [x] Cooldown is 8 s and it costs 10 mana. Does that feel right?

### Seeing threat (each player's own view)
- [x] A **red eye** pops up next to every mob that's after **you**, only in **your** view.
- [x] The eye moves to the other player's view when the mob switches to them.
- [x] Mobs that spot you and charge get an eye, even before anyone has hit them.
- [x] **Tab** a mob that's fighting: a thin **threat meter** appears under it. Tank: full red. Mage: green → yellow as you get close.
- [x] Mage, nuke until nearly pulling it: **PULLING AGGRO!** pops up over your hero, not more than once every 3 s per mob.
- [x] The eye, meter and warning never show in the wrong player's half of the screen.

### Feel & numbers (talk it over)
- [x] Is the tank's job too easy or too hard? (Paladin threat ×2, Rune Hammer ×1.5, pull margins 110% next to it, 130% from range.)
- [x] Can a healer accidentally pull mobs? Should they?
- [x] F4 FPS with 20+ mobs still around 120+?

---

## Roadmap 5.4: Ruins & the Sunken Arena (Steps A-C)

Your saved world (seed 20261027) keeps all your edits; ruins only appear in ground nobody has dug or built on yet. Walk there, or press **F6** / **Shift+F6**. Positions are block x, z.

### Find the ruins near spawn (256, 256)
- [x] **Frost Shrine** at about **(156, 361)**, up in the Snowy Peaks: a ring of snow-capped pillars round a glowing rune slab.
- [x] **Broken Watchtower** at about **(230, 51)**: a mossy tower stump with a south doorway, its top lying in pieces to the east, plus a torch and a chest inside (the chest is empty until loot, 5.5).
- [x] **Standing Stones** at about **(473, 486)**, Thunderhoof Plains: red and sandstone stones round a fire pit, with a lintel over one pair.
- [x] More of each further out (another watchtower at (127, 524), stones at (17, 30)).
- [x] Ruins sit on the ground properly on slopes (a brick or stone foundation under them, never floating), and no tree grows through one.

### The Sunken Arena (for the 5.9 boss)
- [x] Nearest at about **(199, 1110)**, 855 blocks north of spawn. **Shift+F6** drops the party on the boss dais in the middle.
- [x] A deep pit of dark Warded Stone with glowing gold runes, two stairways down (north and south), torch-lit pillars and a two-step dais in the middle.
- [x] **Warded Stone can't be mined, and EarthShatter or Pyroblast can't crater it.** Ruin Bricks can be mined.
- [x] Mobs that chase you follow you down the stairs and back up.

### Finding them: map markers & discovery (Step C)
- [x] Walk up to a ruin: **DISCOVERED! Broken Watchtower** (or whichever) pops up, only in the finder's view, once.
- [x] Open the map (**M**) and check the minimap: a **brown broken-tower** marker for ruins, a **red ring** for arenas, but only where *that* player has explored. The other player doesn't see it until they've been there too.
- [x] Ruins don't show as arrows round the map's edge.
- [x] After quitting and coming back, found ruins are still on your map, and loading the saved map doesn't pop up "DISCOVERED!" again.
- [x] **F6** again and again hops the party to new ruins each time (it prefers ones someone hasn't found). It waits for the land to load and then lands everyone side by side.
- [x] F6 into a site where neither player has been: both get DISCOVERED!

### New blocks
- [x] Ruin Brick, Mossy Ruin Brick and Warded Stone look right up close, and sound like stone to walk on and mine.

---

## Roadmap 5.5: Loot & Equipment (Step A)

Step A is the loot data, so most of it is checked by looking in the bag after **F2** (mob drops come in Step B).

### The loot kit and rarity colours (F2, then B)
- [x] Press **F2**: both players get pickup text for each item, only in their own view.
- [x] Open the bag (**B**). The **Gear & Tools** tab has 16 new pieces; the **Materials** tab has 11 materials (5 of each).
- [x] Point at items. Their names show in **rarity colours**: Common white (Bristlehide Cap), Rare blue (Tuskhide Jerkin, Earth Essence), Epic purple (Crown of the Crystal Deep), Legendary orange (**Skyrender, Maul of the High Mesa**).
- [x] Gear says its slot and stats, e.g. "Rare Chest: Armor 11, Stamina 6". Materials say "Materials" (essences: "Rare Materials").
- [x] Every item has a picture in its slot: hides, tusk, fang, horn, crystal shard, four glowing essences in gold cages, caps/hood/crown, jerkins/hauberk, leggings/greaves, boots, cudgel, wand, blade, the big molten maul, buckler and shield.
- [x] Hold a weapon (select it on the hotbar): the cudgel, wand, blade and maul sit in the hand like a tool. Armour pieces are held small and upright (just stand-ins until 5.9).
- [x] Materials stack (press F2 twice: 10 Boar Hide in one slot); gear doesn't.
- [x] Send to Partner and dropping work for the new items.

## Roadmap 5.5: Loot & Equipment (Step B: personal loot)

Best set-up: both players fight the same mobs. **F10** spawns a mob kind by each hero (press again for the next kind).

### Mob drops, one sack each
- [x] Both players hit a **Boar** and kill it: a sack pops out of the body **for each player**, dyed in that player's colour.
- [x] Walk over your **partner's** sack: it doesn't fly to you, and you can't pick it up. Your own flies into your bag.
- [x] Pickups say **+2 Boar Hide** for common materials; gear and Rare+ items say **LOOT!** with the name in its rarity colour (blue, purple). Epic gets a comic burst; **LEGENDARY!** for Skyrender. Only in the looter's own view.
- [x] A **Rare** sack has a soft coloured glow; **Epic** and **Legendary** sacks have a pillar of light you can see across a fight.
- [x] The **healer** (who only healed the tank) still gets their own sack.
- [x] One player kills a mob alone: only they get a sack.
- [x] Each mob drops its own stuff: Boar (Boar Hide, Boar Tusk, Earth Essence), Wolf (Wolf Pelt, Frostfang, Frost Essence), Rhino (Rhino Hide, Thunder Horn, Storm Essence), Crystal Slimes (Crystal Shards, Arcane Essence; small ones only shards).
- [x] Roughly a third of kills give a piece of gear (generous for today). Too many? Too few?
- [x] Leave your sack lying there for a while: it doesn't vanish while you're still in the game.
- [x] Bag full: your sack waits ("Bag full!") until you make room.

### Ruin chests (one-time personal cache)
- [x] **F6** to a Broken Watchtower and open its chest (aim + Interact): a sack in your colour pops out of the chest and flies into your bag (2 rolls: gear, plus a chance of essences).
- [x] Close and open it again: **nothing more** for you. Your partner opens it: **they** get their own cache.
- [x] The chest still works as normal shared storage after that.
- [x] Place your own Storage Chest and open it: no cache.
- [x] Save and quit (or F5), come back, open the same ruin chest: still nothing (it remembers). Save format is now 7; your old save loads fine.

## Roadmap 5.5: Loot & Equipment (Step C: wearing gear)

Press **F2** for a kit, open the bag (**B**), point at a piece of gear and press **Use**. It appears in the **Worn gear** row at the bottom of the bag (Step D, below).

### Putting it on
- [x] **Use** on gear says **Equipped!** and it leaves the bag. Use another piece for the same slot (e.g. Crown after the Cap): they **swap**, and the old one lands in the same bag slot.
- [x] Equipping works with the keyboard, the mouse and a gamepad, in each player's own window.
- [x] Bags and tools still work as before (Use on a bag wears it; Use on a pickaxe puts it in your hand).

### Stats
- [x] **Stamina:** your health bar's maximum goes up when you put gear on (2 health per Stamina). Full best kit: about +80.
- [x] **Armor:** let a Boar gore you with and without gear on; the red damage numbers are smaller with gear (Common set about 18% less, best kit about 36% less).
- [x] **Power:** a spell's hit numbers on a Training Dummy go up with the Shardglass Wand or Skyrender (Skyrender alone +26%). Healing (Wellspring Totem, Hallowed Forge) and Bulwark Rune shields get stronger too.
- [x] Only the wearer gets stronger, never the partner.

### Saving
- [x] Wear some gear, save and quit (or F5), come back: you're still wearing it (and your health maximum still includes it).

## Roadmap 5.5: Loot & Equipment (Step D: the equipment panel)

### The Worn gear row (bag window, **B**)
- [x] Under **Worn bags** there's a **Worn gear** row of 6 sockets saying **Head, Chest, Legs, Feet, Main, Off** while empty.
- [x] Right of the bag sockets, your totals: **Armor N (-X% dmg)** and **Stamina N  Power N**. They change as you put things on.
- [x] Move the cursor down from the bag sockets into the gear row and back up (keyboard, right-hand keys, gamepad). Mouse: point at a socket.
- [x] **Use** on a worn piece takes it off into the bag. With a full bag: "No room...", and it stays on.
- [x] Pick up a piece of gear (**Pick up / Put**) and put it on its own socket: it's worn. On the wrong socket: nothing happens.
- [x] Point at an empty socket: "Head: nothing worn. Use gear from your bag to put it on."

### Comparing (tooltip line)
- [x] Point at gear in the bag while wearing something in that slot: **vs Bristlehide Cap: +5 Armor, +5 Stamina, +8 Power**, gains green, losses red.
- [x] Nothing worn there: **nothing worn there: +3 Armor, ...**. The piece you're wearing's twin: **same as worn**.
- [x] Also works for gear sitting in an open Storage Chest.

### Rarity at a glance
- [x] Slots holding **Rare** gear or essences are tinted blue, **Epic** purple, **Legendary** orange (bag, chest and worn sockets). Common stays plain.

### Fit
- [x] The bag window is a row taller now. It still fits inside each player's half of the screen in split-screen, with a chest open too.

## Roadmap 5.5: Loot & Equipment (Step E: wearing it & sounds)

Stand-in models (5.9 brings the real armour sets). Try it on **both races** (a Wayfarer and a Thunderhoof).

### Gear on the hero (both players see it on each other)
- [x] **Head:** cap / wolf-eared hood / crystal crown sits on top of the head.
- [x] **Chest:** jerkin / tusk-shouldered jerkin / purple hauberk over the chest.
- [x] **Legs:** a guard on each **shin**, moving with the legs as you walk and run.
- [x] **Feet:** big boots over the hero's own, one on each foot, moving as you walk.
- [x] **Main hand:** the weapon is **slung across your back**, head up over the right shoulder (Skyrender looks huge!).
- [x] **Off hand:** the buckler / Thunderhorn Shield on the **outside of the left forearm**, upright when the arms are down.
- [x] Taking a piece off removes it at once; swapping shows the new one.
- [x] Gear pieces fade with the hero when the camera squeezes in close (like the held item does).
- [x] Odd fits to note: anything floating, sunk into the body or pointing the wrong way, on either race.

### Sounds
- [x] Putting gear on or taking it off: a leather-and-metal **clank** (not when you first join with gear already on).
- [x] Picking up a **Rare** sack: a bright chime. **Epic:** a longer shimmering run. **Legendary:** a little **fanfare**. Common: the usual pop.

## Roadmap 5.5: Loot & Equipment (Step G: weapon attacks)

Wear a weapon in the **Main Hand** (bag, Use on it). Then the **Mine** button (left click / RT / Q / Right Shift) attacks a foe in reach instead of mining.

### Melee: Ironbound Cudgel, Glacierheart Blade, Skyrender
- [x] Walk up to a boar and hold Mine: the weapon comes **off your back into your hand**, and your hero **winds up over the shoulder and smashes down** in front (leaning into it). The hit lands as the weapon comes down: numbers pop up, its health bar drops, a thud and white sparks.
- [x] You don't have to aim exactly: any mob **in front of you** (roughly where you're looking) and within reach gets hit. Reach is measured to the edge of the mob's body, so standing where it fights you is close enough.
- [x] The swing looks right on both races (Wayfarer and Thunderhoof).
- [x] **Skyrender / cudgel:** the head goes back over the shoulder, then the wrist snaps and the head **slams down square** into the ground in front (not sideways).
- [x] **Glacierheart Blade:** it slashes **edge-first** (you see its flat side from beside the hero, and its thin edge leads).

### No craters in a fight
- [x] While a mob is fighting you (and for 5 s after it dies), spells that miss and hit the ground (Pyroblast, Arcane Orb, EarthShatter, M.G.R.S. bolts) still flash, hurt and stun mobs in the blast, but **don't dig holes**.
- [x] Out of a fight, EarthShatter and the M.G.R.S. still dig and crater as before.
- [x] Earthen Wall and Rising Pillar still raise rock in a fight (they build, they don't dig).
- [x] Each weapon feels different: cudgel **8** every 0.8 s, blade **13** every 0.6 s (fast), Skyrender **26** every 1.2 s (slow, huge). Reach about 2.6-3 m.
- [x] **Tab** a mob and stand next to it: Mine hits it even if you're not aiming right at it.
- [x] Aim at a block (no foe in reach) and Mine **still mines** as before.
- [x] A wall between you and the mob: no hit.
- [x] Stop fighting: after about 1.5 seconds the weapon goes back on your back.
- [x] With a weapon worn, your **backpack isn't drawn** (the weapon hangs there instead); the bag's extra slots still work. Take the weapon off and the backpack comes back.
- [x] Power counts: with the Crown (+8) and Skyrender (+26), hits are about a third bigger.
- [x] Kills from weapon hits still give loot sacks and XP, and hitting a mob pulls its aggro like a spell does (the Paladin's 2× threat applies).
- [x] Holding the M.G.R.S. in your hand still shoots Mana Bolts instead (it wins over the worn weapon).

### Ranged: Shardglass Wand
- [x] With the wand worn, aim at (or Tab) a mob up to about 22 m away and hold Mine: it fires small **pink-cyan crystal bolts** (9 damage every 0.7 s), **free** (no mana).
- [x] Aim at the ground with no foe in range: Mine mines as usual (the wand never digs).

### Feel (talk it over)
- [x] Are weapons too strong or too weak next to spells? Should melee have a bigger swing animation?

---

## Roadmap 5.6: Experience & Levels (Step A: XP and levels)

**F1** takes every hero up a level; **Shift+F1** puts everyone back to level 1. (The level badge and XP bar arrive in Step B, below.)

### Earning XP
- [x] Kill mobs: Boar 20 XP, Wolf 15, Rhino 40, Crystal Slime 25 (its pieces 10 and 5). About **5 boars** to reach level 2.
- [x] **Both** players get the full XP when a mob dies within about 40 m of them, even if only one fought it (the healer too).
- [x] A partner far away (more than 40 m, e.g. back at base) gets nothing for that kill.

### Levelling up
- [x] Each level adds **+10 max health** and **+5 max mana** (watch the numbers on the bars), and **refills** both.
- [x] Levelling up in the middle of casting doesn't cancel the cast.
- [x] A knocked-out hero who levels up doesn't wake up early.
- [x] Level 20 is the cap (F1 stops there).
- [x] Gear Stamina and level both count: put on Stamina gear at level 5 and the maximum includes both.

### Saving
- [x] Get to level 3 or so, save and quit (or F5), come back: same level and XP, and the bigger health and mana. Save format is now 8; older saves start at level 1.

## Roadmap 5.6: Experience & Levels (Step B: HUD & celebration)

### In each player's own view
- [x] A **gold starburst badge** with your level number sits at the left end of your action bar. Two-digit levels show both digits (**10**, not "1").
- [x] A thin **gold XP bar** runs under the action bar with **"40 / 135 XP"**; it fills as you kill things and empties on a level-up. At level 20 it's full and says **Max level**.
- [x] Kills show **+20 XP** in gold over your hero; several quick kills add up (**+35 XP**).
- [x] A level-up shows a big **LEVEL UP! Level 5** burst, and the badge bounces.
- [x] Each player's badge, bar and text only show their own level, in their own half of the screen.

### Everyone sees and hears it
- [x] A **golden pillar of light** shoots up round the hero who levelled and fades in under 2 s, with a warm glow (the partner sees it too).
- [x] A short **level-up fanfare** plays.
- [x] **F1** for a quick check of all of the above; both heroes level at once, so you should see two pillars.

---

## Fix to check: seeing under the world through dug holes

GPU occlusion culling is off in the 15:30+ build (it can wrongly hide a whole chunk under the surface, especially with two split-screen cameras). The faster GPU drawing stays on.
- [x] In split-screen, dig a deep hole (10+ blocks, through the layer change around y = 64) and look down it from both players' views: the walls are there all the way down, no grey void.
- [x] Blow craters and dig tunnels around a base: no see-through gaps.
- [x] **F4** with 20+ mobs: still around 120+ FPS? (Turning occlusion off draws more hidden ground; tell me if it dropped.)

### Notes
- 20+ mobs, 0.5.6 build, 2560×1440: Classic split 131-140 FPS, one player 141-166, but Dynamic split only 83 FPS (needs a look).

---

## Roadmap 5.7: Gathering Professions (Steps A-B: skills & ore)

**Handy keys:** **Ctrl+F1** gives every hero +25 in each profession (**Ctrl+Shift+F1**: back to 1). **F6** goes to the nearest ruin, handy for reaching Red Crags or Snowy Peaks.

### Mining & the skill gate
- [ ] At Mining 1, hit an **Iron Ore** block: nothing breaks, and **Requires Mining 25** shows in your own view (not more than every 1.5 s while you hold Mine).
- [ ] **Ctrl+F1** (Mining 26), then mine the iron: it breaks and drops an **Iron Ore** item.
- [ ] Mana Crystal Ore needs **Mining 50** (Ctrl+F1 twice).
- [ ] Breaking ore sometimes shows **Mining 12**-style text, often at first and less as the ore gets easy for you.
- [ ] A skilled miner sometimes gets a bonus ore piece straight into the bag.
- [ ] Spells and the M.G.R.S.'s craters don't break ore; it's left standing.

### Finding ore (Derek: 5 minutes of mining found only lots of copper)
- [ ] Iron and Mana Crystal are only in **Red Crags** and **Snowy Peaks**, deep: Iron below **y 56**, Mana Crystal below **y 40** (most below 30). Dig down there: can you find some iron in a few minutes?
- [ ] Do Iron (~2% of the rock there) and Mana Crystal (~0.4-0.6%) feel too rare, too common, or right? Copper is ~3-4% everywhere.
- [ ] Is the Mana Crystal item easy enough to get for the M.G.R.S. (5), Home Totem (1), Pick of Destiny (3) and Delver's Rucksack (3)?

### Saving
- [ ] Raise a skill, save (F5) and reload: same skills. Save format is now 9; older saves start every profession at 1.

### Notes

## Roadmap 5.7: Gathering Professions (Step C: herbs)

### Finding and picking herbs
- [ ] Herbs grow on open ground, about one every 20 blocks where they belong: golden **Sunpetal** (Meadows, Thunderhoof Plains), icy blue **Frostbloom** (Snowy Peaks), thorny **Emberthorn** with glowing red buds (Red Crags, Desert). Their flowers glow a little. Easy to spot?
- [ ] Aim at a herb (or the block it stands on) and press **Interact**: a **1.5 s gather** on the cast bar with the herb's name and icon, then the herb vanishes and 1-2 go into your bag (**LOOT!**-style pickup text). Frostbloom and Emberthorn sometimes give an essence too.
- [ ] Moving, jumping or mining during the gather cancels it (the bar turns red).
- [ ] Frostbloom needs **Herbalism 25**, Emberthorn **50**: below that, **Requires Herbalism 25** and no gather (Ctrl+F1 to raise it).
- [ ] Picking raises Herbalism now and then (**Herbalism 12** text).
- [ ] Interact aimed at a chest still opens it; aimed at nothing still opens crafting.

### Two players
- [ ] A herb one player picks disappears in the partner's view too.
- [ ] Both try to pick the same herb: only one gets it; the other's gather stops.

### Growing back & saving
- [ ] A picked herb grows back after **5 minutes** in the same spot.
- [ ] Pick a herb, save (F5), reload: still gone, and back on time. Save format is now 10.

### Notes

## Roadmap 5.7: Gathering Professions (Step D: skinning & salvaging)

**Handy keys:** **F10** spawns the next mob kind near every hero. **Ctrl+F1** raises every profession by 25.

### Skinning a body
- [ ] Kill a **boar**: its body stays down for about **20 s** (not 2) and **Skin** floats over it in your view.
- [ ] Aim at the body and press **Interact**: the cast bar says **Skinning Bristleback Boar** for 1.5 s, then **1-2 Boar Hide** (sometimes a tusk) go into your bag, on top of your loot sack.
- [ ] You can only skin each body **once**; the hint goes away after.
- [ ] Moving, jumping or mining during it cancels it.
- [ ] Wolves need **Skinning 15**, rhinos **40**: below that the hint is red (**Skinning 40**) and Interact says **Requires Skinning 40**.
- [ ] Skinning raises Skinning now and then (**Skinning 12** text).

### Salvaging slimes
- [ ] A big Crystal Slime splits as before; only its **smallest pieces** leave a body to **Salvage** (Skinning 25), for a Crystal Shard each.

### Two players
- [ ] Both fight the same boar: **each** sees **Skin** over it and can skin it once for their own hides.
- [ ] A hero who didn't fight it sees no hint and can't skin it.
- [ ] Once both have skinned it, the body goes straight away; otherwise it waits the full 20 s.

### Notes

## Roadmap 5.7: Gathering Professions (Step E: heat maps, panel & sounds)

### Resource heat maps (World Map, M)
- [ ] At **Mining 10** or **Herbalism 10** the World Map's hints show **F Resources** (gamepad **X**, right keyboard **;**). Below 10 there's no hint and F does nothing on the map.
- [ ] **F** cycles **Off -> Ore -> Herbs -> Off** (skipping a map you haven't unlocked). The hint names the tiers shown, in their colours.
- [ ] **Ore**: a see-through wash where ore is rich, in the tiers you can mine: copper (orange) only until you reach **Mining 25**; after that iron (red) and, from **50**, Mana Crystal (purple), blended where both are rich. Copper drops off once you can mine iron (it's everywhere anyway).
- [ ] **Herbs**: Sunpetal (gold), Frostbloom (blue), Emberthorn (red) over the biomes they grow in, only the ones your Herbalism can pick.
- [ ] The wash covers unexplored (fogged) land too, as a hint of where to go, without showing the land itself.
- [ ] The **minimap** shows the same heat map as your World Map.
- [ ] Each player's choice is their own (one on Ore, the other on Herbs or Off).
- [ ] Does it actually help you find iron and Mana Crystal? (Your copper-only mining run!)

### Professions in the bag (B)
- [ ] A **Professions** row under Worn gear: **Mining**, **Herbalism**, **Skinning**, each with its number and a bar towards 100; it updates as you skill up.

### Sounds
- [ ] Picking a herb: a leafy rustle and a soft pluck. Skinning: two knife strokes. Both heard by the partner nearby.
- [ ] A skill-up: two small bells (quiet, only for you).

### Notes

---

## Roadmap 5.8: Crafting Professions

The whole 5.8 checklist (steps A-F) is under **step G of 5.8 in `ROADMAP.md`**. Tick the boxes there.
