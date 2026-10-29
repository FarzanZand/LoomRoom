# The Dungeon Master's voice

How every line the Dungeon Master says, and every message the game prints, is written. Read it before adding lines.

## The voice

He is dry, a little smug, and on the player's side more than he admits. He says what happened, plainly, and lets a
small dig do the rest. He never narrates like a book.

## Rules

- **Say the thing.** A line tells the player something true and concrete: what happened, what is next, what it costs.
  "There's a boss two floors down." not "Something waits below."
- **Vary the shape.** Do not open more than about a third of lines with a clipped fragment and a full stop ("Here.",
  "Not yet.", "Greedy.", "Fine."). Most lines are one plain sentence. Two short sentences are fine now and then; three
  in a row ("A duel. One on one. Face him.") almost never.
- **One joke at most, and never explained.** "Should have crouched." is enough; "I'll mock you every time" explains
  the joke and kills it.
- **No stock phrases or idioms.** Not "Fortune favours the bold", "The bigger they are", "learn the hard way", "Well
  played", "fair and square", "new roads, new trouble", "start to end".
- **No vague dread.** No "something waits", "something stirs", "it remembers", "the shadows", "ancient", "the fallen",
  "beckons", "awaits". Name the thing.
- **No sensory clichés in messages.** Not "warmth spreads through your limbs", "your weapon hums", "a chill settles in
  your bones". Say the result: "Your weapon hits harder (+2 damage)."
- **No drama punctuation.** No ellipses for suspense ("You die..."), no exclamation marks on log lines, no em or en
  dashes anywhere.
- **No lists of three for rhythm** ("Food, a fire, and things I've heard"). List three things only when there are three.
- **No poetic twists** ("That's not how the songs will tell it", "Some come back with a crowd still cheering in their
  ears").
- **Same words for the same things everywhere:** the bag (not inventory or pack), armour (British spelling), a run
  (not an adventure), Level (not LVL).
- **Read a list of lines out loud in a row.** If they all have the same rhythm, rewrite some.

## Checking

Lines live in code (`DungeonMaster.Line` defaults), in prefabs and assets, and in `Room.unity`. The Asset Studio's
Dialogue view lists them all in one place; read them there, by where they play, before calling a batch done.
