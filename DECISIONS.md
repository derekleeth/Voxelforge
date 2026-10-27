# Voxelforge - Decision Log

> A record of the big choices we make and *why*, so future-us (and Claude) don't re-argue them.
> Newest decisions go at the top. Status is one of: **Proposed**, **Accepted**, **Replaced**.

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
  - `Voxelforge.Voxel`: chunks, meshing, noise (Burst + Collections + Mathematics, unsafe allowed)
  - `Voxelforge.Editor`: editor-only tools
  - `Voxelforge.Tests.EditMode`: unit tests
- **Packages:** Burst 2.0, Collections 6.6, Mathematics 1.4 and Test Framework 1.8 (all built into Unity 6.6). Netcode for GameObjects waits until Phase 2.1.
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
