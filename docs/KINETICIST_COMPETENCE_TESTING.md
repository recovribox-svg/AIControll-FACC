# Kineticist competence: review and gameplay handover

Branch: `codex/kineticist-competence`. PR target: `claude/dreamy-pasteur-b9h3rn`.
Source revision for the test DLL: `17cf71c5d06fbd09dfde686a3eaa5e10967ac6a2`.
The test package keeps the existing Info.json version; identify it by commit and SHA-256, not by version alone.
Test ZIP: `AIControll-FACC-kineticist-17cf71c-test.zip`.
ZIP SHA-256: `9d359c9e0b6345fc40713e33374c670f7ac54082d32a59ee5b03a90354966b1f`.
DLL SHA-256: `70d7d007774e566ce887a50a47ba9640a8b91b70ef13ed004f3d46d79353a9db`.

No in-game tests have been performed by Codex. No release or integration into main is implied.

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
