// ★ pure-mage: cast-specific delivery. Reach belongs to AbilityData, never to a cached blueprint profile.
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SpellDelivery
    {
        public static BlueprintAbility AttackBlueprint(AbilityData ability)
        {
            var delivery = ability?.GetDeliverBlueprint() ?? ability?.Blueprint;
            return delivery?.GetComponent<AbilityEffectStickyTouch>()?.TouchDeliveryAbility ?? delivery;
        }

        public static bool IsTouchAttack(AbilityData ability) => AttackBlueprint(ability)?.GetComponent<AbilityDeliverTouch>() != null;

        public static bool IsReachTouch(AbilityData ability) => ability != null &&
            IsTouchAttack(ability) && ability.HasMetamagic(Metamagic.Reach);

        public static bool IsPhysicalTouch(AbilityData ability) => ability != null &&
            (ability.Range == AbilityRange.Touch || IsTouchAttack(ability)) && !ability.HasMetamagic(Metamagic.Reach);

        public static bool HasRangedAttackRoll(AbilityData ability, AbilityProfile profile) =>
            profile != null && (profile.RangedAttackRoll || IsReachTouch(ability));
    }
}
