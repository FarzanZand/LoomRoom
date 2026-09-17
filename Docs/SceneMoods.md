# Scene moods

Select **Managers > ScreenManager**. On **Scene Mood Controller**, choose **Selected Mood** and set **Blend Duration** in seconds. In Play Mode, press **F5** to blend to that preset; press it again to restore the original scene. The inspector also has **Blend Selected Mood** and **Restore Original Mood** buttons.

The supplied preset is `Assets/Data/Moods/RedSky.asset`. Duplicate it, or use **Create > World > Scene Mood**, to make another mood. Adjust sky tint/exposure, sunlight colour/intensity, ambient colours, and fog colour. Main Light is optional; assign it explicitly if your scene contains multiple directional lights.

From code:

```csharp
ScreenManager.Instance.BlendToMood(myMood, 3f);
ScreenManager.Instance.RestoreMood(2f);
```

New requests blend from the current appearance, so you can change moods mid-transition. Zero seconds applies immediately. Blends use unscaled time and stopping Play Mode restores the source scene settings. Original screen fade methods still work independently.

Sky tint uses the current skybox shader's `_SkyTint` or `_Tint` property. Fog colour changes only affect visible fog when the scene already has fog enabled. The blend controls realtime lighting; it does not rebake lightmaps or reflection probes.
