// F0: declared-list configuration templates, through native UI metadata utilities and the actual mod reader.
// No actors, casts, target queries, Rulebook or game/save writes. Not an availability or gameplay verdict.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.MagicHack;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;

namespace ProfileAudit
{
    internal static class FusionCoverage
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class Source
        {
            internal BlueprintAbility Spell;
            internal int Level;
        }
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, string output, int failedLoads, out int failures)
        {
            failures = 0;
            try
            {
                var all = loaded.ToList();
                var list = all.OfType<BlueprintSpellList>().SingleOrDefault(b => b.AssetGuid.ToString() == "cf20c10b131344c1a61b75e9cc4a4c13");
                var book = all.OfType<BlueprintSpellbook>().SingleOrDefault(b => b.AssetGuid.ToString() == "587066af76a74f47a904bb017697ba08");
                var native = book?.GetComponent<MagicHackSpellbookComponent>();
                var holder = native?.GetDefaultBlueprint(0); var touchHolder = native?.GetTouchBlueprint(0);
                if (list == null || holder == null || touchHolder == null)
                    throw new InvalidDataException("required Magic Deceiver list/book/default/touch slot graph missing");
                var sources = new List<Source>();
                foreach (var bucket in list.SpellsByLevel.Where(b => b.SpellLevel > 0))
                {
                    if (bucket.SpellsRefs.Count != bucket.Spells.Count || bucket.Spells.Any(s => s == null))
                        throw new InvalidDataException("unresolved levelled source in declared spell list");
                    foreach (var spell in bucket.Spells)
                    {
                        var duplicate = sources.FirstOrDefault(s => s.Spell == spell);
                        if (duplicate != null && duplicate.Level != bucket.SpellLevel)
                            throw new InvalidDataException("ambiguous source level; actual spellbook required");
                        if (duplicate == null) sources.Add(new Source { Spell = spell, Level = bucket.SpellLevel });
                    }
                }
                if (sources.Count < 2) throw new InvalidDataException("fewer than two resolved levelled sources");
                sources = sources.OrderBy(s => s.Spell.AssetGuid.ToString(), StringComparer.Ordinal).ToList();
                var unsupported = typeof(AbilityProfile).GetProperty("FusionUnsupported", Hidden);
                var limitations = typeof(AbilityProfile).GetProperty("FusionLimitations", Hidden);
                if (unsupported == null || limitations == null) throw new MissingMemberException("Fusion diagnostic reader contract missing");
                var coneResolver = typeof(CombatMath).Assembly.GetType("CompanionAI_Pathfinder.Tactics.ProjectileCone", true)
                    .GetMethod("DeliveryComponent", BindingFlags.Static | BindingFlags.NonPublic);
                if (coneResolver == null) throw new MissingMemberException("selected cone resolver missing");
                var jointModel = typeof(TacticalEngine).GetMethod("FusionOutcomeModel", BindingFlags.Static | BindingFlags.NonPublic);
                if (jointModel == null) throw new MissingMemberException("Fusion joint-value qualification missing");
                var jointReason = typeof(AbilityProfile).GetField("FusionOutcomeMetadataReason", Hidden);
                if (jointReason == null) throw new MissingMemberException("Fusion joint-value qualification reason missing");
                // ★ Fusion F4: the engine's own admission rule and the recorded passed/either-outcome application.
                var withheld = typeof(TacticalEngine).GetMethod("FusionWithheld", BindingFlags.Static | BindingFlags.NonPublic);
                var outcomeLocation = typeof(AbilityProfile).GetProperty("FusionOutcomeEffectLocation", Hidden);
                if (withheld == null || outcomeLocation == null) throw new MissingMemberException("Fusion outcome admission contract missing");
                int outcomeProfiles = 0, outcomeAdmitted = 0;
                // ★ Fusion F7: a shared Hit Dice pool is admitted by the pool reader plus the joint outcome value.
                var sharedHd = typeof(AbilityProfile).GetProperty("FusionSharedHd", Hidden);
                var withheldDetail = typeof(TacticalEngine).GetMethod("FusionWithheldDetail", BindingFlags.Static | BindingFlags.NonPublic);
                if (sharedHd == null || withheldDetail == null) throw new MissingMemberException("Fusion shared-pool admission contract missing");
                int poolProfiles = 0, poolAdmitted = 0;
                var poolWithheld = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var outcomeWithheld = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var schools = new List<SpellSchool>(); var saves = new List<SavingThrowType>();
                var shapes = new List<SpellTargetType>(); var deliveries = new Dictionary<SpellTargetType, BlueprintAbility>();
                var rows = new List<object>(); var anyReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var soleReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var combinations = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var jointFallbacks = new SortedDictionary<string, int>(StringComparer.Ordinal);
                int pairs = 0, noConfiguration = 0, accepted = 0, readerErrors = 0, conesAccepted = 0, conesResolved = 0, coneErrors = 0, jointMetadata = 0;
                foreach (var first in sources)
                foreach (var second in sources)
                {
                    if (first.Spell == second.Spell) continue;
                    pairs++;
                    SpellbookMagicHackUtility.FetchSchools(first.Spell, second.Spell, schools);
                    SpellbookMagicHackUtility.FetchThrowTypes(first.Spell, second.Spell, saves);
                    SpellbookMagicHackUtility.FetchTargetTypes(first.Spell, second.Spell, shapes, deliveries);
                    if (saves.Count == 0) saves.Add(SavingThrowType.Unknown); // native UI's empty-list default
                    if (schools.Count == 0 || shapes.Count == 0) { noConfiguration++; continue; }
                    foreach (var shape in shapes)
                    foreach (var school in schools)
                    foreach (var save in saves)
                    {
                        var delivery = deliveries[shape];
                        bool touch = delivery.GetComponent<AbilityEffectStickyTouch>() != null;
                        var other = delivery == first.Spell ? second.Spell : first.Spell;
                        var extra = other.GetComponent<AdditionalAoeForMagicHack>() != null ? other : null;
                        var ability = CasterPolicyCheck.Data(touch ? touchHolder : holder);
                        ability.MagicHackData = new MagicHackData {
                            Spell1 = first.Spell, Spell2 = second.Spell, DeliverBlueprint = delivery, AdditionalAoeBlueprint = extra,
                            SpellLevel = Math.Max(first.Level, second.Level), SavingThrowType = save,
                            SpellSchool = school, SpellTargetType = shape, IsTouch = touch
                        };
                        var profile = AbilityProfile.Get(ability);
                        string structural = (string)unsupported.GetValue(profile);
                        string primary = (string)withheld.Invoke(null, new object[] { ability, profile });
                        var reasons = new List<string>(); var details = new List<object>();
                        foreach (var issue in (IEnumerable)limitations.GetValue(profile))
                        {
                            var type = issue.GetType();
                            string reason = (string)type.GetField("Reason").GetValue(issue);
                            string location = (string)type.GetField("Location").GetValue(issue);
                            if (!reasons.Contains(reason)) reasons.Add(reason);
                            details.Add(new { reason, location });
                        }
                        if ((structural == null) != (reasons.Count == 0) || structural != null && !reasons.Contains(structural))
                            throw new InvalidDataException("primary admission and complete rejection reasons disagree");
                        if (reasons.Any(r => r.StartsWith("profile reader ", StringComparison.Ordinal))) readerErrors++;
                        bool? coneComponentResolved = null;
                        bool jointOutcomeMetadataQualified = structural == null && (bool)jointModel.Invoke(null, new object[] { ability, profile });
                        if (jointOutcomeMetadataQualified) jointMetadata++;
                        string jointOutcomeMetadataReason = structural ?? (string)jointReason.GetValue(profile);
                        if ((bool)sharedHd.GetValue(profile))
                        {
                            poolProfiles++;
                            const string pool = "shared Hit Dice selection";
                            if (structural != null) reasons.Add(pool + "; pool forecast not assessed");
                            else if (primary == pool)
                            {
                                reasons.Add(pool);
                                Increment(poolWithheld, (string)withheldDetail.Invoke(null, new object[] { ability, profile }) ?? "not qualified");
                            }
                            else poolAdmitted++;
                            details.Add(new { reason = "shared Hit Dice pool", location = "profile/shared-hd" });
                        }
                        string outcomeAt = (string)outcomeLocation.GetValue(profile);
                        if (outcomeAt != null)
                        {
                            outcomeProfiles++;
                            const string outside = "effect outside the failed-save branch; joint value ";
                            if (structural != null) reasons.Add(outside + "not assessed");
                            else if (!jointOutcomeMetadataQualified)
                            {
                                reasons.Add(outside + "not qualified");
                                Increment(outcomeWithheld, jointOutcomeMetadataReason ?? "not qualified");
                            }
                            else outcomeAdmitted++;
                            details.Add(new { reason = "effect outside the failed-save branch", location = outcomeAt });
                        }
                        if ((primary == null) != (reasons.Count == 0) || structural != null && primary != structural)
                            throw new InvalidDataException("engine admission and audit reasons disagree");
                        if (primary == null && !jointOutcomeMetadataQualified) Increment(jointFallbacks, jointOutcomeMetadataReason ?? "not qualified");
                        if (primary == null)
                        {
                            accepted++;
                            if (profile.IsProjectileCone)
                            {
                                conesAccepted++;
                                object[] args = { ability, null };
                                var expectedCone = delivery.GetComponent<AbilityDeliverProjectile>();
                                coneComponentResolved = expectedCone?.Type == AbilityProjectileType.Cone &&
                                                        ReferenceEquals(coneResolver.Invoke(null, args), expectedCone) &&
                                                        ReferenceEquals(args[1], delivery);
                                if (coneComponentResolved.Value) conesResolved++; else coneErrors++;
                            }
                        }
                        foreach (string reason in reasons) Increment(anyReasons, reason);
                        if (reasons.Count == 1) Increment(soleReasons, reasons[0]);
                        reasons.Sort(StringComparer.Ordinal);
                        if (reasons.Count > 0) Increment(combinations, string.Join(" | ", reasons));
                        rows.Add(new {
                            source1 = first.Spell.AssetGuid.ToString(), source2 = second.Spell.AssetGuid.ToString(),
                            source1Name = first.Spell.name, source2Name = second.Spell.name,
                            level = ability.MagicHackData.SpellLevel, school = school.ToString(), save = save.ToString(), shape = shape.ToString(),
                            delivery = delivery.AssetGuid.ToString(), additional = extra?.AssetGuid.ToString(), touch,
                            profileAccepted = primary == null, primaryReason = primary, limitations = details,
                            coneComponentResolved,
                            jointOutcomeMetadataQualified,
                            jointOutcomeMetadataReason,
                            damageActions = profile.Damage.Count, targetBuffs = profile.TargetBuffs.Count,
                            casterBuffs = profile.CasterBuffs.Count, heals = profile.Heals.Count, casterHeals = profile.CasterHeals.Count,
                            hasDetectableEffect = profile.HasDetectableEffect,
                            legalAim = "not assessed: actorless metadata audit", usefulValue = "not assessed", execution = "not attempted"
                        });
                    }
                }
                failures = readerErrors + coneErrors;
                var status = failures > 0 ? "FAILED" : failedLoads > 0 ? "PARTIAL" : "PASS";
                File.WriteAllText(output, JsonSerializer.Serialize(new {
                    status, scope = "declared-list native UI configuration templates; not actor availability, aim, utility or gameplay acceptance",
                    sourceList = list.AssetGuid.ToString(), spellbook = book.AssetGuid.ToString(), failedBlueprintLoads = failedLoads,
                    sources = sources.Select(s => new { guid = s.Spell.AssetGuid.ToString(), name = s.Spell.name, level = s.Level }),
                    orderedDistinctPairs = pairs, pairsWithoutConfiguration = noConfiguration, configurations = rows.Count,
                    profileAccepted = accepted, profileWithheld = rows.Count - accepted, acceptedConeProfilesWithAimUnvalidated = conesAccepted,
                    coneComponentsResolved = conesResolved, coneComponentErrors = coneErrors,
                    jointOutcomeMetadataQualified = jointMetadata,
                    jointOutcomeMetadataFallbacks = jointFallbacks,
                    jointValueScope = "metadata gate only; live recipient applicability, duration, damage outcomes and useful value remain unassessed",
                    outcomeEffectProfiles = outcomeProfiles, outcomeEffectAdmitted = outcomeAdmitted,
                    outcomeEffectWithheldByJointReason = outcomeWithheld,
                    sharedPoolProfiles = poolProfiles, sharedPoolAdmitted = poolAdmitted, sharedPoolWithheldByReason = poolWithheld,
                    readerErrors, blockersPresent = anyReasons, soleBlockers = soleReasons, blockerCooccurrence = combinations,
                    cases = rows
                }, new JsonSerializerOptions { WriteIndented = true }));
                return new List<string> { $"FUSION-COVERAGE {sources.Count} sources, {pairs} pairs, {rows.Count} configurations; profile accepted {accepted}, withheld {rows.Count - accepted}, reader errors {readerErrors}; {status}; aim/value/execution unassessed" };
            }
            catch (Exception ex)
            {
                failures = 1;
                string reason = ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message;
                File.WriteAllText(output, JsonSerializer.Serialize(new { status = "INCOMPLETE", reason }));
                return new List<string> { "FUSION-COVERAGE FAILED: " + reason };
            }
        }
        private static void Increment(IDictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int count); counts[key] = count + 1;
        }
    }
}
