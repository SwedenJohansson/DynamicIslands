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

HOW TO USE (short version - full steps in ..\_ProjectDocs\PROJECT_OVERVIEW.md)
  - Quick test: copy/link the DynamicIslands\ folder (or a built .rmod) into
    <Raft>\mods\, start Raft via RMLLauncher, load the mod in the Mod Manager.
  - build.bat must run from a folder literally named "DynamicIslands"
    (it uses its own folder name) - from this "-master" zip it produces an empty .rmod.
  - Console (F10): LoadEditor, SpawnCustomLandmark <name>, SetToRaise/Lower/Flatten,
    ChangeWidth/ChangeHeight/ChangeStrength, EnableEditing/DisableEditing.

KNOWN PROBLEMS (details in the overview doc)
  - Needs vasagatanboat.goodv1.txt (list of placeable objects) - file is missing.
  - SpawnCustomLandmark always says "Invalid Landmark" (bundle-loading code dropped;
    recoverable from ..\DynamicIslands.rmod v1.1.1).
  - Terrain heightmap never loads back (string split bug) and saves are locale-dependent.
  - The .csproj references point to the original author's PC paths.
