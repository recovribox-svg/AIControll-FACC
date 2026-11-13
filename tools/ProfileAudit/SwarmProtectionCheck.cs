// ★ v0.3.50 playtest review: automatic swarm harm stays dangerous without buying attack-roll protection.
// Exported graph/scalar/compiled-route checks only; no actor, buff application or native attack execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class SwarmProtectionCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("SWARM-PROTECTION FAILED: " + why); } }
            try
            {
                var spider = loaded.OfType<BlueprintAbility>().SingleOrDefault(b =>
                    b.AssetGuid.ToString() == "f9b287e4e5d046f2b8e0b545de5e0548");
                if (spider == null) lines.Add("SWARM-PROTECTION SKIPPED: SpiderSwarmDamageAbility input graph is absent");
                else
                {
                    Check(spider.GetComponent<AbilityEffectRunAction>()?.Actions.Actions.OfType<ContextActionDealDamage>().Any() == true,
                        "actual spider cast deals damage directly");
                    Check(spider.GetComponent<AbilityDeliverProjectile>() == null &&
                          spider.GetComponent<AbilityTargetsAround>() != null, "actual spider damage has a selector and no projectile delivery");
                    var reader = typeof(CombatMath).GetMethod("ReadSwarmCastBlueprint", flags);
                    Check(reader?.Invoke(null, new object[] { spider }) == spider.GetComponent<AbilityTargetsAround>(),
                        "actual spider graph is admitted by the existing bounded automatic-swarm reader");
                }
                Check(ValueModel.AttackMitigationIncoming(40f, 0f, 20f) == 0f, "swarm-only attack protection is zero");
                Check(ValueModel.AttackMitigationIncoming(40f, 5f, 20f) == 10f, "mixed danger keeps its calibrated ordinary share");
                Check(ValueModel.AttackMitigationIncoming(17f, 30f, 30f) == 17f, "ordinary-only incoming is unchanged");
                Check(ValueModel.AttackMitigationIncoming(7f, 0f, 25f) == 0f, "predictive swarm-only share also stays zero");
                Check(ValueModel.TempHpAbsorbed(10f, 20f, 3f) > 0f, "total swarm harm still values temporary HP");
                var defence = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("DefenceOf", flags)).ToArray();
                Check(defence.Any(m => m.Name == "SwarmSpaceOf") && defence.Any(m => m.Name == "AttackMitigationIncoming"),
                    "compiled defence uses the current recognised swarm space and tested split");
                Check(defence.Any(m => m.Name == "IncomingParts") && defence.Any(m => m.Name == "IncomingEnemyDamage"),
                    "compiled defence retains existing calibrated total/exposure paths");
                Check(!defence.Any(m => m.Name == "AddBuff" || m.Name == "RunAction" || m.Name == "TriggerRule"),
                    "defence estimate executes no native action, buff application or rule event");
                var appraisal = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("AppraiseParts", flags)).ToArray();
                // Max(layer => ...) lifts the actual energy formula into a directly referenced generated delegate;
                // inspect that one-hop production route as well, rather than pretending it is in the parent IL body.
                var appraisalDelegates = appraisal.OfType<MethodInfo>().Where(m =>
                    m.Module == typeof(TacticalEngine).Module && m.Name.StartsWith("<AppraiseParts>", StringComparison.Ordinal))
                    .SelectMany(SaveBoostCheck.ReferencedMembers).ToArray();
                Check(appraisal.Any(m => m.Name == "AttackIncoming") && appraisal.Any(m => m.Name == "AttackPerHit") &&
                      appraisal.Any(m => m.Name == "AttackHitChance"), "compiled appraisal reads split attack damage, packet and hit chance");
                Check(appraisal.Any(m => m.Name == "Incoming") && appraisal.Any(m => m.Name == "TempHpAbsorbed") &&
                      appraisal.Any(m => m.Name == "EnergyProtectionOf") && appraisal.Any(m => m.Name == "EnergyHitsPerRound") &&
                      appraisalDelegates.Any(m => m.Name == "EnergyProtectionPerRound"),
                    "HP and energy protections retain total incoming through the compiled layer delegate");
                Check(appraisal.Any(m => m.Name == "ArmorGain") && appraisal.Any(m => m.Name == "MirrorImagesAbsorbed"),
                    "ordinary armour and images remain in the existing shared value model");
            }
            catch (Exception ex) { bad++; lines.Add("SWARM-PROTECTION FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"SWARM-PROTECTION {checks} checks, failures {bad}; exported graph, calibrated split and compiled routes; native actor/gameplay NOT TESTED");
            return lines;
        }
    }
}
