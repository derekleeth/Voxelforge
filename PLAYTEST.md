# Play-Test List: Sunday 2026-11-01 (Derek + Kian)

Everything built today that still needs trying in the real game. Tick each box as you go, and jot down anything odd or not fun under **Notes**.

**Before you start:** close any running built copy of Voxelforge (it holds the network port, so the editor can't host). Then press Play, and both players join.

**Handy keys:** **F10** spawns the next mob kind near every hero (Shift+F10 clears them). **F6** takes the whole party to the nearest ruin someone hasn't found (**Shift+F6**: the nearest boss arena). **Tab** picks a target. **K** opens the class window. **M** opens the World Map. **F4** shows FPS.

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

### Notes
-
