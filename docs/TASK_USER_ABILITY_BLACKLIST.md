# User ability blacklist ("Abilities AI must not use")

Task branch `claude/user-ability-blacklist`, pull request into development (`claude/dreamy-pasteur-b9h3rn`). The
author asked for this in `Blacklist_system_Prompt.txt` (2 November 2026).

## 1. Plan (written before the code)

**Baseline.** Development, `main` and tag `v0.3.25` are all `b1c8cbf`. `Info.json` is 0.3.25 and stays 0.3.25. No pull
requests were open and the working tree was clean.

**Entries and matching.**
- Each entry stores a blueprint GUID (normalised to 32 lower-case hex digits), a display name kept for when the
  ability is gone, and a kind (`Ability` or `Toggle`, stored as text so an unknown value cannot break loading).
- There is one shared list (`ModSettings.SharedBlockedAbilities`) and one list per character
  (`CharacterSettings.BlockedAbilities`, keyed by `UniqueId` as before). Both default to empty.
- A unit is blocked from an ability when the shared list **or** its own list names it. Neither list can unblock what the
  other blocks.
- An ability is blocked when any of these is on a list:
  - its blueprint;
  - its variant parents (`BlueprintAbility.Parent`, at most 4 levels, so a loop cannot run forever);
  - the parent it was expanded from at run time, but only when that parent really has it as a variant
    (`ConvertedFrom.Blueprint.HasVariant`). A Cure converted from a Bless slot is not a Bless variant, so it does not
    inherit a Bless ban.
  - for a held touch charge, the spell it came from (`AbilityData.StickyTouch`, which `UnitPartTouch.Init` sets).
- Spellbook, metamagic and item copies share the spell's blueprint, so one entry covers all of them.
- An entry that names a variant parent is the "all variants" choice. An entry that names a child blocks that variant
  only.
- Toggles match their own blueprint only.
- Entries are checked one by one when settings load: missing or null lists become empty, a malformed GUID is dropped and
  logged, and duplicates are merged. Unresolved entries are kept. A file that cannot be read at all keeps today's
  handling (backed up, then defaults are used).
- The lists are compiled into lookup sets when the settings load or change. Nothing is parsed during a fight.

**One policy.**
- `Settings/AbilityBlocklist.cs` and `Tactics/UserAbilityRules.cs` hold the pure parts (entries, normalising, compiled
  sets, the "shared or own" test, the bounded parent walk). They are unit-tested.
- `Tactics/UserAbilityPolicy.cs` is the game side. It answers for an `AbilityData`, an `ActivatableAbility` and a
  `Candidate` (its ability, any attack modes it switches on, a substance infusion it starts, a Gather Power the game will
  insert before it).
- It has an empty-list fast path, and the check allocates nothing.
- `Revision` changes with every edit. Caches that store filtered results compare it.
- A policy refusal carries its own failure text. The drivers do not count it as a failed option, a silent failure, a
  learned resistance or a cast.

**Where it is enforced.**

| Route | Before appraisal / planning | Fresh check before acting |
|---|---|---|
| Normal abilities | `AddAbilityCandidates`: filtered view `ctx.AllowedAbilities`, before the profile is read. `EvaluateAbility` refuses for other callers. | `Execute`, first line (before infusion switch, approach walk, touch approach) |
| Held touch charge | blocked through its spell; kept for manual use (still priced when another spell would drop it) | same |
| Kinetic recovery / Create Quiver | quiver and the blasts that justify it come from the filtered view | `Execute` |
| Gather Power inserted by the game | a blast whose plan needs a blocked Gather Power is refused | `Execute` |
| Toggles (performances, Rage, trances) | `AddToggleCandidates` skips blocked starts; stop-only cleanup unchanged | `Execute` |
| Attack modes | a blocked mode is never switched by the AI; its current state stays in the estimate | `Execute` (whole attack) + `ApplyModes` |
| Mobility | not offered when blocked; switching it off is unchanged | `Execute` |
| Substance infusions | blocked starts are not search options; installed ones stay in the accounting | `Execute` + dispatcher infusion overload, before any stop |
| Fight-end restore (`SwitchOffAiToggles`) | — | a blocked toggle is not put back on; switching off unchanged |
| Trance restore (`RestoreEndedToggles`, `NoteEndedByAi`) | not scheduled when blocked | dropped (not kept pending) when blocked |
| Self-buffs after fights | filtered before "first castable" | dispatcher; the ignored `Run` result is now checked |
| PreBuff | filtered when requests are collected | re-checked when each request runs; dispatcher |
| Legacy real-time engine | filtered where it gathers abilities | dispatcher; a policy refusal is not recorded as a failure |
| Dispatcher | — | `UnitUseAbility` issued by AI Control is refused when blocked (only the mod calls it; no game patch) |
| Coup de Grace by an ally | allies controlled by AI Control with it blocked do not count | — |
| Party can revive | the same for heals | frame cache also keyed by `Revision` |
| Held states (focus) | blocked restore abilities and toggles are left out of cost, worth and dependence | caches keyed by `Revision` |

**Boundaries kept.**
- Manual orders, other mods (Wrath Tactics) and the game's own procs are untouched. Dreadful Carnage's Dazzling Display
  stays in the attack estimates.
- Existing buffs and toggles that are already on are not removed.
- Commands that have already been issued are not interrupted.

**Acceptance tests.** These are the prompt's list:
- persistence and old files;
- shared or own blocks;
- exact variant versus family; Cure from a Bless slot; held touch;
- malformed entries;
- the compiled sets and the revision;
- the rule that a policy refusal is not a failure.

They use the shared rule code itself, not a copy. In-game behaviour needs the replay steps below; tests alone do not
show it.

**Uncertainties.**
- Character entries follow `UniqueId`, as the other per-character settings do.
- Shared entries live in the mod folder, so they apply in every campaign.
- Toggles the AI never starts by itself can still be listed. Blocking them changes nothing.

## 2. Implementation, checks and replay steps

Filled in below as the stages are done.
