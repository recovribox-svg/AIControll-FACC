// Exported native HP kinds, cached quote projection and compiled swarm-exit routes. No initialized actor or native move.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class SwarmEscapeCheck
    {
        private sealed class UninspectedDamage : ContextActionDealDamage { }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("SWARM-ESCAPE FAILED: " + why); } }
            try
            {
                var graph = typeof(CombatMath).GetMethod("SwarmDirectHpGraph", flags);
                var known = typeof(CombatMath).GetMethod("KnownSwarmDirectHp", flags);
                var kind = typeof(ContextActionDealDamage).GetField("m_Type", flags);
                var damageField = typeof(AbilityProfile).GetField("Damage", flags);
                var clone = typeof(object).GetMethod("MemberwiseClone", flags);
                var victimType = typeof(CombatMath).GetNestedType("SwarmCast", flags).GetNestedType("CastVictim", flags);
                var victim = Activator.CreateInstance(victimType, true);
                void Set(string field, object value) => victimType.GetField(field, flags).SetValue(victim, value);
                bool Admitted(AbilityProfile profile) => (bool)graph.Invoke(null, new object[] { profile });
                float Quote(AbilityProfile profile) => (float)known.Invoke(null, new object[] { profile, victim });
                AbilityProfile Changed(AbilityProfile original, params GameAction[] actions)
                {
                    var copy = (AbilityProfile)clone.Invoke(original, null);
                    damageField.SetValue(copy, new List<GameAction>(actions));
                    return copy;
                }
                ContextActionDealDamage Damage(string nativeKind)
                {
                    var action = (ContextActionDealDamage)RuntimeHelpers.GetUninitializedObject(typeof(ContextActionDealDamage));
                    kind.SetValue(action, Enum.Parse(kind.FieldType, nativeKind));
                    return action;
                }
                var abilities = loaded.OfType<BlueprintAbility>().ToArray();
                AbilityProfile first = null;
                foreach (string name in new[] { "RatSwarmDamageAbility", "VescavorDamageAbility", "SpiderSwarmDamageAbility" })
                {
                    var ability = abilities.SingleOrDefault(a => a.name == name);
                    if (ability == null) { lines.Add("SWARM-ESCAPE SKIPPED: " + name + " graph is absent"); continue; }
                    var profile = AbilityProfile.Get(ability);
                    first = first ?? profile;
                    Check(Admitted(profile), name + " damage estimate contains only exact native direct-HP actions");
                    var selector = typeof(CombatMath).GetMethod("ReadSwarmCastBlueprint", flags).Invoke(null, new object[] { ability });
                    Check(selector != null && selector == ability.GetComponent<AbilityTargetsAround>(), name + " remains inside the bounded native cast contract");
                }
                if (first != null)
                {
                    Set("KnownHarm", true); Set("Damage", 3.5f); Set("Extra", 500f);
                    Check(Quote(first) == 3.5f, "known direct HP is retained without poison/control Extra");
                    Set("KnownHarm", false); Check(Quote(first) == 0f, "unknown native consequence gets no HP reserve");
                    Set("KnownHarm", true); Set("Damage", 0f); Check(Quote(first) == 0f, "known immune/zero native damage gets no HP reserve");
                    Set("Damage", float.NaN); Check(Quote(first) == 0f, "NaN native damage stays unavailable");
                    Set("Damage", float.PositiveInfinity); Check(Quote(first) == 0f, "infinite native damage stays unavailable");
                    Set("Damage", 3.5f);
                    Check(!Admitted(Changed(first)), "empty damage graph gets no HP reserve");
                    Check(!Admitted(Changed(first, Damage("AbilityDamage"))), "native ability damage is not HP somebody heals later");
                    Check(!Admitted(Changed(first, Damage("EnergyDrain"))), "native negative levels are not direct HP");
                    Check(!Admitted(Changed(first, Damage("Damage"), Damage("AbilityDamage"))), "mixed HP/stat graph is withheld as a whole");
                    var unknown = Damage("Damage"); kind.SetValue(unknown, Enum.ToObject(kind.FieldType, int.MaxValue));
                    Check(!Admitted(Changed(first, unknown)), "unrecognized native kind does not fall back to HP");
                    Check(!Admitted(Changed(first, (GameAction)RuntimeHelpers.GetUninitializedObject(typeof(UninspectedDamage)))),
                        "uninspected damage subclass is withheld");
                    Check(!Admitted(Changed(first, new ContextActionKill())), "non-HP provider cannot buy HP reserve");
                }
                Check(!Admitted(null), "missing profile stays unknown");
                Check(Math.Abs(ValueModel.SwarmEscapeGain(0f, 0f, 20f, 3.5f, 0.5f, 1f) - 0.9f) < .0001f,
                    "healthy nonlethal prevention can exceed the ordinary half-point action bar");
                Check(ValueModel.SwarmEscapeGain(0f, 0f, 20f, 8f, 10f, 1f) == 0f, "destination cast harm offsets HP supposedly avoided");
                Check(Math.Abs(ValueModel.SwarmEscapeGain(.4f, .1f, 20f, 10f, 0f, 1f) - 9f) < .0001f,
                    "survival difference is counted once alongside the separate HP reserve");
                var at = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("SwarmDirectHpAt", flags)).ToArray();
                Check(at.Any(m => m.Name == "Enemies") && at.Any(m => m.Name == "IsDisabled") && at.Any(m => m.Name == "SwarmSpaceOf"),
                    "both spots read the current whole enemy list and skip disabled/unrecognized sources");
                Check(at.Any(m => m.Name == "SwarmShareAt") && at.Any(m => m.Name == "Consequence") && at.Any(m => m.Name == "KnownSwarmDirectHp"),
                    "native coverage and cached per-victim quote feed local HP exposure");
                Check(!at.Any(m => m.Name == "AttachedDamageOf" || m.Name == "SwarmRoundHp" || m.Name == "AfflictionHp" ||
                                      m.Name == "EstimateAbilityDamage" || m.Name == "CreateContext" || m.Name == "RunAction" || m.Name == "TriggerRule"),
                    "HP exposure adds no attached/equivalent harm or fresh context/rule estimate");
                var projection = SaveBoostCheck.ReferencedMembers(known).ToArray();
                Check(projection.Any(m => m.Name == "KnownHarm") && projection.Any(m => m.Name == "Damage") &&
                      !projection.Any(m => m.Name == "Harm" || m.Name == "Extra"), "compiled HP projection cannot use aggregate poison/control harm");
                var exit = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("AddOutOfSwarmCandidate", flags)).ToArray();
                Check(exit.Any(m => m.Name == "SpotDrop") && exit.Any(m => m.Name == "SwarmDirectHpAt") && exit.Any(m => m.Name == "SwarmEscapeGain"),
                    "existing swarm exit uses shared survival and symmetric HP prevention");
                Check(exit.Any(m => m.Name == "MoveRisk") && exit.Any(m => m.Name == "StopChance") && exit.Any(m => m.Name == "Cost"),
                    "stops, AoO/area/ally risk costs remain after prevention gain");
                var delegates = exit.OfType<MethodInfo>().Where(m => m.Module == typeof(TacticalEngine).Module &&
                    m.Name.StartsWith("<AddOutOfSwarmCandidate>", StringComparison.Ordinal)).SelectMany(SaveBoostCheck.ReferencedMembers).ToArray();
                Check(delegates.Any(m => m.Name == "Affordable") && delegates.Any(m => m.Name == "ReachesFrom") && delegates.Any(m => m.Name == "Slot"),
                    "offensive opportunity still checks affordability, reach and full-round action slot");
            }
            catch (Exception ex) { bad++; lines.Add("SWARM-ESCAPE FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"SWARM-ESCAPE {checks} checks, failures {bad}; native damage kinds, cached quotes and compiled routes; live actor/move NOT TESTED");
            return lines;
        }
    }
}
