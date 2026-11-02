# AI Control - Fully Autonomous Combat Companions v0.3.25

Everything since v0.3.24, from the author's requests and three play-test rounds (test1-test3).

## Highlights

- **Smoother play.** Each decision now leaves much less memory behind. The stutters were the game's memory clean-ups
  landing inside a big caster's decisions, which left 4-10 MB each. In the test3 log most Orvath decisions grew the heap
  by 40-300 KB. Two more savings: spells a character cannot cast this turn (action already used, or still cooling down in
  real time) are no longer weighed, and a spell's caster level and DC are worked out once per decision instead of once
  per target.
- **Kineticist: Unraveling and Chain Arrows** are chosen automatically when they are worth their burn. Unraveling is
  judged by which enemy buff it would dispel and how likely the dispel is; Chain Arrows by the enemies its blast would
  jump to.
- **Psionics mod: psionic focus is regained.** The AI meditates for focus when the feats, trance and maneuvers that need
  it are worth the action. It pairs Psionic Meditation's move action with the turn's attack instead of always preferring
  a full attack. After a fight it regains focus and puts back a trance that the AI's own spending ended.
- **Potions.**
  - Magical Vestment is drunk only when it beats the armor's or shield's own bonus, and never without a shield.
  - Align Weapon and Bless Weapon only against damage reduction they get through; Bless Weapon also for criticals on
    evil enemies.
  - Lesser Restoration when the drinker is fatigued, exhausted or under a stat penalty.
  - Healing potions are no longer drunk to top up HP in a nearly won fight, but still are when a companion is about to
    drop.
- **Weapons against damage reduction** follow the game's own rule (cold iron, silver, holy, "and" versus "or" reductions).

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.25.zip` through Unity Mod Manager. UMM displays
**0.3.25**. The mod ID and settings location remain `CompanionAI_Pathfinder`, so existing settings are kept. Startup
line:

```text
[Build] v0.3.25 module=b1f83486-ff07-4245-b5d2-f4b0998b5d1a
```

## Known limitations

- A large caster still takes about 0.3 s per decision (Orvath, 77 abilities), and a memory clean-up can still fall
  inside one (3 in the test3 log, under 1.5 s).
- Not yet seen in play: Unraveling and Chain Arrows chosen, psionic focus regained, the Magical Vestment, Align Weapon and
  Bless Weapon potions.
- An archer that cannot see an enemy it is in range of may try an "advance" that goes nowhere (no action is lost).
  Fixing it changes movement and is left for discussion.
