DynamicIslands-master - the mod code ("Custom Islands" v2.0, in development)
==========================================================================

WHAT IT IS
  The C# source of the Raft mod "Custom Islands" by FranzFischer78 (code) and
  MegaMatrixs (design). Source: https://github.com/FranzFischer78/DynamicIslands
  This is the part that runs inside Raft. It adds:
    - an EDITOR button to Raft's main menu that opens the in-game island editor
      (terrain raise/lower/flatten, place/move/rotate/scale objects, save/load)
    - console commands to spawn custom islands in a running game
    - multiplayer: host tells clients which island to spawn (network channel 6969)

WHAT'S IN HERE
  DynamicIslands.sln / .csproj   Visual Studio solution (only for editing/IntelliSense;
                                 see "How RML builds" below).
  build.bat                      Zips the DynamicIslands\ subfolder into DynamicIslands.rmod.
  Public Islands\demoisland1.assets   A sample island bundle (made in CustomIslandsWorkspace).
  DynamicIslands\                The actual mod folder:
    DynamicIslands.cs            Main mod: startup, menu button, editor loading,
                                 save/load, island spawning, Harmony patches.
    terraineditor.cs             Terrain brushes (1000x600x1000 terrain, 513 heightmap).
    ObjectPlacer.cs              Object follows mouse; left-click places, Esc cancels,
                                 Ctrl = fine-tune.
    IslandData.cs                Save-file data classes (JSON).
    RTSCamera.cs, CameraMovementScript.cs   Editor camera controls.
    RuntimeGizmo\                Third-party move/rotate/scale gizmo with undo/redo.
    AwaitExtensions\             Helper lib so Unity things can be "await"-ed.
    ProceduralTerrainGenerator.cs  Written but not wired up yet.
    PrivateAccessor.cs           4 MB of GENERATED code giving access to Raft's private
                                 members. Generated against an older Raft - may break.
    UnityScripts\TabSelector.cs  Tab switching in the editor UI.
    editorsceneci.assets         Prebuilt UI bundle (Editor + CustomIsland scenes),
                                 built from ..\..\Custom-Islands-UI-main.
    maincustomislandsbundle.assets  Prebuilt shader bundle.
    modinfo.json                 Mod name/version/author for RML.
    icon.png, banner.jpg (+ .old/.veryold copies)   Mod artwork.

HOW RML BUILDS
  RML compiles the .cs SOURCE files itself when the mod loads. An .rmod is just a
  zip of this DynamicIslands\ folder. So "building" = zipping. Visual Studio is
  only needed to get error checking while editing.

HOW TO USE (updated 2026-09-23; this folder is now a local git repo - see git log)
  compile.ps1          Compile-check with Visual Studio's MSBuild (catches errors before starting Raft).
  pack.ps1 [-Install]  Zip DynamicIslands\ into DynamicIslands.rmod; -Install copies it to <Raft>\mods.
  build.bat            Now just calls pack.ps1.
  The .csproj finds Raft/RML via the RaftDir / RMLDir properties (defaults match this PC).
  Raft must have been started once with RML, which creates the publicized assemblies the project uses.

  Console (F10):
    LoadEditor                    open the editor (same as the EDITOR menu button)
    SaveIsland <name>             editor: save to Mods\DynamicIslands\<name>.island
    LoadIsland <name>             editor: load a saved island
    ListIslands                   list saved islands and .assets bundles
    SpawnIsland <name>            in game (host): spawn a saved island 400 m ahead of the raft
    SpawnCustomLandmark <name>    in game (host): spawn a legacy .assets island bundle
    RefreshLandmarkBundles        reload .assets bundles
    SetToRaise/Lower/Flatten/Smooth, ChangeWidth <brush diameter in m>, ChangeStrength <m per second>
    PaintTexture <sand|grass|rock|seabed>, SetToAutoPaint   texture brush (same as the paint buttons)
    CITest / CITestWorld / CILook  dev self-tests (results in the console with a [CITEST] prefix)

  Editor: Terrain tab (layers icon) = Raise/Lower/Flatten/Smooth, texture paint Sand/Grass/Rock/Seabed,
    Auto (brush back to automatic texturing) + brush size/strength sliders;
    Objects tab (tree icon) = object list + Move/Rotate/Scale/Delete. Right-drag rotates the camera,
    WASD moves, mouse wheel changes height. Textures paint automatically from height/slope unless painted
    by hand; hand paint is saved in the .island file (format 2) and used when the island spawns in a world.

  Placeable objects: built automatically from Raft's Vasagatan scene the first time the editor opens.
    The list is written to Mods\DynamicIslands\placeables_generated.txt. To curate it, copy it to
    placeables.txt and delete lines. The blue plane in the editor is sea level for in-game spawning.

KNOWN LIMITATIONS
  - Spawned islands don't persist in savegames, and they aren't part of Raft's natural spawn pool yet.
  - Multiplayer: clients only see an island if they have the same .island file; Raft's networking
    changed (Unity Netcode) and this path is untested.
  - The UI bundles were built with Unity 2019.3.5f1; Raft runs 2021.3.45.
