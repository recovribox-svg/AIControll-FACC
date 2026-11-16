// Common-buff selector metadata and fail-closed counterexamples. No actor, selector execution, crit roll or cast.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class ValueSelectionCheck
    {
        private sealed class OtherSelector : ContextActionSelectByValue { }
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int count = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { count++; if (!ok) { bad++; lines.Add("VALUE-SELECTION FAILED: " + why); } }
            try
            {
                var all = loaded.ToList();
                var spells = all.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => spells.First(x => x.name == name);
                var frigid = Spell("FrigidTouch"); var profile = AbilityProfile.Get(frigid);
                var selector = frigid.GetComponent<AbilityEffectRunAction>().Actions.Actions.OfType<ContextActionSelectByValue>().Single();
                var factory = typeof(AbilityProfile).GetMethod("ReadValueSelectionCore", All);
                object Read(ContextActionSelectByValue s) => factory.Invoke(null, new object[] { s });
                object Field(object o, string name) => o.GetType().GetField(name, All).GetValue(o);
                var floor = Read(selector);
                var minimum = (ContextActionApplyBuff)Field(floor, "Application");
                Check(minimum != null && minimum.DurationValue.Rate == DurationRate.Rounds && minimum.DurationValue.BonusValue.Value == 1,
                    "actual Frigid Touch common effect has one fixed round, without summing mutually exclusive variants");
                Check((float)Field(floor, "MaximumRounds") == 10f && (bool)Field(floor, "OnlyConditions") &&
                    ((UnitCondition[])Field(floor, "Conditions")).SequenceEqual(new[] { UnitCondition.Staggered }),
                    "the same native stagger has a ten-round upper duration for ally safety");
                Check(profile.TargetBuffs.Contains(minimum.Buff) && profile.ControlStrength == ControlStrength.Soft &&
                    ReferenceEquals(profile.ApplicationOf(minimum.Buff), minimum), "ordinary reader reaches the real minimum application");
                var parent = AbilityProfile.Get(Spell("FrigidTouchCast"));
                Check(parent.NeedsAttackRoll && parent.TargetBuffs.Contains(minimum.Buff), "sticky-touch parent retains native delivery and reads stagger");
                var damage = frigid.GetComponent<AbilityEffectRunAction>().Actions.Actions.OfType<ContextActionDealDamage>().Single();
                Check(damage.WriteCriticalToSharedValue && damage.CriticalSharedValue == AbilitySharedValue.DamageBonus,
                    "Frigid Touch's native damage writes the selector input; no guessed critical frequency");
                Check(AbilityProfile.Get(Spell("Sirocco")).TargetBuffs.All(b => b != minimum.Buff) &&
                    ((IEnumerable)typeof(AbilityProfile).GetProperty("SelectedBuffFloors", All).GetValue(AbilityProfile.Get(Spell("ElementalAssessor")))).Cast<object>().Count() == 0,
                    "other selector spells are not assigned Frigid Touch's common effect");

                var variantsField = typeof(ContextActionSelectByValue).GetField("m_Variants", All);
                var typeField = typeof(ContextActionSelectByValue).GetField("m_Type", All);
                var variants = (Array)variantsField.GetValue(selector); var vt = variants.GetType().GetElementType();
                var actionField = vt.GetField("Action"); var valueField = vt.GetField("Value");
                T Clone<T>(T obj) => (T)typeof(object).GetMethod("MemberwiseClone", All).Invoke(obj, null);
                ContextActionApplyBuff Copy() => Clone(minimum);
                ContextActionSelectByValue Make(ActionList[] actions, ContextValue[] values = null)
                {
                    var s = (ContextActionSelectByValue)RuntimeHelpers.GetUninitializedObject(typeof(ContextActionSelectByValue));
                    typeField.SetValue(s, Enum.ToObject(typeField.FieldType, 0));
                    var a = Array.CreateInstance(vt, actions.Length);
                    for (int i = 0; i < actions.Length; i++)
                    {
                        var entry = Activator.CreateInstance(vt);
                        actionField.SetValue(entry, actions[i]); valueField.SetValue(entry, values?[i] ?? new ContextValue { Value = 0 }); a.SetValue(entry, i);
                    }
                    variantsField.SetValue(s, a); return s;
                }
                ActionList One(ContextActionApplyBuff a) => new ActionList { Actions = new GameAction[] { a } };
                bool Refused(ContextActionSelectByValue s) => Field(Read(s), "Application") == null && Field(Read(s), "Refusal") is string;
                Check(Refused(Make(new[] { One(Copy()), new ActionList() })), "an empty alternative is not a guaranteed buff");
                var other = Copy(); typeof(ContextActionApplyBuff).GetField("m_Buff", All).SetValue(other,
                    all.OfType<BlueprintBuff>().First(b => b.name == "SnowballBuff").ToReference<BlueprintBuffReference>());
                Check(Refused(Make(new[] { One(Copy()), One(other) })), "different buffs are not added or treated as common");
                Check(Refused(Make(new[] { One(Copy()), new ActionList { Actions = new GameAction[] { Copy(), Copy() } } })), "additional branch effects remain unread");
                var dynamic = Copy(); dynamic.DurationValue = Clone(minimum.DurationValue); dynamic.DurationValue.DiceType = DiceType.D4;
                Check(Refused(Make(new[] { One(Copy()), One(dynamic) })), "random duration is not a fixed lower bound");
                var counted = Copy(); counted.DurationValue = Clone(minimum.DurationValue);
                counted.DurationValue.DiceCountValue = new ContextValue { ValueType = ContextValueType.Rank };
                Check(Refused(Make(new[] { One(Copy()), One(counted) })), "even a zero-sided die must not hide a computed duration input");
                var subject = Copy(); subject.ToCaster = !subject.ToCaster;
                Check(Refused(Make(new[] { One(Copy()), One(subject) })), "caster and target applications are not interchangeable");
                var flags = Copy(); flags.AsChild = !flags.AsChild;
                Check(Refused(Make(new[] { One(Copy()), One(flags) })), "different application flags are refused");
                var invalid = Make(new[] { One(Copy()), One(Copy()) }); typeField.SetValue(invalid, Enum.ToObject(typeField.FieldType, 99));
                Check(Refused(invalid), "an unknown native selection mode is refused");
                Check(Refused(Make(Enumerable.Repeat(One(Copy()), 9).ToArray())), "cold traversal has an eight-variant bound");
                Check(Refused((ContextActionSelectByValue)RuntimeHelpers.GetUninitializedObject(typeof(OtherSelector))), "custom selector subclasses are not implicitly executed or understood");
                Check(Refused(Make(new[] { One(Copy()), One(Copy()) }, new[] { new ContextValue(), new ContextValue { ValueType = ContextValueType.Rank } })),
                    "uninspected rank/property selector inputs remain unread");
                Check(Field(Read(Make(new[] { One(Copy()), One(Copy()) })), "Application") != null, "equal outcomes qualify independently of last-on-tie selection");

                var holder = spells.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                AbilityData Fuse(string first, SavingThrowType save)
                {
                    var a = CasterPolicyCheck.Data(holder);
                    a.MagicHackData = new MagicHackData { Spell1 = Spell(first), Spell2 = Spell("FrigidTouchCast"), DeliverBlueprint = Spell(first),
                        SpellLevel = 2, SavingThrowType = save }; return a;
                }
                var fused = Fuse("ExplosiveRunes", SavingThrowType.Unknown); var fusedProfile = AbilityProfile.Get(fused);
                Check(typeof(AbilityProfile).GetProperty("FusionUnsupported", All).GetValue(fusedProfile) == null && fusedProfile.TargetBuffs.Contains(minimum.Buff),
                    "played no-save Runes/Frigid fusion reads the common stagger without adding another touch roll");
                Check((bool)typeof(TacticalEngine).GetMethod("FusionOutcomeModel", All).Invoke(null, new object[] { fused, fusedProfile }),
                    "played no-save fusion retains joint-outcome qualification");
                // ★ Fusion F4: the saved configuration is no longer refused outright; it is offered only through the joint
                // outcome value, which prices the stagger on both save outcomes (FUSION-OUTCOME checks the routes).
                var saved = Fuse("Fireball", SavingThrowType.Reflex); var savedProfile = AbilityProfile.Get(saved);
                Check(typeof(AbilityProfile).GetProperty("FusionUnsupported", All).GetValue(savedProfile) == null &&
                      typeof(AbilityProfile).GetProperty("FusionOutcomeEffectLocation", All).GetValue(savedProfile) != null &&
                      typeof(TacticalEngine).GetMethod("FusionWithheld", All).Invoke(null, new object[] { saved, savedProfile }) == null &&
                      (bool)typeof(TacticalEngine).GetMethod("FusionOutcomeModel", All).Invoke(null, new object[] { saved, savedProfile }),
                    "saved Frigid configuration is admitted only through the outcome model");
                var ally = typeof(TacticalEngine).GetMethod("SelectedBuffAllyRounds", All);
                var members = SaveBoostCheck.ReferencedMembers(ally).ToArray();
                Check(members.Any(m => m.Name == "MaximumRounds") && members.Any(m => m.Name == "HasConditionImmunity") && !members.Any(m => m.Name == "HasFact"),
                    "ally safety reads the upper duration and condition immunity, including possible active-buff refresh");
                Check(!SaveBoostCheck.ReferencedMembers(factory).Any(m => m.Name == "Calculate" || m.Name == "RunAction" || m.Name == "Trigger"),
                    "selector metadata never executes an action, score value or native roll");
                var fast = (Func<TacticalContext,AbilityData,AbilityProfile,UnitEntityData,float>)Delegate.CreateDelegate(
                    typeof(Func<TacticalContext,AbilityData,AbilityProfile,UnitEntityData,float>), ally);
                var ordinary = AbilityProfile.Get(Spell("Fireball"));
                for (int i = 0; i < 128; i++) fast(null, null, ordinary, null);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) fast(null, null, ordinary, null);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(bytes == 0, "ordinary empty-selection safety fast path allocates zero bytes");
                lines.Add($"VALUE-SELECTION ordinary safety: 10000 warm calls, {bytes} bytes; actor-free microcheck, not Unity timing");
            }
            catch (Exception ex) { bad++; lines.Add("VALUE-SELECTION FAILED: " + ex.GetBaseException()); }
            failures = bad; lines.Insert(0, $"VALUE-SELECTION {count} checks, failures {bad}; native common-buff metadata, negative fixtures and compiled routes; no actor, selector execution, crit roll or cast"); return lines;
        }
    }
}
