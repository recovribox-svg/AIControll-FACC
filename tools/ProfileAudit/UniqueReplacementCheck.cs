// Exported native invocation groups and source-context identities; no actor construction, buff application or cast.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class UniqueReplacementCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("UNIQUE-REPLACEMENT FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var assets = loaded.ToDictionary(b => b.AssetGuid.ToString());
                string[][] abilities = {
                    new[] { "4ff5f23901104b77a481eac095fc8967", "9d1b94bea3984ee3922d201ef49a9163", "cbcfe6f6242d4d588493cfca27fdee58", "3eec5b30dba74e6c8e3364526312c1dc", "6ba52e25e53d4c148ca4c1f780b4551f", "9ff93194eea84d569b9fdbe6bd284da3" },
                    new[] { "8ea302645ae341e9bcca4929d1822c77", "4d25cab4db9f4816a710265dfee532e4", "74fde6f81db9434cb94c6f4c2eef9ecc", "037b267ffad54f9dbeb8a9a145b57acd", "f6b7825f6b2c4b97936d90fd37adaf7e", "ada16c52b3224649990dc84f88a8a341" }
                };
                string[][] buffs = {
                    new[] { "39add2083bb14781bc273eb1a0fc9abf", "8ecd62d8b7124e708f8263521de2d0ab", "4bd5006416df4d13982bb957e82ae8ed", "3cc9e07250d1406381a8f29d504697e4", "35e151a0beee4d73a4706ac42c5c4dd3", "503131ef5904487c86a4dbe51b84b66c" },
                    new[] { "43743a509399408b9aeba87c517dbc2d", "3589cda0c53e47c98a96d247583af6b1", "71276ae418024ce08910fa201b6dded1", "2d1fb8e3a9394a469cfaf9ddc63f0897", "e83eb90ff21d438590cd105ea69c1dd3", "d0f50ea08a4f4f3fb0db79876f0bd3b8" }
                };
                if (abilities.SelectMany(a => a).Concat(buffs.SelectMany(b => b)).Any(id => !assets.ContainsKey(id)))
                    lines.Add("UNIQUE-REPLACEMENT SKIPPED: complete two-tier Profane Ascension input graphs not loaded");
                else
                {
                    for (int tier = 0; tier < 2; tier++)
                    for (int stat = 0; stat < 6; stat++)
                    {
                        var ability = (BlueprintAbility)assets[abilities[tier][stat]];
                        var buff = (BlueprintBuff)assets[buffs[tier][stat]];
                        var profile = AbilityProfile.Get(ability);
                        Check(profile.TargetBuffs.Count == 1 && profile.TargetBuffs[0] == buff,
                            $"tier {tier + 1} stat {stat}: exact target buff survives production reader");
                        var expected = buffs[tier].Where(id => id != buffs[tier][stat]).ToHashSet();
                        Check(profile.RemovedBuffs.Count == 5 && expected.SetEquals(profile.RemovedBuffs.Select(b => b.AssetGuid.ToString())),
                            $"tier {tier + 1} stat {stat}: only five same-tier siblings are removed");
                        var bonus = buff.GetComponent<AddContextStatBonus>();
                        Check(buff.GetComponent<UniqueBuff>() != null && bonus?.Descriptor == ModifierDescriptor.Profane &&
                              bonus.Value.ValueType == ContextValueType.Simple && bonus.Value.Value == 2,
                            $"tier {tier + 1} stat {stat}: native unique +2 profane stat buff");
                        Check(profile.ApplicationOf(buff)?.Permanent == true && ability.ActionType.ToString() == "Free",
                            $"tier {tier + 1} stat {stat}: permanent free placement is modelled");
                    }
                    Check(!buffs[0].Intersect(buffs[1]).Any(), "both tiers keep independent unique buff identities");
                }

                var engine = typeof(TacticalEngine);
                var source = engine.GetMethod("BuffSourceAbility", all);
                var data = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                var execution = (AbilityExecutionContext)RuntimeHelpers.GetUninitializedObject(typeof(AbilityExecutionContext));
                typeof(AbilityExecutionContext).GetField("Ability", all).SetValue(execution, data);
                var child = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                typeof(MechanicsContext).GetField("<ParentContext>k__BackingField", all).SetValue(child, execution);
                Check(ReferenceEquals(source.Invoke(null, new object[] { execution }), data), "original execution AbilityData is retained");
                Check(ReferenceEquals(source.Invoke(null, new object[] { child }), data), "child buff context resolves the original source ability");
                Check(source.Invoke(null, new object[] { null }) == null, "missing context creates no fabricated placement lock");
                Check(source.Invoke(null, new object[] { RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext)) }) == null,
                    "a non-ability root creates no fabricated placement lock");

                var placement = SaveBoostCheck.ReferencedMembers(engine.GetMethod("UniqueBuffPlacedRound", all)).ToArray();
                Check(placement.Any(m => m.Name == "BuffSourceAbility") && placement.Any(m => m.Name == "LastCastRound"),
                    "placement uses actual source ability with the existing performed-cast ledger");
                var scoring = SaveBoostCheck.ReferencedMembers(engine.GetMethod("ScoreAbility", all)).ToArray();
                Check(scoring.Any(m => m.Name == "NewestUniqueReplacementAge") && scoring.Any(m => m.Name == "UniqueReplacementAllowed"),
                    "production candidate valuation enforces sibling settling");
                var held = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AppraiseHeldBuff", all)).ToArray();
                Check(held.Any(m => m.Name == "TryGetValue") && held.Any(m => m.Name == "Remove"),
                    "held appraisal restores or removes its temporary explanation");
                Check(!placement.Any(m => m.Name == "Trigger" || m.Name == "CreateContext" || m.Name == "AddBuff"),
                    "placement lookup creates no rule, cast context or applied buff");
            }
            catch (Exception ex) { bad++; lines.Add("UNIQUE-REPLACEMENT FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"UNIQUE-REPLACEMENT {checks} checks, failures {bad}; exported two-tier graphs, source identity and compiled routes; live casts NOT TESTED");
            return lines;
        }
    }
}
