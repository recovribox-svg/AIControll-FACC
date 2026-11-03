// ★ Test[4]: protect a clearly committed ally arrival from a new harmful lasting control circle.
// ★ v0.3.27 (test3 log): also its walk there, and at any height inside the game's 100 m cylinder.
using System;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <param name="harmsAllies">The existing harmfulArea result, including native Selective/target-side rules.</param>
        /// <param name="wouldBeCaught">The existing per-recipient condition, existing-buff and immunity predicate.</param>
        private static UnitEntityData CommittedApproachCaught(TacticalContext ctx, AbilityData ability,
            AbilityProfile profile, Vector3 areaCenter, bool harmsAllies, Func<UnitEntityData, bool> wouldBeCaught,
            out string explanation)
        {
            explanation = null;
            if (!CombatMemory.IsRealTime || ctx?.Unit == null || !ctx.Unit.IsInCombat ||
                !(ctx.Settings?.AvoidFriendlyFire ?? false) || !harmsAllies || wouldBeCaught == null ||
                ability?.Blueprint == null || profile == null || !profile.IsAoE || profile.IsProjectileCone ||
                (profile.ControlStrength != ControlStrength.Soft && profile.ControlStrength != ControlStrength.Hard)) return null;
            try
            {
                // One native fixed-position spawn only. Conditional/multiple/referenced spawns, attached circles,
                // cones, walls and custom delivery keep existing appraisal; none becomes an invented safety proof.
                var run = ability.Blueprint.GetComponent<AbilityEffectRunAction>();
                var actions = run?.Actions?.Actions;
                if (run?.GetType() != typeof(AbilityEffectRunAction) || actions?.Length != 1 ||
                    actions[0]?.GetType() != typeof(ContextActionSpawnAreaEffect)) return null;
                var spawn = (ContextActionSpawnAreaEffect)actions[0];
                var area = spawn.AreaEffect;
                var areas = profile.SpawnedAreas.Take(2).ToArray();
                if (spawn.OnUnit || spawn.DurationValue == null || area == null || areas.Length != 1 || areas[0] != area ||
                    area.Shape != AreaEffectShape.Cylinder || !area.CanTargetAllies) return null;
                // A literal zero-duration spawn is transient. Variable native duration/rank expressions retain the
                // existing profile's lasting-area interpretation without recalculating spell parameters or ranks.
                var duration = spawn.DurationValue;
                if (duration.BonusValue == null || duration.DiceCountValue == null ||
                    duration.BonusValue.IsZero && (duration.DiceType == DiceType.Zero || duration.DiceCountValue.IsZero)) return null;
                float radius = Math.Min(area.Size.Meters, profile.AoERadius);
                if (!(radius > 0f) || float.IsInfinity(radius)) return null;
                var party = Game.Instance?.Player?.PartyAndPets;
                if (party == null) return null;
                foreach (var ally in ctx.Allies)
                {
                    if (ally == null || ally == ctx.Unit || !party.Contains(ally) || IsSummon(ally) || !ally.IsInCombat ||
                        !ally.IsPlayerFaction || !ally.IsDirectlyControllable || ally.Descriptor?.State?.IsConscious != true ||
                        ally.Commands?.Raw == null) continue;
                    // Currently caught allies retain the old current-position friendly-fire rule.
                    float currentDistance = Vector3.Distance(ally.Position, areaCenter);
                    if (float.IsNaN(currentDistance) || float.IsInfinity(currentDistance) ||
                        currentDistance <= radius + ally.Corpulence) continue;
                    int inspected = 0;
                    foreach (var command in ally.Commands.Raw)
                    {
                        if (++inspected > 8) break; // Native slots are small; do not scan an unbounded custom queue.
                        if (command?.GetType() != typeof(UnitAttack) || command.Executor != ally || !ally.Commands.ContainsOrQueued(command) ||
                            command.IsFinished || command.IsStarted || command.IsActed || command.Cutscene) continue;
                        var attack = (UnitAttack)command;
                        var target = attack.Target;
                        if (target == null || !ally.IsEnemy(target) || target.Descriptor?.State?.IsDead != false) continue;
                        var point = attack.ApproachPoint;
                        float approach = attack.ApproachRadius;
                        // ★ v0.3.27 (test3 log): the game's cylinder is 100 m tall (CommittedApproachSafety.CylinderHalfHeight);
                        // exactly equal elevation was required, and Seelah's goal stood 0.44 m above the Grease point.
                        if (float.IsNaN(point.y - areaCenter.y) ||
                            Math.Abs(point.y - areaCenter.y) > CommittedApproachSafety.CylinderHalfHeight) continue;
                        bool fullyInside = CommittedApproachSafety.FullyInside(Vector3.Distance(point, areaCenter), approach,
                                                                              ally.Corpulence, radius);
                        // ★ v0.3.27 (test3 log): or its walk to where it stops meets the circle (it walks in or ends in it).
                        bool walksIn = !fullyInside && CommittedApproachSafety.WalkMeetsCircle(ally.Position.x, ally.Position.z,
                            point.x, point.z, approach, ally.Corpulence, areaCenter.x, areaCenter.z, radius);
                        if (!fullyInside && !walksIn) continue;
                        // An unstarted attack can merely wait for cooldown while already in reach. Native distance
                        // (with sight disabled) proves this one still needs an approach; no new path or rule is run.
                        if (UnitCommand.IsUnitCloseEnough(point, ally.Position, ally.EyePosition, approach, false, 0f) ||
                            !attack.ShouldUnitApproach) continue;
                        if (!profile.AreaReaches(ability, ally) || !wouldBeCaught(ally)) continue;
                        explanation = fullyInside
                            ? $"{ally.CharacterName} is already approaching {target.CharacterName}; " +
                              $"its full native arrival/body disk ({approach:F1}m + {ally.Corpulence:F1}m) " +
                              $"lies inside the new {radius:F1}m control circle"
                            : $"{ally.CharacterName} is already approaching {target.CharacterName}; " +
                              $"its walk to where it stops ({approach:F1}m short of it) meets the new {radius:F1}m control circle";
                        return ally;
                    }
                }
            }
            catch (Exception ex)
            {
                ReportOnce($"committed-approach|{ability.Blueprint.AssetGuid}|{ex.GetType().Name}",
                    $"[Walk] {ability.Name}: committed-ally check unavailable ({ex.GetType().Name}); retaining existing friendly-fire appraisal");
            }
            return null;
        }
    }
}
