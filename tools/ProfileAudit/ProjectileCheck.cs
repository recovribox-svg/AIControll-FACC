// Actual simple delivery arrays and native rank progressions; no actor setup, projectiles or damage rules execute.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class ProjectileCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("PROJECTILES FAILED: " + why); } }
            try
            {
                var count = typeof(CombatMath).GetMethod("ProjectileDeliveries", hidden);
                var observed = typeof(CombatMath).GetMethod("ObserveProjectileRolls", hidden);
                var ranks = typeof(MechanicsContext).GetField("m_Ranks", hidden);
                var progression = typeof(ContextRankConfig).GetMethod("ApplyProgression", hidden);
                var limits = typeof(ContextRankConfig).GetMethod("ApplyMinMax", hidden);
                Check(count != null && observed != null && ranks != null && progression != null && limits != null,
                    "production count reader and native rank bindings exist");
                if (count == null || observed == null || ranks == null || progression == null || limits == null)
                    throw new InvalidOperationException("projectile fixture bindings unavailable");
                var all = loaded.OfType<BlueprintAbility>().ToList();
                var ctx = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                var rankValues = new int[Enum.GetValues(typeof(AbilityRankType)).Cast<AbilityRankType>().Max(r => (int)r) + 1];
                ranks.SetValue(ctx, rankValues);
                void Fixture(string name, params (int Level, int Count)[] cases)
                {
                    var blueprint = all.FirstOrDefault(a => a.name == name);
                    if (blueprint == null) { lines.Add("PROJECTILES SKIPPED: " + name + " outside scope"); return; }
                    var profile = AbilityProfile.Get(blueprint);
                    var delivery = profile.SimpleProjectiles;
                    Check(delivery != null, name + " actual root simple delivery recognized");
                    if (delivery == null) return;
                    var config = blueprint.GetComponents<ContextRankConfig>().Single(c => c.Type == delivery.MaxProjectilesCountRank);
                    foreach (var test in cases)
                    {
                        int nativeRank = (int)progression.Invoke(config, new object[] { test.Level });
                        nativeRank = (int)limits.Invoke(config, new object[] { nativeRank, false, null });
                        rankValues[(int)delivery.MaxProjectilesCountRank] = nativeRank;
                        int actual = (int)count.Invoke(null, new object[] { profile, ctx });
                        Check(actual == test.Count && actual == delivery.Projectiles.Take(nativeRank).Count(),
                            $"{name} CL{test.Level}: native Take(rank) count {test.Count}, read {actual}");
                    }
                    rankValues[(int)delivery.MaxProjectilesCountRank] = -1;
                    Check((int)count.Invoke(null, new object[] { profile, ctx }) == 0, name + " negative rank yields no projectile");
                    rankValues[(int)delivery.MaxProjectilesCountRank] = 1000;
                    Check((int)count.Invoke(null, new object[] { profile, ctx }) == delivery.Projectiles.Length,
                        name + " array length bounds a rank past the last projectile");
                    Check((bool)observed.Invoke(null, new object[] { profile }) == delivery.NeedAttackRoll,
                        name + " diagnostic separates actual attack rolls, while automatic missiles remain a cast sample");
                }
                Fixture("ScorchingRay", (1, 1), (7, 2), (11, 3), (100, 3));
                Fixture("MagicMissile", (1, 1), (3, 2), (9, 5), (100, 5));
                Fixture("HellfireRay", (11, 1), (15, 2), (19, 3), (100, 3));
                var cone = all.FirstOrDefault(a => a.name == "BurningHands");
                if (cone != null) Check((int)count.Invoke(null, new object[] { AbilityProfile.Get(cone), ctx }) == 1,
                    "actual cone is not multiplied by simple-projectile logic");
                var holder = all.FirstOrDefault(a => a.name == "MagicHackDefaultSlot1");
                var ray = all.FirstOrDefault(a => a.name == "ScorchingRay");
                if (holder != null && ray != null)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new Kingmaker.UnitLogic.Abilities.MagicHackData {
                        Spell1 = ray, Spell2 = ray, DeliverBlueprint = ray, SpellLevel = 2 };
                    var fusion = AbilityProfile.Get(data);
                    Check((int)count.Invoke(null, new object[] { fusion, ctx }) == 1,
                        "fusion's two ordered payloads do not inherit the selected spell's three rays");
                    Check(!(bool)observed.Invoke(null, new object[] { fusion }), "fusion retains its own packet grouping");
                }
                // Verify the compiled production route: corrected counts must reach real estimates, not just fixtures.
                var estimate = typeof(CombatMath).GetMethod("EstimateAbilityDamageRaw", hidden);
                Check(SaveBoostCheck.ReferencedMembers(estimate).Any(m => m.Module == count.Module && m.MetadataToken == count.MetadataToken),
                    "production raw damage estimator calls the native projectile-count reader");
            }
            catch (Exception ex) { bad++; lines.Add("PROJECTILES FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"PROJECTILES {checks} checks, failures {bad}; actual arrays/native ranks and compiled estimate route; no actors or casts");
            return lines;
        }
    }
}
