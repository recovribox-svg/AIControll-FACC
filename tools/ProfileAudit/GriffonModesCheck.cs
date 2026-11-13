// ★ v0.3.52: production cached-routine scalars and compiled integration; no actor setup, mode switch or flight.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Items;

namespace ProfileAudit
{
    internal static class GriffonModesCheck
    {
        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("GRIFFON-MODES FAILED: " + why); } }
            bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var math = typeof(CombatMath); var engine = typeof(TacticalEngine);
                var rt = math.GetNestedType("AttackRoutine", all); var lt = math.GetNestedType("AttackLine", all);
                var routine = Activator.CreateInstance(rt, true); var entries = (IList)rt.GetField("Lines", all).GetValue(routine);
                var weapon = (ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(ItemEntityWeapon));
                var other = (ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(ItemEntityWeapon));
                object Line(ItemEntityWeapon w, int index = 0, int ab = 0, int ac = 11, float raw = 10f, float hit = 10f,
                            int dr = 0, float factor = 1f, float threat = 0f, int crit = 2, float sneak = 0f, float blade = 0f)
                {
                    var l = Activator.CreateInstance(lt, true);
                    void Set(string name, object v) => lt.GetField(name, all).SetValue(l, v);
                    Set("Weapon", w); Set("Index", index); Set("AttackBonus", ab); Set("ArmorClass", ac); Set("RawPerHit", raw);
                    Set("GameHit", hit); Set("DamageReduction", dr); Set("PhysicalFactor", factor); Set("ThreatChance", threat);
                    Set("CritMultiplier", crit); Set("Sneak", sneak); Set("BladeBlast", blade); return l;
                }
                var price = math.GetMethod("GriffonModeQuote", all);
                var quote = new WeaponAttackEstimate { Weapon = weapon, Reach = 3.7f, EndsRegeneration = true };
                typeof(WeaponAttackEstimate).GetField("RoutineLands", all).SetValue(quote, 1f);
                WeaponAttackEstimate Price(Func<ItemEntityWeapon, int, KeyValuePair<int, float>> delta)
                {
                    rt.GetField("Attacks", all).SetValue(routine, entries.Count);
                    return (WeaponAttackEstimate)price.Invoke(null, new object[] { routine, quote, delta });
                }
                entries.Add(Line(weapon)); var unchanged = Price(null);
                Check(Near(unchanged.ExpectedDamage, 5f) && Near(unchanged.AnyHitChance, 0.5f), "unchanged native line retains current damage and hit chance");
                var off = Price((w, i) => new KeyValuePair<int, float>(4, 0f));
                Check(Near(off.ExpectedDamage, 7f) && Near(off.FirstHitChance, 0.7f), "removing a four-point attack penalty changes the cached native roll");
                Check(off.Attacks == 1 && off.EndsRegeneration && off.Weapon == weapon && Near(off.Reach, 3.7f), "damage-value metadata is retained");
                entries.Clear(); entries.Add(Line(weapon, ab: 30));
                Check(Near(Price((w, i) => new KeyValuePair<int, float>(4, 0f)).ExpectedDamage, 9.5f), "attack changes beyond the native hit cap earn no extra hits");
                entries.Clear(); entries.Add(Line(weapon, hit: 2f, dr: 8));
                Check(Near(Price((w, i) => new KeyValuePair<int, float>(0, 3f)).ExpectedDamage, 2.5f), "mode damage crosses the already-quoted native DR threshold once");
                entries.Clear(); entries.Add(Line(weapon)); entries.Add(Line(other, index: 1, ab: -5));
                var mixed = Price((w, i) => w == weapon ? new KeyValuePair<int, float>(4, 2f) : new KeyValuePair<int, float>(0, 1f));
                Check(Near(mixed.ExpectedDamage, 11.15f) && Near(mixed.AnyHitChance, 0.775f) && Near(mixed.FirstHitChance, 0.7f),
                    "distinct native weapons/iteratives receive their own mode deltas and joint hit chance");
                typeof(WeaponAttackEstimate).GetField("RoutineLands", all).SetValue(quote, 0.5f);
                Check(Near(Price((w, i) => w == weapon ? new KeyValuePair<int, float>(4, 2f) : new KeyValuePair<int, float>(0, 1f)).ExpectedDamage, 5.575f),
                    "captured concealment applies once without a second native defense query");
                typeof(WeaponAttackEstimate).GetField("RoutineLands", all).SetValue(quote, 1f);
                entries.Clear(); entries.Add(Line(weapon, threat: 0.1f, crit: 3));
                Check(Near(Price((w, i) => new KeyValuePair<int, float>(0, 2f)).ExpectedDamage, 7.2f), "existing native critical range/multiplier stay in the mode price");
                entries.Clear(); entries.Add(Line(weapon, hit: 5f, factor: 0.5f, sneak: 5f, blade: 3f));
                Check(Near(Price((w, i) => new KeyValuePair<int, float>(0, 2f)).ExpectedDamage, 7f), "physical factor, sneak and separate blade payload are not counted twice");
                entries.Clear(); Check(Price(null) == null, "empty routine is unavailable, not an invented useful attack");

                var guard = SaveBoostCheck.ReferencedMembers(math.GetMethod("RepriceGriffonModes", all)).ToArray();
                Check(guard.Any(m => m.Name == "RoutineIdentity") && guard.Any(m => m.Name == "Frame") && guard.Any(m => m.Name == "TryGetValue"),
                    "production requires the exact current frame/cached routine identity");
                Check(!guard.Any(m => m.Name == "ReadRoutineCore" || m.Name == "EstimateWeaponAttack" || m.Name == "LandsPastDefences" || m.Name == "Trigger"),
                    "stale quote never triggers an extra native own-weapon calculation");
                var scalar = SaveBoostCheck.ReferencedMembers(price).ToArray();
                Check(scalar.Any(m => m.Name == "ExpectedWeaponLine") && !scalar.Any(m => m.Name == "Get" || m.Name == "Trigger" || m.Name == "AddBuff"),
                    "compiled mode quote is the shared scalar kernel, with no actor/rule/state preview");
                var evaluate = SaveBoostCheck.ReferencedMembers(engine.GetMethod("EvaluateGriffonAttack", all)).ToArray();
                Check(evaluate.Any(m => m.Name == "ChooseAttackModes") && evaluate.Any(m => m.Name == "ModeAt") && evaluate.Any(m => m.Name == "ModeFactor"),
                    "safe leap candidate supplies landing and offense factor to shared mode comparison");
                Check(Array.FindIndex(evaluate, m => m.Name == "EstimateGriffonAttack") < Array.FindIndex(evaluate, m => m.Name == "ChooseAttackModes"),
                    "bounded native quote precedes mode repricing within the same leap evaluation");
                var generated = engine.GetNestedTypes(all).SelectMany(t => t.GetMethods(all)).Where(m => m.Name.Contains("ChooseAttackModes"))
                    .SelectMany(SaveBoostCheck.ReferencedMembers).ToArray();
                Check(generated.Any(m => m.Name == "RepriceGriffonModes") && generated.Any(m => m.Name == "EstimateWeaponAttack"),
                    "shared chooser retains ordinary pricing and adds only the cached leap branch");
                Check(generated.Any(m => m.Name == "Armor") && generated.Any(m => m.Name == "DefenseValue"),
                    "mode defense is priced at the landing including the actual charge armor delta");
                var defense = SaveBoostCheck.ReferencedMembers(engine.GetMethod("DefenseValue", all)).ToArray();
                Check(Array.FindIndex(defense, m => m.Name == "SwarmSpaceOf") >= 0 &&
                    Array.FindIndex(defense, m => m.Name == "SwarmSpaceOf") < Array.FindIndex(defense, m => m.Name == "EstimateWeaponAttack"),
                    "shared mode AC excludes recognized automatic swarm sources before nominal weapon quotes, retaining total danger");
                var ownership = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AttackModes", all)).ToArray();
                Check(ownership.Any(m => m.Name == "get_AiManagesToggles") && ownership.Any(m => m.Name == "PlayerOwnsToggle") && ownership.Any(m => m.Name == "SourceOf"),
                    "existing management setting, manual ownership and blocklist gates remain in the shared route");
                var execute = SaveBoostCheck.ReferencedMembers(engine.GetMethod("Execute", all)).ToArray();
                Check(Array.FindIndex(execute, m => m.Name == "ApplyModes") > Array.FindIndex(execute, m => m.Name == "Run" && m.DeclaringType.Name == "CommandDispatcher"),
                    "mode application remains after accepted dispatch, including ability mode plans");
            }
            catch (Exception ex) { bad++; lines.Add("GRIFFON-MODES FAILED: " + ex.GetBaseException()); }
            failures = bad; lines.Insert(0, $"GRIFFON-MODES {checks} checks, failures {bad}; cached native-layout scalars and compiled routes; no actor, toggle, attack, flight or Unity timing execution");
            return lines;
        }
    }
}
