# Room art

Hand-made art for the room (the apartment and everything in it, including the big game table). One folder per thing, holding its textures and material, for example `Game table/` with `Game table top.png` and `Game table top.mat`.

| Folder | What is in it |
| --- | --- |
| `Apartment` | The blockout's source materials (flat colours). Their pixel copies are generated into `Art/Synty Pixel/Apartment` |
| `Paintings` | The paintings' source images and materials. Their pixel copies are generated into `Art/Synty Pixel/Surfaces` |

Generated art lives elsewhere and should not be edited by hand: `Art/Synty Pixel/{Apartment,Surfaces,Room}` (made by `PixelRestyle`).

For a new texture: import it with Filter Mode Point, make its material, put it on the object, then run `Tools > LoomRoom > Pixel Look > Register Pixel Lighting Materials` and `Even Out Texel Sizes` so it gets the pixel lighting and the room's pixel size.
