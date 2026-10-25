// ★ v0.3.22: a damage-trigger action runs in the infusion fact's context, not in a new blast context.
using System;
using System.Collections.Generic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics;

namespace CompanionAI_Pathfinder.Tactics
{
    internal sealed class KineticEffectContext : IDisposable
    {
        [ThreadStatic] private static KineticEffectContext current;
        private readonly KineticEffectContext previous;
        internal readonly AbilityData Ability;
        internal readonly MechanicsContext Context;
        internal readonly string Key;
        internal readonly AbilityProfile Delivery;
        private readonly float? deliveryOverride;
        private readonly IReadOnlyList<float> hits;
        private readonly Func<Kingmaker.EntitySystem.Entities.UnitEntityData, float> repeats;

        internal KineticEffectContext(AbilityData ability, MechanicsContext context, string key, AbilityProfile delivery, float? deliveryChance = null, IReadOnlyList<float> hitChances = null,
                                      Func<Kingmaker.EntitySystem.Entities.UnitEntityData, float> repeatCount = null)
        {
            Ability = ability;
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Key = key;
            Delivery = delivery;
            deliveryOverride = deliveryChance;
            hits = hitChances;
            repeats = repeatCount;
            previous = current;
            current = this;
        }

        internal static MechanicsContext For(AbilityData ability) => current != null && current.Ability == ability ? current.Context : null;
        internal static string KeyFor(AbilityData ability) => current != null && current.Ability == ability ? current.Key : "";
        internal static float DeliveryChance(AbilityData ability, Kingmaker.EntitySystem.Entities.UnitEntityData target)
        {
            var saved = current;
            if (saved == null || saved.Ability != ability || saved.Delivery == null) return 0f;
            if (saved.deliveryOverride.HasValue) return saved.deliveryOverride.Value;
            try
            {
                current = saved.previous;
                return CombatMath.EffectChance(ability.Caster.Unit, target, ability, saved.Delivery, damage: true);
            }
            finally { current = saved; }
        }
        internal static float LandChance(AbilityData ability, Kingmaker.EntitySystem.Entities.UnitEntityData target,
                                         float conditional, bool repeatedDamage)
        {
            var saved = current;
            if (saved == null || saved.Ability != ability) return 0f;
            if (saved.hits == null)
            {
                float count = saved.repeats?.Invoke(target) ?? 1f;
                float p = DeliveryChance(ability, target) * conditional;
                if (repeatedDamage) return p * count;
                return 1f - (float)Math.Pow(1f - p, count);
            }
            return ValueModel.RepeatedHitEffect(saved.hits, conditional, repeatedDamage);
        }
        public void Dispose() { current = previous; }
    }
}
