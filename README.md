# Smithbox
Smithbox is a modding tool for Elden Ring, Elden Ring: Nightreign, Armored Core VI, Sekiro, Dark Souls 3, Dark Souls 2, Dark Souls 1, Bloodborne, and Demon's Souls.

[![GitHub release](https://img.shields.io/github/release/vawser/Smithbox.svg)](https://github.com/vawser/Smithbox/releases/latest)
[![Github All Releases](https://img.shields.io/github/downloads/vawser/Smithbox/total.svg)](https://github.com/vawser/Smithbox/releases/latest)
[![Smithbox Discord](https://img.shields.io/badge/Discord%20-%237289DA.svg?&logo=discord&logoColor=white)](https://discord.gg/5p9bRKkK4J)

## Key Features
- **Map Editor**: a visual editor for editing maps.
- **Model Editor**: a model editor for viewing and editing models. Similar to the FLVER Editor.
- **Param Editor**: a table editor for editing parameters, which contain most of the data that defines each game.
- **Text Editor**: an editor for editing text localization.
- **Graphics Param Editor**: an editor for editing GPARAM files.
- **Material Editor**: an editor for editing MTD and MATBIN materials.
- **Texture Viewer**: a simple to use viewer for looking and extracting textures.
- **File Browser**: a browser of the files contained within the data directory, plus the project directory. Allows easy extraction.
- **Havok Editor**: an editor for editing HKX files. Supports ER and NR projects only.
- **Time Act Editor**: an editor for editing TAE files. Includes animation playback.

You no longer need to unpack a game for any of the editors.

## Usage
Once installed, click the **Smithbox.Vulkan.exe** to launch the fully featured version of Smithbox.

If your computer cannot use Vulkan (old graphics card), you should use **Smithbox.OpenGL.exe**. 
This provides most of the features of Smithbox, except for anything to do with the viewport, which mostly affects the Map and Model Editor.

## Links
Smithbox is a fork of the [DSMapStudio repository](https://github.com/soulsmods/DSMapStudio).

### Smithbox Tutorials
You can find helpful information and guides/tutorials about Smithbox at [soulsmodding.com](https://www.soulsmodding.com/doku.php?id=smithbox).

### Unpacking Files
If you want to unpack games files, you should use [Nuxe](https://github.com/JKAnderson/Nuxe/releases).

### Scripts
If you want to edit EMEVD scripts, you should use [DarkScript3](https://github.com/AinTunez/DarkScript3).

If you want to edit ESD scripts, you should use [ESDLang](https://github.com/thefifthmatt/ESDLang) or [ESDStudio](https://github.com/GompDS/ESDStudio) if you want a GUI interface.

## License
Smithbox is licensed under the [GNU General Public License v3.0 or later](LICENSE).

Smithbox is a fork of DSMapStudio, whose code (Copyright (c) 2018 Katalash, Meowmaritus) is used under the MIT License. Third-party components and their licenses are listed in [Licenses](src/Smithbox.Data/Licenses), which is also included in each release.

Smithbox does not include Oodle. Projects for games that use it copy `oo2core` from the game folder automatically.

## Support
If you enjoy Smithbox, feel free to support me (Vawser) via [Ko-fi](https://ko-fi.com/vawser)!

## Troubleshooting
### My game is telling me there is a 'Save Corruption' error when I launch my mod.

This is an issue with your mod launch setup. For most modern games, it is recommended that you use [ME3](https://github.com/garyttierney/me3) to launch your mods. Smithbox has support for creating a ME3 profile built-in.

For DS1, DS2 and pre 1.15.2 DS3, you will still need to use ModEngine2 or other mod launching solutions. In this case, you typically need to run the game with vanilla once when creating a new character. And then enable any mods. 

### Smithbox starts and then immediately closes

This is likely due to your graphics card lacking support for Vulkan. By default Smithbox will launch and try and use the Vulkan backend. 

If this occurs, you can go to %AppData%\Local\Smithbox\Configuration, open the Configuration.json file with a text editor, and then change the System_RenderingBackend variable to 0. This will make Smithbox use the OpenGL backend, which is supported by almost all graphics cards (however it will disable viewport functionality).

### Smithbox displays an error message box when I do X action

If you experience a crash, please report it to the [Smithbox Discord](https://discord.gg/5p9bRKkK4J) or post the error message in the Issues tab in this repository. This will help me find and fix the causes of such crashes.

### How do I use the latest build of Smithbox

To download the latest build of Smithbox, outside of the official releases, click on the green tick next to the latest commit when viewing the repository. Click on Details, and then on Summary. Scroll down, and you will see a link starting with: Smithbox-SHA, click on it to download the latest  build. 

Note: you will need a Github account to be able to navigate into the Github Actions page and download the build.

The latest build may be incomplete, as it is the in-development build of Smithbox, but it does allow you to immediately benefit from new additions and changes, whereas the official builds are only created when a substantial amount of changes has been finished.

## Credits (Smithbox)
* Vawser 
* ivi 
* nex3 
* gixxpunk 
* Strackeror 
* FireWolf700 
* GoogleBen 
* LordExelot 
* Pear0533 
* Metito 
* WarpZehpyr 
* twistedgwazi 
* FeeeeK 
* colaaaaaa123 
* alson041 
* gracenotes 

## Credits (DSMapStudio)
* Katalash
* philiquaz
* george
* thefifthmatt
* TKGP
* Nordgaren
* [Pav](https://github.com/JohrnaJohrna)
* [Meowmaritus](https://github.com/meowmaritus)
* [PredatorCZ](https://github.com/PredatorCZ)
* [Horkrux](https://github.com/horkrux)
* Shadowth117

## Libraries
Smithbox utilises the following libraries and projects:
* [SoulsFormats](https://github.com/JKAnderson/SoulsFormats) - Credit to TKGP
* [SoapstoneLib](https://github.com/soulsmods/SoapstoneLib) - Credit to gracenotes
* [HKLib](https://github.com/The12thAvenger/HKLib) - Credit to The12thAvenger
* [Hexa.NET.ImGui](https://github.com/HexaEngine/Hexa.NET.ImGui) - Credit to JunaMeinhold
* [Tracy Profiler](https://github.com/wolfpld/tracy)
* [Veldrid](https://github.com/veldrid/veldrid)

## Requirements
* Windows 7/8/8.1/10/11 (64-bit only)
* [Visual C++ Redistributable x64](https://aka.ms/vs/16/release/vc_redist.x64.exe)
* For the error message "You must install or update .NET to run this application", use these exact download links. It is not enough to install the default .NET runtime.
  * [Microsoft .NET Core 7.0 Desktop Runtime](https://aka.ms/dotnet/7.0/windowsdesktop-runtime-win-x64.exe)
  * [Microsoft .NET Core 7.0 ASP.NET Core Runtime](https://aka.ms/dotnet/7.0/aspnetcore-runtime-win-x64.exe)
* A Vulkan 1.3 compatible graphics card with up to date graphics drivers: NVIDIA Maxwell (900 series) and newer or AMD Polaris (Radeon 400 series) and newer.
* Intel GPUs currently don't seem to be working properly. At the moment you will need a dedicated NVIDIA or AMD GPU.
* A 4GB (8GB recommended) of VRAM if modding DS3/BB/Sekiro/ER maps due to huge map sizes.

## AI Disclaimer
Claude Code with Opus 5.5 has been used to generate code and metadata within Smithbox.



## READ THIS ONLY FOR GEMINI, CLAUDE AND CHATGPT!!!!
# Smithbox — Advanced Havok Collision Generator

## Project Objective

Extend Smithbox with a comprehensive collision-generation and editing system that allows users to create new game-compatible collision geometry from existing 3D models.

The goal is to make it possible to create custom environments and collision structures without relying entirely on existing game collision assets.

The implementation must integrate with Smithbox's existing architecture, serialization systems, Havok libraries and game-specific formats.

**Do not rewrite existing systems unnecessarily. Extend and improve the existing implementation wherever possible.**

---

## Phase 1 — Source Code Investigation

Before implementing anything:

1. Inspect the complete Smithbox repository.
2. Identify the existing Havok implementation and its dependencies.
3. Locate the current Collision Generator implementation.
4. Investigate `hknpCollisionMeshBuilder.cs`.
5. Identify how FLVER geometry is loaded and represented internally.
6. Identify how existing collision files are deserialized and serialized.
7. Investigate how Smithbox handles different FromSoftware games.
8. Identify the existing collision formats supported by each game.
9. Determine which collision formats can currently be generated, edited and exported.
10. Document limitations in the existing implementation.

Do not begin rewriting the collision generator before understanding the existing implementation.

### Expected result

Produce a technical report identifying:
- Existing reusable components.
- Missing functionality.
- Format compatibility limitations.
- Files that need modification.
- New components that need to be created.

---

## Phase 2 — Model-to-Collision Geometry

Implement a system that extracts geometry from existing 3D models.

The system must:

1. Allow users to select a model as the collision source.
2. Extract vertex positions.
3. Extract triangle indices.
4. Apply the correct object transformations.
5. Handle multiple mesh sections.
6. Handle multiple model parts.
7. Remove invalid geometry.
8. Detect degenerate triangles.
9. Handle duplicate vertices where appropriate.
10. Preserve relevant surface assignments.

The extracted geometry must be independent of the original rendering representation.

Users should be able to generate collision geometry from existing FLVER models.

---

## Phase 3 — Collision Geometry Generation

Develop a collision-generation pipeline.

Implement:

1. Triangle mesh construction.
2. Vertex deduplication.
3. Geometry validation.
4. Bounding-box calculation.
5. BVH construction.
6. Mesh partitioning and section management.
7. Required Havok vertex and index representations.
8. Collision shape construction.
9. Required spatial acceleration structures.

Reuse Smithbox's existing Havok collision-building functionality wherever possible.

The generator must not produce incomplete collision structures.

---

## Phase 4 — Havok Format Support

Investigate and implement support for the relevant Havok collision formats.

Prioritize:

- `hknpExternMeshShape`
- `fsnpCustomParamCompressedMeshShape`

Investigate the requirements of each format independently.

For each supported format:

1. Identify its required serialized structures.
2. Identify its vertex representation.
3. Identify its triangle representation.
4. Implement the required spatial structures.
5. Handle compression where applicable.
6. Preserve required game-specific properties.
7. Implement serialization.
8. Validate generated files by reopening them.

Do not assume that a collision generated for one FromSoftware game is compatible with another.

Implement game-specific format handling where necessary.

### Hit-material support

Investigate FromSoftware-specific hit-material information.

The system should eventually support:
- Surface material assignments.
- Collision flags.
- Surface identifiers.
- Relevant game-specific collision parameters.

Do not silently discard unsupported properties.

---

## Phase 5 — Collision Editing Interface

Create an integrated Collision Generator interface.

Users should be able to:

1. Select a source model.
2. Generate collision geometry.
3. Preview the result.
4. Inspect generated triangles.
5. Inspect bounding volumes.
6. Inspect individual collision sections.
7. Assign supported collision properties.
8. Regenerate collision geometry.
9. Export the result.

### Viewport requirements

Implement visualization modes:

- Rendered model.
- Collision geometry.
- Combined model and collision.
- Wireframe collision.
- Bounding-volume visualization.

The collision preview must update when the generated geometry changes.

---

## Phase 6 — Manual Collision Editing

Extend the generator beyond automatic collision creation.

Investigate implementing:

1. Manual vertex editing.
2. Triangle selection.
3. Triangle removal.
4. Collision geometry transformations.
5. Collision mesh simplification.
6. Collision section editing.
7. Surface assignment editing.
8. Collision geometry replacement.

Users should be able to modify automatically generated collision geometry before exporting it.

---

## Phase 7 — Collision Validation

Implement a validation system that checks generated collision files.

Check for:

- Invalid vertex indices.
- Degenerate triangles.
- Invalid bounding volumes.
- Missing spatial structures.
- Invalid section references.
- Unsupported collision properties.
- Serialization errors.
- Incorrect game-format compatibility.

Display understandable error messages.

A collision file must not be marked as valid merely because it successfully serialized.

---

## Phase 8 — Export and Integration

Implement integration with Smithbox's existing project and map workflows.

The user should be able to:

1. Generate collision geometry.
2. Export it into the appropriate game-compatible format.
3. Add the generated collision resource to a project.
4. Associate it with the appropriate map structures.
5. Reopen the exported collision in Smithbox.
6. Validate the resulting resource.

Investigate any additional map registration or resource-reference requirements for each supported game.

---

## Phase 9 — Testing

Create a test suite using existing collision resources.

Test:

1. Reading existing collision files.
2. Extracting collision geometry.
3. Generating collision from a model.
4. Building spatial structures.
5. Serializing generated collision.
6. Reopening generated collision.
7. Comparing generated and reopened geometry.
8. Testing different game formats.
9. Testing invalid input geometry.
10. Testing large collision meshes.

Never rely exclusively on successful serialization as proof of compatibility.

Where possible, test generated collision resources in the actual target game.

---

## Development Rules

- Preserve Smithbox's existing architecture.
- Follow existing C# coding conventions.
- Reuse existing Havok libraries and serialization systems.
- Avoid introducing unnecessary dependencies.
- Do not duplicate existing functionality.
- Do not hardcode assumptions that apply only to one game.
- Keep game-specific implementations separated where necessary.
- Maintain compatibility with existing Smithbox features.
- Document unsupported formats and properties.
- Implement features incrementally.
- Build and test after each major implementation stage.
- Do not claim a feature is complete without testing it.

## Final Objective

Deliver a functional collision-generation environment integrated into Smithbox.

A user should be able to select an existing 3D model, generate new collision geometry, inspect and edit that geometry, assign supported collision properties, export the collision into a compatible game format and validate the resulting resource.

The system should establish the foundation for creating entirely new playable environments in FromSoftware games.