using System;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        // Enemy benefit uses the verified minimum. An ally may receive the longer branch, including a refresh of
        // an existing buff, so the minimum must never establish safety. No new actor/context/path cache or roll.
        private static float SelectedBuffAllyRounds(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData ally)
        {
            if (profile.SelectedBuffFloors.Count == 0) return 0f;
            float rounds = 0f;
            foreach (var floor in profile.SelectedBuffFloors)
            {
                var buff = floor.Application.Buff;
                if (profile.BuffAppliesTo(buff, ability, ally) == EffectApplies.No || CombatMath.BuffImmune(ally, buff, ability)) continue;
                if (floor.OnlyConditions && floor.Conditions.Length > 0)
                {
                    bool allImmune = true;
                    foreach (var condition in floor.Conditions)
                        if (!ally.Descriptor.State.HasConditionImmunity(condition)) { allImmune = false; break; }
                    if (allImmune) continue;
                }
                float duration = floor.MaximumRounds;
                if (floor.Application.DurationValue.IsExtendable && ability.HasMetamagic(Metamagic.Extend)) duration *= 2f;
                rounds = Math.Max(rounds, Math.Min(Math.Min(ctx.ExpectedRounds, 3f), duration));
            }
            return rounds;
        }
    }
}
