// Native metadata and pure cached-line fixtures only. No actor setup, rule, buff mutation or Unity clock execution.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;

namespace ProfileAudit
{
    internal static class HeldBuffWeaponCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("HELD-WEAPON FAILED: " + why); } }
            bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var engine = typeof(TacticalEngine); var math = typeof(CombatMath);
                var reader = engine.GetMethod("ReadHeldWeaponBonus", all);
                object Read(BlueprintBuff b) => reader.Invoke(null, new object[] { b });
                var actual = loaded.OfType<BlueprintBuff>().Where(b => b.name.StartsWith("ProfaneAscension", StringComparison.Ordinal) &&
                    b.name.Contains("Buff") && (b.name.EndsWith("1", StringComparison.Ordinal) || b.name.EndsWith("2", StringComparison.Ordinal))).ToArray();
                if (actual.Length == 0) lines.Add("HELD-WEAPON SKIPPED: Profane Ascension buff graphs not loaded");
                else
                {
                    Check(actual.Length == 12, "all twelve native tier/stat buff graphs loaded");
                    foreach (var buff in actual) Check(Read(buff) != null, "actual constant native contract " + buff.name);
                }
                var bonus = new AddContextStatBonus { Stat = StatType.Dexterity,
                    Descriptor = Kingmaker.Enums.ModifierDescriptor.Profane,
                    Value = new ContextValue { ValueType = ContextValueType.Simple, Value = 2 } };
                var fixture = new BlueprintBuff { ComponentsArray = new BlueprintComponent[] { bonus, new UniqueBuff() } };
                Check(Read(fixture) != null, "constant flat bonus plus UniqueBuff is admitted structurally");
                fixture.ComponentsArray = new BlueprintComponent[] { bonus };
                Check(Read(fixture) != null, "constant flat bonus does not require a unique marker");
                fixture.ComponentsArray = new BlueprintComponent[] { bonus, bonus };
                Check(Read(fixture) == null, "two bonus components are outside the scalar contract");
                fixture.ComponentsArray = new BlueprintComponent[] { bonus };
                bonus.Value.ValueType = ContextValueType.CasterProperty;
                Check(Read(fixture) == null, "context-dependent amount is outside the scalar contract");
                bonus.Value.ValueType = ContextValueType.Simple;
                bonus.HasMinimal = true; Check(Read(fixture) == null, "minimum-dependent amount stays on the old appraisal");
                bonus.HasMinimal = false; bonus.Stat = StatType.TemporaryHitPoints;
                Check(Read(fixture) == null, "unrelated statistic is not a weapon scalar");
                bonus.Stat = StatType.Dexterity;
                var restrictions = HarmonyLib.AccessTools.Field(typeof(AddContextStatBonus), "Restrictions");
                var savedRestrictions = restrictions.GetValue(bonus);
                try { restrictions.SetValue(bonus, null); Check(Read(fixture) == null, "missing restriction contract remains unknown"); }
                finally { restrictions.SetValue(bonus, savedRestrictions); }
                Check(Read(fixture) != null, "metadata fixture restored");

                var attribute = engine.GetMethod("HeldAttributeWeaponDelta", all);
                object[] delta;
                bool Attribute(int score, int loss, float multiplier, StatType? damage = StatType.Strength)
                {
                    delta = new object[] { StatType.Strength, score, loss, StatType.Strength, damage, multiplier, 0, 0f };
                    return (bool)attribute.Invoke(null, delta);
                }
                delta = null;
                Check(Attribute(21, 1, 1f) && (int)delta[6] == 0 && Near((float)delta[7], 0f), "odd score can lose one point without losing a modifier");
                Check(Attribute(20, 1, 1f) && (int)delta[6] == 1 && Near((float)delta[7], 1f), "even score loses its actual modifier across parity");
                Check(Attribute(16, 2, 1.5f) && Near((float)delta[7], 1f), "native 1.5 multiplier truncates 4.5 to 4 before loss");
                Check(Attribute(18, 2, 1.5f) && Near((float)delta[7], 2f), "next native 1.5 boundary loses two damage");
                Check(Attribute(10, 2, 1.5f) && Near((float)delta[7], 1f), "negative damage modifier uses native x1");
                Check(Attribute(8, 2, 1.5f) && Near((float)delta[7], 1f), "negative modifiers remain unmultiplied");
                Check(Attribute(20, 0, 1f) && (int)delta[6] == 0 && Near((float)delta[7], 0f), "equal nonstacking replacement leaves zero scalar loss");
                Check(!Attribute(2, 2, 1f) && !Attribute(20, 2, float.NaN), "zero score and unreadable multiplier are withheld");

                var routineType = math.GetNestedType("AttackRoutine", all); var lineType = math.GetNestedType("AttackLine", all);
                var weapon = (ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(ItemEntityWeapon));
                var other = (ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(ItemEntityWeapon));
                var routine = Activator.CreateInstance(routineType, true);
                var entries = (IList)routineType.GetField("Lines", all).GetValue(routine);
                object Line(int ab, int ac, float raw = 10f, float hit = 10f, int dr = 0, float factor = 1f,
                            float threat = 0f, int critical = 2, float sneak = 0f, float blade = 0f)
                {
                    var value = Activator.CreateInstance(lineType, true);
                    void Set(string name, object v) => lineType.GetField(name, all).SetValue(value, v);
                    Set("Weapon", weapon); Set("AttackBonus", ab); Set("ArmorClass", ac); Set("RawPerHit", raw); Set("GameHit", hit);
                    Set("DamageReduction", dr); Set("PhysicalFactor", factor); Set("ThreatChance", threat); Set("CritMultiplier", critical);
                    Set("Sneak", sneak); Set("BladeBlast", blade); return value;
                }
                var kernel = math.GetMethod("ExpectedWeaponLine", all); var lossMethod = math.GetMethod("HeldWeaponRoutineLoss", all);
                float Current(float lands)
                {
                    float total = 0f;
                    foreach (var item in entries) total += (float)kernel.Invoke(null, new object[] { item, 0, 0f, lands, 0f, 0f });
                    return total;
                }
                object[] a;
                bool Loss(int attack, float damage, float lands = 1f)
                {
                    a = new object[] { routine, weapon, Current(lands), lands, attack, damage, 0f };
                    return (bool)lossMethod.Invoke(null, a);
                }
                a = null;
                entries.Add(Line(11, 13));
                Check(Loss(1, 0f) && Near((float)a[6], 0.5f), "held attack at exactly 95 percent keeps its real 5 percent contribution");
                Check(Loss(1, 2f) && Near((float)a[6], 2.3f), "joint attack and damage removal includes their cross term exactly once");
                Check(Loss(1, 0f, 0.5f) && Near((float)a[6], 0.25f), "captured concealment pass remains in the comparison");
                entries.Clear(); entries.Add(Line(21, 13));
                Check(Loss(1, 0f) && Near((float)a[6], 0f), "attack far above the 95 percent cap loses nothing");
                entries.Clear(); entries.Add(Line(11, 13)); entries.Add(Line(6, 13));
                Check(Loss(1, 0f) && Near((float)a[6], 1f), "full routine prices every iterative from its own raw attack bonus");
                lineType.GetField("Weapon", all).SetValue(entries[1], other);
                Check(!Loss(1, 0f), "mixed-weapon routine is withheld instead of applying the primary stat to another hand");
                entries.Clear(); entries.Add(Line(11, 13, raw: 10f, hit: 2f, dr: 8));
                Check(Loss(0, 3f) && Near((float)a[6], 1.9f), "physical damage removal stops at native DR zero");
                entries.Clear(); entries.Add(Line(11, 13, hit: 5f, factor: 0.5f));
                Check(Loss(0, 2f) && Near((float)a[6], 0.95f), "physical reduction factor is applied exactly once");
                entries.Clear(); entries.Add(Line(11, 13, threat: 0.1f, critical: 3));
                Check(Loss(0, 2f) && Near((float)a[6], 2.28f), "native-derived critical range and multiplier remain in lost damage");
                entries.Clear(); entries.Add(Line(11, 13, sneak: 5f, blade: 3f));
                Check(Loss(0, 2f) && Near((float)a[6], 1.9f), "physical modifier removal preserves separate sneak and kinetic blast damage");
                Check(Loss(0, 0f) && Near((float)a[6], 0f), "zero nonstacking increment removes no damage");
                Check(!Loss(-1, 0f) && !Loss(0, float.NaN), "invalid scalar losses remain unknown");
                entries.Clear(); Check(!Loss(1, 0f), "empty routine is unavailable");

                var read = SaveBoostCheck.ReferencedMembers(math.GetMethod("TryHeldWeaponLoss", all)).ToArray();
                Check(read.Any(m => m.Name == "RoutineIdentity") && read.Any(m => m.Name == "HeldWeaponRoutineLoss"), "compiled cache guard requires the exact current routine identity");
                Check(!read.Any(m => m.Name == "ReadRoutineCore" || m.Name == "EstimateWeaponAttack" || m.Name == "LandsPastDefences" || m.Name == "Get" || m.Name == "Trigger"),
                    "held scalar comparison requests no routine, native defence, magus part or rule calculation");
                var score = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AppraiseParts", all)).ToArray();
                Check(score.Any(m => m.Name == "TryAppraiseHeldWeaponLoss"), "compiled held appraisal uses the tested loss route");
                var ordinary = SaveBoostCheck.ReferencedMembers(math.GetMethod("EstimateWeaponAttackCore", all)).ToArray();
                Check(ordinary.Any(m => m.Name == "ExpectedWeaponLine"), "ordinary forecasts and held loss share the exact damage kernel");
            }
            catch (Exception ex) { bad++; lines.Add("HELD-WEAPON FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"HELD-WEAPON {checks} checks, failures {bad}; native constant graphs, cached raw-routine and attribute fixtures; no actor, rule, buff application or Unity timing execution");
            return lines;
        }
    }
}
