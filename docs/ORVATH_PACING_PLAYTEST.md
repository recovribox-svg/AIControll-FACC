# Orvath and summon pacing replay — 25 October 2026

## Supplied evidence

The author tested the development UMM package built from `fcc64b1` (the logs call it 0.3.22) on a mid-tier CPU.
They report smoother companion turns overall, with remaining Orvath/summon stutters and faster animations/turn
transitions. They confirmed that the speed-up affects enemy turns too. These logs do not record clock multipliers
or individual native summon brain durations, so neither symptom has a complete attribution yet.

| Log | Orvath `[Kinetic]` decision summaries | Summary median / maximum | Decisions logged above 100 ms |
| --- | ---: | --- | --- |
| `Turn Based Mode Log 0.3.22.txt` | 38 | 171.9 / 203.9 ms | Orvath 31 |
| `Realtime Mode Log 0.3.22.txt` | 79 | 91.8 / 589.0 ms | Orvath 37; Camellia 7 |

Summary samples include inexpensive spent-slot decisions. The slow-decision log is threshold-filtered; its counts
and medians must not be presented as the whole party's frame times. These are gameplay observations, not a
controlled hardware benchmark. Orvath repeatedly makes hundreds of native parameter queries with only 10–12
reuses and zero ambient-context bypasses. Modifier search itself is usually about 2 ms, while the combined kinetic
stage can take 65–85 ms. Camellia's RT Lullaby evaluation also contributes to several slower decisions.

## Changes on `codex/orvath-summon-pacing`

**Empty rulebook contexts defeated caching.** Native `Rulebook.TriggerEvent` replaces its root context after a
top-level rule completes. The former cache included that object's identity, so ordinary parameter calculations
invalidated their own ability to reuse a result. Parameter reads now share across empty roots within the existing
synchronous decision/state/aim boundary. Active rule events and mutable mechanics scopes bypass the cache.
Results remain cloned, ability instances and target/orientation remain distinct, modifier changes invalidate
state, and nested/subsequent decisions do not inherit entries. No native formula is replaced.

The regression fixture now returns a new real empty context on every lookup; the previous fixture returned null
and missed this behavior. Two hundred same-state reads still make one native parameter query. A pushed native
rule event forces fresh reads. Existing clone, aim, mechanics-scope and modifier isolation checks remain.

**Detached kinetic group defaults are reused.** The native `UnitPartActivatableAbility` constructor allocates an
enum-sized group-capacity array. Mods that extend that enum can make the fallback allocation expensive. A lazy,
detached default part is retained for read-only native `GetGroupSize` queries instead of recreated each decision.
A unit's live part still takes precedence, including its capacity increases. No capacities are copied into a
custom table. The repeated allocation is confirmed in source; its share of live stalls remains unmeasured.

**Attribution is more specific.** Slow-decision stage timing splits burn pricing, modifier plans and burn recovery.
`[Perf]` parameter counters include active-rule bypasses. An allied summoned monster that AI Control declines to
control can emit `[Perf] unmanaged summon brain` when its brain pipeline takes over 50 ms, at most once per
summon/round. This includes native execution and other mods' patches after our prefix; it is not exclusive engine
CPU time and does not cover animation, rendering, or work elsewhere in a frame.

**Pacing is observed, not overridden.** `[Pacing]` records configured/default and effective player/enemy TB speed,
the game's speed-up flag, player/debug scale and Unity scale. It reports combat start and changed combinations at
turn starts, including enemy turns; identical values are suppressed. It also lists Harmony owners on the player
speed getter when patched. This is a clue, not an exhaustive attribution of third-party animation changes.
No time-scale setter, speed-up flag, animation rate or combat-speed setting is written by this change.

Static inspection found no existing AI Control animation-speed/time-scale writes. Native `ForceToEnd(true)` spends
remaining action slots and ends a turn; its boolean is not an animation-speed flag. Ending unused turns promptly
can shorten transitions without accelerating an animation. Enemy-wide acceleration needs the new timing evidence
and current game/other-mod settings before assigning a cause. This patch does not claim to fix that symptom.

All candidate scoring, target enumeration, native game math, toggle ownership and command boundaries remain.
Claude's `3e5e37d` deferred RT positioning / TB preparation behavior and the shared-value restoration remain.
No decision-quality reduction, target cap, priority tree, scoring-weight change or artificial delay is introduced.

## Decision review

- Camellia chooses a Cure Moderate Wounds item at 47% HP; Seelah chooses healing when its expected benefit exceeds
  an attack. Later Camellia/Seelah/Orvath prioritize a threatened injured ally. This is consistent with the existing
  danger-aware healing scale, although this log cannot establish every HP outcome or consumable's optimal use.
- Orvath chooses DC boosts when they improve Sleep's expected outcome, skips a caster-level boost that adds no
  benefit, and transitions to infused/plain Fire Blast as useful control targets diminish or burn becomes limiting.
  Sleep's chosen center can name an undead unit: the Skeletal Champion-centered cast produces saves on nearby
  Corrupted Mongrels. The aim label alone is not evidence that undead immunity was ignored.
- Coup de Grace is used against an eligible disabled Dretch; attacks compare defensive mode changes and approach
  risk. Area casts consider existing Entangle/Grease. Repeated control saves and enemies waking/moving do not alone
  establish an ineffective-cast loop. Complete recipient outcomes/positions are unavailable in these text logs.
- Cambion turn starts explicitly say the game controls it. There are no TacticalEngine decisions for that summon
  in this log. Its stall must not be assumed to be a large spellbook search or fixed by taking away native control.
  Existing summon valuation is coarse; these observations do not establish optimal summon selection.
- `WrathPatches` reports mismatched blueprint owners for `SternGaze` and `ChannelPositiveHarm` at initialization.
  These are errors logged by another mod; they are not AI Control exceptions and no repair is attempted here.

No additional confirmed decision bug was identified in these samples. The cache bug is the confirmed runtime fix;
summon/animation attribution and post-fix live timing remain open.

## Verification

- Game-independent suite: **143 passed**, zero failed/skipped.
- Full Release mod build against supplied real game DLLs: **zero warnings/errors**.
- Shared offline audit: decision-read, condition-context and kinetic checks pass, including rotating empty roots,
  active-event bypass, cloned/state-isolated reads, native default/live group separation and preserved deferred work.
  The real brain method's signature is checked; Harmony state-pair binding is checked against a stand-in of that
  signature because the actual engine-bound body cannot be patched/executed in this offline .NET host.
- Blueprint run is explicitly **PARTIAL: 4,097 loaded, 1 failed; 1,220 ability rows, zero reader errors** with
  `--allow-failures 1`. The known `AchaekekSacredWeaponFeature.jbp` engine-bound SecurityException is unchanged.
  See [the integration handover](INTEGRATION_HANDOVER.md) for offline staging limitations. Stubs, game binaries,
  decompiled IL and harness outputs are not shipped.
- Unity/Mono gameplay, installed-mod parity, observed FPS and animation-speed restoration are **not verified**.

## Focused in-game replay

Use the newest package linked in [test-builds](../test-builds/README.md), and record its filename/hash. UMM's
unchanged manifest version is not sufficient to identify the build. Keep the same save, encounter, mods and game
speed settings for a useful comparison.

1. In TB, replay Orvath with his full action bar, including a free DC boost followed by Sleep/Grease and an infused
   Fire Blast. Check animation smoothness and collect complete `[Perf]`, `[Kinetic]`, `[Tactics]` and `[Pacing]` lines.
   Expect more reused parameter reads, not a guaranteed frame budget. Any active-rule bypass counts are intentional.
2. Include Cambion's and enemies' turns. Collect `[Perf] unmanaged summon brain` if present. Its absence does not
   rule out a stall elsewhere in the frame. Do not enable direct summon control merely to collect this diagnostic.
3. Replay RT, including Camellia's Lullaby, simultaneous summons and Orvath's action cooldown/positioning. Check
   healing, control, burn, player-selected toggles and end-of-combat toggle restoration still work.
4. Record the game's player/non-player combat animation settings and any speed-mod settings. Compare `[Pacing]`
   values to the intended multipliers. If safe for the save, an AI-Control-disabled comparison in the same encounter
   helps distinguish animation speed from shortened idle turn transitions. Do not change multiple mods at once.
5. If stutters persist, supply full logs and mark which actor/animation was active. A frame profiler may be needed
   if decision times fall but animation stalls remain; native synchronous calls and garbage collection can still stall.

## Handover to Claude

Codex investigated the author's next replay while Claude was unavailable. This work builds on merged PR #4 and
the repository-hosted `fcc64b1` test package. Consult this task's PR and the newest test-build identity before
resuming; do not reapply the earlier frame-counter fix or infer that gameplay/release approval has been granted.
`main`, release metadata and stable releases are outside this task. The animation-speed symptom remains open.
