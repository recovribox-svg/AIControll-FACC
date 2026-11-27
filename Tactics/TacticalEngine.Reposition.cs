using System;
using System.Linq;
using CompanionAI_Pathfinder.Settings;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands.Base;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    // ★ v0.3.70 F1 (author request, movement): a ranged unit that has spent its main action but still has its move action
    // takes a safer spot before its turn ends, one from which it can still shoot (or cast at) an enemy next turn. Inspired by
    // the Rogue Trader CompanionAI's end-of-turn reposition (fewer melee enemies that reach the spot, less threat there); here
    // the engine's own measures price it: the drop chance before the next turn (SpotDrop: melee enemies' real hits where they
    // strike from), the expected damage there (IncomingDamage) and the walk's own risk (MoveRisk: attacks of opportunity,
    // areas, what leaving costs the party). Turn-based only: real time already has the archer's back-off.
    public static partial class TacticalEngine
    {
        private static void AddRepositionCandidate(TacticalContext ctx, System.Collections.Generic.List<Candidate> list, bool rangedStyle)
        {
            var unit = ctx.Unit;
            if (CombatMemory.IsRealTime || !rangedStyle || !ctx.HasMove || ctx.HasStandard || ctx.HasFullRound ||
                ctx.StepTaken || ctx.NoMovementLeft || ctx.Permissions.MovementOnly || ctx.Enemies.Count == 0) return;
            float keepRange = KeepRange(ctx, list);
            if (!(keepRange > 0f)) return;
            Vector3 eye = unit.EyePosition - unit.Position;
            var awake = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e)).ToList();
            if (awake.Count == 0) return;
            // Keep a target: an enemy it can hit from here should stay in its range (and sight) there.
            // At most the four nearest (each spot tests their sight: 24 spots at most, FindRetreatPoint's 12 directions twice).
            var nearest = awake.OrderBy(e => HorizontalDistance(unit.Position, e.Position)).Take(4).ToList();
            var targets = nearest.Where(e => UnitCommand.IsUnitCloseEnough(e.Position, unit.Position, unit.Position + eye, keepRange * 0.95f, true, 0f)).ToList();
            if (targets.Count == 0) targets = nearest;
            Func<Vector3, bool> accept = p =>
                targets.Any(e => UnitCommand.IsUnitCloseEnough(e.Position, p, p + eye, keepRange * 0.95f, true, 0f)) &&
                !awake.Any(e => Threatens(e, unit, p));
            float worth = AllyWorth(ctx, unit);
            float dropHere = SpotDrop(ctx, unit, unit.Position);
            float inHere = IncomingDamage(ctx, unit, unit.Position);
            float move = ctx.MoveRange > 0f ? ctx.MoveRange : unit.CurrentSpeedMps * 3f;
            Candidate best = null;
            foreach (float share in new[] { 0.5f, 0.9f })
            {
                var point = FindRetreatPoint(ctx, move * share, accept);
                if (!point.HasValue) continue;
                float dropThere = SpotDrop(ctx, unit, point.Value);
                float inThere = IncomingDamage(ctx, unit, point.Value);
                float gain = ValueModel.RepositionGain(dropHere, dropThere, worth, inHere, inThere, RiskTolerance(ctx));
                if (!(gain > 0f)) continue;
                var risk = MoveRisk(ctx, point.Value, ownSafetyCounted: true);
                float value = gain * (1f - (risk?.StopChance ?? 0f)) - (risk?.Cost ?? 0f);
                if (value < MinQuickValue || best != null && value <= best.Value) continue;
                best = new Candidate
                {
                    Kind = CandidateKind.Move, Slot = ActionSlot.Move, Point = point, ApproachRadius = 0.3f, Value = value,
                    Label = $"Reposition before the turn ends (danger {inHere:0.#}->{inThere:0.#}, keeps an enemy within {keepRange:0.#} m)" +
                            (risk?.Tag ?? ""),
                    Key = "move:reposition", Risk = risk, RiskAt = point, PreRiskValue = gain
                };
            }
            if (best != null) list.Add(best);
        }

        /// <summary>
        /// ★ v0.3.70 F1: the range the unit attacks from: its ranged weapon's (when its policy lets it shoot), else the longest
        /// offensive ability it scored this decision, else Close range.
        /// </summary>
        private static float KeepRange(TacticalContext ctx, System.Collections.Generic.List<Candidate> list)
        {
            var unit = ctx.Unit;
            float range = 0f;
            try
            {
                if (ctx.HasRangedWeapon && ctx.PureMageWeapon)
                {
                    var enemy = ctx.Enemies.FirstOrDefault(e => !CombatMath.IsDisabled(e));
                    var est = enemy == null ? null : CombatMath.EstimateWeaponAttack(unit, enemy, false);
                    if (est != null && est.IsRanged) range = est.Reach;
                }
                if (range <= 0f)
                    foreach (var c in list)
                        if (c.Kind == CandidateKind.Ability && c.Offensive && c.Ability != null && c.Target != null && unit.IsEnemy(c.Target))
                            range = Math.Max(range, c.Ability.GetApproachDistance(c.Target));
            }
            catch { }
            return range > 0f ? range : CasterApproachRadius;
        }
    }
}
