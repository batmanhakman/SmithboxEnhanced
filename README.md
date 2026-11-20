# Smithbox — Dark Souls 3 Custom Map Creation Suite (Master Spec & State Tracker)

> **Instructions for AI Orchestrator (Gemini Neon):**
> This document is the single source of truth for building a complete **Dark Souls 3 (DS3) Custom Map Creation Suite** inside Smithbox.
> 1. **Update the Live State Tracker** below after every completed task, build check, or discovered blocker.
> 2. **Follow Vertical Slices strictly.** Do not start Milestone $N+1$ until Milestone $N$ builds cleanly and passes its Verification Gate.
> 3. **DS3-Only Scope.** Target Dark Souls 3 formats exclusively (`MSB3`, `FLVER2`, **Havok 2014 `hk_2014.1.0-r1` `hknp` 64-bit Packfile**, `EMEVD`, `NVA`, `GPARAM`). Do **not** build `TAG0`/Tagfile (Sekiro/ER/AC6 Havok 2016+) or legacy `hkp` (DS1/DS2) collision pipelines for DS3.
> 4. **Extend, Don't Rewrite.** Reuse Smithbox's existing `SoulsFormats`, `Havok.*` unified library, `Veldrid`, `Hexa.NET.ImGui`, and Alias systems.

---

## Live State Tracker

| Field | Status |
| :--- | :--- |
| **Current Milestone** | Milestone 1 — Vertical Slice ("The Standing Box") — Implementation & headless tests complete; awaiting user's in-game ModEngine2 Verification Gate check |
| **Active Task** | In-editor Smithbox & in-game Dark Souls 3 ModEngine2 Verification Gate (`m31_90_00_00` or selected test map slot) |
| **Last Verified Build** | `dotnet build src\Smithbox.Vulkan\Smithbox.Vulkan.csproj --no-restore -v minimal` — 0 Errors; `dotnet test src\Smithbox.Tests.DS3Creator\Smithbox.Tests.DS3Creator.csproj --no-restore -v normal` — 12/12 Passed (2026-11-19) |
| **Current Blockers** | `None` |
| **Next Immediate Action** | Implement `StudioCore.Editors.MapEditor.DS3Creator`, build, and run headless `Smithbox.Tests.DS3Creator` suite. |

### Progress Legend
- `[ ]` Not started
- `[~]` In progress
- `[x]` Completed & verified
- `[!]` Blocked (must document in **Decision & Blocker Log**)

---

## Architecture & Scope Guardrails

1. **Havok Version Lock (Critical — Audited & Corrected in M0):** Dark Souls 3 uses **Havok 2014 (`hk_2014.1.0-r1`) 64-bit Little-Endian Packfiles** (`HavokContainer.Packfile`, `HeaderVersion = 11`, `PointerSize = 8`) containing **Havok New Physics (`hknp`)**: `hkRootLevelContainer` → `hknpPhysicsSceneData` → `hknpPhysicsSystemData` → `hknpBodyCinfo` → `Havok.hk2014.fsnpCustomParamCompressedMeshShape` inside `h*.hkx.dcx` (Hi-res) and `l*.hkx.dcx` (Lo-res) within `BXF4` `.hkxbhd`/`.hkxbdt` binders. *(Correction: DS3 does **not** use legacy `hkp` / `hkpBvCompressedMeshShape`—that is DES/DS1/DS1R/DS2S—and does **not** use `TAG0` Tagfiles—that is SDT/ER/AC6.)*
2. **No Reinventing Blender:** Do **not** build manual vertex-pushing, sculpting, or mesh-editing tools inside Smithbox. Smithbox handles **blockout primitives, kitbashing, placement, property assignment, collision/navmesh generation, and export**.
3. **In-Editor Workflow:** The user must be able to scaffold a new map, drag-and-drop assets, generate collision/navmesh, place cross-map enemies, and wire basic events **entirely inside Smithbox**.
4. **Incremental Verification:** Every milestone ends with a concrete **Verification Gate** (unit/round-trip test + in-game DS3 test via ModEngine2). Never mark a milestone complete based solely on successful serialization.
5. **Preserve Existing Smithbox Features:** Follow existing C# coding conventions and keep new DS3 map-creation modules cleanly separated under a dedicated namespace (`StudioCore.Editors.MapEditor.DS3Creator`).

---

## Milestone 0 — Repository Investigation & Architecture Audit

**Objective:** Map out Smithbox's existing DS3 capabilities before writing feature code.

- [x] `M0.1` Inspect Smithbox's `MapEditor`, `ModelEditor`, `SoulsFormats` integration, and `Alias` system for DS3.
- [x] `M0.2` Audit `Havok.*` / `SoulsFormats` support for **Havok 2014 (`hk_2014.1.0-r1`)** serialization/deserialization (verified pure-C# `Havok.Files.HavokFile` + `HavokPackfileSerializer` + `HavokCompressedMeshBuilder` + `HavokCompressedMeshGenerator_hk2014`; no external CLI wrapper needed).
- [x] `M0.3` Inspect how Smithbox loads, renders (`Veldrid`), and saves DS3 `MSB3`, `FLVER2`, `TPF`, `GPARAM`, `BTL`, `luabnd`, `ffxbnd`, and `EMEVD`.
- [x] `M0.4` Document findings in the **Architecture Audit Summary** section at the bottom of this file (reusable components, missing pieces, files to modify, new modules to create).

**Verification Gate:** Architecture Audit Summary is populated and existing Smithbox solution compiles cleanly (`Smithbox.Vulkan.csproj`: 0 Errors).

---

## Milestone 1 — Vertical Slice: "The Standing Box" (Map Scaffolder + DS3 Collision)

**Objective:** Prove the end-to-end DS3 map pipeline works in-game. A user can create a blank DS3 map inside Smithbox, place a primitive floor box, bake DS3 Havok 2014 collision, export, and physically stand on it in Dark Souls 3.

### 1A. New DS3 Map Scaffolder
- [x] `M1.1` Create a "New DS3 Map" wizard targeting a clean/test DS3 map slot (e.g., replacing an unused or selected map ID) inside the active project directory.
- [x] `M1.2` Generate minimal valid boilerplate files: `MSB3` (with default player spawn), baseline `GPARAM` (visible lighting), empty `BTL`, and empty map/collision binders.

### 1B. In-Editor Primitive Blockout
- [x] `M1.3` Add a primitive spawner in the Map Editor viewport (Box/Floor, Wall, Ramp, Stairs) that generates valid DS3 `FLVER2` geometry with a default visible material/texture (`MTD`/`TPF`) and registers it as a `MapPiece` in `MSB3`.

### 1C. DS3 Havok 2014 (`hknp`) Collision Pipeline
- [x] `M1.4` Build geometry extractor: extract world-space vertices and triangle indices from selected `FLVER2` map pieces/primitives, deduplicate vertices, and strip degenerate triangles.
- [x] `M1.5` Implement DS3 Havok 2014 collision builder: construct valid `hknp` (`fsnpCustomParamCompressedMeshShape` 64-bit Packfile) collision meshes (Hi-res `h*` and Lo-res `l*` `.hkx`) with valid bounding volumes, spatial acceleration, and default walkable hit-material flags.
- [x] `M1.6` Auto-register generated collision parts in `MSB3` (including required `DrawGroups` / `DispGroups` so map geometry renders when standing on the collision).
- [x] `M1.7` Add collision viewport visualization modes (Wireframe collision, Combined model + collision, Bounding volumes).

### 1D. Validation & Round-Trip Testing
- [x] `M1.8` Implement collision validator (checks degenerate triangles, invalid indices, missing spatial structures, and round-trip reload in Smithbox).

**Verification Gate (Pending In-Game Test):** Exported map loads in Dark Souls 3 via ModEngine2; player spawns, sees the lit primitive floor, walks on it without falling through, and camera collides properly.

---

## Milestone 2 — Global Asset Browser & Drag-and-Drop Kitbashing

**Objective:** Make Smithbox aware of all DS3 models with visual thumbnails, enabling Unity/Unreal-style drag-and-drop level building from across all vanilla DS3 maps.

### 2A. Global Asset Indexer
- [ ] `M2.1` Build a background indexer that scans DS3's `map/`, `obj/`, and `chr/` archives (without requiring full game unpacking) and pairs model IDs with Smithbox's community Alias names and tags.
- [ ] `M2.2` Save a fast, searchable local index filterable by **Category** (Map Pieces, Objects, Enemies), **Origin Map**, and **Search Text**.

### 2B. On-Demand Thumbnail Baker
- [ ] `M2.3` Use Smithbox's existing `Veldrid` renderer with an offscreen camera to render 128×128 preview thumbnails on demand as the user scrolls, caching PNGs to disk.

### 2C. Drag-and-Drop + Cross-Map Asset Copying
- [ ] `M2.4` Implement ImGui drag-and-drop (`BeginDragDropSource` on Asset Browser tiles → `BeginDragDropTarget` on the 3D Map Viewport).
- [ ] `M2.5` Implement viewport raycasting on drop to place the asset cleanly on existing terrain/collision (or a default ground plane).
- [ ] `M2.6` Automate cross-map dependency copying: when dropping a `MapPiece` or `Object` from another DS3 map, automatically copy its `FLVER2` and required `TPF` textures into the target map's `mapbnd` and register `Model` + `Part` entries in `MSB3`.

### 2D. Hit-Material Assignment
- [ ] `M2.7` Allow assigning DS3 surface/hit-material presets (Stone, Wood, Water, Killplane, Camera-Ignore) per mesh/selection before baking collision.

**Verification Gate:** User drags a pillar from Lothric Castle and a statue from Irithyll into the custom map, bakes collision, and both render with textures and solid collision in-game.

---

## Milestone 3 — Navmesh & Cross-Map Enemy Importer

**Objective:** Allow dropping enemies from *any* DS3 area into the custom map with working pathfinding, AI scripts, and attack VFX.

### 3A. Basic Navmesh (`NVA` / Havok AI) Generation
- [ ] `M3.1` Implement a walkable-surface navmesh generator from the map's collision geometry (filtering by slope angle and agent height).
- [ ] `M3.2` Export DS3-compatible navmesh data (`NVA` + Havok AI navmesh `.hkx`) and link it in `MSB3` so enemies can navigate the custom level.

### 3B. Cross-Map Enemy Importer
- [ ] `M3.3` Automate `MSB3` enemy registration: add `Model.Enemy` + `Part.Enemy`, assign valid `NpcParamID` / `NpcThinkParamID`, link `CollisionName` for proper drawgroups, and clear conflicting source `EntityID`s.
- [ ] `M3.4` **Lua AI Injection (`luabnd`):** Automatically locate and copy the enemy's AI Lua scripts from its origin map's `script/mXX_XX_XX_XX.luabnd.dcx` into the custom map's `luabnd` and update `luainfo`.
- [ ] `M3.5` **SFX Merging (`ffxbnd`):** Copy the enemy's required particle effects from its origin map's `sfxbnd` into the custom map's `ffxbnd` so attacks, magic, and weapon trails render visibly.

**Verification Gate:** User drags a Pontiff Knight and a Lothric Knight into the custom map; in-game, both render, patrol/chase the player across the custom navmesh, attack with visible VFX, and take/deal damage properly.

---

## Milestone 4 — Visual Event Template Builder (`EMEVD`)

**Objective:** Enable custom gameplay encounters (bosses, ambushes, doors, bonfires) without requiring the user to hand-code raw EMEVD scripts.

- [ ] `M4.1` Implement `SoulsFormats.EMEVD` read/write integration for the custom map's `.emevd.dcx`.
- [ ] `M4.2` Add Viewport-to-Event linking: placing `Region` trigger boxes, fog walls, levers, bonfires, or enemies auto-assigns unique, conflict-free `EntityID`s.
- [ ] `M4.3` Build the **Visual Event Presets UI** with compilable DS3 event templates:
  - **Boss Encounter:** Fog wall + Trigger Region + Boss HP bar + Music + Victory Flag + Item Reward.
  - **Ambush / Spawn Trigger:** Player enters Region → Enable Enemy / Play animation.
  - **Interactive Object:** Pull lever / open door → Set Event Flag + play object animation.
  - **Bonfire & Warp:** Register a functional resting bonfire.
- [ ] `M4.4` Ensure generated `.emevd.dcx` files remain clean and compatible with DarkScript3 for advanced manual editing.

**Verification Gate:** Custom map features a working bonfire, a trigger-box ambush, and a functional boss encounter (fog wall enters, HP bar appears, victory clears fog wall and awards item).

---

## Milestone 5 — Dark Souls 1 to Dark Souls 3 Enemy Porting Pipeline

**Objective:** Provide a guided, semi-automated DS1-to-DS3 enemy importer inside Smithbox, focusing on what can be reliably automated.

- [ ] `M5.1` **Model & Material Conversion:** Convert DS1 `FLVER2` meshes to DS3 `FLVER2`, remap DS1 `.mtd` shader paths to valid DS3 `.mtd` equivalents, and repack textures into DS3 `chrbnd.dcx` / `texbnd.dcx`.
- [ ] `M5.2` **Animation & TAE Pipeline:** Integrate/invoke existing Havok 2010 → Havok 2014 conversion tooling for skeletons and animations; remap DS1 `TAE` animation IDs to DS3's layout, strip unsupported DS1 events, and remap sound event IDs.
- [ ] `M5.3` **Param Scaffolding:** Auto-generate starter rows in `NpcParam`, `NpcThinkParam`, and `AtkParam_Npc` wired to the ported `cXXXX` ID.
- [ ] `M5.4` **AI & VFX Fallbacks:** Attach a user-selectable DS3 AI template (e.g., *Generic Sword & Shield*, *Aggressive Beast*) instead of attempting raw DS1 Lua translation; map missing VFX to closest-match DS3 common effects (`ffxbnd`).

**Verification Gate:** A ported DS1 enemy (e.g., Hollow Soldier or Black Knight) spawns in the custom DS3 map with intact textures, working animations/hitboxes, and functional combat behavior via a DS3 AI template.

---

## Architecture Audit Summary (Populated in Milestone 0)

- **Existing Reusable Components:**
  - **Solution & Rendering (`Smithbox.Vulkan`, `Smithbox.Program`, `Smithbox.Data`, `Veldrid`, `Hexa.NET.ImGui`):** Full Vulkan viewport (`VulkanViewport`, `RenderScene`, `SceneRenderer`), material/shader pipeline (`FlverResource`, `MaterialBank`, `MaterialBinding`), collision mesh uploader (`HavokCollisionMeshBuilder`, `CollisionSubmeshProvider`, `CollisionTransparency`, `MeshProviderCache.RebuildCollisionRenderables`), and debug line batching (`DbgPrimLineBatch`, `DebugPrimitiveRenderableProxy` used by `NavigationOverlayRenderer`).
  - **SoulsFormats (`src/Andre/SoulsFormats`):** Full read/write support for DS3 `MSB3` (`/map/mapstudio/{mapId}.msb.dcx`), `FLVER2` (version `0x20014`, inside `.mapbnd.dcx` `BND4` at `/map/{assetMapId}/{assetMapId}_{id6}.mapbnd.dcx`), `BXF4` (`h*.hkxbhd`/`.hkxbdt`, `l*.hkxbhd`/`.hkxbdt`), `TPF` (plus `/other/systex.tpf.dcx`), `BTL` (DS3 `Version = 6`, `LightSize = 0xC8`, `LongOffsets = true`), `GPARAM` (`GparamVersion.V3`, `WideStrings = true`, Yebis tonemapping), `NVA`, and `EMEVD` (plus existing `EmevdEditorScreen`, `NavigationTool` / `DotRecast`, `MapPiecePort`, `MapTextureTransfer`, `CollisionEditorTool`).
  - **Unified Havok Library (`src/Havok/*`):** Pure-C# read/write support for Havok 2014 64-bit Packfiles (`HavokFile`, `HavokProfile.DS3`, `HavokPackfileSerializer`, `HavokTypeRegistry20140100.xml`), plus game-verified compressed mesh generation (`HavokCompressedMeshBuilder` + `HavokCompressedMeshGenerator_hk2014`) and FLVER geometry extraction (`ModelCollisionExtractor` + `CollisionFlverReader`).
  - **DS3 Asset Conventions & Aliases:** Verified via `DS3-File-Dictionary.json`, `MapNames.json`, `PathBuilder.cs`, `ModelLocator.cs`, `TextureLocator.cs`, and `MsbGameAdapter.cs` (e.g., unused DS3 slots such as `m31_90_00_00`, `m32_90_00_00`, `m36_00_00_00`, `m30_02_00_00`, `m46_00_00_00`).
- **Missing Functionality / Gaps Addressed in Milestone 1:**
  - `CollisionEditorCreate` and `HavokCollisionBank.AddNewFiles` previously required an existing collision binder containing at least one `.hkx.dcx` template file and an existing `MSB3.Part.Collision` in the map—blocking blank/custom map workflows.
  - No wizard existed to scaffold a clean DS3 map (`MSB3` player spawn, baseline visible `GPARAM`, empty `BTL`, empty `h`/`l` `BXF4` collision binders, starter geometry).
  - No procedural `FLVER2` primitive blockout spawner or automated Havok 2014 packfile/mesh structural validator + wireframe/bounding-volume toggle existed for collisions.
  - Legacy `Smithbox.Tests` targets `net9.0` with broken project references and hardcoded author drive paths; a clean `net10.0` `Smithbox.Tests.DS3Creator` xUnit test suite is added.
- **Havok 2014 Strategy:**
  - Use Smithbox's pure-C# **Havok 2014 (`hk_2014.1.0-r1`, `HavokSdk.Hk2014`, 64-bit LE Packfile)** pipeline (`HavokCompressedMeshBuilder` → `HavokCompressedMeshGenerator_hk2014.BuildCompressedMeshShape(..., Variant.DS3)` → `hkRootLevelContainer` / `hknpPhysicsSceneData` / `hknpPhysicsSystemData` / `hknpMaterial` / `hknpBodyCinfo` → `HavokFile.Write` + `DCX_DFLT_10000_44_9`). No external CLI or native Havok SDK binary is needed.
- **Files to Modify:**
  - `src\Smithbox.Program\Editors\Map Editor\Data\HavokCollisionBank.cs` (allow adding collision files to empty/new BXF4 binders without an existing template entry)
  - `src\Smithbox.Program\Application\CFG.cs` (`Viewport_Collision_Wireframe`)
  - `src\Smithbox.Program\Renderer\Providers\CollisionSubmeshProvider.cs` & `CollisionTransparency.cs` (wireframe pipeline support + rebuild detection)
  - `src\Smithbox.Program\Editors\Map Editor\GUI\MapListView.cs` (`RefreshMapList()`), `MapEditorView.cs`, `MapToolWindow.cs`
- **New Modules to Create:**
  - Under `src\Smithbox.Program\Editors\Map Editor\Tools\DS3 Map Creator\` (`StudioCore.Editors.MapEditor.DS3Creator`): `DS3MapCreatorConstants.cs`, `DS3PrimitiveFlverBuilder.cs`, `DS3CollisionBuilder.cs`, `DS3CollisionValidator.cs`, `DS3MapScaffolder.cs`, `DS3CollisionOverlayRenderer.cs`, `DS3MapCreatorTool.cs`.
  - Under `src\Smithbox.Tests.DS3Creator\`: `Smithbox.Tests.DS3Creator.csproj`, `DS3Milestone1Tests.cs`.

---
## BONUS ** ONLY WHEN DONE **
- **Make it able to create new map id meaning, the game can load custom map ids created by the player**
## Decision & Blocker Log

| Date | Milestone | Entry / Decision / Blocker | Resolution |
| :--- | :--- | :--- | :--- |
| 2026-11-19 | `M0` | **Havok 2014 Format Clarification:** Master spec Guardrail #1 initially cited `hkp` (`hkpBvCompressedMeshShape`) and warned against `fsnpCustomParamCompressedMeshShape`. Audit of `HavokProfile.DS3`, `HavokCompressedMeshGenerator_hk2014.cs`, `CollisionEditorShapes.cs`, and `hknpCollisionMeshBuilder.cs` confirms DS3 uses **Havok 2014 `hk_2014.1.0-r1` 64-bit Packfiles** with `hknp` (`fsnpCustomParamCompressedMeshShape`); `hkp` is only DES/DS1/DS1R/DS2S. | Updated Guardrail #1 and Architecture Audit Summary; implemented Milestone 1 with pure-C# `Havok.hk2014` `fsnpCustomParamCompressedMeshShape` packfile serialization. |
| 2026-11-19 | `M0` | **Solution Build Target:** Building root `Smithbox.sln` stalls in `Smithbox.Release.csproj` (`AssembleRelease` copies thousands of output files). | Build `src\Smithbox.Vulkan\Smithbox.Vulkan.csproj --no-restore` (and `Smithbox.Tests.DS3Creator`) directly; passes with 0 errors. |
| 2026-11-19 | `M1` | **Test Infrastructure:** Legacy `src\Smithbox.Tests\Smithbox.Tests.csproj` targets `net9.0`, references missing `Smithbox.csproj`, and hardcodes external local paths. | Created standalone `src\Smithbox.Tests.DS3Creator\Smithbox.Tests.DS3Creator.csproj` targeting `net10.0` with headless unit + round-trip tests (preserving `src\Smithbox.Tests`). |