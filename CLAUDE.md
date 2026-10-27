# Project: Voxelforge
A hybrid voxel-sandbox and stylized fantasy action-RPG (combining *Minecraft* world-building with *World of Warcraft* classes/combat).
- **Current Progress & Active Milestone:** Always reference `@ROADMAP.md`.

## Core Architectural Laws (NEVER VIOLATE)
1. **No Single-Player Shortcuts:** Ban all uses of `GetPlayer(0)`, `Camera.main`, or global player singletons. Every method affecting gameplay, UI, or audio must accept an explicit `PlayerController` or `InstigatorPlayerID`.
2. **Host-Authority Loopback:** Even in local split-screen, run the game in Listen-Server/Host mode. All voxel modifications, inventory changes, and combat damage must flow through authoritative Server RPCs/Commands.
3. **Message-Driven Voxels:** Never modify voxel chunk arrays directly from player input scripts. Always call `VoxelWorldManager.RequestModifyVoxel(coord, blockID, instigatorID)` so changes are logged to the `VoxelDeltaLog` and triggers mesh regeneration cleanly.
4. **Performance First:** Voxel meshing and procedural noise generation must run off the main game thread using Greedy Meshing and hidden-face culling.
5. **Split-Screen UI Isolation:** All player HUDs (action bars, inventories, health bars) must attach to the individual `LocalPlayer` viewport, never the global screen viewport.

## Workflow Instructions for Claude
- **Before coding a new milestone:** Start in Plan Mode (`Shift+Tab`). Read `@ROADMAP.md`, propose the file structure and data contracts, and wait for approval.
- **After completing a milestone:** Run build/compile checks, help the user test in-editor via MCP, and mark the task `[x]` in `ROADMAP.md`.