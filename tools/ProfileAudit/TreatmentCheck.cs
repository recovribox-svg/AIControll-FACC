// Actual treatment metadata and compiled pending-cast lifetime/routes, without live actors or game time.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components.TargetCheckers;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class TreatmentCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("DISPEL-PENDING FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var score = typeof(TacticalEngine).GetMethod("ScoreAbility", all);
                Check(SaveBoostCheck.ReferencedMembers(score).Count(m => m.DeclaringType == typeof(CombatMemory) && m.Name == "CastPendingByOther") == 2,
                    "compiled enemy exclusion and friendly fallback both use the existing pending cast ledger");
                var pending = SaveBoostCheck.ReferencedMembers(typeof(CombatMemory).GetMethod("CastPendingByOther", all)).ToArray();
                Check(pending.Any(m => m.Name == "get_IsRealTime"), "pending guard retains native combat-mode gate");
                Check(pending.Any(m => m.Name == "get_IsFinished") && pending.Any(m => m.Name == "Until"),
                    "finished and expired native commands stop reserving the treatment");
                Check(SaveBoostCheck.ReferencedMembers(typeof(CombatMemory).GetMethod("NotePendingCast", all)).Any(m => m.Name == "Until"),
                    "actual issued commands retain bounded pending registration");

                var ability = loaded.OfType<BlueprintAbility>().FirstOrDefault(b => b.AssetGuid.ToString() == "4843cb4c23951f54290c5149a4907f54");
                if (ability == null) lines.Add("DISPEL-PENDING exported treatment fixture outside scope; compiled routes only");
                else
                {
                    var profile = AbilityProfile.Get(ability);
                    var marker = loaded.OfType<BlueprintBuff>().FirstOrDefault(b => b.AssetGuid.ToString() == "b89dcf508d48da74f8dd5234e6a5eb84");
                    Check(profile.Dispels && !profile.DealsDamage && profile.Heals.Count == 0, "actual treatment is removal without HP healing/damage");
                    Check(marker != null && profile.TargetBuffs.Contains(marker) && profile.IsDayLong(marker), "actual graph applies its daily recipient marker");
                    Check(marker != null && ability.GetComponents<AbilityTargetHasFact>().Any(c => c.Inverted && !c.FromCaster && c.CheckedFacts.Any(f => f == marker)),
                        "native target rule forbids another caster treating the marked recipient");
                    Check(profile.TargetBuffs.All(b => profile.IsDayLong(b)) && !profile.RemovesFromTarget,
                        "actual treatment remains on the unmodelled removal fallback, not HP heal or descriptor cleanse appraisal");
                    var check = typeof(Kingmaker.UnitLogic.Mechanics.Actions.ContextActionDispelMagic).GetField("m_CheckType", all);
                    Check(profile.DispelActions.Count > 0 && profile.DispelActions.All(d => check.GetValue(d).ToString() == "SkillDC"),
                        "actual treatment uses native skill checks, not guessed guaranteed removal");
                    var native = SaveBoostCheck.ReferencedMembers(typeof(AbilityTargetHasFact).GetMethod("IsTargetRestrictionPassed", all)).ToArray();
                    Check(native.Any(m => m.Name == "HasFact") && native.Any(m => m.Name == "Inverted") && native.Any(m => m.Name == "FromCaster"),
                        "supplied native checker binds fact, inversion and source semantics");
                }
            }
            catch (Exception ex) { bad++; lines.Add("DISPEL-PENDING FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"DISPEL-PENDING {checks} checks, failures {bad}; compiled guard/lifetime and in-scope actual treatment graph; no actor, clock, command or Unity execution");
            return lines;
        }
    }
}
