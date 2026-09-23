# Item pickup and use audio

On each ItemData, choose Pickup Audio Source or Use Audio Source:

- AudioData: existing asset references keep working. Pickup still respects InventoryManager's shared pickup setting.
- AudioClip: assign a normal imported sound clip and set its Clip Volume (0–1).
- AudioManagerKey: enter a key from AudioManager's SFX Library; library volume and pitch variance apply.

All modes route through AudioManager's 2D SFX playback and the SFX/master mixer. Direct item pickup clips/keys override shared pickup audio. Missing pickup overrides fall back to the existing shared/default sound. Zero clip volume intentionally silences that event. Unassigned use sounds are silent; item effects still execute. Use audio is emitted when consumption executes, including consumption from the hand. Silent initial inventory grants remain silent.
