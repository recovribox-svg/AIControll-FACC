// ★ v0.3.50 review2: only a known native direct-HP quote can buy the HP reserve part of a swarm exit.
// General danger still includes poison, control and stat/level harm; those are not HP somebody heals later.
using System;
using System.Collections.Generic;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private static readonly Dictionary<AbilityProfile, bool> SwarmDirectHpGraphs = new Dictionary<AbilityProfile, bool>();

        /// <summary>
        /// The existing damage quote may include HP equivalents for stat damage/negative levels. Admit only exact
        /// native HP actions, with the native kind field actually readable. Cached with the immutable profile metadata.
        /// </summary>
        internal static bool SwarmDirectHpGraph(AbilityProfile profile)
        {
            if (profile == null || DealDamageType == null || profile.Damage.Count == 0) return false;
            if (SwarmDirectHpGraphs.TryGetValue(profile, out bool known)) return known;
            bool hpOnly = true;
            try
            {
                foreach (var action in profile.Damage)
                    if (action?.GetType() != typeof(ContextActionDealDamage) ||
                        DealDamageType.GetValue(action)?.ToString() != "Damage") { hpOnly = false; break; }
            }
            catch { hpOnly = false; }
            SwarmDirectHpGraphs[profile] = hpOnly;
            return hpOnly;
        }

        /// <summary>No fresh estimate/context. Extra poison/control harm is deliberately not part of this HP quote.</summary>
        internal static float KnownSwarmDirectHp(AbilityProfile profile, SwarmCast.CastVictim victim)
        {
            if (victim?.KnownHarm != true || !SwarmDirectHpGraph(profile) ||
                !(victim.Damage > 0f) || float.IsInfinity(victim.Damage)) return 0f;
            return victim.Damage;
        }
    }
}
