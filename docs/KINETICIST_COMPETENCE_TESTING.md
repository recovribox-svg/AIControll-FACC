# Kineticist competence: review and gameplay handover

**Current follow-up:** PR #2 was integrated into development. The author's 0.3.22-test1 TB/RT logs show decision
stalls. Use the [stutter-fix evidence, replacement package identity and focused replay](KINETICIST_PLAYTEST_STUTTER.md)
for that follow-up. The original PR #2 build identity and feature checklist below remain historical/reference material.

Branch: `codex/kineticist-competence`. PR target: `claude/dreamy-pasteur-b9h3rn`.
Source revision for the replacement test DLL: `175fad3c8b18ad673d27211913893eca6fe763c2`. This supersedes the initial 17cf71c test build.
The test package keeps the existing Info.json version; identify it by commit and SHA-256, not by version alone.
Test ZIP: `AIControll-FACC-kineticist-175fad3-test.zip`.
ZIP SHA-256: `233ceeb953544417a8ec78ac1e3d4925ab3014cb4f455dce32d47d583a625266`.
DLL SHA-256: `d7dd98250b9b85da5daf017c7540bade66cf743c18179842ede9333a7d8e9d04`.

No in-game tests have been performed by Codex. No release or integration into main is implied.

## Review follow-up: test this revision first

Claude's [review of 7bbfdfa](https://github.com/recovribox-svg/AIControll-FACC/pull/2#issuecomment-6392146121) confirmed
120 tests, a clean real-reference build and 45 intended verdict changes in the author's full 4,801-row dump. It also
identified load-safety and search-cost blockers. The replacement build isolates optional patches and bounds/caches
the search. Local checks now pass 133 tests plus injected patch-failure/rollback, inactive fast exits, query-cache and
explicit partial-load checks. Those are not a live Mono startup/performance test.

After re-review, test **Orvath at level 2 first** in both modes. At startup, look for optional-hook success or a clean
current-setup fallback with the mod still loaded; verify other companions still work. In normal logging, RT waits
must not emit no-action Kinetic summaries. Action summaries now include decision/search milliseconds, scored aims,
published alternatives, native action queries and shape builds. On `[Kinetic] budget`, no proposed modifier changes
should be applied; the current setup remains and published alternative count is zero. The time limit is cooperative
between native calls, so record an overrun if a single call takes longer. A high-level save, if available, should test
many infusions/metakinesis, enemies and Wall orientations, including cache lifetime/pose restoration and decision time.

For the author's unrelated dump failure, rerun ProfileAudit with `--allow-failures 1`; verify the report is PARTIAL,
names the failed blueprint and still audits the rest. Large allowances must not permit empty or mass-failed loads.

## Before testing

Claude should review `TacticalEngine.KineticPlans`, `KineticPreview`, `NativeDispelPreview`, `NativeActionCost`, the
new dispatcher overload, and the changes to end-of-combat restoration. The detailed rules/limits are in
[TACTICAL_AI §66](TACTICAL_AI.md#66-kineticist-competence-modifier-plans-gather-power-and-exposed-delivery-gaps).
Build against the DLLs from the actual installed game, then test the reviewed source/package. Do not use any offline
harness, rewritten game dependency, reference shim or Harmony helper fix in the game.

## Run in both turn-based and real-time combat

For every result, record mode, party/build/level, installed rule mods, source commit/package checksum, expected vs
observed behavior and the game log. Keep the `[Kinetic]`, `[Toggle]`, `[Tactics]`, `[Coverage]` and `[Perf]` lines.

1. **Level-2 Orvath:** blasts remain available, daily/round burn limits hold, and an expensive default infusion does
   not leave every blast blocked. Check kinetic archer quiver recovery too.
2. **Bowling/Entangling:** compare eligible groups with flying/no-leg/trip-immune or freedom-of-movement targets.
   Logs should show the delivered benefit, native burn cost, competing plan and final value. A worthless costly
   infusion should lose to an uninfused option. Test a fixed player infusion alongside an AI-managed gather mode.
3. **Other substance effects:** check Chilling, Dazzling, Flash, Synaptic, Grappling and Gut-Wrenching saves/conditions;
   Burning/Bleeding should not multiply impact damage or be reapplied for a second stream already active. Check
   Magnetic with metal-weapon/electric delivery, Foxfire against visible vs concealed targets (including a passed
   save's short duration), and Cluster Arrows below/at its native rank cap.
4. **Bypass/extra delivery:** Pure Flame vs high SR; Armor-Piercing vs DR, then turn it off; Chain Arrows vs a lone
   enemy and several enemies within native bounce range. The supplied Rare Metal trigger has no priced extra effect.
5. **Unraveling/Vampiric:** compare actual dispellable buffs with no buffs/non-dispellable buffs and the risk of
   removing a harmful buff from an enemy. Confirm no real roll, dispel or resource use occurs merely while the AI
   is considering options. Vampiric should gain a future discount only with its learned healing follow-up useful;
   it must not be valued as extra blast damage. Unsupported dispel handlers/layouts must log a zero contribution.
6. **Empower/Maximize/Quicken:** choose only when the complete option beats burn/opportunity cost. Verify native
   action slots after a Swift action is already spent. Watch free infusion activation: it must finish before the
   blast is re-evaluated, with no duplicate activation command or waiting loop.
7. **Gather Power:** compare a stationary shot with movement needed to attack or escape. Verify low/medium/high
   native gather counts, spent Move, surprise/staggered turns, quickened follow-up, and multi-turn preparation only
   when enough fight remains. Check interruption/provocation on the installed game and actual RT cooldown delay.
   Standalone gather must never appear as an attractive cleanse/unknown buff. No pre-combat gathering.
8. **Areas:** Cloud/Deadly Earth with mobile vs held enemies; allies/immune allies/downed allies inside or near the
   area; avoid-friendly-fire on/off. Check actual ticks and existing-area overlap. Wall should hit along its logged
   rotation, spare allies outside the native box, and receive no recurring damage value from an empty Round list.
9. **Blade:** compare single/full attacks, iterative attacks and Haste/additional attacks. Every actual hit's blast
   counts, each eligible hit can deliver an infusion, and persistent control is not awarded afresh on every hit.
   A blade activation that gathers must pay the gather's action budget; it cannot gather and retain a full attack.
10. **Ownership/restoration:** with AI manages toggles off, no infusion/metakinesis/gather settings change while
    active effects still contribute. During combat manually change a toggle and another member of its group; the
    AI must respect the configured player-ownership setting. After combat restore the pre-AI setup while retaining
    protected player edits. Also turn AI toggle management off mid-fight to verify the existing handoff convention.
11. **Defensive talents:** verify readable parts still work and unread retaliation/Evasion/custom defensive effects
    have zero guessed value. No repeated casting of an empty defensive marker.
12. **Performance/regressions:** use a high-level kineticist with many learned modifiers, several enemies and other
    companions. Check decision time, free-command waits, native-state parity rejection logs, no stalls, and ordinary
    weapon/spell/heal/movement competition. Also check an ordinary non-kineticist in both modes because buff/shape/
    resistance readers and command dispatch are shared code.

## Acceptance

Keep PR #2 draft until Claude's review and these relevant scenarios pass on the same source revision/package.
Report failures with logs and the exact package identity; compilation/offline results do not establish stability.
Claude integrates the reviewed work into development. Main/release updates remain with the normal release process.
