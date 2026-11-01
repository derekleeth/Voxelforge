# Voxelforge
A mix of my son's favorite game with my favorite game mashed together into a multiplayer RPG game.

## Controls

Gamepad buttons use Xbox names (A = bottom, B = right, X = left, Y = top; View = the small left button, Menu = the small right button).

The **split keyboard** columns only matter when `LocalPlayerManager → Allow Split Keyboard` is on (a dev toggle, off by default). Then two people share one keyboard: the left player uses WASD and the right player uses the arrow keys.

### Joining

| Who | Press |
|---|---|
| Keyboard + mouse player | Space or Enter |
| Gamepad player | A or Menu |
| Split keyboard, left player | Space |
| Split keyboard, right player | Enter |

### Playing

| Action | Keyboard + mouse | Gamepad | Split keyboard, left | Split keyboard, right |
|---|---|---|---|---|
| Move | WASD | Left stick | WASD | Arrow keys |
| Look | Mouse | Right stick | — | — |
| Jump | Space | A | Space | Right Ctrl |
| Sprint | Hold Left Shift | Click left stick (L3) to toggle | Hold Left Shift | Hold Numpad 0 |
| Mine / fire the M.G.R.S. | Left click (hold) | RT (hold) | Q | Right Shift |
| Place block | Right click | LT | E | Enter |
| Next block | Mouse wheel up | RB | C | `/` |
| Previous block | Mouse wheel down | LB | Z | `.` |
| Ghost block on/off | G | Y | G | `'` |
| Interact (workbench, chest) | F | X | F | `;` |
| Target nearest creature (press again to cycle) | Tab | Click right stick (R3) | T | Numpad Enter |
| Action bar slots 1–4 | 1–4 | D-pad up / right / down / left | 1–4 | Numpad 1–4 |
| Action bar slots 5–8 | 5–8 | Hold B + d-pad up / right / down / left | 5–8 | Numpad 5–8 |
| Action bar slot 9 (racial ability) | 9 | Tap B | 9 | Numpad 9 |
| Bag (inventory) | B | Menu | B | Backspace |
| Class window | K | LT while the bag is open | K | `]` |
| World map | M | View | M | Numpad + |
| Hearth home | H | Open the map, then LT | H | `[` |
| Split-screen style (Dynamic / Classic) | F6 | — | F6 | — |
| Free the mouse cursor | Esc (click to grab it again) | — | — | — |

### In the windows

**Bag (inventory)**

| Action | Keyboard + mouse | Gamepad | Split keyboard, left | Split keyboard, right |
|---|---|---|---|---|
| Move cursor | Mouse | Left stick / d-pad | WASD | Arrow keys |
| Pick up / put down a stack | Click | A | Space | Right Ctrl |
| Use / wear (or take / store at a chest) | Right click | X | F | `;` |
| Send to partner | G | Y | G | `'` |
| Drop as a loot bag | Click outside the window | RT | Q | Right Shift |
| Sort | H | — (see note below) | H | `[` |
| Change tab | 1–5 | LB / RB | Z / C or 1–5 | `.` / `/` |
| Scroll | Mouse wheel | — | — | — |
| Close | B | B | B | Backspace |

**World map**

| Action | Keyboard + mouse | Gamepad | Split keyboard, left | Split keyboard, right |
|---|---|---|---|---|
| Pan | WASD | Left stick | WASD | Arrow keys |
| Zoom | Mouse wheel | LB / RB | Z / C | `.` / `/` |
| Back to your hero | Space | A | Space | Right Ctrl |
| Hearth home | Right click | LT | E | Enter |
| Close | M | View or tap B | M | Numpad + |

**Class window and race window**

| Action | Keyboard + mouse | Gamepad | Split keyboard, left | Split keyboard, right |
|---|---|---|---|---|
| Pick a card | A / D, number keys, or click | D-pad or left stick left / right | A / D or number keys | Arrow keys |
| Choose | Space or click | A | Space | Right Ctrl |
| Close (class window only) | K | B | K | `]` |

**Crafting (Interact at a workbench)**: next / previous block buttons choose a recipe, and Place crafts it.

**Hearth prompt (after H)**: Place confirms. Home or Interact closes it, and it closes by itself after 6 seconds. Moving, jumping or mining cancels the 2-second cast.

### F keys and debug keys

These are read straight from the keyboard, so on a shared PC any player can press them.

| Key | What it does | Where |
|---|---|---|
| F3 | Show / hide the debug overlay (FPS, mob counts) | Editor and build |
| F4 | Write an FPS snapshot to `Builds/Editor/frame-stats.txt` (in the editor) | Editor and build |
| F5 | Save the world now | Editor and build |
| F6 | Switch split-screen style: Dynamic / Classic | Editor and build |
| F7 | Spawn nav test critters by every hero (host) | Editor and build |
| F8 | Show / hide the test critters' paths (host) | Editor and build |
| F9 | Remove all test critters (host) | Editor and build |
| F10 | Spawn the next kind of mob by every hero (host) | Editor and build |
| Shift + F10 | Remove the mobs F10 spawned (host) | Editor and build |
| Ctrl + F10 | Turn natural mob spawning on / off (host) | Editor and build |
| F11 | Toggle full screen | Build only |
| F12 | Save a screenshot (to `Builds/Editor/Screenshots` in the editor) | Editor and build |
| Shift + Esc | Quit the game | Build only |

The world also saves by itself every 2 minutes and when you quit.

### Known gaps

- **Gamepads can't switch split-screen style.** It used to be the right-stick click, but that's now Target. Use F6 for now; the 6.3 settings screen will take this over.
- **The bag's on-screen hints say "View: Sort" for gamepads, but View opens the map instead,** and Sort has no gamepad button.
