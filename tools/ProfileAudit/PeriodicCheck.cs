// ★ damage over time on ordinary spells (docs/PERIODIC_DAMAGE.md): the carriers ordinary (unfused) abilities apply, which
// are priced as damage, which keep their older rule (a carrier behind a save), and the compiled routes. Actor-free.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace ProfileAudit
{
    internal static class PeriodicCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("PERIODIC FAILED: " + why); } }
            try
            {
                var engine = typeof(TacticalEngine);
                var carriersOnly = engine.GetMethod("CarriersOnly", Hidden);
                var hasCarrier = engine.GetMethod("HasPeriodicCarrier", Hidden);
                var applications = typeof(AbilityProfile).GetProperty("BuffApplications", Hidden);
                bool Only(AbilityProfile p) => (bool)carriersOnly.Invoke(null, new object[] { p });
                bool Has(AbilityProfile p) => (bool)hasCarrier.Invoke(null, new object[] { p });
                IEnumerable<object> Apps(AbilityProfile p) => ((System.Collections.IEnumerable)applications.GetValue(p)).Cast<object>();
                bool Field(object o, string name) => (bool)o.GetType().GetField(name, Hidden).GetValue(o);

                var all = loaded.OfType<BlueprintAbility>().ToList();
                AbilityProfile Profile(string name) => AbilityProfile.Get(CasterPolicyCheck.Data(all.First(b => b.name == name)));
                foreach (var name in new[] { "AcidArrow", "MoltenOrb" })
                {
                    var p = Profile(name);
                    Check(p != null && Has(p) && Only(p) && Apps(p).Where(a => Field(a, "PeriodicDamageOnly")).All(a => !Field(a, "UnderSave")),
                        $"ordinary {name}: its damage over time is priced as damage, applied whatever the save, no debuff guessed");
                }
                // Incendiary Runes burns only on a failed save: cast on its own it keeps the older rule (a fused cast reads its
                // save path and prices it, FUSION-DOT).
                var runes = Profile("IncendiaryRunes");
                Check(runes != null && Has(runes) && !Only(runes) &&
                      Apps(runes).Where(a => Field(a, "PeriodicDamageOnly")).All(a => Field(a, "UnderSave")),
                    "ordinary Incendiary Runes: its burning depends on the save, so it keeps the older rule");

                // Every ordinary ability with a carrier: priced, or kept on its older rule because the carrier depends on a save.
                int priced = 0, underSave = 0;
                var pricedNames = new List<string>();
                var savedNames = new List<string>();
                foreach (var bp in all)
                {
                    AbilityProfile p;
                    try { p = AbilityProfile.Get(CasterPolicyCheck.Data(bp)); }
                    catch { continue; }
                    if (p == null || !Has(p)) continue;
                    var carriers = Apps(p).Where(a => Field(a, "PeriodicDamageOnly")).ToList();
                    if (carriers.Any(a => !Field(a, "UnderSave"))) { priced++; if (pricedNames.Count < 12) pricedNames.Add(bp.name); }
                    if (carriers.Any(a => Field(a, "UnderSave"))) { underSave++; if (savedNames.Count < 8) savedNames.Add(bp.name); }
                }
                Check(priced >= 3, $"ordinary abilities whose damage over time is priced: {priced}");
                lines.Add($"PERIODIC ordinary abilities priced: {priced} (e.g. {string.Join(", ", pricedNames)}); " +
                          $"with a carrier behind a save (older rule kept): {underSave}" +
                          (savedNames.Count > 0 ? $" (e.g. {string.Join(", ", savedNames)})" : ""));

                // --- compiled routes ---
                var score = Body(engine, "ScoreAbility");
                Check(Calls(score, engine, "PeriodicExpected") && Calls(score, engine, "CarriersOnly") &&
                      Calls(score, engine, "HasPeriodicCarrier") && Calls(score, engine, "PeriodicAllyDamage"),
                    "ScoreAbility prices the ticks on enemies and allies, and skips the control section for carriers only");
                var value = engine.GetMethods(Hidden).Where(m => m.Name == "DamageValue" && m.GetParameters().Length == 5).ToList();
                Check(value.Count == 1 && value[0].GetParameters()[4].Name == "laterDamage",
                    "the ordinary damage value takes later damage for HP and progress, not the kill now");
                var periodic = Body(engine, "TryPeriodicDamage");
                Check(periodic.Any(m => m.Name == "UnderSave") && Calls(periodic, typeof(AbilityProfile), "FusionBuffPath"),
                    "a fused cast reads its save path; an ordinary spell prices only carriers outside a save branch");
                Check(Body(typeof(AbilityProfile), "Walk").Any(m => m.Name == "m_SaveDepth"),
                    "the profile walk records whether each application depends on a save");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"PERIODIC {checks} checks, {bad} failures");
            return lines;
        }
    }
}
