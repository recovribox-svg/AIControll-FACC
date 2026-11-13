// ★ v0.3.50 review1: settle the actual unique buff a cast removes, including a sibling on the same recipient.
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Mechanics;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        // Use the execution context's original AbilityData: it retains conversion/fusion identity and needs no
        // reconstructed cast/context. Null means there is no matching AI cast to settle in this fight.
        internal static AbilityData BuffSourceAbility(MechanicsContext context) => context?.SourceAbilityContext?.Ability;

        private static int? UniqueBuffPlacedRound(UnitEntityData caster, Buff buff, UnitEntityData holder)
        {
            if (buff?.Context?.MaybeCaster != caster) return null;
            var source = BuffSourceAbility(buff.Context);
            return source == null ? (int?)null : CombatMemory.LastCastRound(caster, source, holder);
        }

        private static int? NewestUniqueReplacementAge(UnitEntityData caster, List<Buff> replaced,
                                                        UnitEntityData holder, int round)
        {
            int? newest = null;
            foreach (var buff in replaced)
            {
                if (buff?.Blueprint == null || !IsUniqueBuff(buff.Blueprint)) continue;
                int? placed = UniqueBuffPlacedRound(caster, buff, holder);
                if (placed.HasValue && (!newest.HasValue || placed.Value > newest.Value)) newest = placed;
            }
            return newest.HasValue ? round - newest.Value : (int?)null;
        }
    }
}
