// ★ pure-mage: candidate admission, using the existing movement and danger readers unchanged.
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal static void SetAttackFlags(UnitAttack attack, bool full, bool realTime, bool pureMage)
        {
            if (realTime && !pureMage) return; // ordinary real-time martials retain the native attack routine
            attack.ForceFullAttack = full && !pureMage;
            attack.IsSingleAttack = !full || pureMage;
        }

        private static bool PureMageCantripFallback(Candidate candidate)
        {
            if (candidate.Kind != CandidateKind.Ability || !candidate.Offensive || candidate.NeedsMove || candidate.MoveFirst ||
                candidate.Value <= 0.05f || candidate.Target == null || CombatMath.IsDisabled(candidate.Target)) return false;
            var ability = candidate.Ability;
            if (ability?.Spellbook == null || ability.SourceItem != null || ProgressiveGate.EffectiveSlotLevel(ability) != 0 ||
                ConsumablePolicy.SpentMaterial(ability) != null || !AbilityProfile.Get(ability).DealsDamage) return false;
            // AbilityCastRateUtils: a negative count means unlimited charges/resources, including the actual spellbook.
            // Only this reusable fallback replaces the weapon fallback; levelled and resource-paid casts keep their bar.
            try { return ability.GetAvailableForCastCount() < 0; }
            catch (System.Exception ex)
            {
                AbilityDiscovery.ReportOnce("pure-mage-at-will|" + ability.Blueprint.AssetGuid,
                    () => $"[PureMage] {ability.Name}: native cast count unreadable ({ex.GetType().Name}); fallback withheld");
                return false;
            }
        }

        private static bool PureMageAllows(TacticalContext ctx, Candidate candidate)
        {
            if (!PureMagePolicy.IsActive(ctx.Settings)) return true;
            if (candidate.Kind == CandidateKind.WeaponAttack)
                return ctx.PureMageWeapon && !candidate.FullAttack;
            if (candidate.Kind != CandidateKind.Ability) return true;
            if (!PureMagePolicy.AllowsAbility(ctx.Settings, candidate.Ability, AbilityProfile.Get(candidate.Ability))) return false;
            if (!SpellDelivery.IsPhysicalTouch(candidate.Ability)) return true;
            if (candidate.Offensive) return false;
            return !candidate.NeedsMove || CriticalTouchHeal(ctx, candidate.Ability, AbilityProfile.Get(candidate.Ability), candidate.Target) &&
                SafeTouchCandidate(candidate, ctx.Unit);
        }

        private static bool CriticalTouchHeal(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (target == null || ctx.Unit.IsEnemy(target) || profile.Heals.Count == 0) return false;
            float threshold = System.Math.Min(ctx.Settings.HealAtHPPercent / 100f, CombatMath.HealThreshold(ability, ctx.Unit, target, ctx.Settings));
            if (target.Descriptor.State.IsConscious && CombatMath.HpPercent(target) >= threshold) return false;
            return CombatMath.EstimateAbilityHeal(ability, profile, target) > 0f;
        }

        private static bool SafeTouchApproach(TacticalContext ctx, Approach approach, Disengage risk)
        {
            if (approach == null) return false;
            if (risk != null && (risk.Attacks > 0 || risk.Intercepted > 0 || risk.Area != null || risk.Swarm != null ||
                                 risk.Exposed || risk.StopChance > 0f)) return false;
            return !ctx.Enemies.Any(enemy => UnsafeTouchSpot(enemy, ctx.Unit, approach.Point));
        }

        private static bool UnsafeTouchSpot(UnitEntityData enemy, UnitEntityData unit, Vector3 at) =>
            enemy?.View == null || unit?.View == null || Threatens(enemy, unit, at) || FullStrikeLands(enemy, unit, at);

        private static bool SafeTouchCandidate(Candidate candidate, UnitEntityData unit)
        {
            var risk = candidate.Risk;
            if (!candidate.RiskAt.HasValue || candidate.TouchSafetyEnemies == null || !candidate.TouchTargetAt.HasValue ||
                candidate.Target == null || candidate.TouchTargetAt.Value != candidate.Target.Position ||
                candidate.PathAttackers?.Count > 0 || candidate.SeenPathAttackers?.Count > 0) return false;
            if (risk != null && (risk.Attacks > 0 || risk.Area != null || risk.Swarm != null || risk.Exposed || risk.StopChance > 0f)) return false;
            return !candidate.TouchSafetyEnemies.Any(enemy => UnsafeTouchSpot(enemy, unit, candidate.RiskAt.Value));
        }

        private static bool PureMageAllowsIssue(UnitEntityData unit, Candidate candidate)
        {
            var settings = PureMagePolicy.SettingsFor(unit);
            if (!PureMagePolicy.IsActive(settings)) return true;
            if (candidate.Kind == CandidateKind.WeaponAttack)
                return !candidate.FullAttack && PureMagePolicy.AllowsWeapon(unit, settings);
            if (candidate.Kind != CandidateKind.Ability) return true;
            var ability = candidate.Ability;
            var profile = AbilityProfile.Get(ability);
            if (!PureMagePolicy.AllowsAbility(settings, ability, profile)) return false;
            if (!SpellDelivery.IsPhysicalTouch(ability)) return true;
            if (candidate.Offensive) return false;
            if (candidate.Target == null || candidate.Target == unit) return true;
            float range = ability.GetApproachDistance(candidate.Target);
            if (InRangeNow(unit, candidate.Target.Position, range, true)) return true;
            // A moved recipient cannot turn an in-place support touch into an unpriced native auto-approach.
            return candidate.NeedsMove && SafeTouchCandidate(candidate, unit) &&
                (!candidate.Target.Descriptor.State.IsConscious || CombatMath.HpPercent(candidate.Target) < settings.HealAtHPPercent / 100f) &&
                CombatMath.EstimateAbilityHeal(ability, profile, candidate.Target) > 0f;
        }
    }
}
