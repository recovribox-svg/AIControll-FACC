// review2 playtest: permanent, completely read constant stat bonuses do not consume an ability execution context.
using System.Collections.Generic;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// Omit only an unused preview context, never a numerical or duration rule. For this narrow shape PartAmount
        /// reads constants/recipient stats, EffectiveRounds reads Permanent, and no equipment/on-hit part uses the
        /// source context or caster level. Unknown, ranked, timed and equipment effects keep the existing path.
        /// No result or actor is cached; the eligibility is checked on the parts already read for this appraisal.
        /// </summary>
        private static bool CanSkipBuffContext(AbilityProfile profile, List<BlueprintBuff> buffs,
                                               List<BuffPart> parts, HashSet<string> unread)
        {
            if (profile == null || buffs == null || buffs.Count == 0 || parts == null || parts.Count == 0 ||
                unread == null || unread.Count != 0) return false;
            foreach (var p in parts)
            {
                if (p == null || !(p.Kind == BuffPartKind.Strength || p.Kind == BuffPartKind.Dexterity ||
                                  p.Kind == BuffPartKind.Constitution || p.Kind == BuffPartKind.MentalStat)) return false;
                if (p.Value != null && p.Value.ValueType != ContextValueType.Simple || p.Dice != null || p.Pool != null ||
                    p.Formula.Rolls != 0 || p.FormulaBonusByCasterLevel || p.Granted != null || p.CasterFact != null ||
                    p.Weapon != null) return false;
            }
            foreach (var buff in buffs)
            {
                var apply = profile.ApplicationOf(buff);
                if (apply == null || apply.GetType() != typeof(ContextActionApplyBuff) || !apply.Permanent) return false;
            }
            return !BuffContextAddsEquipmentParts(profile, buffs);
        }
    }
}
