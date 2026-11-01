# Play-Test List: Sunday 2026-11-01 (Derek + Kian)

Everything built today that still needs trying in the real game. Tick each box as you go, and jot down anything odd or not fun under **Notes**.

**Before you start:** close any running built copy of Voxelforge (it holds the network port, so the editor can't host). Then press Play, and both players join.

**Handy keys:** **F10** spawns the next mob kind near every hero (Shift+F10 clears them). **F6** takes the whole party to the nearest ruin someone hasn't found (**Shift+F6**: the nearest boss arena). **Tab** picks a target. **K** opens the class window. **M** opens the World Map. **B** opens the bag. **F2** gives every hero a loot kit (one of every piece of gear, 5 of each material). **F4** shows FPS.

---

## Roadmap 5.3: Threat & Aggro (Steps A-D)

Best set-up: one player is a **Runeforged Paladin** (the tank). The other is a **Geomancer Mage** or **Shaman**.

### Class window
- [ ] Press **K**. The Paladin's card lists all 4 spells (Rune Hammer, Bulwark Rune, Hallowed Forge, **Oathbrand**), and no text spills off the taller cards.
- [ ] Under the Paladin's description it says **Runeforged Oath: ...**
- [ ] The other classes show their 3 spells, with no empty rows.
- [ ] Still readable when the screen is split for 2 players.

### Holding aggro (the tank's job)
- [ ] F10 a **Bristleback Boar**. The Paladin hits it with Rune Hammer, then the mage blasts it. The boar should **stay on the Paladin**.
- [ ] The mage keeps nuking hard. Eventually the boar **turns on the mage** (they out-threatened the tank).
- [ ] A boar that's winding up a charge finishes it the way it was aiming before it switches.
- [ ] Healing the tank (Hallowed Forge, Wellspring Totem) makes the mobs fighting the tank a bit angry at the healer, but much less than damage does.
- [ ] If the tank gets knocked out, the mob goes after the next hero.
- [ ] A mob dragged too far from home still gives up and runs home (leash).

### Oathbrand (Paladin taunt, slot 4)
- [ ] Mage pulls the boar → Paladin presses **Tab**, then **4** → a **gold burst** and an **anvil ring** on the boar, **TAUNTED!** in the Paladin's view → the boar comes back to the Paladin.
- [ ] With no target in range, it says **No target** and costs no mana.
- [ ] Aiming at a mob without Tab-targeting it also works.
- [ ] Taunt a mob that's just wandering (not fighting) → it comes for the Paladin.
- [ ] Taunt a **Training Dummy** → its hay bales go to the Paladin.
- [ ] **Wolf pack:** taunt a wolf that's circling → it comes straight in, not waiting its turn.
- [ ] Cooldown is 8 s and it costs 10 mana. Does that feel right?

### Seeing threat (each player's own view)
- [ ] A **red eye** pops up next to every mob that's after **you**, only in **your** view.
- [ ] The eye moves to the other player's view when the mob switches to them.
- [ ] Mobs that spot you and charge get an eye, even before anyone has hit them.
- [ ] **Tab** a mob that's fighting: a thin **threat meter** appears under it. Tank: full red. Mage: green → yellow as you get close.
- [ ] Mage, nuke until nearly pulling it: **PULLING AGGRO!** pops up over your hero, not more than once every 3 s per mob.
- [ ] The eye, meter and warning never show in the wrong player's half of the screen.

### Feel & numbers (talk it over)
- [ ] Is the tank's job too easy or too hard? (Paladin threat ×2, Rune Hammer ×1.5, pull margins 110% next to it, 130% from range.)
- [ ] Can a healer accidentally pull mobs? Should they?
- [ ] F4 FPS with 20+ mobs still around 120+?

---

## Roadmap 5.4: Ruins & the Sunken Arena (Steps A-C)

Your saved world (seed 20261027) keeps all your edits; ruins only appear in ground nobody has dug or built on yet. Walk there, or press **F6** / **Shift+F6**. Positions are block x, z.

### Find the ruins near spawn (256, 256)
- [ ] **Frost Shrine** at about **(156, 361)**, up in the Snowy Peaks: a ring of snow-capped pillars round a glowing rune slab.
- [ ] **Broken Watchtower** at about **(230, 51)**: a mossy tower stump with a south doorway, its top lying in pieces to the east, plus a torch and a chest inside (the chest is empty until loot, 5.5).
- [ ] **Standing Stones** at about **(473, 486)**, Thunderhoof Plains: red and sandstone stones round a fire pit, with a lintel over one pair.
- [ ] More of each further out (another watchtower at (127, 524), stones at (17, 30)).
- [ ] Ruins sit on the ground properly on slopes (a brick or stone foundation under them, never floating), and no tree grows through one.

### The Sunken Arena (for the 5.9 boss)
- [ ] Nearest at about **(199, 1110)**, 855 blocks north of spawn. **Shift+F6** drops the party on the boss dais in the middle.
- [ ] A deep pit of dark Warded Stone with glowing gold runes, two stairways down (north and south), torch-lit pillars and a two-step dais in the middle.
- [ ] **Warded Stone can't be mined, and EarthShatter or Pyroblast can't crater it.** Ruin Bricks can be mined.
- [ ] Mobs that chase you follow you down the stairs and back up.

### Finding them: map markers & discovery (Step C)
- [ ] Walk up to a ruin: **DISCOVERED! Broken Watchtower** (or whichever) pops up, only in the finder's view, once.
- [ ] Open the map (**M**) and check the minimap: a **brown broken-tower** marker for ruins, a **red ring** for arenas, but only where *that* player has explored. The other player doesn't see it until they've been there too.
- [ ] Ruins don't show as arrows round the map's edge.
- [ ] After quitting and coming back, found ruins are still on your map, and loading the saved map doesn't pop up "DISCOVERED!" again.
- [ ] **F6** again and again hops the party to new ruins each time (it prefers ones someone hasn't found). It waits for the land to load and then lands everyone side by side.
- [ ] F6 into a site where neither player has been: both get DISCOVERED!

### New blocks
- [ ] Ruin Brick, Mossy Ruin Brick and Warded Stone look right up close, and sound like stone to walk on and mine.

---

## Roadmap 5.5: Loot & Equipment (Step A)

Step A is the loot data, so most of it is checked by looking in the bag after **F2** (mob drops come in Step B).

### The loot kit and rarity colours (F2, then B)
- [ ] Press **F2**: both players get pickup text for each item, only in their own view.
- [ ] Open the bag (**B**). The **Gear & Tools** tab has 16 new pieces; the **Materials** tab has 11 materials (5 of each).
- [ ] Point at items. Their names show in **rarity colours**: Common white (Bristlehide Cap), Rare blue (Tuskhide Jerkin, Earth Essence), Epic purple (Crown of the Crystal Deep), Legendary orange (**Skyrender, Maul of the High Mesa**).
- [ ] Gear says its slot and stats, e.g. "Rare Chest: Armor 11, Stamina 6". Materials say "Materials" (essences: "Rare Materials").
- [ ] Every item has a picture in its slot: hides, tusk, fang, horn, crystal shard, four glowing essences in gold cages, caps/hood/crown, jerkins/hauberk, leggings/greaves, boots, cudgel, wand, blade, the big molten maul, buckler and shield.
- [ ] Hold a weapon (select it on the hotbar): the cudgel, wand, blade and maul sit in the hand like a tool. Armour pieces are held small and upright (just stand-ins until 5.9).
- [ ] Materials stack (press F2 twice: 10 Boar Hide in one slot); gear doesn't.
- [ ] Send to Partner and dropping work for the new items.

## Roadmap 5.5: Loot & Equipment (Step B: personal loot)

Best set-up: both players fight the same mobs. **F10** spawns a mob kind by each hero (press again for the next kind).

### Mob drops, one sack each
- [ ] Both players hit a **Boar** and kill it: a sack pops out of the body **for each player**, dyed in that player's colour.
- [ ] Walk over your **partner's** sack: it doesn't fly to you, and you can't pick it up. Your own flies into your bag.
- [ ] Pickups say **+2 Boar Hide** for common materials; gear and Rare+ items say **LOOT!** with the name in its rarity colour (blue, purple). Epic gets a comic burst; **LEGENDARY!** for Skyrender. Only in the looter's own view.
- [ ] A **Rare** sack has a soft coloured glow; **Epic** and **Legendary** sacks have a pillar of light you can see across a fight.
- [ ] The **healer** (who only healed the tank) still gets their own sack.
- [ ] One player kills a mob alone: only they get a sack.
- [ ] Each mob drops its own stuff: Boar (Boar Hide, Boar Tusk, Earth Essence), Wolf (Wolf Pelt, Frostfang, Frost Essence), Rhino (Rhino Hide, Thunder Horn, Storm Essence), Crystal Slimes (Crystal Shards, Arcane Essence; small ones only shards).
- [ ] Roughly a third of kills give a piece of gear (generous for today). Too many? Too few?
- [ ] Leave your sack lying there for a while: it doesn't vanish while you're still in the game.
- [ ] Bag full: your sack waits ("Bag full!") until you make room.

### Ruin chests (one-time personal cache)
- [ ] **F6** to a Broken Watchtower and open its chest (aim + Interact): a sack in your colour pops out of the chest and flies into your bag (2 rolls: gear, plus a chance of essences).
- [ ] Close and open it again: **nothing more** for you. Your partner opens it: **they** get their own cache.
- [ ] The chest still works as normal shared storage after that.
- [ ] Place your own Storage Chest and open it: no cache.
- [ ] Save and quit (or F5), come back, open the same ruin chest: still nothing (it remembers). Save format is now 7; your old save loads fine.

### Notes
-
