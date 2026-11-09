// ★ v0.3.28 test 15 (author-approved movement change: "price standing inside a swarm as danger"): where a swarm's round
// strikes, by the game's rules (decompile, Mandragora swarm blueprints).
//   - A swarm carries an area (its aura buff's AddAreaEffect: MandragoraSwarmDamageArea, a 5-foot cylinder that moves with
//     it). Its AbilityAreaEffectBuff gives every enemy inside an effect buff whose Activated actions add that unit to the
//     swarm's targets (ContextActionSwarmTarget -> UnitPartSwarmAttack.TargetsList) and whose Deactivated actions remove it.
//   - Each round (the swarm buff's NewRound: every 6 s of game time; turn-based, at a turn start once the time has passed)
//     ContextActionSwarmAttack strikes every target (ContextActionOnSwarmTargets), but only while one of them is in the
//     swarm's inner circle (ContextSwarmHasEnemiesInInnerCircle: UnitPartSwarmAttack.HasEnemiesInInnerCircle, closer than
//     1 m plus half its body).
//   - Not every swarm waits for that: the Raven swarm and Creeping Doom run ContextActionOnSwarmTargets straight from NewRound
//     (the inner-circle condition only plays their animation) and strike everyone in their space each round.
// So a party member is struck where it stands inside that area (while it or another target is in the inner circle, for a
// swarm whose strike waits for one); one step out of the area and the round cannot reach it before the swarm moves. Until test 14 the swarm's danger counted
// wherever it could walk this round, shared among everyone there, like a melee enemy without a weapon.
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;
using CompanionAI_Pathfinder.Analysis;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private static readonly Dictionary<BlueprintAbilityAreaEffect, bool> s_SwarmTargetAreas =
            new Dictionary<BlueprintAbilityAreaEffect, bool>();
        private static readonly Dictionary<UnitEntityData, SwarmSpace> s_SwarmAreas = new Dictionary<UnitEntityData, SwarmSpace>();
        private static readonly Dictionary<UnitEntityData, AreaEffectEntityData> s_SwarmLiveAreas = new Dictionary<UnitEntityData, AreaEffectEntityData>();
        private static readonly Dictionary<(UnitEntityData, UnitEntityData), float> s_SwarmRoundHp =
            new Dictionary<(UnitEntityData, UnitEntityData), float>();
        private static readonly Dictionary<UnitEntityData, float> s_SwarmMeanHp = new Dictionary<UnitEntityData, float>();
        /// <summary>The swarm's strike waits for a target in its inner circle (ContextSwarmHasEnemiesInInnerCircle).</summary>
        private static readonly Dictionary<UnitEntityData, bool> s_SwarmInnerCircle = new Dictionary<UnitEntityData, bool>();
        private static int s_SwarmFrame = -1;

        /// <summary>
        /// Native registered round area, or the verified near-contact Standard cast. Actor-dependent state is read once
        /// per decision/frame; absence is cached too. Null means the bounded contracts do not describe this unit.
        /// </summary>
        internal static SwarmSpace SwarmSpaceOf(UnitEntityData unit)
        {
            if (unit?.View == null) return null;
            try
            {
                RefreshSwarms();
                var state = SwarmStateOf(unit);
                if (state.SpaceRead) return s_SwarmAreas.TryGetValue(unit, out var ready) ? ready : null;
                state.SpaceRead = true;
                var space = state.Space;
                space.Area = null;
                var rounds = state.Facts.Rounds;
                if (s_SwarmLiveAreas.TryGetValue(unit, out var area) && rounds.Count > 0)
                {
                    space.Area = area; space.Cast = null;
                    bool? gated = null;
                    foreach (var round in rounds) gated = BothGated(gated, StrikeWaitsForInnerCircle(round.Actions, false, 0));
                    s_SwarmInnerCircle[unit] = gated ?? true;
                }
                else space.Cast = FindSwarmCast(unit, state);
                if (space.Area == null && space.Cast == null) return null;
                s_SwarmAreas[unit] = space;
                return space;
            }
            catch (Exception ex)
            {
                if (CombatMemory.FirstThisFight("swarm-read|" + unit.UniqueId))
                    Main.Verbose($"[Swarm] {unit.CharacterName}: unsupported live state ({ex.GetType().Name})");
                return null;
            }
        }

        private static void RefreshSwarms()
        {
            int frame = Time.frameCount;
            if (frame == s_SwarmFrame) return;
            s_SwarmFrame = frame;
            s_SwarmAreas.Clear();
            s_SwarmLiveAreas.Clear();
            s_SwarmInnerCircle.Clear();
            s_SwarmRoundHp.Clear();
            s_SwarmMeanHp.Clear();
            try
            {
                var areas = Game.Instance?.State?.AreaEffects;
                if (areas == null) return;
                foreach (var aoe in areas)
                {
                    if (aoe == null || !aoe.IsInGame || aoe.IsEnded || aoe.View == null) continue;
                    if (!IsSwarmTargetArea(aoe.Blueprint)) continue;
                    var swarm = aoe.Context?.MaybeCaster;
                    if (swarm == null || swarm.Descriptor?.State?.IsDead != false) continue;
                    // Preserve the existing registered-aura choice; cast-only units are discovered lazily per requested enemy.
                    if (!s_SwarmLiveAreas.TryGetValue(swarm, out var had) ||
                        (aoe.Blueprint?.Size.Meters ?? 0f) > (had.Blueprint?.Size.Meters ?? 0f)) s_SwarmLiveAreas[swarm] = aoe;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Swarm] reading swarm areas: {ex.Message}");
            }
        }

        /// <summary>
        /// The area adds the units it gives its buff to a swarm's targets (an AbilityAreaEffectBuff whose buff runs
        /// ContextActionSwarmTarget when it starts). Once per blueprint; ProfileAudit SWARM-AREA reads it on the dumps.
        /// </summary>
        internal static bool IsSwarmTargetArea(BlueprintAbilityAreaEffect blueprint)
        {
            if (blueprint == null) return false;
            if (s_SwarmTargetAreas.TryGetValue(blueprint, out bool known)) return known;
            bool swarm = false;
            try
            {
                foreach (var area in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<
                             Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectBuff>(blueprint))
                {
                    var buff = area.Buff;
                    if (buff == null) continue;
                    foreach (var actions in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<
                                 Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>(buff))
                    {
                        var started = actions.Activated?.Actions;
                        if (started == null) continue;
                        foreach (var action in started)
                            if (action is ContextActionSwarmTarget target && !target.Remove) swarm = true;
                    }
                }
            }
            catch { }
            s_SwarmTargetAreas[blueprint] = swarm;
            return swarm;
        }

        /// <summary>
        /// Whether every way <paramref name="list"/> reaches the swarm's targets (ContextActionOnSwarmTargets) runs only while
        /// one of them is in its inner circle (a Conditional that requires ContextSwarmHasEnemiesInInnerCircle); null when it
        /// strikes nobody. An Or, a negated condition or the other branch does not count as waiting (the larger danger).
        /// </summary>
        internal static bool? StrikeWaitsForInnerCircle(Kingmaker.ElementsSystem.ActionList list, bool gated, int depth)
        {
            if (list?.Actions == null || depth > 8) return null;
            bool? result = null;
            foreach (var action in list.Actions)
            {
                switch (action)
                {
                    case ContextActionOnSwarmTargets _:
                        result = BothGated(result, gated);
                        break;
                    case Kingmaker.Assets.UnitLogic.Mechanics.Actions.ContextActionSwarmAttack attack:
                        result = BothGated(result, StrikeWaitsForInnerCircle(attack.AttackActions, gated, depth + 1));
                        break;
                    case Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional conditional:
                    {
                        var checker = conditional.ConditionsChecker;
                        bool inner = checker?.Conditions != null &&
                                     (checker.Operation == Kingmaker.ElementsSystem.Operation.And || checker.Conditions.Length == 1) &&
                                     Array.Exists(checker.Conditions, c => c is Kingmaker.UnitLogic.Mechanics.Conditions.ContextSwarmHasEnemiesInInnerCircle && !c.Not);
                        result = BothGated(result, StrikeWaitsForInnerCircle(conditional.IfTrue, gated || inner, depth + 1));
                        result = BothGated(result, StrikeWaitsForInnerCircle(conditional.IfFalse, gated, depth + 1));
                        break;
                    }
                }
            }
            return result;
        }

        private static bool? BothGated(bool? a, bool? b) => a == null ? b : b == null ? a : a.Value && b.Value;

        /// <summary>
        /// <paramref name="swarm"/>'s round strikes <paramref name="who"/> standing at <paramref name="at"/>: inside its
        /// area (the game's shape test, bodies included) - for a swarm whose strike waits for it, while <paramref name="who"/>
        /// or another of its targets in <paramref name="party"/> (each where <paramref name="positionOf"/> puts it) is in the
        /// swarm's inner circle.
        /// </summary>
        internal static bool SwarmStrikes(UnitEntityData swarm, SwarmSpace space,
                                          UnitEntityData who, Vector3 at, Func<UnitEntityData, Vector3> positionOf,
                                          IReadOnlyList<UnitEntityData> party)
        {
            if (swarm == null || space == null || who == null) return false;
            try
            {
                if (space.Cast != null) return space.Cast.Covers(who, at, positionOf, party);
                var area = space.Area;
                if (!AoeDangerAnalyzer.CoversPoint(area, at, who.Corpulence)) return false;
                if (!(s_SwarmInnerCircle.TryGetValue(swarm, out bool waits) && waits)) return true;
                if (InSwarmInnerCircle(swarm, at, who.Corpulence)) return true;
                var targets = swarm.Get<Kingmaker.UnitLogic.Parts.UnitPartSwarmAttack>()?.TargetsList;
                if (targets == null) return false;
                foreach (var other in targets)
                {
                    if (other?.View == null || other == who) continue;
                    var p = positionOf != null ? positionOf(other) : other.Position;
                    if (InSwarmInnerCircle(swarm, p, other.Corpulence) && AoeDangerAnalyzer.CoversPoint(area, p, other.Corpulence))
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>The swarm's strike waits for one of its targets at its centre (read with its space, once a frame).</summary>
        internal static bool SwarmWaitsForCentre(UnitEntityData swarm) =>
            swarm != null && s_SwarmInnerCircle.TryGetValue(swarm, out bool waits) && waits;

        /// <summary>UnitPartSwarmAttack.HasEnemiesInInnerCircle for one body: closer than 1 m plus half of it.</summary>
        private static bool InSwarmInnerCircle(UnitEntityData swarm, Vector3 point, float corpulence) =>
            Owlcat.Runtime.Core.Utils.GeometryUtils.MechanicsDistance(swarm.Position, point) < 1f + corpulence / 2f;

        /// <summary>
        /// What one native round or Standard cast is worth on <paramref name="victim"/>, in hit points, once per
        /// decision/frame and pair. The cast uses normal native-context damage/saves/control, not the aura action walker.
        /// </summary>
        internal static float SwarmRoundHp(UnitEntityData swarm, UnitEntityData victim)
        {
            if (swarm == null || victim == null) return 0f;
            RefreshSwarms();
            var key = (swarm, victim);
            if (s_SwarmRoundHp.TryGetValue(key, out float hp)) return hp;
            var space = SwarmSpaceOf(swarm);
            hp = space?.Cast != null ? space.Cast.Consequence(victim).Harm : AfflictionHp(SwarmRoundAffliction(swarm, victim), victim);
            s_SwarmRoundHp[key] = hp;
            return hp;
        }

        /// <summary>
        /// How hard <paramref name="swarm"/>'s round is on <paramref name="victim"/> against the rest of
        /// <paramref name="party"/> (ValueModel.SwarmVictimFactor): the swarm's Threat is its round on its likely victim, and a
        /// unit with a good Fortitude save or immune to its poison takes less of it. The mean once a frame per swarm.
        /// </summary>
        internal static float SwarmVictimFactor(UnitEntityData swarm, UnitEntityData victim, IEnumerable<UnitEntityData> party)
        {
            if (swarm == null || victim == null) return 1f;
            RefreshSwarms();
            if (!s_SwarmMeanHp.TryGetValue(swarm, out float mean))
            {
                float sum = 0f;
                int count = 0;
                if (party != null)
                    foreach (var ally in party)
                    {
                        if (ally?.Descriptor?.State?.IsConscious != true) continue;
                        sum += SwarmRoundHp(swarm, ally);
                        count++;
                    }
                mean = count > 0 ? sum / count : 0f;
                s_SwarmMeanHp[swarm] = mean;
            }
            float own = SwarmRoundHp(swarm, victim);
            var cast = SwarmSpaceOf(swarm)?.Cast;
            if (cast != null && !(own > 0f)) return cast.Consequence(victim).KnownHarm ? 0f : 1f;
            return ValueModel.SwarmVictimFactor(own, mean);
        }
    }
}
