// Native layouts/real exported spells only. Does not initialize actors, issue commands or simulate a successful cast.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Settings;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Components;
using Newtonsoft.Json;

namespace ProfileAudit
{
    internal static class CasterPolicyCheck
    {
        internal static AbilityData Data(BlueprintAbility blueprint)
        {
            var data = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
            typeof(AbilityData).GetField("Blueprint").SetValue(data, blueprint);
            return data;
        }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>(); int checks = 0, bad = 0;
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("CASTER-POLICY FAILED: " + why); } }
            try
            {
                var settings = JsonConvert.DeserializeObject<CharacterSettings>("{\"RangePreference\":2,\"SpellPolicy\":1}");
                Check(!settings.PureMage && !PureMagePolicy.IsActive(settings), "old Ranged settings retain ordinary physical attacks");
                settings.PureMage = true;
                Check(PureMagePolicy.IsActive(settings), "Ranged plus explicit opt-in activates Pure Mage");
                settings.RangePreference = RangePreference.Mixed;
                Check(!PureMagePolicy.IsActive(settings) && settings.PureMage, "Mixed suspends but preserves the opt-in");
                settings.RangePreference = RangePreference.Melee;
                Check(!PureMagePolicy.IsActive(settings), "Melee suspends the opt-in");
                settings.RangePreference = RangePreference.Ranged;
                var roundtrip = JsonConvert.DeserializeObject<CharacterSettings>(JsonConvert.SerializeObject(settings));
                Check(PureMagePolicy.IsActive(roundtrip) && roundtrip.SpellPolicy == SpellResourcePolicy.Progressive, "settings survive native Newtonsoft roundtrip");
                Check(JsonConvert.DeserializeObject<CharacterSettings>("{\"SpellPolicy\":\"Progressive\"}").SpellPolicy == SpellResourcePolicy.Progressive,
                    "old textual Progressive setting remains readable");
                Check((int)SpellResourcePolicy.Standard == 0 && (int)SpellResourcePolicy.Progressive == 1 && (int)SpellResourcePolicy.Aggressive == 2,
                    "saved enum ordinals remain fixed");
                Check(Localization.GetSpellPolicyName(SpellResourcePolicy.Progressive) == "Conservative", "visible conservative name");
                Check(Localization.GetSpellPolicyName(SpellResourcePolicy.Aggressive) == "Aggressive", "visible aggressive name");

                var allow = typeof(TacticalEngine).GetMethod("PureMageAllows", BindingFlags.NonPublic | BindingFlags.Static);
                var ctx = new TacticalContext { Settings = settings };
                var shot = new Candidate { Kind = CandidateKind.WeaponAttack };
                ctx.GetType().GetField("PureMageWeapon", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctx, false);
                Check(!(bool)allow.Invoke(null, new object[] { ctx, shot }), "ordinary bow/staff fallback cannot enter final candidate list");
                settings.PureMage = false;
                Check((bool)allow.Invoke(null, new object[] { ctx, shot }), "ordinary Ranged martial character retains weapon candidate");
                settings.PureMage = true;
                ctx.GetType().GetField("PureMageWeapon", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctx, true);
                Check((bool)allow.Invoke(null, new object[] { ctx, shot }), "positively verified energy shot can enter");
                shot.FullAttack = true;
                Check(!(bool)allow.Invoke(null, new object[] { ctx, shot }), "one-shot rider cannot authorize later bare full-attack arrows");

                void CheckSpells()
                {
                    var spells = loaded.OfType<BlueprintAbility>().ToList();
                    var missile = spells.FirstOrDefault(a => a.name == "MagicMissile");
                    var vital = spells.FirstOrDefault(a => a.GetComponent<AbilityCustomVitalStrike>() != null);
                    var compound = spells.FirstOrDefault(a => AbilityProfile.Get(a).MeleeAttack != null && !AbilityProfile.Get(a).OnlyMeleeAttack);
                    var touch = spells.FirstOrDefault(a => a.Range == AbilityRange.Touch && a.GetComponent<AbilityDeliverTouch>() != null);
                    var source = spells.FirstOrDefault(a => a.Range == AbilityRange.Touch && a.StickyTouch?.TouchDeliveryAbility?.GetComponent<AbilityDeliverTouch>() != null);
                    if (missile == null || vital == null || compound == null || touch == null || source == null)
                    {
                        lines.Add("CASTER-POLICY actual spell fixtures skipped: outside this input scope");
                        return;
                    }
                    Check(missile != null && PureMagePolicy.AllowsAbility(settings, Data(missile), AbilityProfile.Get(missile)), "actual non-weapon spell remains admitted");
                    Check(vital != null && !PureMagePolicy.AllowsAbility(settings, Data(vital), AbilityProfile.Get(vital)), "actual Vital Strike is physical despite AbilityData representation");
                    Check(compound != null && !PureMagePolicy.AllowsAbility(settings, Data(compound), AbilityProfile.Get(compound)),
                        "a melee strike with an extra buff/rider is still a physical attack");

                    var plain = Data(touch); var reached = Data(touch);
                    var magic = (MetamagicData)RuntimeHelpers.GetUninitializedObject(typeof(MetamagicData));
                    typeof(MetamagicData).GetField("<MetamagicMask>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(magic, Metamagic.Reach);
                    typeof(AbilityData).GetField("<MetamagicData>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(reached, magic);
                    Check(SpellDelivery.IsPhysicalTouch(plain) && !SpellDelivery.IsReachTouch(plain), "plain native touch stays physical");
                    Check(!SpellDelivery.IsPhysicalTouch(reached) && SpellDelivery.IsReachTouch(reached), "actual Reach flag makes native touch remote");
                    Check(ReferenceEquals(AbilityProfile.Get(plain), AbilityProfile.Get(reached)) && !AbilityProfile.Get(plain).RangedAttackRoll,
                        "Reach does not contaminate the shared blueprint profile");
                    Check(SpellDelivery.HasRangedAttackRoll(reached, AbilityProfile.Get(reached)) &&
                          !SpellDelivery.HasRangedAttackRoll(plain, AbilityProfile.Get(plain)), "ranged attack-roll risk follows this cast only");
                    var sourceCast = Data(source);
                    Check(SpellDelivery.AttackBlueprint(sourceCast) == source.StickyTouch.TouchDeliveryAbility && SpellDelivery.IsTouchAttack(sourceCast),
                        "source spell's attack comes from its actual sticky-touch delivery");
                    typeof(AbilityData).GetField("<MetamagicData>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sourceCast, magic);
                    Check(SpellDelivery.IsReachTouch(sourceCast) && !SpellDelivery.IsPhysicalTouch(sourceCast), "Reach is recognized before casting the source touch spell");
                    var cure = spells.FirstOrDefault(a => a.name == "CureModerateWoundsCast");
                    Check(cure?.StickyTouch is Kingmaker.UnitLogic.Abilities.Components.TargetCheckers.ILineOfSightIgnore,
                        "played heal's actual sticky-touch source has the native ignore-sight contract");
                }
                CheckSpells();

                // The scene/physics call cannot be run offline. Check its compiled data route and retained safety
                // guard instead; this proves metadata propagation, not a successful native heal or live path.
                var issue = typeof(TacticalEngine).GetMethod("PureMageAllowsIssue", BindingFlags.NonPublic | BindingFlags.Static);
                var evaluate = typeof(TacticalEngine).GetMethod("EvaluateAbility", BindingFlags.NonPublic | BindingFlags.Static);
                var sight = typeof(Candidate).GetField("NeedLoS");
                bool References(MethodInfo method, MemberInfo member) => SaveBoostCheck.ReferencedMembers(method).Any(m =>
                    m.Module == member.Module && m.MetadataToken == member.MetadataToken);
                Check(References(issue, sight), "final touch guard reads the priced native sight flag");
                Check(References(evaluate, sight), "ability evaluation carries the native sight flag into its candidate");
                Check(References(issue, typeof(TacticalEngine).GetMethod("SafeTouchCandidate", BindingFlags.NonPublic | BindingFlags.Static)),
                    "out-of-range touch issue retains the original safe-approach guard");

                var flags = typeof(TacticalEngine).GetMethod("SetAttackFlags", BindingFlags.NonPublic | BindingFlags.Static);
                foreach (bool realTime in new[] { false, true })
                {
                    var command = (Kingmaker.UnitLogic.Commands.UnitAttack)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.UnitLogic.Commands.UnitAttack));
                    flags.Invoke(null, new object[] { command, false, realTime, true });
                    Check(command.IsSingleAttack && !command.ForceFullAttack, "Pure Mage energy shot is a single native attack, RT=" + realTime);
                }
                var martial = (Kingmaker.UnitLogic.Commands.UnitAttack)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.UnitLogic.Commands.UnitAttack));
                flags.Invoke(null, new object[] { martial, false, true, false });
                Check(!martial.IsSingleAttack && !martial.ForceFullAttack, "ordinary ranged martial retains native real-time full-attack selection");
                flags.Invoke(null, new object[] { martial, true, false, false });
                Check(martial.ForceFullAttack && !martial.IsSingleAttack, "ordinary martial retains explicit turn-based full attack");

                var qualifies = typeof(PureMagePolicy).GetMethod("IsEnergyRider", BindingFlags.NonPublic | BindingFlags.Static);
                var rider = new AdditionalDiceOnAttack { DamageType = new Kingmaker.RuleSystem.Rules.Damage.DamageTypeDescription {
                    Type = Kingmaker.RuleSystem.Rules.Damage.DamageType.Energy },
                    InitiatorConditions = new Kingmaker.ElementsSystem.ConditionsChecker(), TargetConditions = new Kingmaker.ElementsSystem.ConditionsChecker() };
                Check((bool)qualifies.Invoke(null, new object[] { rider }), "native transparent energy rider metadata");
                rider.OnlyOnFullAttack = true;
                Check(!(bool)qualifies.Invoke(null, new object[] { rider }), "full-attack-only rider cannot authorize a single shot");
                rider.OnlyOnFullAttack = false; rider.CriticalHit = true;
                Check(!(bool)qualifies.Invoke(null, new object[] { rider }), "critical-only energy cannot promise magic on an ordinary hit");
                rider.CriticalHit = false;
                rider.Value = new Kingmaker.UnitLogic.Mechanics.ContextDiceValue { DiceCountValue = new Kingmaker.UnitLogic.Mechanics.ContextValue {
                    ValueType = Kingmaker.UnitLogic.Mechanics.ContextValueType.TargetCustomProperty } };
                Check(!(bool)qualifies.Invoke(null, new object[] { rider }), "unknown custom property is withheld without invoking its getter");
                rider.Value = null; rider.InitiatorConditions = null;
                Check(!(bool)qualifies.Invoke(null, new object[] { rider }), "missing native condition checker cannot authorize a physical fallback");
                var ordinary = new CharacterSettings { RangePreference = RangePreference.Ranged };
                for (int i = 0; i < 100; i++) _ = PureMagePolicy.AllowsAbility(ordinary, null, null);
                long allocated = GC.GetAllocatedBytesForCurrentThread(); int admitted = 0;
                for (int i = 0; i < 10000; i++) if (PureMagePolicy.AllowsAbility(ordinary, null, null)) admitted++;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Check(admitted == 10000 && allocated == 0, "inactive policy admits unchanged actions without allocation");
                lines.Add($"CASTER-POLICY inactive 10000 checks allocate {allocated} bytes; offline fast-path microcheck, not Unity decision timing");
            }
            catch (Exception ex) { bad++; lines.Add("CASTER-POLICY FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"CASTER-POLICY {checks} checks, failures {bad}; native settings, candidate filters and actual exported spells; no actor setup or command execution");
            return lines;
        }
    }
}
