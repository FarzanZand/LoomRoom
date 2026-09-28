# Debug dungeon

Click **Debug** beside Unity's Play controls with the Room scene open. It enters Play mode directly in a fixed four-room dungeon on the table, with up to four enemies. Enemies stay idle until damaged, then use their usual combat behaviour. The room sequence and dungeon reveal are skipped. Normal Play is unchanged.

Edit `Assets/Game/Development/Debug Dungeon/Debug Dungeon Settings.asset` for the starting class, enemy limit and level. Edit `Debug Dungeon.asset` for the layout/seed and `Debug Biome.asset` for enemy choices and appearance. These are authored assets, not regenerated during play.

Debug saves use `loomroom-debug.json`, separate from the normal adventure save. Stop Play mode to leave testing. The Debug button during play restarts testing.
