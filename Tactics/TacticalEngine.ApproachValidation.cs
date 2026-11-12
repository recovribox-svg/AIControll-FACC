// ★ test1-scoring-fixes: prepare the selected follow-up before a booster can spend a resource on it.
using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Settings;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static bool QuickAction(Candidate c) => c.Slot == ActionSlot.Free || c.Slot == ActionSlot.Swift;

        /// <summary>The main action after the existing restraint rule, shared by preparation and spell coupling.</summary>
        private static Candidate FollowUpMain(IReadOnlyList<Candidate> usable, Func<Candidate, bool> isBoost)
        {
            var main = usable.FirstOrDefault(c => !QuickAction(c) && !isBoost(c));
            if (main == null || main.Value < MinMainValue) return null;
            if (main.PureBuff && (main.Slot == ActionSlot.Standard || main.Slot == ActionSlot.FullRound))
            {
                var immediate = usable.FirstOrDefault(c => c != main && !c.PureBuff && !QuickAction(c) &&
                                                           !isBoost(c) && c.Value >= MinMainValue);
                if (immediate != null && !ValueModel.BuffBeatsImmediate(main.Value, immediate.Value)) return immediate;
            }
            return main;
        }

        private static bool WalksBeforeActing(Candidate c) => c.MoveFirst || c.NeedsMove || c.Kind == CandidateKind.Move;

        private static bool RequiresApproachValidation(Candidate c) => WalksBeforeActing(c) ||
            c.Kind == CandidateKind.WeaponAttack && c.FullAttack && c.FullAttackApproach;

        private static bool WeakWeaponFallback(Candidate c) => c.Kind == CandidateKind.WeaponAttack &&
            !c.NeedsMove && !c.MoveFirst && c.Value > 0.05f && c.Target != null && !CombatMath.IsDisabled(c.Target);

        private static List<Candidate> PreflightMainCandidates(TacticalContext ctx, List<Candidate> usable, TurnMemory memory,
                                                              IReadOnlyList<Candidate> scored)
        {
            if (ctx?.Unit == null || !ctx.Unit.IsInCombat) return usable;
            var boosts = new Dictionary<Candidate, bool>();
            bool IsBoost(Candidate c)
            {
                if (!boosts.TryGetValue(c, out bool boost))
                    boosts[c] = boost = c.Kind == CandidateKind.Ability && c.Ability != null &&
                        SpellBoostOf(AbilityProfile.Get(c.Ability), c.Ability, ctx.Unit) != null;
                return boost;
            }
            var failures = new Dictionary<Candidate, string>();
            bool rejectedOffense = false;
            Candidate SelectMain(IReadOnlyList<Candidate> list)
            {
                // Keep the complete eligible list until path feasibility is known. A refused lower-tier spell
                // must expose the higher tier the Progressive policy previously held back. This silent gate uses
                // the candidates' already calculated chances and recipient metadata, without native re-scoring.
                var eligible = ctx.Settings.SpellPolicy == SpellResourcePolicy.Progressive
                    ? ApplyProgressive(ctx, list.ToList(), diagnostics: false) : list;
                // Final selection also allows a weak weapon fallback below MinMainValue. A full routine
                // needing a step must pass the same path check even when it enters through that fallback.
                return FollowUpMain(eligible, IsBoost) ?? eligible.FirstOrDefault(WeakWeaponFallback);
            }
            return SelectedApproachFilter.Apply(usable, SelectMain,
                c => !QuickAction(c) && !IsBoost(c) && RequiresApproachValidation(c),
                c =>
                {
                    try
                    {
                        var command = ApproachCommand(ctx, c, out bool avoidAreas);
                        var counter = c.RiskAt.HasValue ? RealPathCounter(ctx, c) : c.CountPathAttackers;
                        bool ready = CommandDispatcher.CanPrepare(ctx.Unit, command, out string failure,
                            avoidAreas: avoidAreas, fiveFootOnly: IsFiveFootStepKey(c.Key) && !CombatMemory.IsRealTime,
                            pathAttackers: counter, pricedAttacks: PricedAttacks(c), pathConstraint: c.PathConstraint);
                        if (!ready) failures[c] = failure ?? "approach unavailable";
                        return ready;
                    }
                    catch (Exception ex)
                    {
                        failures[c] = "approach query unavailable: " + ex.GetType().Name;
                        return false;
                    }
                },
                c =>
                {
                    string failure = failures[c];
                    rejectedOffense |= IsOffensiveApproach(c);
                    // Retain only the existing real-path risk evidence. A path refusal must not make another
                    // range/action against this target globally unreachable, or survive into a changed frame.
                    if (memory != null && failure.StartsWith(CommandDispatcher.MoreAttacksOfOpportunityFailure) && c.SeenPathAttackers != null)
                        memory.PathAttackers[c.Key] = (c.SeenPathFrom, c.SeenPathAttackers);
                    // ★ v0.3.29 test 1: farther than this turn's walk-then-act allows - the next decision offers an advance on
                    // it at the refused option's worth, as when the dispatcher refuses the walk (TacticalDriver).
                    if (memory != null && c.Target != null && failure.Contains(CommandDispatcher.LongerThanOneMoveFailure))
                        memory.NoteTooFar(c.Target.UniqueId, c.Value);
                    ReportOnce($"preflight|{ctx.Unit.UniqueId}|{CombatMemory.Round}|{c.Key}|{failure}",
                        $"[Approach] {ctx.Unit.CharacterName}: {c.Label} withheld before issue/boost ({failure})");
                },
                omitted => ReportOnce($"preflight-budget|{ctx.Unit.UniqueId}|{CombatMemory.Round}",
                    $"[Approach] {ctx.Unit.CharacterName}: {SelectedApproachFilter.MaxChecks} selected path checks used; " +
                    $"{omitted} unchecked walking options withheld for this decision (not proven unreachable; plain moves kept)"),
                // ★ v0.3.29 test 1: a plain move (an advance, a withdrawal) spends nothing first; the dispatcher checks its walk.
                c => c.Kind == CandidateKind.Move && !c.NeedsMove && !c.MoveFirst,
                hasUsefulAction: list => list.Any(c => QuickAction(c) && c.Value >= QuickBar(c)) ||
                    SelectMain(list) != null || PureMagePolicy.IsActive(ctx.Settings) && list.Any(PureMageCantripFallback),
                idleFallback: () =>
                {
                    if (!rejectedOffense) return Enumerable.Empty<Candidate>();
                    try { return IdleAdvanceCandidates(ctx, scored, memory); }
                    catch (Exception ex)
                    {
                        Main.Error($"[Approach] {ctx.Unit.CharacterName}: advance recovery unavailable: {ex.Message}");
                        return Enumerable.Empty<Candidate>(); // retain the already-filtered plans on a recovery error
                    }
                }, recoveryKey: PlainMoveRecoveryKey);
        }

        private static string PlainMoveRecoveryKey(Candidate c) =>
            c.Kind == CandidateKind.Move && !c.NeedsMove && !c.MoveFirst ? c.Key : null;

        private static bool IsOffensiveApproach(Candidate c) =>
            c.Offensive && c.Kind != CandidateKind.Move && RequiresApproachValidation(c);

        private static bool CanOfferIdleAdvance(TacticalContext ctx) =>
            (ctx.HasMove || ctx.HasStandard) && ctx.Permissions.CanWalk && !ctx.NoMovementLeft && !ctx.StepTaken &&
            !ctx.Permissions.MovementOnly && ctx.PureMageWeapon;

        private static bool WaitingForInPlaceAttack(bool realTime, IReadOnlyList<Candidate> scored) =>
            realTime && scored.Any(c => AttacksFromHere(c) && !RequiresApproachValidation(c));

        private static IEnumerable<Candidate> IdleAdvanceCandidates(TacticalContext ctx, IReadOnlyList<Candidate> scored,
                                                                    TurnMemory memory)
        {
            if (!CanOfferIdleAdvance(ctx) || WaitingForInPlaceAttack(CombatMemory.IsRealTime, scored))
                return Enumerable.Empty<Candidate>();
            var advances = new List<Candidate>();
            bool rangedStyle = ctx.HasRangedWeapon || ctx.Settings.RangePreference == RangePreference.Ranged ||
                               ctx.Settings.Role == AIRole.Support;
            AddClosingCandidates(ctx, advances, rangedStyle);
            RepriceRealPaths(ctx, advances, memory);
            // A plain move spends nothing before issue; keep the ordinary gates and dispatcher route/hazard checks.
            // Do not re-run all movement generation, main preparation, marks, spells or the entire decision.
            var usable = advances.Where(c => c.Kind == CandidateKind.Move && !c.NeedsMove && !c.MoveFirst &&
                PureMageAllows(ctx, c) && c.Value > 0f && Affordable(ctx, c) && !memory.Failed.Contains(c.Key) &&
                !SilentlyFailing(ctx, c) && !PolicyBlocks(ctx, c) && !PointlessAgainstHelpless(ctx, c, false))
                .OrderByDescending(c => c.Value).ToList();
            if (usable.Count > 0)
                ReportOnce($"idle-advance|{ctx.Unit.UniqueId}|{CombatMemory.Round}",
                    $"[Approach] {ctx.Unit.CharacterName}: rejected approaches left no useful action; " +
                    $"{usable.Count} plain advance options priced (walk checked at issue)");
            return usable;
        }

        /// <summary>Throwaway command matching Execute's approach/reach settings; never queued or retained.</summary>
        private static UnitCommand ApproachCommand(TacticalContext ctx, Candidate c, out bool avoidAreas)
        {
            avoidAreas = c.CheckPath;
            if (c.MoveFirst && c.MovePoint.HasValue)
            {
                avoidAreas = true;
                return new UnitMoveTo(c.MovePoint.Value, c.ApproachRadius > 0f ? c.ApproachRadius : 0.3f);
            }
            if (c.Kind == CandidateKind.WeaponAttack)
            {
                var attack = new UnitAttack(c.Target);
                SetAttackFlags(attack, c.FullAttack, CombatMemory.IsRealTime, PureMagePolicy.IsActive(ctx.Settings));
                return attack;
            }
            if (c.Kind == CandidateKind.Ability && c.Ability != null)
            {
                if (TouchApproachFirst(c))
                    return new UnitMoveTo(c.Target.Position, c.Ability.GetApproachDistance(c.Target) * 0.8f);
                var aim = c.Target != null ? new TargetWrapper(c.Target) : new TargetWrapper(c.Point ?? ctx.Unit.Position);
                return new UnitUseAbility(c.Ability, aim);
            }
            if (c.Kind == CandidateKind.Move)
                return new UnitMoveTo(c.Point ?? ctx.Unit.Position, c.ApproachRadius) { NeedLoS = c.NeedLoS };
            throw new InvalidOperationException("no movement command for this candidate");
        }

        private static bool TouchApproachFirst(Candidate c) => c.NeedsMove && c.Target != null && c.Ability != null &&
            c.Ability.StickyTouch == null && c.Ability.Blueprint.StickyTouch != null && SpellDelivery.IsPhysicalTouch(c.Ability);
    }
}
