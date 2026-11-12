// Actor-free exported guard regression and native flag-store reads. Does not initialize Game/Player or cast a form.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.AreaLogic.QuestSystem;
using Kingmaker.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Conditions;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class CampaignFlagCheck
    {
        private sealed class UninspectedFlag : FlagUnlocked { }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("CAMPAIGN-FLAG FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var safe = typeof(AbilityProfile).GetMethod("IsSafe", all);
                bool Safe(Condition c) => (bool)safe.Invoke(null, new object[] { c });
                foreach (string name in new[] { "ShifterWildShapeGriffonAbillity", "ShifterWildShapeGriffonAbillity9", "ShifterWildShapeGriffonAbillity14" })
                {
                    var bp = loaded.OfType<BlueprintAbility>().FirstOrDefault(b => b.name == name);
                    Check(bp != null, "actual exported form ability present: " + name);
                    if (bp == null) continue;
                    var profile = AbilityProfile.Get(bp);
                    var guards = (Dictionary<BlueprintBuff, List<EffectGuard[]>>)typeof(AbilityProfile).GetField("m_BuffGuards", all).GetValue(profile);
                    var checkers = guards.Values.SelectMany(paths => paths).SelectMany(path => path).Select(g => g.Checker)
                        .Where(c => c != null).Distinct().ToArray();
                    var flags = checkers.SelectMany(c => c.Conditions ?? Array.Empty<Condition>()).OfType<FlagUnlocked>().ToArray();
                    Check(flags.Length > 0 && flags.All(Safe), "played flag guards no longer force unknown form applicability: " + name);
                    Check(checkers.All(c => (c.Conditions ?? Array.Empty<Condition>()).All(Safe)), "complete actual form guard paths are read-only allowlisted: " + name);
                }
                var ordinary = (FlagUnlocked)RuntimeHelpers.GetUninitializedObject(typeof(FlagUnlocked));
                var derived = (UninspectedFlag)RuntimeHelpers.GetUninitializedObject(typeof(UninspectedFlag));
                Check(Safe(ordinary), "exact inspected native flag condition is allowed");
                Check(!Safe(derived), "uninspected mod subclass is not implicitly allowed");

                // Only the native manager's BCL dictionary is supplied. No Game/Player singleton, actor, blueprint
                // initializer, event, unlock/lock/setter or component action is executed.
                var flag = (BlueprintUnlockableFlag)RuntimeHelpers.GetUninitializedObject(typeof(BlueprintUnlockableFlag));
                var locked = (BlueprintUnlockableFlag)RuntimeHelpers.GetUninitializedObject(typeof(BlueprintUnlockableFlag));
                flag.AssetGuid = BlueprintGuid.Parse("dbbca22553bb440aa8f4f79147371486");
                locked.AssetGuid = BlueprintGuid.Parse("00000000000000000000000000000001");
                var store = new Dictionary<BlueprintUnlockableFlag, int> { [flag] = 7 };
                var manager = (UnlockableFlagsManager)RuntimeHelpers.GetUninitializedObject(typeof(UnlockableFlagsManager));
                typeof(UnlockableFlagsManager).GetField("m_UnlockedFlags", all).SetValue(manager, store);
                Check(manager.IsUnlocked(flag) && manager.GetFlagValue(flag) == 7, "native getter reads unlocked flag and its actual value");
                Check(!manager.IsUnlocked(locked) && manager.GetFlagValue(locked) == 0, "missing flag remains locked with native zero value");
                Check(store.Count == 1 && store[flag] == 7 && !store.ContainsKey(locked), "flag reads never add/unlock an absent entry");
                store[flag] = 0; // test fixture steering, not a production setter
                Check(manager.IsUnlocked(flag) && manager.GetFlagValue(flag) == 0, "unlocked zero-value flag remains distinct from a locked flag");
                bool Calls(MethodInfo m, string name) => SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == name);
                var native = typeof(FlagUnlocked).GetMethod("CheckCondition", all);
                Check(Calls(native, "get_IsUnlocked") && Calls(native, "get_Value"), "native condition owns unlocked/value whitelist/blacklist logic");
                Check(!SaveBoostCheck.ReferencedMembers(native).Any(m => m.Name == "Unlock" || m.Name == "Lock" || m.Name == "SetFlagValue" || m.Name == "RunAction"),
                    "allowlisted native condition runs no quest action or flag write");
                Check(Calls(typeof(UnlockableFlagsManager).GetMethod("IsUnlocked", all), "ContainsKey") &&
                    Calls(typeof(UnlockableFlagsManager).GetMethod("GetFlagValue", all), "TryGetValue"), "supplied manager uses read-only dictionary queries");
                var run = typeof(AbilityProfile).GetMethod("Run", all);
                Check(Calls(run, "Check"), "production keeps native Condition.Check including Not instead of copying flag predicates");
                var finish = typeof(TacticalEngine).GetMethod("FinishFormAppraisal", all);
                Check(Calls(finish, "FirstThisFight"), "zero/profitable form diagnostic uses bounded existing fight ledger");
                Check(!SaveBoostCheck.ReferencedMembers(finish).Any(m => m.Name == "Trigger" || m.Name == "CreateContext" || m.Name == "ShifterFormDamage"),
                    "form diagnostic adds no rule/context/forecast calculation");
            }
            catch (Exception ex) { bad++; lines.Add("CAMPAIGN-FLAG FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"CAMPAIGN-FLAG {checks} checks, failures {bad}; actual Griffon guard paths, native dictionary reads and compiled routes; no Game/Player, actor, cast or Unity timing execution");
            return lines;
        }
    }
}
