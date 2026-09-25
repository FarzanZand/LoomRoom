# Scene moods

Moods belong to **LightingManager** (`Assets/Game/World/LightingManager.cs` and `LightingManager.Moods.cs`), on the **Lighting Manager** object in the Room scene. There is no separate mood controller; ScreenManager only handles fades.

A mood is a `SceneMood` asset (**Create > World > Scene Mood**). It sets sky tint and exposure, directional light colour and intensity, ambient sky/horizon/ground colours and fog colour. **Override Light Groups** additionally sets the table and room light groups (brightness and tint) while the mood is active; see `Table-spotlight-mood.md`. Examples: `Assets/Game/World/Moods/RedSky.asset` and the dungeon presets in `Assets/Game/Levels/_Resources/Shared/Resources/DungeonLighting`.

## Inspector

- **Mood Preset** selects the mood for the buttons below. **Mood Blend Duration** is in seconds.
- **Toggle Mood (F5)** blends to the preset; pressing it again restores the default. F5 works in Play Mode through the Dev input map.
- **Preview Mood** applies the preset immediately; **Blend to Mood** blends over Mood Blend Duration.
- **Save Current as Default** stores the current light groups, ambient multiplier, fog override and any active mood. **Restore Default** returns to it. In Play Mode the saved default is applied on enable; a scene that has never saved one starts the table group at **Starting Brightness**.

## From code

```csharp
lighting.BlendToMood(mySceneMood, 3f);                       // a SceneMood asset
lighting.BlendToMood(level.DungeonLighting(floorNumber), 0f); // a LightingManager.MoodState
lighting.RestoreDefault();
```

`TableLevelLoader` is the runtime caller: dungeon levels blend to their mood lighting (instantly, or during the reveal's entry flight), towns blend to their `mood`, and levels with neither restore the default. `LightingManager.FromPreset` converts a SceneMood into a MoodState.

New requests blend from the current appearance, so moods can change mid-transition. Zero seconds applies immediately. Blends use unscaled time. The source skybox material is never modified; a temporary copy is used and is never saved into the scene.

Sky tint uses the skybox shader's `_SkyTint` or `_Tint` property. Fog colour only matters when the scene has fog enabled. Moods drive realtime lighting only; they do not rebake lightmaps or reflection probes.
