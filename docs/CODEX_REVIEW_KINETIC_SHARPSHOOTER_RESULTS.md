# Kinetic Sharpshooter independent audit — 5 November 2026

## Outcome and scope

The quiver payment investigation and the narrow unread-buff correction are substantially sound. Test 4's advertised preparation **after a rest was not reliably driven**, because the native AI skips groups outside combat. This review fixes that route and the queued-command guard, reduces repeated burn queries, and corrects an offline verification rule that had fallen behind the live code. The larger strategic questions remain proposals rather than speculative changes.

- Starting development commit: `d4ac1939ac5fdda8aef0a9d83c79cf37cf531057` (includes `f4f38ee`, `e620e71`, `53664df` and the review brief).
- Task branch: `codex/review-kinetic-sharpshooter`; target: `claude/dreamy-pasteur-b9h3rn`.
- Reviewed code diff: `ea2e570..e620e71`; reviewed notes: TACTICAL_AI §101, relevant §38–39, test 4 README, and TASK_BOMBS_CARNAGE_QUIVER §3.
- Inputs: the supplied review brief, Kineticist blueprint archive, both complete play-test logs, and real game/Unity/UMM DLLs available among workspace attachments. The brief defines the audit scope; its claims are evidence to check, not assumed facts.
- `Assembly-CSharp.dll` SHA-256: `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`. Relevant native types were independently decompiled/inspected. No game DLL, blueprint dump, decompiled source, log or generated stub is committed.
- The separate `Kinetic_sharpshooter_mechanics_audit.md` was not supplied. Its premises can only be assessed as quoted in the brief. Base blueprint dumps do not prove what every installed mod changes at runtime.

Log references below use LF-delimited physical lines, as `rg -n` does. Some combat-log records contain additional bare carriage returns; other line-counting methods can disagree.

## Confirmed issues and implemented fixes

### 1. Preparation has no reliable exploration/post-rest driver

**Severity:** bug. **Verdict:** confirmed; fixed.

**Evidence:** native `Kingmaker.AI.AiBrainController.ForceTick` skips groups with `IsInCombat == false`. The mod's `CustomBrainPatch.TickBrain_Prefix` drives `RealTimeController.ProcessUnit`; its out-of-combat branch therefore is not an ordinary exploration tick. Combat-end preparation can run once, but a subsequent rest removes the quiver without creating a new AI tick. The pending-buff queue also depends on ProcessUnit. This contradicts the test 4 “after a rest” expectation.

**Impact:** Orvath can still enter the first fight after resting with no quiver. Other permanent buffs share an older scheduling limitation, but expanding their behavior is outside this fix.

**Fix/risk:** `GameInterface/PreBuffController.cs:42` uses the existing MonoBehaviour updater for a quiver-only check every 0.5 seconds. `RealTimeController.cs:1354` shares preparation checks between this driver and the existing combat-end route. It requires normal Default mode; no pause, fake pause, load/unload or party combat; an in-game, active, acting, AI-controlled unit; no get-up animation or pending commands; and all existing native cost/blocklist/HP rules. It still dispatches through `CommandDispatcher.Run`. No general pre-buff session is started. Native `UnitState.CanAct` can return **true in Cutscene mode**, so CanAct alone was insufficient. This is a narrow but newly active automatic burn-spend route and needs the replay below in both combat preferences.

### 2. Out-of-combat buff checks ignore queued commands

**Severity:** bug. **Verdict:** confirmed; fixed.

**Evidence:** native `UnitCommands.Empty` checks the command slots, while `Queue` is separate. A new non-queued `Run` clears that queue. `GameInterface/GameAPI.cs:42` already implements the correct combined predicate.

**Impact:** the shared self-buff routine, including the new quiver, could replace an order waiting in the game queue. An actual duplicate quiver cast is **not** established by the supplied logs; an active normal cast occupies a slot and is already protected.

**Fix/risk:** both pending-buff and direct self-buff busy checks now call `GameAPI.HasPendingCommands` (`RealTimeController.cs:1438`, `:1493`); the new quiver driver uses it too. Low-risk consistency correction for every class using those routes.

### 3. Duplicate native burn queries and late preparation rejection

**Severity:** performance. **Verdict:** confirmed; fixed without changing tactical policy.

**Evidence:** original `BlastShortfall` called `BurnOf` for own blasts, followed by another `BurnOf` in `PriceBurn`. Native `AbilityKineticist.CalculateBurnCost` builds a local cost and raises cost-modifier events; spending occurs elsewhere. Original `QuiverToPrepare` also discovered/profiled abilities and scanned native costs before rejecting a melee weapon or an uncastable quiver.

**Impact:** unnecessary event traffic for kineticists and repeated between-fight work for Sharpshooters. No measured millisecond improvement is claimed.

**Fix/risk:** one local burn-quote dictionary is shared within each `PriceBurn` invocation (`TacticalEngine.Kinetic.cs:195`). Wrong-weapon rejection precedes discovery; blocklist/castability rejection precedes the kinetic-cost scan. No mutable answer is cached across ticks: infusions, action bars, equipment and settings can change. Low risk on the inspected native paths; unknown third-party cost handlers remain subject to the same within-decision stability assumptions as existing caches.

### 4. ProfileAudit still copied the pre-fix buff fallback

**Severity:** bug (verification). **Verdict:** confirmed; fixed.

**Evidence:** `tools/ProfileAudit/Audit.cs` previously retained the old 0.05 marker fallback and checked only resource logic. It could label an unread limited marker useful after live `BuffQuality` rejected it, and omitted the live unread day-long gate.

**Impact:** misleading offline conclusions for burn talents and other resource features; this was not another live gameplay bug.

**Fix/risk:** `ValueModel.BuffFallbackQuality` (`Tactics/ValueModel.Resources.cs:12`) is now shared by live appraisal and the offline tool. Recognized effects, enchant carriers, item thresholds and lazy resource queries preserve their live priority. The tool includes unconditional `AbilityAcceptBurnOnCast`, skips unread day-long markers, and labels `AbilityKineticist` costs owner-dependent instead of pretending dumps establish current quiver/infusion costs. Ten tests cover the shared formula. Risk low; the tool still cannot simulate a real character or recipient. Its actual blueprint run remains blocked as described under verification.

### 5. Preparation and Overflow policies lacked executable coverage

**Severity:** gap (verification). **Verdict:** confirmed; addressed.

**Evidence/fix:** the shipped preparation calls the new pure rules in `ValueModel.Kinetic.cs`; `BurnCost` calls its Overflow-credit rule. Nineteen tests cover rank/cost boundaries, exact half-HP acceptance, current nonlethal HP, later-round horizons, missing free blasts and floating-point edge behavior. These pin the existing policies; they do **not** certify the strategic approximations below as optimal. Combined with the ten buff tests, this adds 29 cases.

## Claims checked and found correct

Each row is **severity: note; verdict: confirmed**. No game-rule fix is proposed; the risk of changing these mechanisms without contrary evidence exceeds the benefit.

| Claim | Evidence | Impact / qualification |
|---|---|---|
| Arrows pay native kinetic burn, including partial payment | `AbilityKineticist.GetQuiverResource`, `CalculateCost`, `Spend` | Total3 with rank1 leaves burn2. `BurnOf` already reads this correctly. Ordinary bow full attacks gain no blast damage from quiver ranks. |
| Create Quiver costs one actual burn and is a standard Personal cast | `Archetypes/KineticSharpshooter/Abilities/KineticSharpshooterCreateQuiverAbility.jbp:111`, `:153`, `:179` | `AllowOnlyBurnCost=true` prevents arrows paying for their own creation. Outside combat avoids a combat turn, not the native cast action. |
| Arrow count is Kineticist level + Constitution modifier | `.../KineticSharpshooterQuiverProperty.jbp:9`, `:27`, `:31` | Total multiclass character level is not the arrow-count level; character level separately scales burn HP. |
| Quiver stacks and is removed by rest | quiver buff `.jbp:157`–`:159`; `BuffCollection.AddBuff` Rank branch calls AddRank | Permanent, StayOnDeath, RemoveOnRest, max rank1000. Recasting adds ranks. |
| Direct-burn wild talents bypass the quiver | `AbilityAcceptBurnOnCast.OnCast` calls `AcceptBurn` directly | All seven supplied abilities: Searing Flesh, Enveloping Winds, Flesh of Stone, three Shroud abilities, Aerial Evasion; every BurnValue is1. The earlier claim that arrows pay these is wrong; test 4's correction is right. |
| Native burn is resource-limited, nonlethal and still accepted outside combat | `UnitPartKineticist.AcceptBurn`, `LeftBurnThisRound`; `KineticistController`; `BurnEffectBuff.jbp:12`; `KineticNonLethalDamageProperty.jbp:15` | Day burn increments outside combat; round burn increments only in combat and is reset on combat transitions/new rounds. Burn HP scales by character level and lasts until rest. |
| Ordinary bow attacks do not receive Elemental Overflow | `AddKineticistElementalOverflow.OnEventAboutToTrigger` overloads | Attack bonus requires KineticBlast weapon category; damage requires an own blast source/parent. Normal cap is min(component bonus,Kineticist level/3,AcceptedBurn), ×2 or ×3 fire with Fire's Fury. ElementalEngine/IgnoreBurn are separate native variants. |
| Archer blast is one bow attack per relevant standard-action ability | `AbilityDeliverProjectile.Deliver`, `RuleAttackWithWeapon`; supplied blast ActionType fields | Uses the primary bow, SkipMainDamage, not IsFullAttack. No Rapid Shot/Manyshot series of blast attacks. Exploding Arrows' extra delivery component does not create a blast full attack. |
| Bow attack bonus, enhancement, damage modifiers and swarm decline are read | native projectile delivery + `ContextActionDealDamage.GetDamageInfo`; `CombatMath.cs:759`, `:792`, `:1662` | Native bow rules carry applicable Weapon Focus/enhancement; energy delivery replaces attack type with touch. Current appraisal has the critical/mode gaps below. |
| The narrow unread-resource fix reaches the observed marker branch | `BuffEffectAnalyzer.cs:231`, `:526`; `TacticalEngine.cs:2706` | AddFacts logs `?AddFacts(...)`, changing the detection count without incrementing unrecognized. The new fallback rejects limited-use zero-read markers. Read/appraised parts and enchant carriers remain earlier branches. |
| Full attack beating a blast can be correct play | native independent weapon/ability actions; `Log.txt:542`, `:979` | 77.8 versus49.2 and87.4 versus36.3 are legitimate competing options, not proof the quiver should add blast damage to every arrow. |

## Notes and evidence corrections

### 6. Fight count, coverage and actual burn were overstated

**Severity:** misunderstanding. **Verdict:** confirmed corrections; actual aggregate burn remains unresolved.

**Evidence:** `combatLog.txt:21172` starts fight19, `:23976` ends it; the mod log agrees (`Log.txt:4233`, `:4505`). Fights1–6 are turn-based, fight7 switches from TB (`Log.txt:1937`) to RT, fights8–19 are RT. The character progresses from Kineticist4 to6, rather than being a level6 Sharpshooter throughout. Only five kinetic coverage lines occur (`Log.txt:35`, `:321`, `:1205`, `:1946`, `:2558`), all0/3. They do not prove a rest before every fight.

Native burn messages sum to **48 in32 events:21×1 +6×2 +5×3**. That arithmetic is correct. It is not a reliable accepted-burn total: `GameLogEventKineticistAbility` stores a mutable kineticist reference plus the previous value; `KineticistGlobalLogThread.HandleEvent` later subtracts that previous value from the then-current meter. Delayed events can overlap. Fights3 and6 print four despite the reported cap3. Manual play also occurs (`Log.txt:1697`); do not assign every native message to AI Control. There are31 successful acted AI commands carrying burn labels, summing42 pre-cast labeled burn; these labels are not an independent meter reconstruction either.

**Impact/fix/risk:** corrected §101 and test README to describe19 fights, five zero-burn samples, and a message tally. Do not patch native burn limits based on summed text. Exact accepted totals would need synchronous before/after telemetry or meter snapshots. Documentation correction has no gameplay risk.

### 7. Quiver, buff casts, errors and performance recount

**Severity:** misunderstanding / note. **Verdict:** confirmed counts; generalized performance claims qualified.

**Evidence:** all16 weapon-versus-blast `[Kinetic]` diagnostics show quiver0, and no AI quiver cast occurs. There are **21 successful acted burn-buff commands:11 Searing Flesh,9 Enveloping Winds,1 Shroud**; Searing+Winds exactly20 supports that narrower statement. All21 show burn1 priced0. Four other successful commands show burn1 priced1, one burn2 priced0, and five burn3 priced0 (four Exploding Arrows, one Magma Blast).

No AI Control error occurs, but three WrathPatches blueprint-owner errors appear (`Log.txt:288`, `:2131`, `:2639`). Seelah, Camellia and Lann go unconscious (`:518`, `:602`, `:1880`); this audit does not establish that higher difficulty or Sharpshooter choices caused those outcomes.

There are47 slow lines:45 Orvath,1 Lann,1 Camellia. Five explicitly report memory clean-up (`:1400`, `:2057`, `:2366`, `:4067`, `:4392`). Orvath's slow round1 range is253–477ms after the initial794ms decision; some decisions grow the heap10–16MB, others stay slow with small deltas (`:3182`, `:3186`). The quoted Exploding variants at13.8/13.6/13.1ms and564/540/536KB are real (`:381`). Action-bar discovery is77 at level4 (`:31`) and98–99 at level6 (`:317`, `:2554`); scored-ability counts are a separate measure.

**Impact/fix/risk:** corrected note ranges and wording; heap growth is not an allocation call stack. No speculative performance patch or difficulty retuning follows from these counts.

### 8. Archetype progression and forbidden forms now have blueprint evidence

**Severity:** note / misunderstanding of certainty. **Verdict:** confirmed normal-selection rules; absolute runtime-bar claim rejected.

**Evidence:** `KineticSharpshooterArchetype.jbp:47` adds ArcherBlast mechanical feature, proficiencies/main feature at1, Quiver at3, capstone at20; `:69` removes InfusionSelection and GatherPower at1 and Supercharge at11. Kinetic Blade (`Infusions/FormInfusions/KineticBladeInfusion.jbp:245`) and Extended Range (`ExtendedRangeInfusion.jbp:24`) explicitly forbid Sharpshooter; Blade Whirlwind does too. Deadly Earth requires Extended Range (`DeadlyEarthInfusion.jbp:27`) and level12, so its exclusion is indirect.

Sharpshooter-specific minimums: Exploding/Piercing Arrows3, Cluster11, Armor-Piercing12, Chain16, from the corresponding feature PrerequisiteArchetypeLevel fields. The archive does not define a separate Rending/Splash/Penetrate family. The absent original mechanics audit limits checking its exact terminology.

**Impact/fix/risk:** §101 now cites normal selection instead of claiming these abilities can never reach a runtime bar. Other mods or grants can bypass prerequisites; AbilityDiscovery remains appropriate. No gameplay change.

### 9. Three burn is an observed session limit, not proven universal

**Severity:** misunderstanding. **Verdict:** unsupported generalization rejected.

**Evidence:** Orvath's coverage says3. An isolated `KineticSharpshooterBurnResource.jbp` defines fixed3, but its GUID has no incoming reference in the supplied Kineticist archive. `BurnFeature` instead references ordinary BurnResource (base3 with Constitution scaling); native MaxBurn reads current resource amount.

**Impact/fix/risk:** do not infer all Sharpshooters have3 or infer Orvath's Constitution from that number. The implementation correctly uses native resources; no hard-coded cap was added. Notes retain3 only as Orvath's observed value.

### 10. Cone diagnostics are useful but cannot recover the old cause

**Severity:** note / misunderstanding. **Verdict:** message improvement confirmed; precise historical diagnosis unresolved.

**Evidence:** warnings at `Log.txt:67`, `:78` lack messages. GUID `a240a6d61e1aee040bf7d132bfe1dc07` is **Fan of Flames**, verified by `ElementalFocus/Fire/FireBlast/FireBlastInfused/FanOfFlamesFireBlastAbility.jbp:2`; the second GUID is absent from this archive. The brief's suggestion both are wizard cones is unsupported. Three explicit TryCreate checks are not exhaustive: `ProjectileCone.Query.Reaches` and native calls can also throw InvalidOperationException. The first decision later successfully casts Color Spray (`Log.txt:87`–`:93`).

**Impact/fix/risk:** retain test4's `ex.Message` change and fail-closed candidate omission. There is no evidence which rejected aim should have won. Deduplication still suppresses later same-GUID/same-type reasons. The review corrects the “exactly three causes” comment; changing cone geometry or weakening safety would be unsupported. A replay with new warning text is needed.

## Remaining strategic gaps and proposals

### 11. Preparation reserves only one current configuration's cost

**Severity:** gap. **Verdict:** confirmed policy limitation; occurrence/benefit character-dependent.

**Evidence:** `QuiverToPrepare` scans allowed non-AllowOnlyBurnCost AbilityKineticist abilities and current native TotalBurn. Infusions currently off do not contribute. It does not require each scanned ability to be castable or to have won a tactical comparison. Ranks equal to the maximum current cost suppress preparation, even if a planned infusion would increase it next fight. TotalBurn includes native cost handlers/Gather Power; normal Sharpshooter progression removes Gather Power, but externally retained states still follow game rules.

**Impact:** can under-reserve for future infusions or spend burn for a high-cost form the AI will never choose. At rank2/current cost2 it does not prepare even if next fight uses cost3.

**Proposal/risk:** a reserve for expected useful shots/configurations, with complete invalidation for learned/blocked abilities and toggle state. This is a burn-budget tradeoff, not a proven universal replacement; left unchanged and explicitly tested as current policy. No stale cache keyed only by ranks/burn/weapon was added.

### 12. The half-HP floor is sensible protection, not optimal burn pricing

**Severity:** note. **Verdict:** real-HP handling confirmed; optimal threshold unproven.

**Evidence:** `CombatMath.RealHpBeforeDown` subtracts current nonlethal and excludes temporary HP; pure preparation rejects a result strictly below50% of maxHP. Native burn adds character-level nonlethal. Equality is allowed.

**Impact:** conservative automatic spending for Orvath and any quiver owner, but does not forecast next-fight danger, reserve burn for defense, or value extra Overflow. `BurnCost` depends on a TacticalContext that does not exist in ordinary exploration.

**Proposal/risk:** user-selected budgeting or a separate forecast after evidence, not reuse of an imaginary combat context. Existing half-HP policy retained. After-rest burn spending is intentional when AI control and “AI recasts self buffs” are on; block Kinetic Quiver or disable that setting to reserve it manually.

### 13. Overflow shortfall compares unlike quantities and incomplete alternatives

**Severity:** gap. **Verdict:** confirmed model limitation; effect on a particular choice unproven.

**Evidence:** `BlastShortfall` compares candidate values after hit/kill/risk modifiers, while `BurnCost` credits raw per-hit Overflow points. It ignores spells and other non-weapon alternatives, observes only this decision's available Standard/FullRound candidates, and keeps the old one-blast-per-later-round assumption when no free blast exists. Once the threshold is crossed it credits the whole raw gain, rather than only the improvement above the weapon. Other BurnCost callers still omit the shortfall.

**Impact:** can still underprice burn for caster/kineticist multiclasses and restricted-action decisions. Example: free blast19, weapon20, raw gain2 at50% hit only closes the expected gap, yet the current credit may subtract2 per later round. This is an illustrative model counterexample, not a replayed actor.

**Proposal/risk:** compare compatible expected candidate gains and best sustainable alternatives over a defensible horizon. Also consider the current blast's Overflow: native Spend precedes damage, while existing credit deliberately starts next round. Multi-target/crit/risk interactions require a broader scoring change; no formula was silently retuned. New tests preserve the existing threshold and its null behavior.

### 14. Mid-fight refill and arrow conservation remain incomplete

**Severity:** gap. **Verdict:** structural restriction confirmed; optimal refill policy plausible.

**Evidence:** `AddKineticCandidates` returns when any own blast is available. A free plain blast therefore blocks an elective refill even if arrows would unlock more valuable future forms. The existing special recovery path already prices future gain over other actions minus burn, but never reaches those elective situations. Arrows have no scarcity price when they cover the current cost.

**Impact:** partial payment itself is correct (total3/rank1 means burn2); the missing part is future opportunity cost. Three cost3 shots can consume9 arrows even where cheaper attacks suffice. Creating arrows also spends burn and a standard action, so always refilling would be wrong.

**Proposal/risk:** assess marginal future form value and saved burn against the action and preparation burn, then reserve arrows when useful. Needs a gameplay budgeting choice; unchanged. Empty-hand recovery is a related confirmed mismatch: native quiver payment allows no primary weapon, whereas the old in-combat route requires rangedInHand. Weaponless ordinary blast play is outside the supplied bow scenario; proposal only.

### 15. Unread defensive talents have real incremental effects

**Severity:** gap. **Verdict:** confirmed; proposal only.

**Evidence:** supplied baseline/marker blueprint relationships show Enveloping Winds increments ranged miss chance by5 percentage points per marker rank; Searing Flesh increases retaliation on specified natural/unarmed melee hits, not bow damage. Native `ShroudOfWater.OnTurnOn` computes AC as min(floor(base×1.5),base+upgrade rank), using Armor/Shield descriptors and rank recalculation. Each unsaturated upgrade adds1AC before stacking. Flesh of Stone has readable DR; Aerial Evasion directly grants Evasion.

Five statically traced candidates for the newly zeroed marker branch are Searing Flesh, Enveloping Winds, and Shroud Armor/Shield/Upgrade. Flesh of Stone and Knight's Resolve have readable paths; Aerial Evasion already reached the older unread-limited zero. This is not a runtime availability census. Broader blueprint folders are required to enumerate every affected class ability.

**Impact:** zero prevents unsupported spending but can omit valuable protection. Existing appraised parts, recognized effects and enchant carriers remain valued before fallback. OOC IsWorthKeepingUp already excludes these unread markers; toggle fallback has a separate older positive OnlyUnrecognized policy and is not globally equivalent.

**Proposal/risk:** read the *incremental* cross-fact rank effect, cap and stacking against the attacks actually expected. Blind AddFacts recursion can re-credit baseline protection. Fixing the analyzer count at its root would also promote renewable unknown markers from0.05 to0.5 and alter toggle behavior; that broader policy change was avoided as requested.

### 16. Bow-blast criticals and attack-mode choice are missing

**Severity:** gap / misunderstanding. **Verdict:** confirmed; exact observed decision impact unresolved.

**Evidence:** native `ContextActionDealDamage.GetDamageInfo` copies weapon critical multiplier on a critical unless the damage ignores it. `CombatMath`'s ability estimate has no critical term; the weapon estimate does. `TacticalEngine.cs:329` calls ChooseAttackModes only for weapon candidates, excluding archer-blast abilities. Native attack triggers run on RuleAttackWithWeapon, so direct bonus/stats probes do not necessarily install a transient Deadly Aim effect. Merely having its toggle on is insufficient; only modifiers already installed in queried rules are seen.

**Sizing:** extra expected eligible damage is `(multiplier−1) × P(hit and threat) × P(confirm) × per-hit damage`. With confirm chance equal to hit chance,20/x3 adds10% expected base damage;19–20/x3 adds20% when both threat rolls hit. Orvath actually threatens on a natural19 and deals×3 (`combatLog.txt:1319`, `:1350`), so base20-only cannot be assumed. Treating the cited candidate values as damage-linear sensitivities gives49.2→54.1/59.0, still below77.8, and36.3→39.9/43.6, below87.4. Kill/risk terms are not linear; this does not prove all choices stay unchanged. No Orvath Deadly Aim snapshot exists; the coverage showing it is Lann's.

**Impact/proposal/risk:** a real undervaluation that grows with the level20 critical capstone, but not proof the shown full attacks were wrong. Future native critical forecasting must respect threat/confirmation modifiers, immunity, IgnoreCritical, DR order and observed-damage scaling. Shared mode counterfactuals need native trigger/ranged-touch validation. No copied rule table or speculative gameplay change.

### 17. Allocation hotspot is traced, not quantitatively attributed

**Severity:** performance. **Verdict:** aggregate cost confirmed; exact allocation source plausible/unresolved.

**Evidence:** `CombatMath.CreateContext:571` creates/recalculates execution contexts; damage estimation does it per ability/target. `AbilityProfile.CheckFor` creates contexts for uncached condition/target/aim checks. Exploding Arrows has main-target/splash branches and area recipients; infusion counterfactuals can re-score it. These are concrete repeated paths, but the log's heap delta does not identify which dominates540KB or13ms.

**Impact/proposal/risk:** request an allocation profile or bounded context/pass counters for the exact next build and actor. Do not cache mutable MechanicsContext across aims/targets: shared values, condition branches and native preview events can change. Only the proven duplicate burn queries were removed here. No claim is made that the existing stutter is solved.

### 18. Small diagnostic cache and player control

**Severity:** note. **Verdict:** confirmed, low impact.

**Evidence:** QuiverNotes is a static unit-id→last-reason dictionary and is not cleared. It stores strings, not units/abilities. An old same-id/same-reason can suppress a repeated log line after load, but cannot alter the quiver decision. ShouldControlUnit honors global/per-character AI control; `Log.txt:1697` visibly reports manual control.

**Impact/proposal/risk:** bounded by encountered ids in a session, with potential slow growth; no save corruption or gameplay effect established. A future lifecycle/weak diagnostic cache is optional. Respecting manual control is correct; the ordinary setting/blocklist and issuance log explain automatic preparation. No change needed for this audit.

### 19. Plain-blast warning does not prove doubled damage

**Severity:** note. **Verdict:** alleged simple double-count rejected.

**Evidence:** `Log.txt:65`, `:69`–`:72` warn an IsEqual delivery-layer condition is unread. Supplied Fire/Earth graphs choose full damage for layer0 and half for another layer. `AbilityProfile` records opposing guards; `CombatMath.SumAlternatives:731` excludes mutually exclusive alternatives rather than adding both.

**Impact/fix/risk:** ordinary layer0 does not become1.5× damage from this warning. More precise delivery-layer reading may help another case, but no proven fix is warranted here.

## Verification and missing resources

| Check | Actual result |
|---|---|
| Baseline `dotnet test tests/CompanionAI.Tests` |359 passed;0 failed/skipped.|
| Final same full suite |388 passed;0 failed/skipped.|
| Baseline and final Release build against supplied real DLLs |Both succeed with0 warnings/errors; no deploy flag used.|
| SigDump before/after |10 intended Assembly-CSharp safety-gate signatures added;0 removed; no new Unity/Owlcat/Harmony signatures. Full real-DLL compilation verifies native symbol availability.|
| `git diff --check` |Pass.|
| ProfileAudit, strict baseline and final on supplied Kineticist dumps |INCOMPLETE/exit1:5 loaded,800 failed. Missing `UnityEngine.SharedInternalsModule.dll` prevents meaningful ability verdicts; no passing coverage claimed.|
| In-game replay of this review branch |Not run. Supplied logs are test3, MVID `09bc562d-a4dc-4c51-a8ff-ebc2e6f1cf3a`; they validate historical observations, not these fixes.|

Final validation build DLL SHA-256: `26e3dab2bedb109e610bbe38c0a1d281a97483f47dbfdbf2ce1705c094c30f16`. No new release or test package is published by this review; existing ZIPs and version metadata are unchanged.

To retry blueprint execution, supply **`UnityEngine.SharedInternalsModule.dll` from the same installation's `Wrath_Data/Managed`**. Other metadata references are absent too, but the current failure establishes only this first blocking dependency; a retry may reveal another. Supply the original mechanics-audit Markdown for an exact premise-by-premise review. The second cone GUID needs its matching spell blueprint for verified naming, and allocation attribution needs a live profile/new diagnostic capture rather than additional static rules alone.

Reproduction: build the mod with `dotnet build CompanionAI_Pathfinder.csproj -c Release -p:ManagedPath=<real DLL folder> -p:UmmPath=<UMM DLL folder>`; run the tests above; run SigDump on preserved baseline/final DLLs and compare sorted output. The attempted strict reader command used `tools/ProfileAudit/run.sh <real DLL folder> <absolute mod DLL> <scratch folder> <report file> <Kineticist dump folder>` with no allowed-failure override.

## Focused gameplay handover

1. Use a build from this branch and record its commit, startup MVID and package checksum. Keep the longbow equipped, AI control on for Orvath, “AI recasts self buffs” on, Kinetic Quiver unblocked, and a currently costly blast such as Exploding Arrows available on the bar.
2. **After rest**, unpause in normal exploration, before initiating a fight. Expect one accepted quiver cast, `[Kinetic] ... make the quiver` and the out-of-combat issuance line; verify one burn/nonlethal increment and Kineticist-level+Con arrows. Wait several seconds: sufficient ranks must not cause another cast. Repeat with TB preference and RT preference.
3. During pause/fake pause, dialogue, cutscene, loading and rest, expect no new quiver order. Repeat with AI disabled for Orvath, self-buff recasting off, Kinetic Quiver blocked, melee weapon equipped, insufficient native burn or HP below the preparation floor. Each applicable gate must prevent the cast. Test the exact half-HP boundary separately.
4. Leave an ordinary command running or queued while combat ends/rest completes; it must finish without automatic preparation replacing it. After it finishes and normal exploration resumes, eligible quiver preparation should retry once. Watch for repeated Interrupted/never-acted quiver commands.
5. Enter a group fight in each combat mode. Confirm quiver ranks pay costly blasts, partial ranks leave only the native shortfall as burn, and one archer blast is one action. The bow full attack should still win where its real value is greater. No out-of-combat preparation should fire while any party member is fighting.
6. Confirm unread burn markers are not guessed into use; manual defensive talent use must remain possible. Capture current burn/ranks before and after actual casts, rather than summing delayed native text.
7. Replay Fan of Flames and the other warned cone with enemies/nearby allies; save complete new warning messages and note geometry. Record slow decisions and an allocation profile if stutter persists. This audit does not change movement/kiting or declare those failures repaired.
