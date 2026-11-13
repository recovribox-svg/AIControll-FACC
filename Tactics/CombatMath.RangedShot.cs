// ★ v0.3.30: read the prospective stat bonus without applying the short-lived buff or making an attack.
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private static bool RangedShotMatches(WeaponAttackEstimate estimate, Kingmaker.Items.ItemEntityWeapon weapon) =>
            estimate != null && estimate.Attacks == 1 && estimate.IsRanged && !ReferenceEquals(weapon, null) &&
            ReferenceEquals(estimate.Weapon, weapon);

        private static float RangedShotBonus(AbilityProfile.RangedShotProfile shot, MechanicsContext context,
                                             bool alreadyPresent, bool disabled)
        {
            if (alreadyPresent || disabled) return 0f; // The live native quote already includes an existing modifier.
            int value = shot.Bonus.CalculateBaseValue(context);
            return shot.Bonus.HasMinimal ? Math.Max(value, shot.Bonus.Minimal) : value;
        }

        internal static WeaponAttackEstimate EstimateRangedShot(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            var shot = profile.RangedShot;
            var unit = ability.Caster?.Unit;
            var hand = unit?.GetThreatHandRanged();
            if (shot == null || hand == null || target == null || ability.MagicHackData != null || ability.ShadowSpellSettings != null) return null;
            var context = CreateContext(ability, new Kingmaker.Utility.TargetWrapper(target));
            if (context == null) return null;
            bool disabled = (unit.Get<Kingmaker.UnitLogic.Parts.UnitPartDisableBonusForDamage>()?.DisableAdditionalDamage ?? false) ||
                            BuffImmune(unit, shot.Buff, ability);
            float bonus = RangedShotBonus(shot, context, unit.Buffs.HasFact(shot.Buff), disabled);
            var estimate = EstimateWeaponAttack(unit, target, false, (w, i) => new KeyValuePair<int, float>(0, bonus));
            // The native action uses GetThreatHandRanged, one RuleAttackWithWeapon, no full routine/retargeting.
            // A mixed-hand or differently enumerated routine is unknown, not an invented extra shot.
            return RangedShotMatches(estimate, hand.Weapon) ? estimate : null;
        }
    }
}
