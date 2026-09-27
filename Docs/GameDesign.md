# LoomRoom: Game Design Document

Written 27 September 2026, revised the same day. This is a proposal, and nothing in it is built yet. It builds on the systems that already exist (see *Starting point*) and follows the settled decisions in `CLAUDE.md`: one data asset per thing, append-only enums, singletons that never auto-create, UI authored in the scene, and plain C# events.

**Scope:** the game takes place in **one room** with **one NPC**, the Dungeon Master (DM), who sits across the table. No other rooms or characters are part of this design.

Sections 1–3 describe the game. Sections 4–12 cover one system each, with rules, data shapes and hook points. Sections 13–16 cover art, audio, UI and story. Sections 17–20 are the roadmap, enum additions, open decisions and risks.

---

## 1. Vision

> **You play a dungeon game at a table with the Dungeon Master. The game and the room affect each other.**

LoomRoom has two layers:

- **The Room**: one first-person room with the table in the middle and the DM sitting across from you. You walk, look, talk to the DM and pick things up. You never fight here.
- **The Table**: a miniature dungeon crawler that the DM runs on the table. You play it as a small first-person figurine with melee combat.

Inscryption works because its layers affect each other: the card game changes the cabin, and the cabin changes the card game. Most of this document is about those connections. Each run should change something in the room, and time spent in the room should change the next run.

### Pillars

| Pillar | Meaning | Test for any feature |
|---|---|---|
| **Two layers, one game** | Room and table always affect each other. | Does this change something in the other layer? |
| **It's a miniature** | Everything on the table reads as a hand-made model sitting in a real room. | Would this look right if a hand reached in? |
| **Short runs, lasting changes** | A run lasts 15–30 minutes. The room keeps a record of every run. | Is there a trace of this after the run ends? |
| **The DM is a person** | The dungeon has an author with moods and opinions. | Could the DM plausibly comment on this? |
| **Readable melee** | First-person combat that is weighty and fair. | Can a player explain why they got hit? |

### Out of scope

- Other rooms, other NPCs, a house to explore.
- Multiplayer.
- Cards and deck-building. The table game stays a real-time dungeon crawl.

---

## 2. Starting point

Already built and used by this design:

| Area | What exists |
|---|---|
| Players | Room player (exploration) and Table player (arms, melee). `PlayerManager` switches between them, and `TableManager.EnterTable()` is the transition. |
| Game state | `GameManager` state stack (`Explore`, `Menu`, `Dialogue`, `Cutscene`, `Dead`, `Inventory`). |
| Run | `RunManager`: seed, floor, kills, gold, bosses, time, killer, `RunStarted` / `RunEnded(victory)`, `RunSummary`, `RunRecapUI`, run-blessing modifier source. |
| Progression | `ProgressionManager` flags (string → int) and `FlagChanged`, exposed to Lua through `DialogueBridge` (`SetFlag`, `GetFlag`, `GiveItem`, `HasItem`). |
| Dungeon | Growth generator, themes, room profiles/styles, templates and sockets, doors, destructibles, corpses, boss encounters, merchant, ambience. Features: altar, fountain, grave, bookshelf, lever, all resolved through `DungeonBoon` (weighted outcomes, effect lists, enemy spawns). |
| Tables | `Town` and `Dungeon1` in the `TableLevels` catalog, and `TableLevelReveal` for the build-up animation. |
| Items | `ItemData`, `EffectEntry` (Heal, RestoreMana, TimedStatBuff, PlayAudio, SpawnPrefab, SetFlag, Damage, FoodRegen, Custom), triggers (OnUse, OnEquip, OnUnequip, OnHitLanded, OnHurt, OnPickup), equipment slots including 2 trinkets. |
| Combat | Light, heavy and alternate releases, blocking with stamina costs, `DamageInfo`, `FactionRules`, hit reactions, knockback, hit flash. `Docs/Stamina-and-blocking.md` currently says **"No timed parries"** (see open decision D3). |
| Room | The `DungeonMaster` NPC, the table intro, `LightingManager` moods including **Table Spotlight** with separate room and table light groups. |
| Screen | `ScreenManager` fades and the `RetroScreen` full-screen shader. |
| Missing | **Saving.** The data shapes are ready for it, but nothing writes to disk yet. |

---

## 3. Core loop

### 3.1 One session

```
 ┌──────────────────────── THE ROOM ────────────────────────┐
 │  Look around: see what changed since the last run         │
 │  Talk to the DM, spend Threads, pick up room items,       │
 │  read the notebook                                        │
 │     ↓                                                     │
 │  Sit at the table: choose a game box (Town, Crypt, ...),  │
 │  choose up to 2 rule cards, set up the figurine           │
 └────────────────────────────┬─────────────────────────────┘
                              ↓  (table intro, spotlight mood)
 ┌──────────────────────── THE TABLE ───────────────────────┐
 │  Floor 1 → path choice → Floor 2 → ... → Boss            │
 │  Gold, items, boons and curses last for this run only     │
 │  The DM comments, changes rules, sometimes steps in       │
 │  Optional: stand up from the table mid-run                │
 └────────────────────────────┬─────────────────────────────┘
                              ↓  (death or victory)
 ┌─────────────────────── AFTER THE RUN ────────────────────┐
 │  The DM fills in the score sheet → Threads awarded        │
 │  The figurine goes on the shelf, or a trophy is added     │
 │  The room changes (flags, objects, time of day)           │
 └────────────────────────────┬─────────────────────────────┘
                              ↓
                          next run
```

### 3.2 Target lengths

| Beat | Target length |
|---|---|
| Time in the room between runs | 1–3 minutes, and under 20 s when nothing new has happened |
| Run (3 floors + boss) | 15–25 minutes |
| Main game to the credits | 8–12 hours |
| After the credits (rule cards, all boxes, secrets) | +10 hours |

### 3.3 What carries over

| Kept after the run | Lost after the run |
|---|---|
| Threads (meta currency) | Gold |
| Unlocks (room items, boxes, rule cards, figurine parts) | Items and equipment found in the run |
| Story flags, notebook entries | Boons, curses, run blessings |
| Shelf figurines, trophies | Floor layouts (seeded, can be replayed by seed) |
| Statistics | |

---

## 4. Save system (prerequisite)

Everything after this section depends on saving. Build it first.

### 4.1 Rules

- **One `SaveManager : Singleton<SaveManager>`** under `Systems` (a prefab in `Core/Prefabs`). It owns the save file, and nothing else writes to it.
- **Slots:** 3 slots, one JSON file each: `Application.persistentDataPath/save_{slot}.json`, plus `save_{slot}.bak` holding the previous good write.
- **Safe writes:** write to `.tmp`, rename the current file to `.bak`, then rename `.tmp` to the real name.
- **Versioned:** `SaveData.version` is an int, with one migration function per step (`Migrate1To2`...).
- **Save points (automatic only):** run start, reaching each floor, run end, sitting down at the table, quitting from the pause menu.
- **Save by id:** items are saved by catalog id (the `InventoryManager` catalog exists), never by object reference.
- `UserSettings` stays separate, because it's per machine rather than per slot.

### 4.2 Data shape

These are plain serializable classes, matching the "(key, value)" rule in `CLAUDE.md`:

```csharp
[Serializable] public class SaveData
{
    public int version = 1;
    public string createdUtc, lastPlayedUtc;
    public float playSeconds;

    public List<ProgressionManager.FlagEntry> flags;   // story + unlocks (existing shape)
    public int threads;                                // meta currency
    public List<string> unlocked;                      // ids: room items, boxes, rule cards, parts
    public List<ShelfRecord> shelf;                    // one figurine per lost run
    public List<string> notebook;                      // unlocked entry ids
    public FigurineLoadout figurine;
    public RoomSnapshot room;                          // time of day, moved/changed objects
    public SuspendedRun suspended;                     // null when no run is in progress
    public MetaStats stats;
}

[Serializable] public class SuspendedRun
{
    public string levelId; public int seed, floor;
    public List<ItemStack> bag, hotbar; public List<EquipEntry> equipment;
    public int gold, health; public List<string> boons, curses, ruleCards;
    public float seconds; public int kills;
}
```

### 4.3 Saving during a run

**Quitting saves the run, and loading resumes at the start of that floor.** On load, the floor is rebuilt from `seed + floor`, and inventory, health and boons are restored. The player starts at the floor entrance and enemies respawn. This keeps the save simple (no per-enemy state) and prevents reloading to undo mistakes within a floor.

Hooks: `RunManager.RunStarted`, `ReachFloor`, `RunEnded`, and the floor-built callback in `TableLevelLoader`.

---

## 5. Meta economy

### 5.1 Currencies

| Currency | Lasts | Earned from | Spent on |
|---|---|---|---|
| **Gold** (exists) | One run | Coins, selling, chests | Merchant, altars and shrines in the run |
| **Threads** (new) | Permanent | End-of-run payout, bosses, rare Thread pickups in the dungeon, some events | Unlocks in the room (5.3) |

Threads are small coloured spools stored in a **sewing basket** by the table. How full the basket looks is its UI.

### 5.2 Payout

Shown on the score sheet at the end of the run (`RunSummary` already has every input):

```
threads = floorReached * 3
        + bossesSlain * 10
        + (victory ? 15 : 0)
        + ruleCardBonus           // sum of the active rule cards' bonus (Section 8)
        + threadPickups           // picked up in the dungeon
```

Tune toward roughly 25 for an average failed early run and 80 for a clean win. The cheapest unlocks cost 20–40, so the player can buy something after the first or second run.

### 5.3 What Threads buy

Everything is bought **from the DM** or **from objects in the room**, not from a menu screen:

| Where | Buys | Examples |
|---|---|---|
| **The DM** (dialogue) | New game boxes and difficulty levels | Swamp box, Crypt difficulty 2 |
| **Supply box** on the table | Weapons and starting gear for the figurine | Mace, spear, hand axe, buckler |
| **Recipe cards** pinned by the table | Consumables added to the dungeon loot pool | Bread (FoodRegen), tonic (TimedStatBuff AttackSpeed) |
| **Paint set** | Figurine paint and a second trinket slot | Unlock trinket slot 2, cosmetic paints |
| **Bookshelf** | Notebook pages: hints, lore, secret locations | "Where the Crypt Lord keeps his key" |

Each unlock is an id in `SaveData.unlocked`, defined by an `UnlockData : ScriptableObject` (id, cost, name, icon, where it's sold, prerequisites, flag set on purchase). There's one asset per unlock, stored under `Core/Unlocks/`.

---

## 6. Room items: room → table

**This is the main connection between the layers.** Real objects in the room become items at the table.

### 6.1 Rules

1. A **room item** is an `InteractableTrigger` in the room linked to an `ItemData` (flagged `roomItem`).
2. Picking it up in the room unlocks it (saved). The object **disappears from its spot** while it's in use, so the player can see it's gone.
3. Before a run, the player places up to **N room items** in the figurine's tray. N starts at 1, and an unlock raises it to 2, then 3.
4. In the dungeon, a room item is a normal equippable or usable item with a small bonus.
5. **Losing a run can cost a room item** (6.3). A lost item comes back to its spot in the room *worn*: cracked, burnt or bent. The worn version works differently, not just worse.

### 6.2 First set (8 items)

All eight are found in the one room.

| Room object | Where | Table item | Effect (existing effect types where possible) | Worn version |
|---|---|---|---|---|
| Pocket watch | On the side table | Trinket | OnEquip: TimedStatBuff AttackSpeed +10%. Custom: slow time briefly at low health, once per floor | Cracked: time slows on *every* parry, for half as long |
| Candle | On the windowsill | Tool (left hand) | Lights a radius around you and reveals hidden regions nearby | Burnt down: smaller radius, but enemies inside the light take +15% damage |
| Brass key | Hanging by the door | Key | Opens the first locked vault door on any floor | Bent: opens *any* door once, then is gone for that run |
| Thimble | In the sewing basket | Trinket | OnHurt: 20% chance to ignore the hit (Custom) | Dented: 35% chance, but an ignored hit costs 5 stamina |
| Marble | Under the armchair | Throwable (11.3) | Rolls along the floor and makes enemies it hits stumble | Chipped: splits into 3 small marbles |
| Salt shaker | On the table | Consumable (3 uses) | SpawnPrefab: a salt circle that undead won't cross for 8 s | Clogged: 1 use, lasts 20 s |
| Photo frame | On the mantel | Trinket | OnFloorStart: heal 10 | Faded: heal 20, but -5 max HP |
| Fork | Next to the DM's plate | Weapon | Fast, weak stabbing weapon that ignores 1 armor | Bent: slower, and knocks enemies back |

### 6.3 Losing room items

- **On death:** each room item you brought has a 50% chance to stay behind in the dungeon. The DM puts it in their box, and you can see it there.
- It can be **won back** on a later run: it appears as a guarded reward on the floor where you lost it (`DungeonCorpse` already exists). Getting it back gives you the worn version.
- **On a win:** all room items come back, and the DM comments on them.

Death costs something, but it isn't harsh, and it gives the player a reason to go back to that floor.

### 6.4 Implementation notes

- Add to `ItemData`: `bool roomItem`, `ItemData wornVersion`, `string roomObjectId`.
- Add a `RoomItemSpot` component on each item's place in the room. It shows the intact mesh, the worn mesh or nothing, based on save data.
- The pre-run tray is a physical tray on the table (15.1). It fills the Table player's `Inventory` at `BeginRun`.

---

## 7. Run history: table → room

This is the other direction: every run leaves something behind in the room.

### 7.1 Figurine shelf

- Each lost run puts a **figurine on the shelf** next to the table: your figurine, lying down, with a small paper tag showing the box, floor, what killed you and the run time (`RunSummary` has all of this).
- Interacting with one opens its score sheet (reuse the `RunRecapUI` layout).
- At 10, 25 and 50 figurines, the DM has new lines about the collection. At 25, the shelf is full and the DM brings a bigger one.
- Record shape: `ShelfRecord { levelId, floor, killer, seed, seconds, kills, dateUtc, paintId }`.

### 7.2 Trophies

- Beating a boss puts its **miniature** in the room. For example, the Crypt Lord goes on the mantel.
- Some trophies can be interacted with: the Crypt Lord miniature unlocks Crypt difficulty 2.

### 7.3 Room changes

A `RoomStateDirector` (a scene component, not a manager) reads flags whenever the player is in the room and applies authored **room changes**:

```csharp
[CreateAssetMenu] public class RoomChange : ScriptableObject
{
    public string id;
    public FlagCondition[] when;          // e.g. crypt_lord_killed >= 1, shelf_count >= 5
    public RoomChangeAction[] actions;    // enable object, move object, swap material,
                                          // set SceneMood, start conversation, play sound
    public bool once = true;
}
```

`RoomChangeAction` follows the `InteractionAction` pattern: an enum `type`, Odin `[ShowIf]` fields, and a `Custom` escape hatch.

First set:

| Trigger | What changes in the room |
|---|---|
| First death | The DM brings it up the next time you talk |
| Crypt Lord beaten | The candles in the room flicker for one run. The Crypt Lord miniature goes on the mantel |
| Killed by Crypt Mites 3 times | The DM leaves a small toy mousetrap next to your seat as a joke |
| First win on any box | The DM's chair is empty when you return, with a note on it |
| Room item lost | Its spot is empty, with a dust outline where it was |
| 5 runs in one sitting | The room is at late night: the clock shows 3:00 and the DM is tired and more talkative |

### 7.4 Time of day

Each run moves the room one step forward: Dusk → Evening → Night → Late Night → Dawn. At Dawn the session ends and the next one starts at Dusk. The `SceneMood` presets and `LightingManager.Moods` blending already support this. It gives the room a visible change every run and gives the story a way to mark progress.

---

## 8. Rule cards (difficulty and modifiers)

### 8.1 Concept

A drawer under the table holds **rule cards**. Before a run you can put up to 2 (3 later) in front of the DM. Each card makes the run harder and adds bonus Threads. Some cards change how you play, not only how hard it is.

### 8.2 Data

```csharp
[CreateAssetMenu] public class RuleCardData : ScriptableObject
{
    public string id, displayName;
    [TextArea] public string cardText;
    public int threadBonus;
    public RuleCardEntry[] effects;         // enum type + [ShowIf], Custom escape hatch
    public string[] incompatibleWith;
}
```

`RuleCardEntry` types (new enum `RuleCardType`, explicit values):
`EnemyStatMultiplier = 0`, `PlayerStatMultiplier = 1`, `LootTableShift = 2`, `DisableFeature = 3`, `ExtraEncounterChance = 4`, `MerchantPriceMultiplier = 5`, `FloorModifier = 6`, `Custom = 99`.

### 8.3 First set (12 cards)

| Card | Effect | Bonus |
|---|---|---|
| Hidden map | Hidden regions stay hidden until you enter them | +5 |
| Higher prices | Merchant prices ×1.5 | +4 |
| Costly blocks | Blocking costs double stamina | +8 |
| Tougher enemies | Enemies have +30% HP | +8 |
| No food | Food items don't drop | +6 |
| Dark floors | Floor lighting is halved, so the candle matters much more | +7 |
| Risky altars | Altars always give curses, but curses pay double gold | +5 |
| Second wind | Every third enemy you kill gets back up once | +10 |
| Silence | No music, and enemies hear you from further away (`NoiseEvents`) | +6 |
| More elites | Elites appear twice as often, and elite loot is doubled | +6 |
| One hit | You have 1 max HP, and every enemy also dies in one hit | +20 |
| Fork only | Unlocked late: every weapon is replaced by the fork | +15 |

### 8.4 Difficulty levels per box

Each box has **difficulty levels 0–5**. Winning with cards worth at least *X* bonus unlocks the next level. Each level adds a permanent base modifier, and levels 3 and 5 add a new boss phase. This keeps runs interesting after the credits.

---

## 9. The Dungeon Master

### 9.1 Role

The DM is the only other character. They sit across the table (`DungeonMaster.prefab` exists, and `TableManager` places it at `dmPlacement`). In the room, the DM is who you talk to, buy from and get feedback from. During runs, the DM **narrates, changes the rules, and sometimes steps in**. The player doesn't see the DM from inside the dungeon, except at key moments: a hand reaching in, or their face visible above rooms without a roof (hidden roofs already exist).

### 9.2 In the room

- **Conversation hub.** Talking to the DM covers the run recap, buying boxes and difficulty levels, hints and small talk. It's a Pixel Crushers conversation that branches on flags.
- **Reactions to what you do in the room.** Picking up a room item, reading the notebook or looking at the shelf gets a short comment.
- **Idle behaviour.** Between runs the DM does small things: sorts miniatures, rolls dice, reads rule books, drinks tea, looks at you. These are authored animation clips chosen by time of day.

### 9.3 `DungeonMasterDirector`

This is a component on the DM, not a singleton. It subscribes to events and picks reactions:

| Event (existing source) | Example reactions |
|---|---|
| `RunManager.RunStarted` | An opening line based on the rule cards and the last run |
| Floor reached | A short description line, and sometimes a rule change (9.5) |
| `Character.Damaged` (player, big hit) | "Ouch." |
| Player at low HP | Leans in. The table spotlight narrows (mood blend) |
| Enemy killed by a parry | "Nice." |
| Boss killed | Reacts, and knocks the table (camera shake) |
| `RunEnded(false)` | Picks up your figurine, looks at it, puts it on the shelf |
| Room item used | Recognises it: "Is that the watch from the side table?" |
| Player stands up mid-run (Section 12) | Taps the table and waits. Comments if it takes long |

### 9.4 Comments

- Short lines (1–2 s), shown **as subtitles at the bottom**, optionally voiced (Section 14).
- Stored as Pixel Crushers bark conversations per event, with Lua conditions on flags, so lines can be added without code.
- Rules: no more than 1 comment every 12 s, never during a boss wind-up, never the same line twice in a row, and important lines can interrupt.

### 9.5 Rule changes between floors

Between floors, the DM sometimes **changes something** (about 1 in 3 floors, never on floor 1):

| Change | Effect | How it's shown |
|---|---|---|
| Extra rule | A random rule card applies for the next floor, for double bonus | The DM puts a card on the table |
| Extra enemy | An elite spawns behind you after 60 s | Dice roll sound |
| Bonus chest | An extra chest on this floor | The DM's hand places it |
| Weapon swap | The DM swaps your weapon for a random one | The hand reaches in, takes it and leaves another |
| Help (only below 25% HP) | Heal 30%, but fewer Threads at the end | The DM drops a potion in |

The player can sometimes **decline** a change by paying gold.

### 9.6 The DM's hand

A reusable **hand** actor for the table world: a large arm model that reaches in from above.

- Places or removes objects (chests, enemies, walls).
- Picks up the figurine when you die. The camera stays with the figurine as it's lifted, tilts to show the room, and ends at the shelf.
- Takes part in boss fights (10.7).

It's an animated prefab with authored `.anim` clips (clips, not code), driven by a `DmHand` component with `Reach(Vector3 target, HandGesture gesture)`.

---

## 10. Dungeon runs

### 10.1 Run structure

```
Floor 1 ── path choice ── Floor 2 ── path choice ── Floor 3 ── Boss floor
 (6-8 rooms)               (8-10 rooms)              (10-12 rooms)
```

Each floor already has an exit room (`DungeonExit`). New: the exit has **2–3 doors**, each with a painted sign for the next floor's type:

| Sign | Next floor |
|---|---|
| Swords | More fights, better loot |
| Coin | A merchant is guaranteed, fewer fights |
| Skull | An elite room with a guaranteed boon |
| Candle | A shrine floor: 2 altars or fountains, fewer enemies |
| Question mark | An event floor (10.5) |
| Key (needs a key) | A vault floor with treasure and traps |

Implementation: a `FloorModifier` asset (or a serializable entry on `TableLevelData`) adjusts `encounterChance`, `features`, `roomCount` and loot for the next floor. `DungeonExit` becomes a choice, and the result is passed to `TableLevelLoader` before generating.

### 10.2 Boons that change how you play

Right now boons are mostly stat changes. The goal is boons that **change how you play**. `DungeonBoon` stays the resolver, with **new triggers** and **Custom effects** added:

New `EffectTrigger` values (appended): `OnKill = 6`, `OnBlock = 7`, `OnParry = 8`, `OnFloorStart = 9`, `OnLowHealth = 10`, `OnDodge = 11`.

Boons belong to groups, so builds form over a run:

| Group | Focus | Examples |
|---|---|---|
| **Parry** | Rewards parries and precise hits | After a parry, the next hit deals ×3. Heavy-attack kills refund stamina |
| **Sustain** | Healing and fire | Kills heal 2. Blocked attackers catch fire |
| **Control** | Knockback and stagger | Heavy hits knock enemies into each other for damage. Blocks push attackers back |
| **Risk** | Power at low health | Below 30% HP, +40% damage. Gain gold when hurt |
| **Meta** | Rare, affects later runs | +1 Thread per elite. The next run starts with this boon at half strength |

After picking 3 boons from one group, the next altar offers that group's **final boon**. For example, the Parry group's final boon freezes time for 0.5 s after a perfect parry.

### 10.3 Curses as a trade

Altars can already roll bad outcomes (`spawnEnemies`, negative effects). The change is to make curses **a choice with a reward** instead of bad luck:

- A **trade altar** offers 2 options: a strong boon with a curse, or a small boon with no curse.
- Curses: Slow (-20% move speed), Loud (noise radius ×2), No armor (armor 0), Followed (a slow ghost follows you for the rest of the floor).
- Curses can be removed at fountains (new `EffectType.Cleanse = 9`) or by the merchant for gold.

### 10.4 Elites

Elites are normal enemies with **1–2 traits** added as components on the spawned enemy, so no new prefabs are needed:

| Trait | Behaviour | Visual |
|---|---|---|
| Burning | Leaves fire patches. Hits set you on fire | Ember particles, orange rim light |
| Shielded | Blocks hits from the front until staggered | A shield decal glows when it blocks |
| Splitting | Splits into 2 small copies on death | Wobbly scale when idle |
| Fast | +40% move and attack speed | Motion trails |
| Draining | Heals when it hits you | Red outline pulse |
| Escorted | Spawns with 2 minions that die when it dies | Chains linking them |

`EliteTraitData : ScriptableObject` (one per trait) holds stat modifiers, a VFX prefab, and a Custom behaviour. Elites get a **name tag** via `DungeonEnemyBar` (for example, "Burning Crypt Soldier") and drop extra gold and a guaranteed item.

### 10.5 Events (rooms without combat)

These are authored **event rooms** (reusing `DungeonRoomTemplate` and sockets) that run a Pixel Crushers conversation with choices:

| Event | Setup | Choices |
|---|---|---|
| Ghost | A small ghost in an empty room asks for help | Give it an item (a boon later), ignore it, or attack it (it becomes an elite) |
| Cup game | A small creature with 3 cups | Bet gold on a shell game (it's fair, and the notebook says so) |
| Question chest | A chest asks about the **room** ("What's on the mantel?") | Answer from what you've seen in the room. A right answer gives a rare item |
| Old figurine | One of your lost figurines from the shelf, standing in a room | Fight it (it has that run's weapon) or leave it (Threads) |
| Crumb | A cracker crumb from the DM's plate blocks a corridor | Eat it (heal, the DM notices) or go around |

The question chest is a key example of the two layers connecting: the answer is in the room.

### 10.6 Secrets

- **Breakable walls** (`DungeonDestructible` exists) that hide vaults, hinted at by cracks and a draft sound.
- **Lever sequences** (`DungeonLever` exists) that open a hidden floor.
- **Model room floor:** a hidden floor under the Crypt, reached with the candle and brass key together. It's a miniature copy of the room you're sitting in, and it sets up the story in 16.2.

### 10.7 Bosses

Every boss has **3 phases**, and in the last one the DM takes part directly:

**Crypt Lord (exists)**
1. Duel: sword attacks with clear wind-ups.
2. Calls Crypt Mites out of graves (`DungeonGrave` exists).
3. **DM phase:** the hand removes part of the arena wall, showing the room beyond. The hand hits the table every few seconds, knocking everyone down, including the Crypt Lord. Timing a dodge to the hit is the skill.

**Village Brute (Town)**
1. Charges and breaks furniture.
2. Throws barrels.
3. **DM phase:** the DM tilts the table, so the arena slopes and barrels and the player slide.

---

## 11. Combat additions

### 11.1 Parry (open decision D3)

`Stamina-and-blocking.md` rules out timed parries today. This design proposes **adding** one, because it's the biggest single improvement to melee feel and it's what the Parry boon group is built around. If D3 stays "no", replace the Parry group and parry hooks with "perfectly timed block costs no stamina".

- **Window:** block pressed within 150 ms **before** the hit lands. Tunable on `CombatManager`, and tighter at higher difficulty levels.
- **Result:** no stamina cost. The attacker is **staggered** for 0.8 s (a new `EnemyState` value, appended). 60 ms hit pause, a spark VFX, a metal sound, and a short RetroScreen pulse.
- **Follow-up:** during the stagger, the next light attack deals ×2 damage and uses an alternate release.
- **Unblockable attacks** (red glint) can't be parried, only dodged.
- Hooks: `PlayerCombat.TryBlock` already gets `ref DamageInfo`. Add a `DamageInfo.Parried` flag that's broadcast on `Character.Damaged`, so FX, boons (`OnParry`) and the DM all see it.

### 11.2 Dodge

- A short sidestep on a dedicated key (or double-tapping a direction). Costs 12 stamina, with about 200 ms of invulnerability.
- It gives Risk and Control builds an alternative to blocking, and it's required for unblockable attacks and the boss table hit.

### 11.3 Throwables

- `ItemType.Throwable = 7` (appended). Thrown with the right hand, using an authored throw clip.
- Sources: marbles (room item), skulls and bottles from destructibles, and your own weapon (which you then have to pick up again).
- Damage plus a short stagger. Headshots on humanoids deal ×1.5.

### 11.4 Using the environment

- **Hazards:** spike pits, braziers and walls. Knockback (via `EnemyFX`) into a hazard deals hazard damage, and into a wall deals +5 damage and staggers.
- **Kick:** a new action costing 8 stamina, with strong knockback and low damage. It breaks a shielded enemy's guard.
- The generator places hazards through `DungeonPropRule` with a new hazard category.

### 11.5 Readable attacks

Every enemy attack has:
1. A **wind-up** of at least 0.35 s (at least 0.25 s for Fast elites).
2. A **glint** (white = blockable, red = unblockable) timed to the parry window.
3. A **distinct sound** per attack type, so attacks can be learned by ear.
4. A faint **ground marker** for area attacks.

Also: a **hit direction indicator** (a light red vignette on the side the hit came from), and a sound cue when an enemy starts an attack behind you.

---

## 12. Standing up mid-run

### 12.1 Concept

At any moment outside combat, the player can **stand up from the table**. The camera leaves the figurine and you're the Room player again, while the dungeon stays paused on the table. You can walk around the room, use things, and sit back down.

### 12.2 Why

It keeps the two layers from feeling like separate games, and it lets secrets and puzzles use the room.

### 12.3 Things to do in the room during a run

| Action in the room | Effect on the run |
|---|---|
| Drink from the glass of water on the table | Heal 20% (once per run) |
| Switch on the desk lamp by the table | The current floor gets brighter, and hidden regions nearby are revealed |
| Open the window | Wind blows out the torches on this floor: darker, but enemies notice you less |
| Read the notebook | Nothing mechanical. You check hints |
| Look down at the table | You see the whole floor from above, with enemies as miniatures. **This is the map.** |
| Take the DM's dice | The next rule change is cancelled. The DM notices (flag) |

### 12.4 Cost

- Each time you stand up, the DM makes a small change while you're away (an extra patrol, a moved chest), shown when you sit back down.
- You can't stand up while enemies are alerted nearby or during a boss fight.

### 12.5 Implementation

- `GameManager`: push a new `PausedRun` state (or use `Cutscene` for the transition). The dungeon is paused by disabling its `EnemyBrain`s and the table player's controller, without touching `Time.timeScale`, so the room keeps running.
- `PlayerManager` switches the active player to Room. The Table player's figurine stays visible and frozen.
- The map is the real dungeon seen from above. Hidden-roof regions already exist, so from above you see only the rooms you've revealed.

---

## 13. Art direction

### 13.1 Two looks

| | Room | Table |
|---|---|---|
| **Feel** | Real, warm, lived-in, a little strange | Hand-made miniature, like a diorama |
| **Scale cues** | Normal human scale | Visible felt, cardboard, wood grain, brush strokes, glue seams |
| **Colour** | Warm lamp light, cool moonlight from the window | More saturated, clearly lit like a stage |
| **Depth of field** | Almost none | **Tilt-shift** when the Room player looks at the table |
| **Lighting** | Lamps, window, candles | Table spotlight plus small lights inside the dungeon |
| **Post-processing** | Light film grain, slight vignette | RetroScreen only for story moments |

### 13.2 Tilt-shift

- When the **Room player looks at the table** (the camera's forward ray hits the table collider within 3 m), blend in a URP depth of field volume: Bokeh, focused on the table surface, with strong blur above and below. This makes the dungeon read as a miniature straight away.
- When the Table player is active, use only a light blur on distant geometry, so combat stays readable.
- A `TableFocusVolume` component drives the volume weight. `ScreenManager` owns it, so post-processing has one owner.

### 13.3 Miniature materials (on top of Synty)

Synty models are clean and flat, so the table needs a hand-made layer:

- A **table-world shader variant** (URP Lit plus custom): a triplanar **brush-stroke / paper-grain** detail normal, light **edge wear** (a curvature-based highlight on edges), and a small specular boost that reads as varnished paint.
- **Floors:** a light felt texture blended into the stone.
- **Wall tops** in roofless rooms show a **cut cardboard edge** instead of a clean cap. It's cheap and reads as a diorama immediately.
- **Roofless rooms** show the room above: far away, blurred, warm. Keep a low-detail version of the room ceiling and the DM's face above, visible when looking up.

### 13.4 Room lighting

- **Every light has a visible source:** desk lamp, floor lamp, window, candles, the table spotlight. No unmotivated fill light.
- **Time of day** (7.4): Dusk (orange sunlight through the window, long shadows), Evening (lamps on, blue outside), Night (only the table lamp and moonlight), Late Night (the table spotlight is the only warm light), Dawn (grey-blue, cold).
- **Light shafts:** URP has no volumetric fog, so use additive, depth-faded light-shaft meshes through the window and under the table spotlight, with dust particles in each shaft.
- **Rain on the window** at night: a screen-space refraction shader on the window material and a rain sound loop.
- **One box-projected reflection probe** for the room, so the table and floor reflect the lamps properly.

### 13.5 Dungeon lighting

- **Candles and torches** flicker (noise-driven intensity and a small position jitter). Only the nearest 4–6 cast shadows (a distance-sorted budget), to keep URP's additional-light shadow cost down.
- **Deeper floors are darker:** floor 1 ambient 0.35, floor 3 ambient 0.12. This makes the candle item worth bringing.
- **The table spotlight** is the dungeon's sun. When the DM leans in, it narrows and warms (mood blend), which tells the player the DM is paying attention.
- **Rim light on enemies** (a small emissive fresnel on the enemy material), so their outlines stay readable in the dark.

### 13.6 Small changes in the room

These are subtle, and triggered by flags or time of day. They keep the room worth looking at:

- The shelf fills up, and trophies appear on the mantel.
- A room item's spot is empty or holds the worn version.
- A second cup appears next to the DM's at Late Night.
- The clock stops at the time of your last loss until the next run starts.
- After the model room floor (10.6) is found, the DM's miniature box contains a tiny copy of the room.

### 13.7 VFX

| Effect | When |
|---|---|
| Spark and white flash | Parry |
| Dust puff and a wooden click | An enemy dies on the table (it falls over like a miniature, then fades) |
| Coins as small painted discs | Gold (keeps the miniature look) |
| Paper confetti / small flag | Floor cleared |
| Dust falling from above | The DM's hand reaching in |
| Glow on spools | Thread pickups and the Thread payout |

### 13.8 Enemy deaths

Enemies **fall over like knocked miniatures**: they tip onto their base with a small bounce. This fits the miniature look better than ragdolls and is cheaper. Bosses keep a full death animation, then settle into a still miniature pose.

---

## 14. Audio

### 14.1 Mix

- **The Room** is quiet: clock ticking, the lamp buzzing, rain, the DM moving pieces and turning pages.
- **The Table** has two layers: the dungeon's own sound (swords, monsters, corridor reverb) and a quiet **table layer** (wooden clicks on footsteps, dice rolls when enemies spawn, cardboard creaks on doors).
- **Standing up** crossfades the two over 1 s, with a low-pass filter on the dungeon while you're in the room.

### 14.2 Room sound that follows the run

A `RoomAmbience` component reads run state and adjusts the mix:

| Run state | Room sound |
|---|---|
| Player below 30% HP | The clock ticks louder |
| Boss fight | Everything in the room goes quiet except the DM's breathing |
| Win | The DM laughs or claps |
| Death | The DM's chair creaks and they breathe out |

### 14.3 Music

- Room: a simple piano or music-box theme, one variation per time of day.
- Table: one track per box (folk for the Town, choir drone for the Crypt), plus a combat layer that fades in based on how many enemies are alerted.
- Boss: its own track. In phase 3 the room theme comes in on top.

### 14.4 DM voice

Start with **gibberish voice** (pitch-varied syllables) plus subtitles. It avoids a voice-acting budget and suits the DM. Record key lines later.

---

## 15. UI and UX

### 15.1 Physical UI where possible

| Information | Current | Proposed |
|---|---|---|
| Health | HUD bar | Keep the bar for combat. **Also** show candles on the table edge, visible from the room |
| Gold | `GoldCounterUI` | Keep it, and add a small coin stack next to the board that grows |
| Run recap | `RunRecapUI` panel | The **DM's score sheet**: a paper UI in a handwritten font, filled in line by line with a pen sound |
| Pre-run setup | `TableAdventureMenuView` menu | A **setup tray**: choose a box by picking it up from the shelf, and choose rule cards by sliding them over |
| Map | none | Stand up and look down (Section 12) |
| Threads | none | The sewing basket fill level, plus the payout on the score sheet |
| Notebook | none | A real notebook on the table, opened in a close-up view |

### 15.2 Notebook

- Tabs: **Enemies** (enemies met and kills; weaknesses are added after enough kills), **Items** (everything found, with descriptions), **Boxes** (a sketch of each box's areas), **Notes** (hints for secrets and events, e.g. the question chest), **Runs** (the last 20 runs).
- Entries are ids in `SaveData.notebook`. New pages appear with a handwriting animation the next time you open it.

### 15.3 Accessibility and options

- A wider parry window (+100 ms), aim assist for throws, subtitle size and background, and toggles for screen shake, RetroScreen and flashes. Hold or toggle for blocking. Glints that differ by shape as well as colour (a star for blockable, a triangle for unblockable).

### 15.4 First runs

- First run: the DM **explains as you go**, with scripted lines on floor 1 ("Try raising your shield.") and prompts that disappear once you've done the action (flags).
- The pocket watch is on the side table right next to your seat, so the first room item is found before run 2.
- The first Thread unlock is affordable after run 1 and is offered by the DM directly, which teaches the meta loop.

---

## 16. Story

The story is kept small: one room, one person across the table. The beats below are a starting structure (see D1).

### 16.1 Characters

| Character | Role |
|---|---|
| **You** | Sitting down to play. The figurine is you in the game |
| **The DM** | Runs the game. Friendly at first, then more involved. The dungeon reflects what the DM is thinking about |

### 16.2 Three parts

**Part 1: Playing (runs 1 to ~8)**
Relaxed. The DM is playful and the rules are fair. You learn the Town and the Crypt. The room changes are small and funny. *Ends when you beat the Crypt Lord for the first time:* the DM goes quiet, and their chair is empty when you get back to the room.

**Part 2: The game changes (runs ~9 to ~20)**
The DM changes rules more often and more openly. The room changes between runs in ways the DM doesn't mention. The model room floor (10.6) shows the dungeon is built from the room itself: dungeon rooms echo objects in this room, and question chests ask about it. *Ends when you take the DM's dice* (12.3). The DM gets up and leaves the table.

**Part 3: Swapping seats (final runs)**
You sit in the DM's chair and run a dungeon for the DM's figurine. The Room player and Table player are already separate `Player`s, so this works without a new character setup. The last encounter is a conversation with the DM across the table, not a fight.

### 16.3 Endings (2)

- **Keep playing:** the game goes on, and new boxes and difficulty levels unlock.
- **Pack the game away:** you close the box. Dawn comes, and the DM leaves. Credits. The save stays playable, and runs are narrated only by the room sounds.

### 16.4 Writing rules

- The DM never explains the game directly. The story comes through objects, room changes and what the DM chooses to say.
- Each run has at least one line referring to something the player did (a kill, a room item, where they died).
- No text longer than 3 lines. Lore goes in the notebook.
- Plain names: things are called what they are ("the shelf", "the watch", "the Crypt Lord").

---

## 17. Roadmap

Each milestone leaves the game playable from start to end.

| Milestone | Content | Done when |
|---|---|---|
| **M0: Foundation** | `SaveManager` (4), Threads and payout (5.1–5.2), score sheet (15.1), figurine shelf (7.1) | Quitting and relaunching keeps flags, Threads and the shelf. Every run ends with a Thread payout |
| **M1: Room and table connected** | Room items (6) with 4 items, `UnlockData` and the room sellers (5.3), `RoomStateDirector` with 6 room changes (7.3), time of day (7.4) | A run with a room item changes the room, and the room changes the next run |
| **M2: The DM** | `DungeonMasterDirector` and comments (9.3–9.4), 3 rule changes (9.5), the hand (9.6) for the death pickup, tilt-shift (13.2) | The DM comments at least 5 times per run. Dying plays the pickup-to-shelf sequence |
| **M3: Run depth** | Path choice (10.1), new triggers and 2 boon groups (10.2), trade altar and curses (10.3), 3 elite traits (10.4), parry or perfect block (11.1, D3) | Two runs with different paths and boon groups play differently |
| **M4: Room depth** | Standing up (12), notebook (15.2), rule cards (8) with 6 cards, 2 events (10.5) including the question chest | You can win a run you'd otherwise lose by using the room mid-run |
| **M5: Look and sound** | Miniature materials (13.3), room lighting (13.4), dungeon light budget (13.5), miniature deaths (13.8), room sound that follows the run (14.2) | A screenshot of the table reads as a miniature without any UI |
| **M6: Story** | Parts 1–3 (16.2), Crypt Lord DM phase (10.7), model room floor, endings | A new player can reach the credits |
| **M7: Content** | A third box, difficulty levels (8.4), the remaining room items, boons, cards and events | 10+ hours before the credits |

---

## 18. Enum and data additions (append only)

| Enum | New values | Notes |
|---|---|---|
| `EffectType` | `GrantThreads = 8`, `Cleanse = 9`, `Stagger = 10` | Add `Describe` lines and `ItemEffectProcessor.Apply` cases for each |
| `EffectTrigger` | `OnKill = 6`, `OnBlock = 7`, `OnParry = 8`, `OnFloorStart = 9`, `OnLowHealth = 10`, `OnDodge = 11` | Raised from `EffectDispatcher` |
| `ItemType` | `Throwable = 7` | Add the matching `ItemTypeMask` bit |
| `EnemyState` | `Staggered` = next unused value | Check the next unused value first. Retired numbers stay retired |
| `RuleCardType` (new) | `EnemyStatMultiplier = 0` … `FloorModifier = 6`, `Custom = 99` | |
| `RoomChangeType` (new) | `SetActive = 0`, `Move = 1`, `SwapMaterial = 2`, `SetMood = 3`, `StartConversation = 4`, `PlaySound = 5`, `Custom = 99` | Same pattern as `InteractionAction` |
| `GameManager` state | `PausedRun` (if standing up gets its own state) | Reference-counted like the others |

New data assets (one per thing, under `Assets/Game/<Area>/`):

| Asset | Folder |
|---|---|
| `UnlockData` | `Core/Unlocks/` |
| `RuleCardData` | `Levels/RuleCards/` |
| `RoomChange` | `World/RoomChanges/` |
| `EliteTraitData` | `Characters/Enemies/EliteTraits/` |
| `FloorModifier` | `Levels/FloorModifiers/` |
| `BoonGroupData` | `Levels/Boons/` |

New managers and components:

| Name | Kind | Where |
|---|---|---|
| `SaveManager` | Singleton (prefab) | `Systems` |
| `MetaManager` (Threads, unlocks) | Singleton (prefab), or an extension of `ProgressionManager` (D2) | `Systems` |
| `RoomStateDirector` | Scene component | Room |
| `DungeonMasterDirector` | Component on the DM | DM prefab |
| `DmHand` | Component on the hand prefab | Table |
| `RoomItemSpot` | Component | Each room item's spot |
| `FigurineShelf` | Component | The shelf |
| `TableFocusVolume` | Driven by `ScreenManager` | Room camera |

---

## 19. Open decisions

| # | Decision | Options | Recommendation |
|---|---|---|---|
| D1 | Who is the DM? | A friend / a relative / a stranger / never said | Never said directly: the player learns about the DM through what they say and the room changes |
| D2 | Where do Threads and unlocks live? | Extend `ProgressionManager` / a new `MetaManager` | A new `MetaManager`, so `ProgressionManager` stays a plain flag store |
| D3 | Timed parry | Add a parry (11.1) / keep "no timed parries" and add a perfect block | Add a parry: it's the biggest improvement to melee feel |
| D4 | Saving mid-run | Save on quit, resume at the floor start / no mid-run saves | Save on quit, resume at the floor start (4.3) |
| D5 | When can you stand up? | Any time outside combat / only at floor exits | Any time outside combat, with a DM change as the cost |
| D6 | Losing room items on death | 50% / always / never | 50%, with a chance to win it back on that floor |
| D7 | Enemy deaths | Ragdoll / fall over like a miniature | Fall over like a miniature, full animation for bosses |
| D8 | Part 3 seat swap | Build it / cut for scope | Build it: it's the most memorable moment, and the two-player setup already supports it |
| D9 | DM voice | Gibberish voice / TTS / full voice acting | Gibberish voice for now |

## 20. Risks

| Risk | Mitigation |
|---|---|
| Time in the room feels like a delay between runs | Keep required room time short. Room changes are visible from the seat. When nothing is new, go straight back to the table |
| Too many systems before combat feels good | M3 includes combat feel. Playtest after M3, before M4 |
| Standing up is abused (free map, healing) | Room effects work once per run, and every stand-up triggers a DM change |
| Save changes break old saves | `version` plus one migration per step. Keep `.bak`. Never rename saved ids |
| URP light cost in dungeons | Shadow light budget (13.5), and bake lighting where floors don't change after they're revealed |
| Story beats depend on unfinished content | Every story beat is a flag-driven `RoomChange` or conversation, so beats can be reordered without code changes |
