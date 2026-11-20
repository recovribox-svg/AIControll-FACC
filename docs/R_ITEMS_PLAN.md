# R1–R7 from the #86–#89 review: assessment and work order

20 November 2026, Claude. This page answers the author's request to decide which of Codex's R1–R7 items
([REVIEW_86_89.md](REVIEW_86_89.md)) are worth doing, and in what order. Only work that is sound without a playtest goes
in now. Where a decision needs evidence, the next build collects it, and the next playtest decides.

The death-trigger proposal is set aside, at the author's request.

**Evidence available now** comes from Codex's review2 playtest ([REVIEW2_PLAYTEST.md](REVIEW2_PLAYTEST.md)), 13 fights:
- **Bursts:** six watches, at most one at a time, 16 re-checks, two cancellations for an ally walking in.
- **Disruption:** 13 cast-threat witnesses, and one break attack that acted.
- **Flanks:** the logs say nothing about mispredicted flanks.

## Assessment

| Item | What it is | Verdict | Why |
| --- | --- | --- | --- |
| **R1** | Engagement forecast that knows where the attacker will stand: 3D reach, sight and concealment from that spot | **Evidence first** | Full version: large change; it touches the danger, defence, held-buff and leap quotes that share the routine caches. The gain is only a flank misjudged on a height difference or behind cover, and no log has shown one. **Now:** a per-fight flank tally that compares each forecast with what the game decided at the roll (below). |
| **R2** | Burst watch overflow can drop a turn-based landing wait and skip the landing spot shared with other AI walks | **Do now** | Small, and needs no playtest. The landing spot is shared, and the landing wait kept, for every burst. The cap limits only re-checks. Play never came near the cap (at most one watch), so nothing a player would notice changes. |
| **R3** | An ally's burst damage and control price are frozen for the whole watch | **Evidence first** | The window is one cast, about a second. A change of immunity in that second is rare, and a fresh read needs the decision's whole context. **Now:** count each ally that walks in with no control price from the decision (for example a new summon). |
| **R4** | Attack and cast timing are estimates (first swing 0.6 s, next swings 0.7 s or 6 s / N, arrows 30 m/s) | **Evidence first** | Calibrating these needs the game's real timeline. **Now:** a per-fight timing tally of AI weapon attacks and of break forecasts against what happened. |
| **R5** | An ally whose attack has started, and who chases a moving target, counts as standing still in the burst forecast | **Do now** | Verified in the game code: a started attack keeps approaching its target (`UnitCommandController.TickCommand` → `TickApproaching` when `CanMoveAfterStart`). The fix is a burst-only read that changes no other movement reader. A melee ally chasing the enemy the burst is aimed at is the case that matters most. |
| **R6** | Performance of the walk-in control table and the suppressor search | **Not now** | Profiling first. The playtest shows few watches. The slow-decision lines already name the costliest abilities. |
| **R7** | The cleanse share is relative, not an absolute active-time model | **No** | A design difference with little at stake. Redesigning cleanse and dispel together is not justified by any log. |

## Work order for the next build

1. **R2 and R5** (burst watch), on `claude/review-r-items`.
2. **Evidence logging**, same branch. One summary line per fight per topic:
   - `[Burst] fight summary` gains:
     - the error of the forecast cast time;
     - how far the anchor enemy was from the landing point when the burst landed;
     - allies that walked in with no control price.
   - `[Timing] fight summary`:
     - AI weapon attacks: start to first swing, and the gap between swings;
     - break forecasts: forecast and actual first hit, whether the cast went off or broke.
   - `[Flank] fight summary`: flanks forecast at the attack against the game's answer at the roll. Forecasts by a second
     engager are counted apart, with the height difference when they were wrong.
3. **Magic Fusion C2** (Hideous Laughter's Best Jokes recast; arcan's bar), then the other slices of
   [FUSION_COVERAGE_PROGRAMME.md](FUSION_COVERAGE_PROGRAMME.md) whose game rules can be checked offline.
4. One cumulative test package for the author to play.

## What the next playtest decides

| Log line | Decides |
| --- | --- |
| `[Flank] fight summary` | **R1.** If a share of the second-engager forecasts proves wrong, build the origin-aware quote; otherwise leave it. |
| `[Timing] fight summary` | **R4.** The first-swing, swing-gap and arrow-speed constants are corrected from measured means. |
| `[Burst] fight summary` (cast time error, anchor at landing) | Burst lead and walk forecast tuning. |
| `[Burst] fight summary` (unpriced walk-in allies) | **R3.** Whether a fresh control read during the watch is worth its cost. |
