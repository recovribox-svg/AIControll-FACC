// ★ v0.3.30: native transformation metadata and suppressed-form presence, shared by combat and upkeep.
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using CompanionAI_Pathfinder.Tactics;

namespace CompanionAI_Pathfinder.Analysis
{
    /// <summary>Blueprint metadata only. Suppressed form buffs still belong to the owner: a safe-zone suppression
    /// is not a request to cast another transformation. Never retain a unit's active form in this cache.</summary>
    internal static class PolymorphFacts
    {
        private static readonly Dictionary<BlueprintBuff, Polymorph> Forms = new Dictionary<BlueprintBuff, Polymorph>();

        internal static Polymorph Form(BlueprintBuff buff)
        {
            if (buff == null) return null;
            if (!Forms.TryGetValue(buff, out var form)) Forms[buff] = form = buff.GetComponent<Polymorph>();
            return form;
        }

        internal static bool GivesForm(AbilityProfile profile)
        {
            if (profile == null) return false;
            foreach (var buff in profile.CasterBuffs) if (Form(buff) != null) return true;
            foreach (var buff in profile.TargetBuffs) if (Form(buff) != null) return true;
            return false;
        }

        internal static bool GivesShifterForm(AbilityProfile profile)
        {
            if (profile == null) return false;
            foreach (var buff in profile.CasterBuffs) if (Form(buff)?.AllowDamageTransfer == true) return true;
            foreach (var buff in profile.TargetBuffs) if (Form(buff)?.AllowDamageTransfer == true) return true;
            return false;
        }

        internal static bool HasForm(UnitEntityData unit)
        {
            if (unit?.Body?.IsPolymorphed == true) return true;
            if (unit?.Buffs == null) return false;
            foreach (var buff in unit.Buffs.Enumerable)
                if (Form(buff?.Blueprint) != null) return true;
            return false;
        }
    }
}
