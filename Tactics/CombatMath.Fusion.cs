// ★ Magic Deceiver: native per-action dice cap and the one shared save, without a second save discount on all damage.
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        internal static bool IsHitPointDamage(ContextActionDealDamage action) => KindOf(action) == DealKind.HitPoints;
        // F2/F3: caller-owned synchronous scratch. No actor/context is retained and normal estimates allocate none.
        internal sealed class FusionDamageRead
        {
            internal float Failed, Passed, FailChance;
            internal bool Known;
            internal void Reset() { Failed = Passed = 0f; FailChance = 1f; Known = true; }
        }

        internal static float EstimateFusionDamage(AbilityData ability, AbilityProfile profile, UnitEntityData target,
            FusionDamageRead read)
        {
            read.Reset();
            long heap = HeapMark();
            try
            {
                float raw = EstimateAbilityDamageRaw(ability, profile, target, null, fusionRead: read);
                float scale = raw > 0f ? AbilityDamageScale(ability, profile, target) : 1f;
                read.Failed *= scale; read.Passed *= scale;
                return raw * scale;
            }
            finally { HeapSince(heap, ref EstimateBytes); }
        }

        private static void ReadFusionDamageOutcomes(AbilityProfile profile, List<KeyValuePair<GameAction, float>> parts,
            float fail, bool evasion, bool improved, float deliveries, FusionDamageRead read)
        {
            if (read == null || !read.Known) return;
            read.FailChance = fail;
            if (parts.Count > ValueModel.ControlEnvelope.Capacity) { read.Known = false; return; }
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                for (int j = 0; j < i; j++)
                    if (profile.Exclusive(part.Key, parts[j].Key)) { read.Known = false; return; }
                float factor = FusionDamageFactor(profile, part.Key, fail, evasion, improved);
                if (factor <= 0f) continue; // any omitted outcome has probability zero under this forecast
                float unweighted = part.Value / factor;
                read.Failed += unweighted * FusionDamageFactor(profile, part.Key, 1f, evasion, improved) * deliveries;
                read.Passed += unweighted * FusionDamageFactor(profile, part.Key, 0f, evasion, improved) * deliveries;
            }
            read.Known = !(float.IsNaN(read.Failed) || float.IsInfinity(read.Failed) ||
                           float.IsNaN(read.Passed) || float.IsInfinity(read.Passed));
        }

        private static float EvaluateDamageDice(AbilityData ability, ContextDiceValue value, MechanicsContext context,
                                                int fallbackLevel, bool maximize, bool empower)
        {
            if (ability?.MagicHackData == null) return EvaluateDice(value, context, fallbackLevel, maximize, empower);
            if (value == null) return 0f;
            int count = EvaluateValue(value.DiceCountValue, context, fallbackLevel);
            var component = ability.Spellbook?.Blueprint?.GetComponent<MagicHackSpellbookComponent>();
            // ContextActionDealDamage.GetDamageInfo: this is an actual spellbook component query, not a copied cap table.
            if (component != null) count = Math.Min(count, component.GetMaxDamageDicesForAction(ability.SpellLevel));
            int sides = value.DiceType.Sides();
            float dice = sides <= 0 ? 0f : count * (maximize ? sides : (sides + 1f) / 2f);
            float result = dice + EvaluateValue(value.BonusValue, context, 0);
            return empower ? result * 1.5f : result;
        }

        internal static float FusionDamageFactor(AbilityProfile profile, GameAction action, float fail, bool evasion, bool improved)
        {
            if (profile.Fusion == null) return 1f;
            bool half = action is ContextActionDealDamage damage && KindOf(damage) != DealKind.EnergyDrain && damage.HalfIfSaved;
            // No invented independent saves for Spell1 and Spell2: their actions see the same SavingThrowData.
            switch (profile.FusionPath(action))
            {
                case AbilityProfile.FusionSavePath.Failed: return fail * (half && improved ? 0.5f : 1f);
                case AbilityProfile.FusionSavePath.Passed: return (1f - fail) * (half ? evasion || improved ? 0f : 0.5f : 1f);
                default: return half ? ValueModel.SaveEffectFactor(fail, true, evasion, improved) : 1f;
            }
        }

        private static void RememberFusionDamageChance(AbilityData ability, UnitEntityData target, AbilityProfile profile,
            List<KeyValuePair<GameAction, float>> parts, float fail, bool evasion, bool improved)
        {
            if (profile.Fusion == null || s_ResistanceScope == null) return;
            bool onFail = false, onPass = false;
            foreach (var part in parts)
            {
                if (part.Value <= 0f) continue; // target reductions and the actual branch have already been applied
                onFail |= FusionDamageFactor(profile, part.Key, 1f, evasion, improved) > 0f;
                onPass |= FusionDamageFactor(profile, part.Key, 0f, evasion, improved) > 0f;
            }
            if (s_ResistanceScope.FusionChances == null) s_ResistanceScope.FusionChances = new ReferenceReadMemo();
            s_ResistanceScope.FusionChances.Store(ability, target, profile, s_ResistanceVersion,
                (onFail ? fail : 0f) + (onPass ? 1f - fail : 0f));
        }

        internal static float FusionDamageChance(AbilityData ability, UnitEntityData target, AbilityProfile profile)
        {
            if (profile.Fusion != null && s_ResistanceScope?.FusionChances != null &&
                s_ResistanceScope.FusionChances.TryGet(ability, target, profile, s_ResistanceVersion, out float chance)) return chance;
            return 1f;
        }
    }
}
