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

## 2. What was built (handover)

Commits on `claude/user-ability-blacklist`:
- `62ca917`: lists and policy;
- `3096842`: candidates and predictions;
- `aaf0827`: checks before acting;
- `442e31a`: panels;
- `a030c79`: offline check.

Test ZIP: [AI-Control-FACC-v0.3.25-dev-blacklist1-a030c79-UMM-test.zip](../test-builds/AI-Control-FACC-v0.3.25-dev-blacklist1-a030c79-UMM-test.zip?raw=true).
- Startup line: `[Build] v0.3.25 module=aab34bce-39d7-45c1-9346-551a9b5aa05a`.
- ZIP SHA-256: `974abbb1cf2966f414b6dab399331261542f171cf462521115b9ba1278742d5a`.
- DLL SHA-256: `ffc9ccf0ced7890dad7273bdac7747e41e4d4450d2d36eae1c2f03c72eeadad7`.
- Not played yet.

### 2.1 How to use it (Ctrl+F10)

1. **Per character.** Expand the character in *Party Members* and open *Abilities AI must not use*. It lists the
   character's spells, abilities, quick-slot items and toggles.
   - Search a name, and choose who new blocks are for: *This character* or *Everyone*.
   - Press *Block*. Variant spells (Command, the kineticist blasts' forms, Bestow Curse...) also offer *Block all
     variants*.
   - A blocked row is shown in orange, with the list that blocks it. Unblock it there.
   - *Show blocked only* narrows the list. Press *Refresh* after a level-up, respec or equipment change.
2. **Shared list.** The top-level *Abilities AI must not use - every character* panel lists the "Everyone" entries,
   each with *Remove*.
3. **Entries the character does not have any more** (respec, item gone, mod removed) appear under the catalogue with
   their stored name and GUID, and *Remove*.

### 2.2 Matching and scope (as implemented)

- **Blocked** means: on the shared list or on the character's own list.
  - The character's list cannot lift a shared entry; the panel offers removal at the list that holds it.
  - Lifting a block restores the ability at once. Nothing in the AI's memory remembered it as failed.
- **Same blueprint, same entry.** The policy compares blueprint GUIDs only:
  - the spell from any spellbook, with any metamagic, or from a scroll, wand or potion of that spell (the game gives
    those the spell's blueprint);
  - a converted cast (a spontaneous Cure);
  - a held touch charge of the spell (`AbilityData.StickyTouch`, which is the original spell's data: `UnitPartTouch.Init`).
- **Variants.** An entry on a variant parent blocks all its variants. An entry on one variant blocks only that variant.
  The parent is found two ways:
  - the blueprint's own `Parent` link;
  - the parent it was expanded from (`ConvertedFrom`), but only when that parent lists it as a variant (`HasVariant`).
    In the author's blueprint dumps, 161 of 813 variants have no `Parent` link and match this way. The panel uses the
    same rule.
  - Any other conversion is not followed. A Cure converted from a Bless slot is not Bless, so a Bless ban does not reach
    it.
- **Toggles** match their own blueprint.
- **Not listed:** hidden toggles, and what discovery leaves out (the kinetic blade's internal blast, hidden
  abilities). The game uses those itself and the list cannot stop them.
- **Where it is saved.** In `settings.json` in the mod folder:
  - shared entries apply in every campaign;
  - character entries follow the character's `UniqueId`, like the other per-character settings.
- **Loading.**
  - An old file has no lists: both are empty.
  - A malformed GUID is dropped and logged (`[Blocklist] ... ignored`).
  - Duplicates are merged.
  - An unknown kind is read as an ability.
  - A file that cannot be read at all keeps today's handling (backed up, then defaults are used).

### 2.3 Boundaries (shown in the panel too)

- **Your own orders and other mods.** Orders you give by hand still work: nothing patches the game's casting or
  `UnitCommands.Run`; the checks are in AI Control's own code paths. Commands from other mods (Wrath Tactics) are theirs:
  AI Control does not intercept them and the Wrath Tactics hand-off is unchanged.
- **Automatic effects stay.** Dreadful Carnage's free Dazzling Display is the feat's own trigger (it builds its own
  ability data), so it stays in attack estimates even with the manual Display blocked.
- **What is already on stays on.** Nothing already active is removed: buffs, toggles that are on, an installed
  infusion. Their effects, penalties and burn stay in the estimates.
- **AI Control still switches things off where it did before.**
  - A performance with nobody in its area.
  - Mobility once no enemy threatens the unit.
  - An infusion whose burn blocks every blast.
  - The after-fight return of a toggle the player had off.
- **Attack modes on a list are left alone.** A listed attack mode (Power Attack, Fighting Defensively...) is left as it
  stands, neither switched on nor off by the AI, and so is its group while it is on.
- **Issued commands run.** A command already issued, and the game's own follow-up of it, is not interrupted.
- **A blocked held charge is kept for you.**
  - The AI does not deliver it.
  - Casting another spell drops a held charge (the game's rule). An option that would do that still pays what the
    charge is worth, exactly as before.

### 2.4 Enforcement coverage

| Route | Where | What happens to a listed ability / toggle |
|---|---|---|
| Ability options | `TacticalEngine.AddAbilityCandidates` → `UserAbilityPolicy.Allowed` (`ctx.AllowedAbilities`) | never appraised (discovery stays unfiltered) |
| Other callers of `EvaluateAbility` (focus re-aim, kinetic recovery) | first line of `EvaluateAbility` | nothing returned |
| Decided option | `Decide` usable filter `PolicyBlocks` | not usable if it switches on a listed mode, needs a listed Gather Power or starts a listed toggle |
| Fresh check | `TacticalEngine.Execute`, first line | refused before infusion switch, approach walk, touch approach, mode switches |
| Create Quiver / infusion stop for burn | `AddKineticCandidates` (filtered view) | listed quiver not offered; listed blasts do not justify a quiver or a stop |
| Burn relief from heals | `InfusionEffects` (filtered view) | listed heals not counted |
| Toggle starts | `AddToggleCandidates` | not started; stop-if-pointless unchanged |
| Attack modes | `ChooseAttackModes` (fixed modes), `ApplyModes` (switch-on check) | never switched; current state still estimated |
| Mobility | `AddMobilityCandidate` | not offered; `MobilityHousekeeping` unchanged |
| Substance infusions | `AddInfusionChoice` options; `CommandDispatcher.Run` (infusion overload) before any stop | not started; installed ones stay accounted; a stale plan is refused before anything is stopped |
| Ability commands | `CommandDispatcher.Run` | a listed `UnitUseAbility` issued by AI Control is refused (only the mod calls it) |
| Fight-end restore | `CombatMemory.SwitchOffAiToggles` | not switched back on; off unchanged |
| Trance restore | `NoteEndedByAi`, `RestoreEndedToggles` | not scheduled; an old request is dropped, not kept pending |
| Self-buffs after fights | `RealTimeController.ApplyPermanentBuffsOutOfCombat` | filtered before "first castable"; the dispatcher's result is now checked (no bookkeeping on refusal) |
| PreBuff | `PreBuffAnalyzer.AnalyzeAllPreBuffs`; `PreBuffController.IsValidRequest`; dispatcher | never requested; a request queued before the edit is dropped when it comes up; the rest go on |
| Legacy real-time engine | `SituationAnalyzer.GetAvailableAbilities`; `ExecuteAbilityAction` | not a candidate; not cast; a policy refusal is not recorded as a failed ability |
| Ally Coup de Grace (sleepers left for it) | `CoupDeGraceAllies` | an ally AI Control drives with it listed does not count |
| Party can revive | `PartyCanRevive` (+ revision in its frame cache) | listed heals of AI-driven allies do not count |
| Held states | `HeldState`, `ToggleRestoreActions`, `Dependence` (+ revision in both caches) | listed restores/toggles/requiring abilities do not count |
| Diagnostics | `CoverageReport` | listed abilities and toggles are named as such |
| Wrath Tactics | `ModInterop`, unchanged | its own commands are not affected |

Predictions about **allies** apply the lists only to allies AI Control drives (`BlocksAiUse`). A character the player
plays keeps every ability.

**Refusals.** A refusal by the lists has its own failure text (`UserAbilityRules.RefusalPrefix`).
`TacticalDriver.RecordRejected` does not count it (`UserAbilityRules.CountsAsFailure`): no failed option for the turn, no
unreachable target, no learned path. Nothing is cast, so `CastOutcome`, `CastLedger` and resistance learning never see
it. The next decision does not offer it, so there is no select-and-reject loop. With every useful ability listed, the
engine falls back as usual: weapon attack, wait, or end the turn.

### 2.5 Checks run

- **Unit tests.** `dotnet test tests/CompanionAI.Tests`: 311 pass, 29 of them new (`UserAbilityBlocklistTests`, on the
  shipped `Settings/AbilityBlocklist.cs` and `Tactics/UserAbilityRules.cs`). They cover:
  - an old file, explicit nulls, save and reload, GUID forms;
  - malformed entries dropped one by one, duplicates;
  - shared OR own, both lists, removing the own entry leaves the shared one, unblocking;
  - the empty fast path;
  - family versus exact variant, run-time parent only with `HasVariant`, Cure from Bless, held charge through its spell;
  - copies sharing a blueprint, parent loops and the depth limit;
  - refusals not counted as failures.
- **Real blueprints.** `tools/ProfileAudit/run.sh ... --blocklist-checks <dumps>` runs the mod's own
  `UserAbilityPolicy.SourceOf` on the game's blueprints, as the game code reads them. On the author's dumps
  (16,839 loaded, 1 unrelated failure allowed):
  - 159 parents and 813 variants: each variant is blocked by its parent's entry and by its own, and not by a sibling's
    (812 checks);
  - 201 conversions that are not variants, including Cure Light Wounds from a Bless slot: none inherits the source's ban;
  - empty lists block nothing and keep the same list.
  - All checks held.
- **Release build** against the game's assemblies: 0 warnings, 0 errors.
- **SigDump against the v0.3.25 release.** The new game bindings are only `BlueprintGuid(Guid)`, `GUILayout.TextField`
  and `Event.current` / `.type`. The build against the real assemblies shows they exist. `BlueprintAbility.Parent`,
  `HasVariant`, `AbilityData.ConvertedFrom` and `StickyTouch` were already used.
- **Package.** The packed DLL and `Info.json` are byte-identical to the build.

### 2.6 Performance (measured offline; not measured in the game)

On a 400-ability actor (real blueprints, this machine's .NET 8, not the game's Mono), `--blocklist-checks`:
- **Cost per check:**
  - **empty lists:** 37 ns and 0 bytes a check;
  - **50 entries:** 165 ns and 0 bytes a check (the variant-parent walk).
- **Per decision.** One check per ability is 15–66 µs a decision. The engine checks an ability two or three times (the
  view, `EvaluateAbility`, an option's own check), so expect under 0.2 ms for the largest actors. The slowest real
  decisions are about 300 ms (Orvath).
- **Fast paths.**
  - With empty lists, the view is the same list (no copy) and the usable filter is skipped.
  - A blocked ability skips its appraisal entirely. It could save time, but that is not claimed without game evidence.
- **Allocations.** A check allocates nothing. The lists are compiled once per edit or load. The `[Blocklist] ... not
  used` evidence line is logged once per unit, ability and edit.
- **Settings panel.**
  - Closed, it reads two list counts.
  - Opened, it runs one ability discovery, builds the rows and sorts them once.
  - Filtering runs only on Layout events, and only when the search, the filter or the lists change.
  - It shows 12 rows a page, with 2 set lookups a row.
- **In the game.** The `[Perf]` lines of a play test show the real cost: compare decision times with an empty and a
  populated list.

### 2.7 Focused play tests (not done; tests and the offline harness do not show in-game behaviour)

1. **Turn-based, spell.** Block one of a caster's spells for that character (e.g. Grease or Magic Missile).
   - Fight: it is never cast.
   - Log: `[Blocklist] <name>: <spell> not used by AI Control (this character's list)`.
   - Other spells and attacks go on.
   - Unblock it mid-fight: it is used again from the next decision.
2. **Shared list.** Block Bless for *Everyone*.
   - No AI companion casts it, in combat, after fights or with Pre-Buff.
   - A cleric's spontaneous Cure from a Bless slot is still used.
   - You can still cast Bless by hand.
3. **Variants.** Block *all variants* of Command, then only one variant. Only the listed ones disappear from that
   caster's choices.
4. **Toggles and modes.**
   - Block Power Attack, or a bard's performance.
   - The AI never switches it on. If you switched it on yourself it stays on, and the attack estimates include it.
   - Block Mobility: no "Start Mobility before ..." lines.
5. **Kineticist.**
   - Block an infusion: it is never chosen, even if it was on. An installed one stays and its burn is still priced.
   - Block Create Quiver: no quiver even when burn blocks every blast.
   - Block a blast: it does not justify a quiver.
6. **Held charge.**
   - Hold a touch charge (cast Cure by hand, do not deliver it), then block that Cure.
   - The AI does not deliver it, and it does not throw it away for a weak spell.
7. **Psychic Warrior (Psionics mod).** Block the trance.
   - After a fight the trance is not switched back on: `[Held] ... not switched on again after the fight (on a list)`.
   - The focus is still regained.
8. **Real time.** Repeat 1 and 4 in real time. The listed ability is never used, and no `could not be issued (blocked
   by the 'Abilities AI must not use' list ...)` lines appear: the decision already leaves it out.
9. **Respec.** Block a spell, respec the character out of it, and open the panel. The entry is under "not on it now"
   with *Remove*.
10. **Regression.** With empty lists, fights should look like v0.3.25: same decisions, same `[Perf]` timings.

### 2.8 Limits and open points

- A toggle the AI never starts by itself can still be listed. Blocking it changes nothing.
- Gather Power is enforced through the blast that needs it, and only when its ability is listed. The gather mode
  toggles are ordinary toggles.
- A held charge you blocked can still be dropped by the game when the AI casts another spell that is worth more. The
  price of the charge is counted, as before this feature.
- Not played yet; no in-game timings.
