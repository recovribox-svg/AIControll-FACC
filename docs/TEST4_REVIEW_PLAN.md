# Test[4] review scope and verification

The author supplied TB/RT AI logs and a native CombatLog, requesting review of
logic/decisions and necessary bug fixes. Both Test[3] and Test[4] ran on the same budget laptop. The
author reports occasional stutter through more frequent slowdown across multiple
actors, much milder than the withdrawn preview version, and suspects the weaker
CPU explains some of the difference. Preserve that context; aggregate timings
do not isolate a regression, one actor or one stage.

## Evidence and implementation scope

- Verify native infusion execution, actual buffs/saves/ticks, cost/ownership and
  outcomes. Eight successful activations and real Burning damage are visible;
  do not treat the earlier activation failure as still present.
- Correlate actual outcomes before changing decisions. An immune/high-HD main
  aim may still be useful for other area recipients, and partial revives can
  accumulate toward success. Do not tune those away from labels alone.
- Investigate the continuing optimistic Fire Blast/Burning values against fire
  resistance. Distinguish source-proven defects from missing live query/parameter
  evidence; expose a silent native-query failure instead of inventing game math.
- Source review identifies repeated spell-boost description reads within one
  coupling call and identical periodic damage reductions within one effect
  evaluation. Reuse descriptions/plain floats locally with full identity/input
  keys; preserve every candidate, effect, native context construction and survival
  weighting. No native rule/context cache crosses decisions or appraisal calls.
- Add low-allocation stage measurements to the existing slow-decision report,
  separating active-infusion work from its enclosing ability stage. Include the
  otherwise unreported TB stand-up preview where practical. No per-frame trace,
  global hook, new path query, broad pruning or new scoring weight.

## Explicitly approved spell safety

The prior movement restriction persists: no target locks, movement score changes,
navigation changes or emergency-cancellation changes. Test[4]'s RT7 shows Seelah
committed to an elemental approach, followed by allied Grease landing and a
native failed Grease save. The author explicitly approved a **narrow spell-safety
check** to withhold a new harmful persistent area when an ally's existing approach
clearly ends inside it, respecting the existing friendly-fire setting and using
existing command/range data, with no extra pathfinding. Restrict initial support
to structurally proven circles and actual unfinished attack approaches; uncertain
geometry remains with the existing policy.

## Coordination and checks

Branch `codex/test4-logic-fixes` starts at remote development `f51189f` and includes
pending PR #9 head `10d2b96` as an explicit dependency, including PR #8/#7/#6.
Main/development remain unchanged. Read AGENTS/CONTRIBUTING/CLAUDE and §65–68 before
edits. Submit a draft development PR, durable evidence/limits and a new identified
flat UMM test ZIP if runtime changes are made. Preserve previous distributed ZIPs.

Use meaningful pure geometry tests, Release with actual game references, native
metadata/API checks, ordinary reader comparison and source review of both modes.
No fake actor benchmark establishes Unity/Mono speed or gameplay correctness.
CombatLog has CRCRLF/lone-CR formatting: this review will cite **LF-delimited**
physical lines (not Python universal-newline splitlines). Its order is seven RT
encounters followed by five TB encounters, unlike Test[3].
