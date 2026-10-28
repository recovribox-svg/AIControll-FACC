// v0.3.22 log review: author-approved reach/area diagnostics only. No candidate or actor state is changed.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        // TurnMemory is mod-owned and replaced per actor/round, and cleared between fights. Weak keys neither
        // retain an old turn nor need another reset hook. Values contain only diagnostic scalar bookkeeping.
        private sealed class ReachDiagnosticSample
        {
            internal readonly int Serial = Interlocked.Increment(ref s_ReachDiagnosticSerial);
            internal int Claimed;
        }

        private static int s_ReachDiagnosticSerial;
        private static readonly ConditionalWeakTable<TurnMemory, ReachDiagnosticSample> ReachDiagnosticSamples =
            new ConditionalWeakTable<TurnMemory, ReachDiagnosticSample>();
        private static ReachDiagnosticSample NewReachDiagnosticSample(TurnMemory _) => new ReachDiagnosticSample();

        private static void LogReachStandoffDiagnostics(TacticalContext ctx, List<Candidate> candidates,
            Candidate chosen, TurnMemory memory)
        {
            try
            {
                // Only the incident's primary-melee RT shape. This is called after the preview early return, not
                // from an idle/UI tick, a counterfactual appraisal or a native event callback.
                if (!CombatMemory.IsRealTime || ctx?.Unit == null || !ctx.Unit.IsInCombat || ctx.HasRangedWeapon ||
                    candidates == null || memory == null) return;
                Candidate approach = null, normalRetreat = null;
                foreach (var candidate in candidates)
                {
                    if (candidate == null) continue;
                    // A useful in-reach weapon option is not the blocked-melee situation being sampled.
                    if (candidate.Kind == CandidateKind.WeaponAttack && !candidate.NeedsMove &&
                        candidate.Value >= MinMainValue) return;
                    if (candidate.Kind == CandidateKind.WeaponAttack && candidate.NeedsMove &&
                        candidate.Target != null && candidate.Risk?.Area != null && candidate.PreRiskValue > 0f &&
                        (approach == null || candidate.PreRiskValue > approach.PreRiskValue)) approach = candidate;
                    if (candidate.Kind == CandidateKind.Move &&
                        (candidate.Key == "move:withdraw" || IsFiveFootStepKey(candidate.Key)) &&
                        (normalRetreat == null || candidate.Value > normalRetreat.Value)) normalRetreat = candidate;
                }
                if (approach == null) return;

                var sample = ReachDiagnosticSamples.GetValue(memory, NewReachDiagnosticSample);
                if (Interlocked.Exchange(ref sample.Claimed, 1) != 0) return;
                // One foe/row per actor TurnMemory. Expensive names/formatting and bounded native-command reads
                // occur only in the first ReportOnce callback; no new rule, forecast, risk or path query is made.
                ReportOnce($"reach-area|{ctx.Unit.UniqueId}|{memory.Round}|{sample.Serial}", () =>
                    DescribeReachStandoff(ctx, approach, normalRetreat, chosen));
            }
            catch { } // Missing diagnostic data must never remove an action or change Decide's result.
        }

        private static string DescribeReachStandoff(TacticalContext ctx, Candidate approach, Candidate normalRetreat,
            Candidate chosen)
        {
            var unit = ctx.Unit;
            var foe = approach.Target;
            string intent = "current melee attack/distance evidence unavailable";
            int inspected = 0;
            try
            {
                // The primary equipment flag only narrows the sample; it is not a new attack estimate or proof
                // of which hand a custom attack uses. Unknown/ranged primary layouts retain the unknown label.
                var primary = foe.Body?.PrimaryHand?.MaybeWeapon;
                if (primary != null && !primary.Blueprint.IsRanged && foe.Commands?.Raw != null)
                {
                    foreach (var command in foe.Commands.Raw)
                    {
                        if (++inspected > 8) { inspected = 8; break; }
                        if (command?.GetType() != typeof(UnitAttack) || command.Executor != foe ||
                            !foe.Commands.ContainsOrQueued(command) || command.IsFinished || command.Cutscene) continue;
                        var attack = (UnitAttack)command;
                        if (attack.Target != unit) continue;
                        float radius = attack.ApproachRadius;
                        var point = attack.ApproachPoint;
                        var position = foe.Position;
                        float distance = Vector3.Distance(point, position);
                        if (!(radius > 0f) || float.IsInfinity(radius) || float.IsNaN(distance) || float.IsInfinity(distance)) continue;
                        // needLoS=false is deliberately distance-only. Passing position as eye position is unused
                        // in that branch. Do not call ShouldUnitApproach, which can also query sight internally.
                        bool inDistance = UnitCommand.IsUnitCloseEnough(point, position, position, radius, false, 0f);
                        intent = $"accepted UnitAttack targets this actor, primary={primary.Name}, " +
                            $"native approach radius={radius:F2}m, target-point distance={distance:F2}m, " +
                            $"in native distance={inDistance}, started={command.IsStarted}, acted={command.IsActed}; LoS not queried";
                        break;
                    }
                }
            }
            catch (Exception ex) { intent = $"current attack evidence unavailable ({ex.GetType().Name})"; }

            var risk = approach.Risk;
            string retreat = normalRetreat == null ? "none already priced"
                : $"{normalRetreat.Label}={normalRetreat.Value:F1}";
            return $"[Reach] {unit.CharacterName} R{ctx.Round} HP{ctx.HpPct:P0}: possible melee/area standoff vs " +
                $"{foe.CharacterName}[{foe.UniqueId}]; chosen={chosen?.Label ?? "wait"} value={chosen?.Value ?? 0f:F1}; " +
                $"own weapon appraisal requires approach (range/LoS); own weapon reach not retained; " +
                $"planned arrival tolerance={approach.ApproachRadius:F2}m; " +
                $"existing approach={approach.Label}, stored pre-risk value={approach.PreRiskValue:F1}, " +
                $"current candidate value={approach.Value:F1}, area={risk.Area}, stop={risk.StopChance:P0}, " +
                $"drop={risk.DropChance:P0}, priced AoOs={risk.Attacks}, risk cost={risk.Cost:F1}; " +
                $"foe evidence: {intent}; inspected current slots={inspected}/8; normal backoff={retreat}; " +
                "standoff-specific backoff payoff not modelled; no pursuit or prevented-hit forecast; movement unchanged";
        }
    }
}
