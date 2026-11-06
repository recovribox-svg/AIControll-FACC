// ★ v0.3.28 test 15 (author-approved movement change: "price standing inside a swarm as danger"). Until now a swarm's danger
// at a spot was that of a melee enemy without a weapon: its whole Threat wherever it could walk this round, shared among the
// party members there - standing in its space cost no more than standing beside it, and nothing walked a unit out of it.
// Now (the game's rule: CombatMath.Swarms.cs):
//   - the danger at a spot (IncomingParts, StandingDropChance) counts a swarm's round in full on everyone it strikes there
//     (inside its space while one of its targets is at its centre), not shared out; outside its space but within its walk,
//     half of it (ValueModel.SwarmShare), shared among those it could walk onto;
//   - so every walk prices ending in a swarm (MoveRisk's exposure, tagged "[in X's swarm]"), an approach looks for a spot in
//     reach of its target clear of the swarm (PlanApproach), and a unit the swarm strikes is offered a step or a short move
//     out of its space (AddOutOfSwarmCandidate), worth the lower danger there less what the move costs and gives up.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>How far a swarm walks onto a party member this round (IncomingParts' zone for a melee enemy).</summary>
        private static float SwarmZone(UnitEntityData swarm)
        {
            try { return swarm.CurrentSpeedMps * 3f + 2f + swarm.Corpulence; }
            catch { return 11f; }
        }

        /// <summary>
        /// <paramref name="swarm"/>'s part of the danger to <paramref name="who"/> at <paramref name="at"/> (IncomingParts): its
        /// Threat, all of it where its round strikes, else half of it shared among the party members within its walk
        /// (<paramref name="zone"/>), times how hard its round is on <paramref name="who"/> (CombatMath.SwarmVictimFactor).
        /// </summary>
        private static float SwarmPart(TacticalContext ctx, UnitEntityData swarm, AreaEffectEntityData area, UnitEntityData who,
                                       Vector3 at, float zone)
        {
            bool struck = CombatMath.SwarmStrikes(swarm, area, who, at, a => a == who ? at : a.Position, ctx.Allies);
            int sharedBy = struck ? 1 : Math.Max(1, ctx.Allies.Count(a => Vector3.Distance(swarm.Position, a == who ? at : a.Position) <= zone + a.Corpulence));
            return CombatMath.Threat(swarm) * ValueModel.SwarmShare(struck, true) *
                   CombatMath.SwarmVictimFactor(swarm, who, ctx.Allies) / sharedBy;
        }

        /// <summary>
        /// <paramref name="swarm"/>'s chance to come for <paramref name="who"/> at <paramref name="at"/> and drop it
        /// (StandingDropChance): certain where its round strikes, else half of it shared among those within its walk; the
        /// drop by its Threat, the fight's calibration and how hard its round is on <paramref name="who"/>. No hit weight: a
        /// swarm's round rolls no attack.
        /// </summary>
        private static (float chance, float drop) SwarmDrop(TacticalContext ctx, UnitEntityData swarm, AreaEffectEntityData area,
                                                            UnitEntityData who, Vector3 at, Func<UnitEntityData, Vector3> positionOf, float hp)
        {
            float chance;
            if (CombatMath.SwarmStrikes(swarm, area, who, at, positionOf, ctx.Allies)) chance = 1f;
            else
            {
                float zone = SwarmZone(swarm);
                if (Vector3.Distance(swarm.Position, at) > zone + who.Corpulence) return (0f, 0f);
                int sharedBy = Math.Max(1, ctx.Allies.Count(a => Vector3.Distance(swarm.Position, positionOf(a)) <= zone + a.Corpulence));
                chance = ValueModel.SwarmShare(false, true) / sharedBy;
            }
            float threat = CombatMath.Threat(swarm) * ctx.DangerScale * CombatMath.SwarmVictimFactor(swarm, who, ctx.Allies);
            return (chance, RiskMath.RoundDropChance(hp, threat));
        }

        /// <summary>
        /// The name of a swarm whose round strikes the unit at <paramref name="at"/> (the rest of the party where it stands);
        /// null when none does. Cheap when no swarm is in the fight (CombatMath.SwarmAreaOf is a lookup).
        /// </summary>
        private static string StruckBySwarm(TacticalContext ctx, Vector3 at)
        {
            var unit = ctx.Unit;
            foreach (var enemy in ctx.Enemies)
            {
                var area = CombatMath.SwarmAreaOf(enemy);
                if (area == null || CombatMath.IsDisabled(enemy)) continue;
                if (CombatMath.SwarmStrikes(enemy, area, unit, at, null, ctx.Allies)) return enemy.CharacterName;
            }
            return null;
        }

        /// <summary>
        /// What being struck by the swarms at <paramref name="at"/> costs the unit (PlanApproach's yardstick for a spot clear
        /// of them): the chance their rounds drop it before its next turn (as StandingDropChance counts them), times its worth
        /// and its role's tolerance.
        /// </summary>
        private static float SwarmStayPrice(TacticalContext ctx, Vector3 at)
        {
            var unit = ctx.Unit;
            try
            {
                float hp = Math.Max(1f, CombatMath.HpBeforeDown(unit));
                var threats = new List<(float chance, float drop)>();
                foreach (var enemy in ctx.Enemies)
                {
                    var area = CombatMath.SwarmAreaOf(enemy);
                    if (area == null || CombatMath.IsDisabled(enemy)) continue;
                    if (!CombatMath.SwarmStrikes(enemy, area, unit, at, null, ctx.Allies)) continue;
                    threats.Add(SwarmDrop(ctx, enemy, area, unit, at, a => a == unit ? at : a.Position, hp));
                }
                return threats.Count == 0 ? 0f : ValueModel.CombinedDrop(threats) * AllyWorth(ctx, unit) * RiskTolerance(ctx);
            }
            catch { return 0f; }
        }

        /// <summary>Whether <paramref name="unit"/>'s melee weapon reaches <paramref name="target"/> from <paramref name="from"/>
        /// (the reach StrikeShareAt reads: the weapon's range and both bodies).</summary>
        private static bool ReachesFrom(UnitEntityData unit, UnitEntityData target, Vector3 from)
        {
            try
            {
                var weapon = unit.GetThreatHandMelee()?.Weapon;
                if (weapon == null || target == null) return false;
                float reach = Math.Max(1f, weapon.AttackRange.Meters) + unit.Corpulence + target.Corpulence;
                return HorizontalDistance(from, target.Position) <= reach;
            }
            catch { return false; }
        }

        /// <summary>One line a fight per swarm: what its round is read as and how far its space reaches.</summary>
        private static void NoteSwarm(UnitEntityData swarm, AreaEffectEntityData area, UnitEntityData victim)
        {
            if (!CombatMemory.FirstThisFight("swarm|" + swarm.UniqueId)) return;
            try
            {
                var round = CombatMath.SwarmRoundAffliction(swarm, victim);
                float radius = area.Blueprint?.Size.Meters ?? 0f;
                Main.Log($"[Swarm] {swarm.CharacterName}: its round strikes every party member in its space ({radius:0.#} m plus the " +
                         $"body{(CombatMath.SwarmWaitsForCentre(swarm) ? ", while one is at its centre" : "")}) - on {victim.CharacterName} " +
                         $"{CombatMath.SwarmRoundHp(swarm, victim):0.#} HP " +
                         $"worth ({round.HitPoints:0.#} damage{(round.Control > 0.05f ? $", control {round.Control:0.#} rounds" : "")}" +
                         $"{string.Concat(round.Points.Where(p => p.Value > 0.05f).Select(p => $", {p.Key} {p.Value:0.#}"))}); " +
                         $"half of it within its walk");
            }
            catch { }
        }

        /// <summary>
        /// ★ v0.3.28 test 15 (author-approved): at any HP, a step (turn-based: a 5-foot step) or a short move out of the
        /// space of the swarms whose round strikes the unit where it stands. Worth how much less likely it is to drop
        /// before its next turn there (the swarm-aware danger of <see cref="IncomingParts"/>), less what the move costs
        /// (MoveRisk: attacks of opportunity, areas, what leaving costs the allies) and the best option it gives up - what
        /// it can still do from there stays (a weapon attack on a target still in its reach, a ranged attack or a spell;
        /// turn-based, nothing that walks first after a 5-foot step, nothing of the full round after a move). A fighter
        /// beating the swarm from inside stays while its attacks are worth more than the danger it leaves.
        /// </summary>
        private static void AddOutOfSwarmCandidate(TacticalContext ctx, List<Candidate> list, Func<Candidate, bool> kept)
        {
            var unit = ctx.Unit;
            bool realTime = CombatMemory.IsRealTime;
            bool canStep = !realTime && ctx.CanFiveFoot;
            List<(UnitEntityData swarm, AreaEffectEntityData area)> striking = null;
            foreach (var enemy in ctx.Enemies)
            {
                var area = CombatMath.SwarmAreaOf(enemy);
                if (area == null) continue;
                NoteSwarm(enemy, area, unit);   // once a fight per swarm, here: once a decision, not in the danger sums
                if (CombatMath.IsDisabled(enemy)) continue;
                if (!CombatMath.SwarmStrikes(enemy, area, unit, unit.Position, null, ctx.Allies)) continue;
                (striking ?? (striking = new List<(UnitEntityData, AreaEffectEntityData)>())).Add((enemy, area));
            }
            if (striking == null || !canStep && !ctx.HasMove) return;
            float dangerHere = IncomingDamage(ctx, unit, unit.Position);
            if (!(dangerHere > 0f)) return;
            float pHere = SpotDrop(ctx, unit, unit.Position);   // ★ v0.3.29 test 2 (R3)
            float worth = AllyWorth(ctx, unit);
            float best = list.Where(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && Affordable(ctx, c) && kept(c))
                             .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            var refused = realTime ? null : CombatMemory.ForTurn(unit).RefusedSteps;
            Func<Vector3, bool> clear = p => striking.All(s => !CombatMath.SwarmStrikes(s.swarm, s.area, unit, p, null, ctx.Allies)) &&
                                             (refused == null || refused.All(q => Vector3.Distance(p, q) > 0.5f));
            var distances = new List<(float distance, bool step)>();
            if (canStep && (refused == null || refused.Count < 3)) distances.Add((ctx.FiveFootRange * 0.9f, true));
            if (ctx.HasMove)
            {
                float range = ctx.MoveRange > 0f ? ctx.MoveRange : unit.CurrentSpeedMps * 3f;
                foreach (float d in new[] { 3f, 4.5f, 6f })
                    if (d <= range) distances.Add((d, false));
            }
            string names = string.Join(", ", striking.Select(s => s.swarm.CharacterName).Distinct().Take(2));
            Candidate chosen = null;
            float bestSeen = float.NegativeInfinity;
            foreach (var (distance, step) in distances)
            {
                var point = FindRetreatPoint(ctx, distance, clear);
                if (!point.HasValue) continue;
                var risk = MoveRisk(ctx, point.Value, ownSafetyCounted: true);
                float dangerThere = IncomingDamage(ctx, unit, point.Value);
                // By the role's tolerance, as walking in is priced (MoveRisk's exposure): a tank that walks into a swarm
                // for an attack worth more than the discounted danger does not step straight back out.
                float gain = (pHere - SpotDrop(ctx, unit, point.Value)) * worth * RiskTolerance(ctx);
                var p = point.Value;
                float keeps = list.Where(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && Affordable(ctx, c) && kept(c) &&
                                              (c.Kind != CandidateKind.WeaponAttack || ctx.HasRangedWeapon || ReachesFrom(unit, c.Target, p)) &&
                                              (realTime || !c.NeedsMove && !c.MoveFirst) &&
                                              (realTime || step || c.Slot != ActionSlot.FullRound))
                                  .Select(c => c.Value).DefaultIfEmpty(0f).Max();
                float givenUp = Math.Max(0f, best - keeps);
                float value = gain * (1f - (risk?.StopChance ?? 0f)) - (risk?.Cost ?? 0f) - givenUp;
                bestSeen = Math.Max(bestSeen, value);
                if (value < MinMainValue || (chosen != null && value <= chosen.Value)) continue;
                chosen = new Candidate
                {
                    Kind = CandidateKind.Move, Slot = step ? ActionSlot.Free : ActionSlot.Move, Point = point, ApproachRadius = 0.3f,
                    Value = value,
                    Label = $"{(step ? "5ft step" : "Move")} out of {names}'s swarm (danger {dangerHere:0.#}->{dangerThere:0.#})" +
                            (risk?.Tag ?? ""),
                    Key = step ? FiveFootStepKey + "#swarm" : "move:outofswarm",
                    Risk = risk, RiskAt = point, PreRiskValue = gain
                };
            }
            if (chosen != null) list.Add(chosen);
            else if (Main.VerboseOn && CombatMemory.FirstThisFight($"swarmstay|{unit.UniqueId}|{CombatMemory.Round}"))
                Main.Verbose($"[Swarm] {unit.CharacterName}: struck by {names}'s round where it stands (danger here {dangerHere:0.#}); " +
                             (float.IsNegativeInfinity(bestSeen) ? "no free spot out of its space within a step or a short move"
                                                                  : $"the best way out is worth {bestSeen:0.#}, not enough"));
        }
    }
}
