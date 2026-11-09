using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Designers;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Owlcat.Runtime.Core.Utils;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        internal sealed class SwarmSpace
        {
            internal AreaEffectEntityData Area;
            internal SwarmCast Cast;
        }

        private static readonly FieldInfo SwarmCastCondition = typeof(AbilityTargetsAround).GetField("m_Condition", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SwarmCastSpread = typeof(AbilityTargetsAround).GetField("m_SpreadSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SwarmCastDead = typeof(AbilityTargetsAround).GetField("m_IncludeDead", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Dictionary<BlueprintAbility, AbilityTargetsAround> s_SwarmCastBlueprints = new Dictionary<BlueprintAbility, AbilityTargetsAround>();

        /// <summary>Bounded native contract of the supplied Rat/Bhoga casts. The selector is centred on its unit target,
        /// not on the caster. Do not turn arbitrary swarm-looking abilities or unknown filters into periodic auras.</summary>
        internal static AbilityTargetsAround ReadSwarmCastBlueprint(BlueprintAbility bp)
        {
            if (bp == null) return null;
            if (s_SwarmCastBlueprints.TryGetValue(bp, out var known)) return known;
            AbilityTargetsAround result = null;
            if (bp.Type == AbilityType.Special && bp.Range == AbilityRange.Custom && bp.CustomRange.Meters < 0f &&
                bp.ActionType == Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Standard && !bp.IsFullRoundAction &&
                bp.CanTargetEnemies && !bp.CanTargetFriends && !bp.CanTargetSelf && !bp.CanTargetPoint &&
                bp.TargetRestrictions.Length == 0 && bp.StickyTouch == null && bp.GetComponent<AbilityResourceLogic>() == null &&
                bp.GetComponent<AbilityDeliverProjectile>() == null)
            {
                var around = bp.GetComponent<AbilityTargetsAround>();
                var effect = bp.GetComponent<AbilityEffectRunAction>();
                var condition = around == null ? null : SwarmCastCondition?.GetValue(around) as ConditionsChecker;
                int selectors = 0, effects = 0;
                foreach (var component in bp.ComponentsArray)
                {
                    if (component is Kingmaker.UnitLogic.Abilities.Components.Base.AbilitySelectTarget) selectors++;
                    if (component is AbilityEffectRunAction) effects++;
                }
                if (selectors == 1 && effects == 1 && around?.GetType() == typeof(AbilityTargetsAround) &&
                    effect?.GetType() == typeof(AbilityEffectRunAction) && around.Targets == TargetType.Enemy && around.AoERadius.Meters > 0f &&
                    condition != null && !condition.HasConditions && SwarmCastDead?.GetValue(around) is bool dead && !dead &&
                    SwarmCastSpread?.GetValue(around) is Kingmaker.Utility.Feet spread && spread.Meters == 0f &&
                    effect?.Actions?.Actions != null && Array.Exists(effect.Actions.Actions, a => a is ContextActionDealDamage))
                    result = around;
            }
            s_SwarmCastBlueprints[bp] = result;
            return result;
        }

        private static SwarmCast FindSwarmCast(UnitEntityData unit, SwarmState state)
        {
            if (!state.Facts.Body) return null;
            AbilityData found = null; AbilityTargetsAround selector = null;
            foreach (var fact in unit.Abilities.Enumerable)
            {
                var data = fact?.Data;
                if (fact?.Active != true || data?.Blueprint == null) continue;
                var around = ReadSwarmCastBlueprint(data.Blueprint);
                if (around == null || data.Range != AbilityRange.Custom || data.TargetAnchor != AbilityTargetAnchor.Unit ||
                    data.GetDeliverBlueprint(false) != data.Blueprint || !data.IsAvailableForCast) continue;
                if (found != null && !ReferenceEquals(found, data)) return null; // Ambiguous delivery contract; generic model remains.
                found = data; selector = around;
            }
            if (found == null) return null;
            var cast = state.KnownCast;
            if (cast == null || !ReferenceEquals(cast.Ability, found)) state.KnownCast = cast = new SwarmCast(found, selector);
            cast.Reset();
            return cast;
        }

        internal sealed class SwarmCast
        {
            internal readonly AbilityData Ability;
            internal readonly AbilityTargetsAround Selector;
            internal readonly AbilityProfile Profile;
            private readonly Dictionary<UnitEntityData, CastVictim> Victims = new Dictionary<UnitEntityData, CastVictim>();
            private readonly Dictionary<(Vector3, Vector3), bool> Sight = new Dictionary<(Vector3, Vector3), bool>();
            private readonly Dictionary<(UnitEntityData, Vector3), bool> Anchors = new Dictionary<(UnitEntityData, Vector3), bool>();
            private readonly bool NeedCastSight;
            private int Generation;
            internal SwarmCast(AbilityData ability, AbilityTargetsAround selector)
            {
                Ability = ability; Selector = selector; Profile = AbilityProfile.Get(ability);
                NeedCastSight = ability.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.Base.ILineOfSightIgnore>() == null;
            }
            internal void Reset() { unchecked { Generation++; } Sight.Clear(); Anchors.Clear(); }

            internal sealed class CastVictim
            {
                internal bool Affected, Primary, OwnBurst, HarmRead, KnownHarm;
                internal int Stamp = -1;
                internal float Reach, Body, Damage, Extra;
                internal Vector3 EyeOffset;
                internal float Harm => Damage + Extra;
            }

            internal CastVictim Victim(UnitEntityData who)
            {
                if (!Victims.TryGetValue(who, out var v)) Victims[who] = v = new CastVictim();
                if (v.Stamp == Generation) return v;
                v.Stamp = Generation; v.Affected = v.Primary = v.OwnBurst = v.HarmRead = v.KnownHarm = false;
                v.Damage = v.Extra = v.Reach = v.Body = 0f;
                try
                {
                    var caster = Ability.Caster.Unit;
                    if (who?.View == null || caster?.View == null || who.Descriptor.State.IsDead ||
                        who.Descriptor.State.Features.IsUntargetable || !caster.IsEnemy(who) ||
                        (!who.IsPlayerFaction && !caster.CanAttack(who))) return v;
                    float body = who.Corpulence;
                    Vector3 eye = who.EyePosition - who.Position;
                    bool primary = Ability.CanTarget(new Kingmaker.Utility.TargetWrapper(who));
                    float reach = primary ? Ability.GetApproachDistance(who) : 0f; // Native negative custom range + both native bodies; no guessed radius.
                    v.Body = body; v.EyeOffset = eye; v.Primary = primary; v.Reach = reach; v.Affected = true;
                    v.OwnBurst = GeometryUtils.MechanicsDistance(Vector3.zero, eye) <= Selector.AoERadius.Meters + body;
                }
                catch { /* Missing native target data stays unclassified for this decision. */ }
                return v;
            }

            private bool ClearSight(Vector3 from, Vector3 to)
            {
                var key = (from, to);
                if (Sight.TryGetValue(key, out bool known)) return known;
                bool clear = GameHelper.CheckLOS(from, to);
                Sight[key] = clear;
                return clear;
            }

            internal bool CanAnchor(UnitEntityData anchor, Vector3 at)
            {
                var v = Victim(anchor);
                if (!v.Primary) return false;
                var key = (anchor, at);
                if (Anchors.TryGetValue(key, out bool ready)) return ready;
                var caster = Ability.Caster.Unit;
                // UnitUseAbility approaches Target.Point (the body's position). Use the native gate, including its
                // signed-radius squaring and command LOS, rather than approximating it by a positive radius/eye ray.
                bool reachable = Kingmaker.UnitLogic.Commands.Base.UnitCommand.IsUnitCloseEnough(at, caster.Position,
                    caster.EyePosition, v.Reach, NeedCastSight, 0f);
                Anchors[key] = reachable;
                return reachable;
            }

            internal bool Covers(UnitEntityData who, Vector3 at, Func<UnitEntityData, Vector3> positionOf, IReadOnlyList<UnitEntityData> party)
            {
                var v = Victim(who);
                if (!v.Affected) return false;
                if (v.OwnBurst && CanAnchor(who, at) && ClearSight(at + v.EyeOffset, at)) return true;
                if (party == null) return false;
                for (int i = 0; i < party.Count; i++)
                {
                    var anchor = party[i];
                    if (anchor == null || anchor == who) continue;
                    Vector3 p = positionOf != null ? positionOf(anchor) : anchor.Position;
                    if (!CanAnchor(anchor, p)) continue;
                    Vector3 eye = p + Victim(anchor).EyeOffset;
                    if (GeometryUtils.MechanicsDistance(at, eye) <= Selector.AoERadius.Meters + v.Body && ClearSight(eye, at)) return true;
                }
                return false;
            }

            internal CastVictim Consequence(UnitEntityData who)
            {
                var v = Victim(who);
                if (v.HarmRead || !v.Affected) return v;
                v.HarmRead = true;
                try
                {
                    // Same native-context damage, saves, poison and control readers as ordinary enemy ability appraisal.
                    v.Damage = EstimateAbilityDamage(Ability, Profile, who) * EffectChance(Ability.Caster.Unit, who, Ability, Profile, true);
                    float control = Profile.ControlStrength == ControlStrength.Hard ? 1f : Profile.ControlStrength == ControlStrength.Soft ? 0.4f : 0f;
                    float effectChance = Profile.TargetBuffs.Count > 0 || control > 0f ? EffectChance(Ability.Caster.Unit, who, Ability, Profile, false) : 0f;
                    foreach (var buff in Profile.TargetBuffs) v.Extra += PoisonHpLoss(Ability, buff, who, 3f) * effectChance;
                    if (control > 0f && !Analysis.AbilityClassifier.HasCCImmunity(who, Profile.Control))
                        v.Extra += control * ValueModel.PerRound(Math.Max(1, who.Descriptor.Progression.CharacterLevel)) *
                                   Mathf.Clamp(Profile.EffectRoundsOn(Ability, who), 1f, 2f) * effectChance;
                    v.KnownHarm = true;
                }
                catch (Exception ex)
                {
                    v.Damage = v.Extra = 0f;
                    if (CombatMemory.FirstThisFight("swarm-harm|" + Ability.Caster.Unit.UniqueId))
                        Main.Verbose($"[Swarm] {Ability.Blueprint.name}: consequence unavailable ({ex.GetType().Name})");
                }
                return v;
            }

            internal void AddExitCircles(UnitEntityData who, IReadOnlyList<UnitEntityData> party, List<SwarmGeometry.Circle> circles)
            {
                var v = Victim(who);
                if (!v.Affected) return;
                var caster = Ability.Caster.Unit;
                if (v.Primary && v.OwnBurst) circles.Add(new SwarmGeometry.Circle(caster.Position.x, caster.Position.z, Math.Abs(v.Reach)));
                if (party == null) return;
                for (int i = 0; i < party.Count; i++)
                {
                    var anchor = party[i];
                    if (anchor != null && anchor != who && CanAnchor(anchor, anchor.Position))
                    {
                        var centre = anchor.Position + Victim(anchor).EyeOffset;
                        circles.Add(new SwarmGeometry.Circle(centre.x, centre.z, Selector.AoERadius.Meters + v.Body));
                    }
                }
            }
        }
    }
}
