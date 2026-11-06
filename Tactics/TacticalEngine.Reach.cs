// ★ v0.3.28 test 7 (author-approved; fight 7 of the test-6 turn-based log): reach-aware danger and the step out of an
// unanswered reach. The Ferocious Schir swung its masterwork bardiche from inside Camellia's (non-Selective) Grease at
// Camellia and Seelah on the Grease's edge; neither could hit back without walking into the Grease. Healthy, Camellia was
// offered Demoralize (1.2) and nothing else - no move: stepping back was offered only below half HP, and was worth nothing
// even then, because the danger at a spot counted the Schir's full attack wherever it could walk this round. She ended her
// turn in its reach with her move and 5-foot step unused, and went down before Orvath killed it.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Controllers.Combat;
using Kingmaker.UnitLogic;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static int s_ReachFrame = -1;
        private static readonly Dictionary<string, float> HeldMemo = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> SingleShareMemo = new Dictionary<string, float>();

        private static void ReachMemoFrame()
        {
            int frame = Time.frameCount;
            if (frame == s_ReachFrame) return;
            s_ReachFrame = frame;
            HeldMemo.Clear();
            SingleShareMemo.Clear();
        }

        /// <summary>The chance the areas <paramref name="enemy"/> stands in hold it this round (once a frame).</summary>
        private static float HeldThisRound(UnitEntityData enemy)
        {
            ReachMemoFrame();
            if (HeldMemo.TryGetValue(enemy.UniqueId, out float held)) return held;
            held = AoeDangerAnalyzer.HeldInsideChance(enemy, out _);
            HeldMemo[enemy.UniqueId] = held;
            return held;
        }

        /// <summary>
        /// What one attack of <paramref name="enemy"/> on <paramref name="victim"/> is of its full attack, by the game's
        /// attack rule (CombatMath.EstimateWeaponAttack: a single attack after a move against the iterative and natural
        /// attacks of a full round). 1 when it cannot be read. Once a frame per enemy, against the first party member asked
        /// about: the share hardly depends on the target, and each reading runs the game's attack rule twice.
        /// </summary>
        private static float SingleAttackShare(UnitEntityData enemy, UnitEntityData victim)
        {
            ReachMemoFrame();
            string key = enemy.UniqueId;
            if (SingleShareMemo.TryGetValue(key, out float share)) return share;
            share = 1f;
            try
            {
                float full = CombatMath.EstimateWeaponAttack(enemy, victim, full: true)?.ExpectedDamage ?? 0f;
                float single = CombatMath.EstimateWeaponAttack(enemy, victim, full: false)?.ExpectedDamage ?? 0f;
                if (full > 0f && single > 0f) share = Mathf.Clamp(single / full, 0.05f, 1f);
            }
            catch { }
            SingleShareMemo[key] = share;
            return share;
        }

        /// <summary>
        /// The share of a melee <paramref name="enemy"/>'s round that reaches <paramref name="who"/> standing at
        /// <paramref name="at"/> (ValueModel.StrikeShare): all of it within its weapon's reach (bodies included) or a 5-foot
        /// step of it (TurnController.MetersOfFiveFootStep: the game's own AI steps and full-attacks), else one attack after a
        /// walk - each time only if an area it stands in does not hold it (its failed save). Turn-based, an enemy lying down
        /// stands up with its move action: one attack in its reach, none beyond. 1 for an enemy without a melee weapon.
        /// </summary>
        private static float StrikeShareAt(UnitEntityData enemy, UnitEntityData who, Vector3 at)
        {
            try
            {
                var weapon = enemy.GetThreatHandMelee()?.Weapon;
                if (weapon == null) return 1f;
                float reach = Math.Max(1f, weapon.AttackRange.Meters) + enemy.Corpulence + who.Corpulence;
                float distance = HorizontalDistance(enemy.Position, at);
                bool inReach = distance <= reach;
                bool prone = !CombatMemory.IsRealTime && (enemy.Descriptor?.State?.Prone?.Active ?? false);
                if (prone) return inReach ? SingleAttackShare(enemy, who) : 0f;
                if (inReach) return 1f;
                bool inStep = distance <= reach + TurnBased.Controllers.TurnController.MetersOfFiveFootStep;
                return ValueModel.StrikeShare(false, inStep, HeldThisRound(enemy), SingleAttackShare(enemy, who));
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// ★ v0.3.28 test 7: at any HP, a step (turn-based: a 5-foot step, no attack of opportunity) or a short move out of
        /// the reach of enemies that strike the unit where it stands while its own weapon reaches none of them (nor any
        /// other enemy: it is not holding a melee there). Not for a unit with a ranged weapon in hand. Worth what
        /// the withdrawal is worth - how much less likely the unit is to drop before its next turn there (the reach-aware
        /// danger of <see cref="IncomingParts"/>) - less what the move costs (MoveRisk: attacks of opportunity, areas, and what
        /// leaving costs the allies it covers, so a front-liner holding an enemy off a caster stays) and less the best option
        /// it gives up (a 5-foot step: everything that walks first; a move: the full round and the move action's options).
        /// If the enemy then leaves its area to follow, the unit's attacks on it are there again; walking in to hit it (the
        /// approach "into" an area) stays an option, priced as before.
        /// </summary>
        private static void AddOutOfReachCandidate(TacticalContext ctx, List<Candidate> list, Func<Candidate, bool> kept)
        {
            var unit = ctx.Unit;
            bool realTime = CombatMemory.IsRealTime;
            bool canStep = !realTime && ctx.CanFiveFoot;
            if (!canStep && !ctx.HasMove) return;
            // ★ v0.3.28 test 8 (test-7 logs): an archer's way out of melee is the step or back-off before its shot (1) above;
            // offered here too, Lann and Orvath walked off in real time between their own shots.
            if (ctx.HasRangedWeapon) return;
            List<UnitEntityData> unanswered;
            try
            {
                // ★ v0.3.28 test 8: "cannot hit it back" by the game's reach test on the unit's own weapon, not by the
                // options left this turn. Turn-based, Camellia attacked the Cultist Champion and then walked out of its reach
                // (an attack of opportunity) once her standard action was spent; real time, Seelah walked off the Corrupted
                // Mongrel she was beating between swings (no attack is listed while the swing's cooldown runs). A unit that
                // reaches any enemy where it stands holds that melee: what leaving costs it there (its next attacks, the walk
                // back) is not in this turn's options.
                if (ctx.Enemies.Any(e => !CombatMath.IsDisabled(e) && Threatens(unit, e, e.Position))) return;
                unanswered = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e) && e.GetThreatHandMelee() != null &&
                                                    Threatens(e, unit, unit.Position))
                                        .ToList();
            }
            catch { return; }
            if (unanswered.Count == 0) return;
            float hp = Math.Max(1f, CombatMath.HpBeforeDown(unit));
            float dangerHere = IncomingDamage(ctx, unit, unit.Position);
            if (!(dangerHere > 0f)) return;
            float pHere = RiskMath.RoundDropChance(hp, dangerHere);
            float worth = AllyWorth(ctx, unit);
            float best = list.Where(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && Affordable(ctx, c) && kept(c))
                             .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            var refused = realTime ? null : CombatMemory.ForTurn(unit).RefusedSteps;
            Func<Vector3, bool> outOfReach = p => unanswered.All(e => !Threatens(e, unit, p)) &&
                                                  (refused == null || refused.All(q => Vector3.Distance(p, q) > 0.5f));

            var distances = new List<(float distance, bool step)>();
            if (canStep && (refused == null || refused.Count < 3)) distances.Add((ctx.FiveFootRange * 0.9f, true));
            if (ctx.HasMove)
            {
                float range = ctx.MoveRange > 0f ? ctx.MoveRange : unit.CurrentSpeedMps * 3f;
                foreach (float d in new[] { 3f, 4.5f, 6f })
                    if (d <= range) distances.Add((d, false));
            }
            Candidate chosen = null;
            string names = string.Join(", ", unanswered.Select(e => e.CharacterName).Distinct().Take(2));
            float bestSeen = float.NegativeInfinity;
            foreach (var (distance, step) in distances)
            {
                var point = FindRetreatPoint(ctx, distance, outOfReach);
                if (!point.HasValue) continue;
                var risk = MoveRisk(ctx, point.Value, ownSafetyCounted: true);
                float pThere = RiskMath.RoundDropChance(hp, IncomingDamage(ctx, unit, point.Value));
                float gain = (pHere - pThere) * worth;
                // What is left after the move: no melee attack (out of reach of where it stood), and turn-based nothing
                // that walks first after a 5-foot step, nothing of the full round after a move.
                float keeps = list.Where(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && Affordable(ctx, c) && kept(c) &&
                                              !(c.Kind == CandidateKind.WeaponAttack && !ctx.HasRangedWeapon) &&
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
                    Label = $"{(step ? "5ft step" : "Move")} out of {names}'s reach (danger {dangerHere:0.#}->" +
                            $"{IncomingDamage(ctx, unit, point.Value):0.#})" + (risk?.Tag ?? ""),
                    Key = step ? FiveFootStepKey + "#reach" : "move:outofreach",
                    Risk = risk, RiskAt = point, PreRiskValue = gain
                };
            }
            if (chosen != null) list.Add(chosen);
            // Why it stays where it is struck, once a round (the review of the next play-test needs it).
            else if (Main.VerboseOn && CombatMemory.FirstThisFight($"reach|{unit.UniqueId}|{CombatMemory.Round}"))
                Main.Verbose($"[Reach] {unit.CharacterName}: in the reach of {names}, which its weapon does not reach (danger here {dangerHere:0.#}); " +
                             (float.IsNegativeInfinity(bestSeen) ? "no free spot out of its reach within a step or a short move"
                                                                  : $"the best way out is worth {bestSeen:0.#}, not enough"));
        }
    }
}
