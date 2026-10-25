// ★ v0.3.22: gathering is part of a blast plan, priced against other uses of its native action budget.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Commands.Base;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static bool GatherPlanFits(TacticalContext ctx, Candidate c, out bool move, out bool standard)
        {
            move = standard = false;
            var gather = ctx.Unit.Get<UnitPartKineticist>()?.GatherPowerAbility?.Data;
            if (gather == null || !gather.IsAvailable || c.NeedsMove || c.MoveFirst) return false;
            if (CombatMemory.IsRealTime)
                return !ctx.Unit.CombatState.HasCooldownForCommand(gather.RuntimeActionType);
            var slot = c.KineticCastSlot ?? c.Slot;
            return KineticConfigurations.GatherFits(c.GatherPower,
                ctx.HasMove && ctx.Unit.CombatState.Cooldown.MoveAction <= 0f, ctx.HasStandard, ctx.HasSwift,
                ctx.SurpriseRound || ctx.Permissions.OneActionOnly,
                slot == ActionSlot.Move, slot == ActionSlot.Standard, slot == ActionSlot.Swift, slot == ActionSlot.FullRound,
                out move, out standard);
        }

        private static void PriceGatherPlans(TacticalContext ctx, List<Candidate> list)
        {
            var gather = ctx.Unit.Get<UnitPartKineticist>()?.GatherPowerAbility?.Data;
            if (gather == null) return;
            float movement = list.Where(c => c.Kind == CandidateKind.Move && c.Slot == ActionSlot.Move && Affordable(ctx, c))
                .Select(c => Math.Max(0f, c.Value)).DefaultIfEmpty(0f).Max();
            float main = list.Where(c => !c.KineticPreparation && c.GatherPower == 0 && c.Slot == ActionSlot.Standard && !c.NeedsMove && Affordable(ctx, c))
                .Select(c => Math.Max(0f, c.Value)).DefaultIfEmpty(0f).Max();
            foreach (var prep in list.Where(c => c.KineticPreparation && c.Slot == ActionSlot.Move))
            {
                prep.Value -= movement;
                prep.Label += $" [move opportunity -{movement:F1}]";
            }
            foreach (var c in list.Where(c => c.GatherPower > 0 && (c.Ability != null || c.KineticValidationAbility != null)))
            {
                if (!GatherPlanFits(ctx, c, out bool usesMove, out bool usesStandard)) { c.Value = 0f; continue; }
                float actionSeconds = 0f, delay = 0f;
                if (CombatMemory.IsRealTime &&
                    NativeActionCost.TrySeconds(ctx.Unit, gather.RuntimeActionType, gather.RequireFullRoundAction, out float gatherSeconds) &&
                    NativeActionCost.TrySeconds(ctx.Unit, UnitCommand.CommandType.Standard, false, out actionSeconds))
                    delay = Math.Max(0f, c.GatherPower * gatherSeconds - ctx.Unit.CombatState.Cooldown.StandardAction);
                float opportunity = CombatMemory.IsRealTime ? movement :
                    (usesMove ? movement : 0f) + (usesStandard ? main : 0f);
                if (!CombatMemory.IsRealTime && (ctx.SurpriseRound || ctx.Permissions.OneActionOnly))
                    opportunity = Math.Max(movement, main);
                var risk = GatherRisk(ctx, gather, c.GatherPower);
                c.Value = ValueModel.PreparedBlastValue(c.Value, opportunity, actionSeconds, delay,
                    risk?.Cost ?? 0f, risk?.StopChance ?? 0f);
                c.Label += $" [gather: action opportunity -{opportunity:F1}, delay~{delay:F1}s, risk -{(risk?.Cost ?? 0f):F1}]";
                if (!CombatMemory.IsRealTime)
                {
                    c.KineticCastSlot = c.Slot;
                    bool castStandard = c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound;
                    if ((usesMove && (usesStandard || castStandard))) c.Slot = ActionSlot.FullRound;
                    else if (usesStandard) c.Slot = ActionSlot.Standard;
                    else if (usesMove) c.Slot = ActionSlot.Move;
                }
            }
        }

        private static Disengage GatherRisk(TacticalContext ctx, AbilityData gather, int count)
        {
            // The supplied game's Gather Power is Special/Move, so the ordinary spell-casting provoke path
            // does not apply. Honor runtime flags and explicit provoke actions rather than importing tabletop rules.
            var profile = AbilityProfile.Get(gather);
            return gather.ProvokeAttackOfOpportunity || profile?.CasterProvokes == true
                ? AttackRollProvokeRisk(ctx, ctx.Unit.Position, count) : null;
        }

        private static void AddGatherPreparations(TacticalContext ctx, List<Candidate> destination, List<Candidate> plans)
        {
            var gather = ctx.Unit.Get<UnitPartKineticist>()?.GatherPowerAbility?.Data;
            if (gather == null || !gather.IsAvailable || !NativeActionCost.CanQuery || ctx.Permissions.MovementOnly) return;
            if (CombatMemory.IsRealTime) return; // RT uses the game's queued gather chain, priced in PriceGatherPlans.
            if (!ctx.HasMove && !ctx.HasStandard) return;
            if (!NativeActionCost.TrySeconds(ctx.Unit, gather.RuntimeActionType, gather.RequireFullRoundAction, out float prepSeconds) ||
                !NativeActionCost.TrySeconds(ctx.Unit, UnitCommand.CommandType.Standard, false, out float mainSeconds)) return;
            float immediate = destination.Where(c => !c.KineticPreparation && c.GatherPower == 0 &&
                    (c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound) && Affordable(ctx, c))
                .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            bool hasMove = ctx.HasMove && ctx.Unit.CombatState.Cooldown.MoveAction <= 0f;
            if (!hasMove && !ctx.HasStandard) return;
            foreach (var best in plans.Where(c => c.GatherPower > 0 && !c.NeedsMove && !c.MoveFirst && !Affordable(ctx, c) && c.Value > 0f))
            {
                var risk = GatherRisk(ctx, gather, best.GatherPower);
                float roundsPreparing = best.GatherPower * prepSeconds / mainSeconds;
                float future = ValueModel.PreparationGain(best.Value, immediate, ctx.ExpectedRounds, roundsPreparing);
                float lostMain = hasMove ? 0f : immediate;
                float value = ValueModel.PreparedBlastValue(future, lostMain, 0f, 0f, risk?.Cost ?? 0f, risk?.StopChance ?? 0f);
                if (value <= 0f) continue;
                destination.Add(new Candidate
                {
                    Kind = CandidateKind.Ability, Slot = hasMove ? SlotOf(gather) : ActionSlot.Standard,
                    Ability = gather, Target = ctx.Unit, Value = value, KineticPreparation = true, KineticCastSlot = SlotOf(gather),
                    KineticModes = best.KineticModes, KineticFixedModes = best.KineticFixedModes, KineticBurn = BurnOf(gather),
                    Label = $"Gather for {best.Label} (future gain {future:F1}, preparation~{roundsPreparing:F1} rounds, lost main {lostMain:F1})",
                    Key = "gather-prep:" + best.Key
                });
            }
        }
    }
}
