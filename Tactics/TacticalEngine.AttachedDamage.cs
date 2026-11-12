// ★ v0.3.30 C06R4: author-approved recipient-owned periodic damage; native contracts, no action execution.
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static float AttachedCleanseWorth(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData ally)
        {
            var snapshot = CombatMath.AttachedDamageOf(ctx, ally);
            if (snapshot.Effects.Count == 0) return 0f;
            // Uses the existing native removal filters/branches. Unknown applicability earns no new attached credit.
            var removed = new HashSet<Buff>(profile.BuffsRemovedFrom(ability, ally, certainOnly: true));
            float worth = 0f;
            foreach (var effect in snapshot.Effects) if (removed.Contains(effect.Buff)) worth += effect.FutureWorth;
            return worth;
        }
    }
}
