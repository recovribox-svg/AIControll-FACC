# v0.3.70 features: reposition, weapon sets, summon choice, far escape teleports (26 November 2026, Claude)

The author asked for three features (the proposal list) and one follow-up to P1. Each is evidence-based and bounded, and
uses the engine's existing measures. Source `a6cd4a9` on `claude/v0370-features`, stacked on
`claude/v0370-playtest-review` (draft PR #116). Package
`test-builds/AI-Control-FACC-v0.3.70-dev-features1-a6cd4a9-UMM-test.zip`, module `82dcebbe-430b-479e-9e9b-58116176795e`,
UMM 0.3.70, **unplayed**. [Receipt](competence/V0370_FEATURES_CHECKS.json).

## F1. Ranged units reposition with their leftover move action (turn-based, movement)

**Reference.** The Rogue Trader CompanionAI commit (216cfad) moves a "prefer ranged" unit at the end of its turn when its
movement points are left. It moves when fewer melee enemies can reach the retreat tile, or when the enemy-turn threat
there is clearly lower. It does not check weapon range at the destination.

**Here.** In a turn-based fight, a unit is offered "Reposition before the turn ends" when it is ranged-style (a ranged
weapon, the Ranged preference or the Support role), has spent its main action and still has its move action. The new
spot:

- comes from the retreat-spot search at half and nine tenths of a move: away from enemies, towards the allies;
- must keep one of the four nearest enemies in range and sight (the ranged weapon's reach, else the longest offensive
  ability it scored, else Close range);
- must not be in any enemy's reach.

Its value is the lower chance to go down before the next turn, times the unit's worth, plus the expected damage kept
off at the existing HP-reserve price (`ValueModel.RepositionGain`). The walk's own risk is subtracted: attacks of
opportunity, areas, and what leaving costs the party (`MoveRisk`). It is offered only at the quick-action bar (2
points). It is a Move-slot option, so it can only follow the main action. Real time already has the archer's back-off.

## F2. Mixed-range characters switch weapon sets (both modes)

**Native evidence.**

- The game's mob AI switches with `BlueprintAiSwitchWeapon`, which issues `UnitSwitchHandEquipmentSet`, a **move-action**
  command.
- The summoned Cambion's brain (`Cambion_Brain_StandardRanged`) has a single rule: switch to melee when an enemy
  threatens it (`ThreatedByConsideration`). It never switches back.
- An inactive set's weapon has its enchantments switched off with its slot (`ItemSlot.OnActiveChanged`).
  `RuleCalculateAttacksCount` reads only the active hands.

**Here.**

- A unit set to **Mixed** (the default) with a melee and a ranged weapon set is offered "Switch to … weapon set" when
  the other set clearly beats its current weapon.
- The other set is estimated with the game's own rules for that weapon (`CombatMath.EstimateWithWeapon`): attack bonus,
  AC, weapon stats, and the fake damage on the target. Its switched-off enhancement bonus is added back. Attacks are
  counted by the native rule: one, the base attack bonus's iteratives, and the haste attack the unit has now.
- Only targets it can hit from where it stands count. A shot from melee pays its provoke.
- The value is the attack it can still make after the switch this turn (turn-based: one attack), plus two rounds of what
  its full attack gains over the best the current weapon can do, a walk included. That gain counts only past 1.2 times
  the current value plus 1 point (`ValueModel.WeaponSetSwitchWorth`).
- It is issued with the native command (a move action). It is never offered under the Melee or Ranged preference, for a
  Pure Mage, a rider or a polymorphed unit, nor within two rounds of the last switch.

## F3. Choosing the most fitting summon (both modes)

**Evidence.** A summon was worth `(3 + 3 × spell level) × rounds × 0.5`, regardless of the creature. Every variant of one
spell scored the same: Summon Monster VIII's Monadic Deva against Frost Giant, or Summon Monster V's one Redcap (CR 6)
against 1d3+1 Dire Wolves (CR 4) or 1d4+1 Monitor Lizards (CR 2). Nothing about the fight entered the choice.

**Here** (`TacticalEngine.SummonChoice.cs`), each spawn action is read under the ability's own context:

- **how many:** the average of its count dice, then the game's own count handlers (`ICalculateSummonUnitsCount`: Superior
  Summoning, the Book of Dreams);
- **for how long:** its duration, at most the fight's expected rounds (4);
- **how good, before it has fought:** an ally of its CR, half for a summon, by its CR against the enemies' average level
  (between half and one and a half: `ValueModel.SummonPrior`);
- **how good, from evidence:** what that kind of creature has actually dealt per round it fought this session (the
  game's final damage of its hits, `SummonWorthMemory`). This weighs in with the rounds seen, at most 60 %, because a
  creature also holds enemies off, which its damage does not show (`ValueModel.SummonPerRound`);
- **numbers against the fight:** the share of the new bodies that have an enemy to fight, about two an enemy, less the
  party's summons already fighting (`ValueModel.SummonBodyShare`).

The existing restraints stay: the crowd share, the easy-fight restraint, a shadow summon's partial reality, and allies
caught by the creature's area. The decision row names each creature, its count, its value per round and, once seen, what
it dealt.

Parts of the player's guide that are not modelled, and why:

- **Touch AC and trip-on-bite** need each creature's attack routine before it exists. The measured damage captures them
  once the creature has fought.
- **Mind-affecting and poison immunity matching** needs a model of the enemies' threats by descriptor.
- **Full-round arrival delay** is not counted: the WotR summons in the exports are full-round casts, alike for all
  variants.

## F4. Escape teleports to safe spots beyond a walk (both modes, author follow-up to P1)

Feasible and bounded:

- From the most valuable escape walk, the retreat-spot search also runs at 60 % and 90 % of the longest point teleport's
  range, when that is at least 1.5 m beyond the walk's spot.
- Each far spot must still do the escape's job (the walk's own test: out of the reach, out of the swarm, out of the
  area). It must be clear of harmful areas and within the unit's fight reach of an awake enemy: its ranged reach, else a
  move plus its melee reach, at least Close range. That way it rejoins the fight next turn instead of fleeing it.
- It is priced at the walk's worth plus how much less likely the unit is to go down there than at the walk's spot.
- The landing, its single native relaxation, its safety and the issue check are the P1 ones. That adds two spot searches
  of 12 directions each, only for a unit that has a point teleport and an escape.

## Checks

On `a6cd4a9`:

- Release 0 warnings; 1,172 unit tests (6 new) and 27/21/3 script tests.
- ProfileAudit exit 0, with the new `V0370-FEATURES` (7 checks). Its stock facts:
  - the native switch is a Move action;
  - Summon Monster V's variants read as Redcap CR 6 × 1, Dire Wolf CR 4 × 1d3 + rank, Monitor Lizard CR 2 × 1d4 + rank.
- Every other report line, the Fusion matrix and the summon detail are byte-identical to proposals1. The packaged DLL
  gives the same report.
- Harnesses 98/14/10.
- 17 external members added, all compiled against the supplied game assembly.

## Replay

Both modes, Control Summoned Creatures on. Send `Log.txt` and `combatLog.txt`.

1. **Reposition:** an archer (or a caster set to Ranged) in a turn-based fight shoots, then has its move action left with
   a melee enemy closing. Look for "Reposition before the turn ends (danger X->Y …)".
2. **Weapon sets:** a Mixed companion with a bow in one set and a melee weapon in the other. Against a distant or
   unreachable enemy, look for "Switch to ranged weapon set"; with an enemy next to it holding the bow, look for "Switch
   to melee weapon set". Check that it does not flip every round.
3. **Summons:** a caster with Summon Monster variants. The decision rows now differ by variant and name the creatures
   ("1x RedcapSummoned CR6 …", "2.5x DireWolfSummon CR4 …"). After a few fights the rows show "dealt N/rd".
4. **Far escapes:** the Knight Commander or the Derakni at low HP in melee. "teleport escape: Withdraw (low HP), N m away"
   marks a far spot.
