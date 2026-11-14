// F1: native slot/selected-component contract and compiled scorer handoff. No actor or geometric query is run.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;

namespace ProfileAudit
{
    internal static class FusionConeCheck
    {
        private delegate AbilityDeliverProjectile Resolve(AbilityData ability, out BlueprintAbility blueprint);
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION-CONE FAILED: " + why); } }
            try
            {
                var type = typeof(CombatMath).Assembly.GetType("CompanionAI_Pathfinder.Tactics.ProjectileCone", true);
                var method = type.GetMethod("DeliveryComponent", hidden);
                var resolve = (Resolve)Delegate.CreateDelegate(typeof(Resolve), method);
                var all = loaded.OfType<BlueprintAbility>().ToList();
                var holder = all.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                var fire = all.First(b => b.name == "Fireball");
                Check(holder.GetComponent<AbilityDeliverProjectile>() == null, "actual native slot contains no cone delivery");
                Check(resolve(null, out var absent) == null && absent == null, "missing cast cannot invent a footprint");
                var cases = new[] { "BurningHands", "ColorSpray", "SilverDarts", "Shout", "Fear", "ConeOfCold", "BansheeBlast" };
                AbilityData last = null;
                foreach (string name in cases)
                {
                    var source = all.First(b => b.name == name);
                    var component = source.GetComponent<AbilityDeliverProjectile>();
                    Check(component?.Type == AbilityProjectileType.Cone, name + " actual cone fixture");
                    var ordinary = CasterPolicyCheck.Data(source);
                    Check(ReferenceEquals(resolve(ordinary, out var ordinaryBp), component) && ordinaryBp == source,
                        name + " ordinary cast retains its exact component");
                    var fused = CasterPolicyCheck.Data(holder);
                    fused.MagicHackData = new MagicHackData { Spell1 = fire, Spell2 = source, DeliverBlueprint = source,
                        SavingThrowType = SavingThrowType.Reflex, SpellSchool = SpellSchool.Evocation, SpellTargetType = SpellTargetType.Cone, SpellLevel = 6 };
                    Check(AbilityProfile.Get(fused).IsProjectileCone && ReferenceEquals(resolve(fused, out var selected), component) && selected == source,
                        name + " native holder resolves the selected cone, not its empty slot");
                    fused.MagicHackData.DeliverBlueprint = fire;
                    Check(resolve(fused, out var changed) == null && changed == fire,
                        name + " edited burst delivery cannot reuse a stale cone component");
                    fused.MagicHackData.DeliverBlueprint = source; last = fused;
                }
                var create = type.GetMethod("TryCreate", hidden);
                Check(SaveBoostCheck.ReferencedMembers(create).Any(m => m.Module == method.Module && m.MetadataToken == method.MetadataToken),
                    "actual native-query entry calls the checked selected-delivery resolver");
                var field = typeof(TacticalContext).GetField("FriendlyFireVeto");
                var score = typeof(TacticalEngine).GetMethod("ScoreAbility", hidden);
                var evaluate = typeof(TacticalEngine).GetMethod("EvaluateAbility", hidden);
                Check(field != null && SaveBoostCheck.ReferencedMembers(score).Any(m => m.Module == field.Module && m.MetadataToken == field.MetadataToken) &&
                      SaveBoostCheck.ReferencedMembers(evaluate).Any(m => m.Module == field.Module && m.MetadataToken == field.MetadataToken),
                    "scorer's hard friendly-fire veto reaches the bounded re-aim admission");
                var turns = (float[])typeof(TacticalEngine).GetField("ConeTurns", hidden).GetValue(null);
                Check(turns.Length == 6, "existing six-turn work bound retained");
                for (int i = 0; i < 256; i++) resolve(last, out _);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) resolve(last, out _);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(allocated == 0, "warm selected-component resolution allocates zero bytes in 10000 calls");
                lines.Add($"FUSION-CONE warm resolution: 10000 calls, {allocated} bytes; metadata-only harness microcheck, not Unity timing");
            }
            catch (Exception ex) { bad++; lines.Add("FUSION-CONE FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"FUSION-CONE {checks} checks, failures {bad}; real slots/cones, metadata edits and compiled handoff; no actor, recipient/LOS query, cast or Unity execution");
            return lines;
        }
    }
}
