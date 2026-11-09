using System;
using System.Collections.Generic;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Parts;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly Dictionary<BlueprintBuff, bool> s_OnlyRevealUtility = new Dictionary<BlueprintBuff, bool>();

        // A narrow fully-read contract. Blindness, penalties and unknown components keep their ordinary appraisal.
        internal static bool OnlyRevealUtility(BlueprintBuff buff)
        {
            if (buff == null) return false;
            if (s_OnlyRevealUtility.TryGetValue(buff, out bool known)) return known;
            bool removal = false, understood = buff.ComponentsArray != null;
            foreach (var component in buff.ComponentsArray ?? Array.Empty<Kingmaker.Blueprints.BlueprintComponent>())
            {
                if (component is DoNotBenefitFromConcealment) removal = true;
                else if (component is AddConditionImmunity immune && immune.Condition == UnitCondition.Invisible) { }
                else if (component is BuffSkillStealthBonus stealth && stealth.Value <= 0) { }
                else { understood = false; break; }
            }
            return s_OnlyRevealUtility[buff] = removal && understood;
        }

        internal static Concealment WithDisabledConcealment(UnitPartConcealment part, Func<Concealment> read)
        {
            try { part.Disable = true; return read(); }
            finally { part.Disable = false; } // Native setter retains/releases a counter; balance one retain exactly.
        }

        private static float ConcealmentPass(Concealment concealment) =>
            ValueModel.LandsPastDefences(concealment == Concealment.Total ? 50 : concealment == Concealment.Partial ? 20 : 0, 0);

        private static float RevealCombatGain(TacticalContext ctx, UnitEntityData enemy)
        {
            var cache = ctx.RevealCombatGains ?? (ctx.RevealCombatGains = new Dictionary<UnitEntityData, float>());
            if (cache.TryGetValue(enemy, out float known)) return known;
            float gain = 0f;
            // DoNotBenefitFromConcealment sets this existing native part's Disable flag. No part, buff, condition or
            // command is created; the field is restored even if a native/mod query fails. Direct Calculate avoids the
            // normal attack/concealment caches, which continue to describe the actual restored battlefield.
            var part = enemy.Get<UnitPartConcealment>();
            if (part != null)
                foreach (var ally in ctx.Allies)
                {
                    if (ally?.View == null || ally.Descriptor.State.IsConscious != true) continue;
                    try
                    {
                        var before = UnitPartConcealment.Calculate(ally, enemy, attack: true);
                        if (before == Concealment.None) continue;
                        var after = WithDisabledConcealment(part, () => UnitPartConcealment.Calculate(ally, enemy, attack: true));
                        if (ConcealmentPass(after) <= ConcealmentPass(before)) continue;
                        var attack = CombatMath.EstimateWeaponAttack(ally, enemy, full: true);
                        if (attack == null || HorizontalDistance(ally.Position, enemy.Position) > attack.Reach) continue;
                        gain += ValueModel.ConcealmentRemovalGain(attack.ExpectedDamage, ConcealmentPass(before), ConcealmentPass(after));
                    }
                    catch { /* Unread geometry/defences do not earn a generic debuff reward. */ }
                }
            cache[enemy] = gain;
            return gain;
        }
    }
}
