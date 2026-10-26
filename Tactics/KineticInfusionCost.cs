// ★ infusion-selection: restricted marginal changes to a copy of the game's actual cost value.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class KineticInfusionCost
    {
        private static readonly Func<AbilityKineticist, UnitPartKineticist, int> ReadQuiver = BindQuiver();

        private static Func<AbilityKineticist, UnitPartKineticist, int> BindQuiver()
        {
            try
            {
                var method = typeof(AbilityKineticist).GetMethod("GetQuiverResource", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(UnitPartKineticist) }, null);
                return method == null ? null : (Func<AbilityKineticist, UnitPartKineticist, int>)
                    Delegate.CreateDelegate(typeof(Func<AbilityKineticist, UnitPartKineticist, int>), method);
            }
            catch { return null; }
        }

        internal static int QuiverAllowance(AbilityKineticist native, UnitPartKineticist part)
        {
            if (native == null || native.AllowOnlyBurnCost || part == null || ReadQuiver == null) return 0;
            try { return Math.Max(0, ReadQuiver(native, part)); }
            catch { return 0; } // Missing native query retains a conservative upper bound, never invents arrows.
        }

        internal static bool TryContribution(BlueprintBuff buff, AbilityData blast, out int amount)
        {
            amount = 0;
            if (buff == null || blast?.Blueprint == null) return false;
            var modifiers = buff.GetComponents<AddKineticistBurnModifier>().ToArray();
            if (modifiers.Length == 0) return false;
            foreach (var modifier in modifiers)
            {
                if (modifier.GetType() != typeof(AddKineticistBurnModifier) || modifier.UseContextValue ||
                    modifier.BurnType != KineticistBurnType.Infusion || modifier.Value < 0 || modifier.RemoveBuffOnAcceptBurn)
                    return false;
                var allowed = modifier.AppliableTo;
                if (!allowed.Any() || allowed.Contains(blast.Blueprint) || allowed.Contains(blast.Blueprint.Parent))
                    amount += modifier.Value;
            }
            return true;
        }

        internal static bool TryReplace(KineticistAbilityBurnCost actual, AbilityData blast,
            IEnumerable<BlueprintBuff> removed, BlueprintBuff added, out int total, int quiver = 0)
        {
            total = 0;
            int subtract = 0;
            foreach (var buff in removed.Distinct())
            {
                if (!TryContribution(buff, blast, out int cost)) return false;
                subtract += cost;
            }
            // Positive known modifiers were installed via native Increase. Do not remove more than that source can
            // account for, or duplicate specialization/gather reductions by subtracting from the final Total.
            if (subtract > actual.InfusionIncrease) return false;
            int increase = 0;
            if (added != null && !TryContribution(added, blast, out increase)) return false;
            var changed = Rebuild(actual, subtract, increase, quiver);
            total = changed.Total;
            return total >= 0;
        }

        internal static KineticistAbilityBurnCost Rebuild(KineticistAbilityBurnCost actual, int subtract, int increase, int quiver)
        {
            // Base fields are readonly. Reconstruct through native constructors/methods, retaining every category,
            // decrease and gather value; do not use reflection writes or replace the game's Total arithmetic.
            var changed = new KineticistAbilityBurnCost(actual.BlastBase, actual.InfusionBase,
                actual.MetakinesisBase, actual.WildTalentBase);
            changed.Increase(actual.BlastIncrease, KineticistBurnType.Blast);
            changed.Increase(actual.InfusionIncrease - subtract, KineticistBurnType.Infusion);
            changed.Increase(actual.MetakinesisIncrease, KineticistBurnType.Metakinesis);
            changed.Increase(actual.WildTalentIncrease, KineticistBurnType.WildTalent);
            changed.Decrease(actual.BlastDecrease, KineticistBurnType.Blast);
            changed.Decrease(actual.InfusionDecrease, KineticistBurnType.Infusion);
            changed.Decrease(actual.MetakinesisDecrease, KineticistBurnType.Metakinesis);
            changed.Decrease(actual.WildTalentDecrease, KineticistBurnType.WildTalent);
            changed.IncreaseGatherPower(actual.GatherPower);
            changed.Increase(increase, KineticistBurnType.Infusion);
            // For ordinary blasts there is no wild-talent category. Native Total's pooled reduction then gives the
            // same accepted cost as native CalculateCost's quiver payment. Keep unknown hybrid wild costs bounded
            // above instead of inventing a different reduction rule. Only this detached cost value is changed.
            if (actual.WildTalentBase == 0 && actual.WildTalentIncrease == 0 && quiver > 0)
                changed.IncreaseGatherPower(quiver);
            return changed;
        }
    }
}
