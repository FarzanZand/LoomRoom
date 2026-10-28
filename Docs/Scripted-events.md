# Scripted events

Hand-built rooms with an event of their own, like the Skeleton Party and Not a witch, plus the Dungeon Master's
meddling. Built on branch `events` (2026-10-28).

## Trying them

- Dev console (the key left of 1): `event` lists them; `event <name>` starts a new run at the way into that room;
  `event dice 1`..`6` lands the die on that face; `event cheats` starts a run where he meddles at the first chance;
  `meddle add` / `meddle steal` makes him do it right where you are.
- Asset Studio: pick the room (Rooms, `Shared/Rooms/Scripted/Events`) and press **Play this event** (the dice room
  also has **Roll a 1** .. **Roll a 6**); any dungeon level has **Play with the DM cheating**.
- A run opened in an event room starts at its `Entrance` marker (ScriptedEventRoom), not in the middle of the event.

## The events

| Event | Room | What happens |
|---|---|---|
| `duel` | Duel bridge (7 x 9, two tiles high, a chasm each side) | The Duellist waits on the bridge and ignores you. Walk up (5 m) and he salutes and the DM calls a duel. Win fair and he leaves gear rolled Unique and the biggest spoils. Hit him from behind, with a spell (not the shield bash) or before the salute is over: the DM gives house rule three ("A duel is fought face to face", quoted after the first time) and his friends come out at both ends of the bridge. |
| `bell` | Bell room (7 x 7, two tiles high) | Pull the bell's chain: gates slam across the ways out and a wave comes (2, 3, 4, 5). Clear it and the chest at the back comes back a tier better (wood, iron, gold, then gold with a Legendary). Open the chest any time to stop. |
| `wager` | Card table (7 x 5, candlelit) | A skeleton runs a shell game: bet gold at the table (10 + 5 a floor), he hides the coin under one of three bowls and shuffles, pick a bowl. A win pays double. After two wins he cheats (the coin is under none). Hit him then and you called it: his friends come out of the walls, and once they are down his pot (what you lost plus 40) is yours. Hit him when he played fair and his friends come anyway, no pot. Six games at most. |
| `giant` | Giant's hoard (9 x 9, two tiles high, candlelit) | A giant sleeps in the middle of gold and gear (rolled Rare) with a gold chest at its back. It sleeps like a sleeping pack but hears better (`hearing` 1.7): a crouching step is never heard, a walking step within about 5 m wakes it, a running one from across the room. Leave with loot and it never stirred: the DM is impressed. Kill it: gear rolled Unique and the biggest spoils. |
| `dice` | Dice room (7 x 7, open to the sky) | Walk in and the DM brings his die up and drops it through the opening (or, when the room is out of his reach, tosses it in from as far as he reaches; what he sets down for a 2, 5 or 6 is tossed in the same way). 1 ambush (gates, two waves), 2 he sets down a tough enemy (the Demon), 3 nothing, 4 a rest (health full), 5 he sets down an iron chest, 6 a gold chest with gear rolled Unique. The face comes from the floor's seed; the die is made to land on it. |
| `cheats` | any floor (`DungeonMasterMeddling`) | Clear a room of two or more enemies within 14 s losing at most 10% health: by chance (50%, once a floor) he says it was too easy, lifts the floor's ceilings off like a lid (they rise a little and are gone, so the bedroom and he show above), sets one more enemy down in front of you and puts the lid back. Rarely (5% a chest, once a run) he takes a chest you walk up to and drops a single coin "for the walk". Only where his hand reaches from the table's edge (his side and both ends, never yours); never in the intro, a boss fight, a pocket or an event room. Settings on the room features (`meddling`, `meddleChance`, `tooEasySeconds`, `tooEasyHealth`, `stealChance`). |

## Code

- `Levels/Scripts/Events/`: `ScriptedEventRoom` (on every event room: name, entrance, and the floor helpers: room,
  player, gates, summons, chests, drops), one script per event (`DuelEvent`, `BellEvent` + `BellRope`, `WagerEvent` +
  `WagerTable` + `WagerCup`, `SleepingGiantEvent`, `DiceEvent`), `DungeonMasterMeddling`, and `DungeonMasterReach`
  (the DM reaching into any room: the lid, walking round the table to the nearest side, Put, PutThing, Take, DropOver,
  Leave).
- `DungeonMasterHand.Take` and `Drop` join `Place`. `DungeonGenerator.ScriptedEvents.cs` gives the events the floor's
  enemies, chests and ways out, and starts a run opened in an event room at its entrance.
- `Levels/Scripts/Editor/EventRoomsSetup.Build()` (from an eval; safe to run again, `Build(true)` rebuilds) makes the
  rooms, their enemies (`Characters/Enemies/Events`), prop stand-ins (`Levels/Shared/Props/Events`), house rule three,
  and deals the rooms into the Cellars, Catacombs, Crypt and ruins biomes at weight .3 each (about one a floor).

## Notes from testing (2026-10-28)

- An event room's own enemy (`EventActor` on the Duellist, Card sharp, Sleeping giant) stands still from the moment it
  exists until its event lets it go; without it a player starting in the room was attacked before the event found it.
- At most `DungeonRoomFeatures.maxEventRooms` (1) event rooms a floor; the room a run opens in counts. Over 36 Dungeon1
  floors that gave an event room on about two floors in three.
- An event room's chests are never trapped (the bell room swaps its chest).
- The bell pull, the bowls and the betting table are trigger colliders: a solid pull kept the player's blows off an
  enemy beside it.
- He walks round the table to his own side or an end (never the player's) at a hurried pace while reaching; a reach
  whose floor went (left mid-reach) frees his hands, so he is never stuck busy.
- Art (Ciri): the DM's die and the great bell are in; the bell rope, the Duellist's knight body, the giant's body and
  the Card sharp's hat are being made.
