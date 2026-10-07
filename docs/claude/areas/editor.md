# Editor

The in-game island editor: shape and paint the land, place and set up objects, island settings, save and open, test in a world. It opens from Raft's main menu only.

## Files
| File | Role |
|---|---|
| `DynamicIslands.cs` | `HookUI` (EDITOR button), `LoadEditor` -> `OpenEditor`; state `currentIslandName`, `currentIslandProps`, `currentStyle`, `currentMixStyle`, `currentElevation`, `EditorWaterLevel`, `EditorGizmoHandler`; `NewIsland`, `SaveIsland`, `LoadIsland`, `CaptureIsland`, `KeepUnsaved`, `PlaceObject`, `Notify`, `InEditor` |
| `EditorUI.cs` / `UIKit.cs` / `EditorLoadingBox.cs` | The screen (`Setup`, `Tick`, `RefreshIsland`, `RefreshStyle`, `Flash`, `CopySelected`, `Paste`) / the kit every window is built with / the cover while the editor opens |
| `EditorTools.cs` | `EditorInput` (`IsTyping`, `WindowOpen`, `SubWindowClosedFrame`, `HandleShortcuts`: Ctrl+Z/Y/S), `TerrainStrokeCommand`, `ObjectVisibilityCommand` |
| `terraineditor.cs` | Makes the terrain (`terrainSize` 1000 x 600 x 1000, `heightmapResolution` 513); brush state (`modificationAction`, `brushRadius`, `strength`, `paintLayer`, `paintMask`); strokes |
| `TerrainPainter.cs` / `TerrainBrushes.cs` / `TerrainStamps.cs` | Styles and automatic paint (`Styles`, `Setup`, `PaintWorldArea`, `AutoWeights`) / `Noise`, `Erode`, `SwitchSeaFloor`, `SetEditorMix` / stamps (`Load`, `Apply`, `Capture`, `Save`) |
| `PlaceableCatalog.cs` | Every placeable object: core ones (`EnsureBuilt`), an index of Raft's scenes (`catalog_index.txt`, `EnsureIndex`), loaded on demand (`EnsureLoaded`, `EnsureCategory`, `ScenesNeededFor`); `Spawn`, `DisplayName` |
| `ObjectBrowser.cs` / `ObjectPlacer.cs` | Right panel (`Rebuild`, `Pick`, `StartPlacing`) / the object that follows the mouse until `Place` |
| `PlacementTools.cs` / `SelectionTools.cs` | `PlacementOptions` (`RandomTurnAndSize`, `AlignToSlope`, `SnapToGrid`, `GroundAt`, `DropSelectionToGround`, `PickObject`, `DuplicateSelection`, `CopySelection`, `PasteClipboard`) / box select, select all or same kind, hide, lock, `Placed`, `PlacedListWindow` |
| `EditorGameObject.cs` | `EditorGameObject` (`GameObjectName`, `Props`, `Attach`) on every placed object; `ObjectLimit` |
| `ObjectProps.cs` / `ObjectInspector.cs` | Prop keys (`creature.*`, `note.*`, `tint.*`, `loot.*`, `zone.*`, `atmo.*`, `sound.*`), `Defaults`, `With`, `ApplyInEditor`, `PropsCommand` / the inspector for one selected object |
| `IslandSettingsUndo.cs` / `EditorAutosave.cs` | `Change` / `Record`: island props, style, elevation as undo steps / `autosave\<name>.island`: `Unsaved`, `Tick`, `WriteNow`, `Saved`, `OnEditorReady` |
| `IslandTest.cs` / `EditorCamera.cs` | Test: `Start` -> `Tick` state machine -> `OpenWorld` -> `BringIsland`; `Back` / camera (`Look`, `Pan`, `Orbit`, `ZoomAt`, `Frame`, `Sync`; `Looking`, `UsingMouse`) |

## Main flow
1. Main menu EDITOR (`HookUI`) -> `LaunchEditor` -> `LoadEditor` (refused while a world is loaded) -> `EditorLoadingBox.Show` -> `OpenEditor`.
2. `OpenEditor`: loads the bundle's `Editor` scene, `HideOldCanvases`, `RaftSkySea.Adopt`; adds `terraineditor` and `EditorCamera` to `Camera.main`; `EditorUI.Setup(null, tabSelector)`; shaders; resets name, sea level and undo; `TransformGizmo` as `EditorGizmoHandler`; `CreateWaterLevelPlane`; each window's `Create(EditorUI.Canvas.transform, ...)`; `await PlaceableCatalog.EnsureBuilt()`; Tropical style; `EditorAutosave.OnEditorReady`; `EnsureIndex` when `IndexIsCurrent` is false.
3. Each frame `terraineditor.Update`: `EditorInput.HandleShortcuts` -> `EditorUI.Tick` (F1-F3 tabs; Ctrl+D/C/V/A on the Objects tab; `ObjectInspector.Tick`, `SelectionTools.Tick`) -> `ModifyTerrain`. `DynamicIslands.Update` runs `EditorAutosave.Tick` and `IslandTest.Tick`.
4. Brush: `BeginStroke` (full snapshot) -> `ApplyAt` -> `EndStroke` (`TerrainPainter.PaintWorldArea` unless painting by hand) -> `RecordUndo` (a `TerrainStrokeCommand` of the touched area, plus `FollowGround` moving objects that stood on it).
5. Place: tile -> `ObjectBrowser.Pick` (`EnsureLoaded` if needed) -> `StartPlacing` -> `DynamicIslands.PlaceObject` -> `ObjectPlacer.Place`: `ObjectLimit.Allow`, `EditorGameObject.Attach`, parent `PlacedObjects`, `ObjectVisibilityCommand(..., true)`.
6. Edit: `ObjectInspector.Rebuild` -> controls call `Set` -> `PropsCommand.Change`. Delete: `TransformGizmo.DeleteSelection` -> `UndoRedoManager.Execute(new ObjectVisibilityCommand(..., false))`.
7. Save: `IslandFilesWindow.QuickSave` / `SaveIsland` -> `CaptureIsland` (`IslandFile.Capture` + props, style, elevation) -> `KeepForWorldsIfShifted` -> `Save` -> `EditorAutosave.Saved`. Open: `LoadIsland` loads needed scenes first, spawns under `LoadedObjects`, clears undo.

## Data and keys
- `Mods\DynamicIslands\<name>.island` (`IslandSpawner.PathFor`): each object's `Props` and the island's `currentIslandProps`. Stamps: `stamps\<name>.stamp` (`CIST`, version 1, 65 x 65 floats). `placeables.txt` (only these names) and `placeables_generated.txt`.
- Console: `LoadEditor`, `SaveIsland`, `LoadIsland`, `ListIslands`, `DeleteGroup`, `SetToRaise`, `SetToLower`, `SetToFlatten`, `SetToSmooth`, `SetToAutoPaint`, `PaintTexture`, `ChangeWidth`, `ChangeStrength`, `SetElevation`, `SetStyle`.

## UIKit, and a new window
- `CreateCanvas` (fits 1400 x 800), `Panel`, `Group`, `Row`, `Label`, `Button` + `SetLook` (`Look`: `Plain`, `Choice`, `Chosen`, `Primary`, `Danger`, `Slot`; `SetActive` marks the chosen one), `Slider` -> `SliderRow`, `Field`, `TextArea`, `Tabs`, `ScrollList`, `ScrollPanel`, `Picture`, `Help` (the ? popup), `Hint` (status bar text on hover), `Size`, `Anchor`, `Stretch`. Drop-downs: `DropList.Make`. Raft's look: `CaptureRaftLook`.
- A modal window (copy `TextPromptWindow`): `MonoBehaviour`, static `instance`, `IsOpen`; `Create(Transform canvas)` makes a full-screen `Image` blocker (black, alpha 0.65, layer 5), `Build()`, `SetActive(false)`; `Open` activates and `SetAsLastSibling`; `Close` sets `EditorInput.SubWindowClosedFrame` and hides; `Update` sets `EditorInput.IsTyping` from its field, Esc and Enter; `OnDisable` clears `IsTyping`. Call `Create` in `DynamicIslands.OpenEditor`; add `IsOpen` to `EditorInput.WindowOpen`.

## Where to change X
| Task | Edit |
|---|---|
| A top-bar or tool-panel button | `EditorUI.BuildTopBar`, `BuildTerrainTools`, `BuildObjectTools`, `BuildIslandTools` |
| A new brush | `terraineditor.TerrainModificationAction`, `ApplyAt`; a button in `BuildTerrainTools` (`brushButtons`) |
| Automatic ground paint | `TerrainPainter.AutoWeights`, `GrassLine` |
| A setting on an object | a key in `ObjectProps`, its default in `Defaults`, a group in `ObjectInspector.Groups` |
| An island setting | a key in `currentIslandProps`, changed inside `IslandSettingsUndo.Change` |
| Placing rules / browser | `ObjectPlacer.FollowMouse` / `Place`, `PlacementOptions` / `PlaceableCatalog.Sources`, `Browse`, `ObjectBrowser.Rebuild` |

## Traps
- Every change goes through `UndoRedoManager`: `EditorAutosave.Unsaved` compares `UndoRedoManager.Changes`, so a change outside it is not autosaved or seen as unsaved. `Insert` records a change already made; `Execute` makes and records it. Several changes in one step: `CommandGroup` (undone in reverse order).
- Deleting only hides (`SetActive(false)`) so undo can bring it back. Saving and counting use active objects only.
- Placed objects can sit one level down in holders (`LoadedObjects`, `GeneratedObjects`, `ReRolledObjects`) under `PlacedObjects`, and a placed object can contain others. `SelectionTools.Placed()` gives the top-level ones.
- Saved worlds remember used objects by their order in the file. `SaveIsland` keeps a `<name>_<hash>` copy for them when objects were removed or reordered (`KeepForWorldsIfShifted`).
- Code that replaces the island calls `DynamicIslands.KeepUnsaved` first; `LoadIsland` and `NewIsland` clear undo.
- Text fields: set `EditorInput.IsTyping` while one is focused, or WASD flies the camera and shortcuts fire. Windows under a sub-window skip the frame it closed (`SubWindowJustClosed`). `terraineditor.CanSculpt` has its own, shorter list of open windows.
- Rebuilding a list: `SetActive(false)` then `Destroy` (Destroy waits for the frame's end). A button not made with `UIKit.Button` calls `UIKit.Register` (dev command `CIButtons` presses every registered one).
- `PlaceableCatalog.Spawn` returns null until the object is loaded; harvestables lose their scripts unless `withGameplay` is set.
- Moving `Camera.main` from code is fine: `EditorCamera.Update` calls `Sync`.
- `terraineditor.terrainSize` / `heightmapResolution` and `IslandGenerator.BuildArea` / `BuildResolution` hold the same numbers.
- C# 7.3 (`LangVersion` in `DynamicIslands.csproj`, which lists each `.cs` file for `compile.ps1`: add new ones there). `pack.ps1 -Release` leaves out `DevTests*.cs`.
- Docs: README `The editor` and the source overview; `docs/GUIDE.md` chapter 4 (4.1 The screen to 4.9 Test); then the PDF is made again from `GUIDE.md`.
