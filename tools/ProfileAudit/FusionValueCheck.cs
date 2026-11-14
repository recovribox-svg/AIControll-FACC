using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class FusionValueCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION-VALUE FAILED: " + why); } }
            try
            {
                var all = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => all.First(b => b.name == name);
                var holder = all.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                AbilityData Fuse(string a, string b, SavingThrowType save)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(a),
                        SavingThrowType = save, SpellSchool = SpellSchool.Evocation, SpellLevel = 3 };
                    return data;
                }
                var classify = typeof(TacticalEngine).GetMethod("FusionOutcomeModel", Hidden);
                var applications = typeof(AbilityProfile).GetProperty("BuffApplications", Hidden);
                var ownControl = typeof(AbilityProfile).GetMethod("BuffControlOn", Hidden);
                var joint = typeof(TacticalEngine).GetMethod("TryFusionValues", Hidden);
                var getRounds = typeof(TacticalEngine).GetMethod("TryFusionControlRounds", Hidden);
                Check(classify != null && applications != null && ownControl != null && joint != null && getRounds != null,
                    "production occurrence, qualification and shared-value contracts exist");
                var slow = Fuse("Slow", "ConfusionSpell", SavingThrowType.Will);
                // Export names differ between game revisions; use the exact loaded name for Confusion.
                var pair = AbilityProfile.Get(slow);
                var occurrences = ((IEnumerable)applications.GetValue(pair)).Cast<object>().ToList();
                Check(occurrences.Count >= 2 && occurrences.All(o => o.GetType().GetField("Action", Hidden).GetValue(o) is ContextActionApplyBuff),
                    "actual Slow/Confusion keep their separate native apply actions");
                Check((bool)classify.Invoke(null, new object[] { slow, pair }), "actual Slow/Confusion qualifies for occurrence-local control");
                var played = Fuse("IncendiaryRunes", "Snowball", SavingThrowType.Reflex);
                var playedProfile = AbilityProfile.Get(played);
                var playedApplications = ((IEnumerable)applications.GetValue(playedProfile)).Cast<object>().ToList();
                Check(playedApplications.Any(a => (bool)a.GetType().GetField("PeriodicDamageOnly", Hidden).GetValue(a) &&
                    (float)a.GetType().GetField("Rounds", Hidden).GetValue(a) >= 10f),
                    "native Burning carrier is timed HP harm, not three rounds of generic control");
                Check(playedApplications.Any(a =>
                    ((Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff)a.GetType().GetField("Buff").GetValue(a)).name == "SnowballBuff" &&
                    (float)a.GetType().GetField("Rounds").GetValue(a) == 1f),
                    "native Snowball stagger keeps its one-round duration alongside the longer carrier");
                Check((bool)classify.Invoke(null, new object[] { played, playedProfile }),
                    "played Incendiary Runes/Snowball reaches the new model, rather than a metadata-only dead path");
                var colour = Fuse("IncendiaryRunes", "ColorSpray", SavingThrowType.Will);
                var colourProfile = AbilityProfile.Get(colour);
                bool colourQualified = (bool)classify.Invoke(null, new object[] { colour, colourProfile });
                if (!colourQualified)
                {
                    lines.Add($"FUSION-VALUE metadata: control={colourProfile.Control}, mark={colourProfile.MarksForOwnAttacks}, maneuvers={colourProfile.Maneuvers.Count}, " +
                        $"guards={typeof(AbilityProfile).GetProperty("FusionIndependentGuards",Hidden).GetValue(colourProfile)}, " +
                        $"paths={typeof(AbilityProfile).GetProperty("FusionBuffPathsConsistent",Hidden).GetValue(colourProfile)}");
                    foreach (var b in colourProfile.TargetBuffs)
                        lines.Add($"FUSION-VALUE metadata buff {b.name}: harmful={typeof(AbilityProfile).GetMethod("IsHarmfulBuff",Hidden).Invoke(null,new object[]{b})}, " +
                            $"control={typeof(AbilityProfile).GetMethod("BuffControl",Hidden).Invoke(null,new object[]{b})}");
                }
                Check(colourQualified,
                    "played Incendiary Runes/Color Spray qualifies before recipient-specific guards");
                var readType = typeof(CombatMath).GetNestedType("FusionDamageRead", Hidden);
                var capture = typeof(CombatMath).GetMethod("ReadFusionDamageOutcomes", Hidden);
                var fire = Fuse("Fireball", "LightningBolt", SavingThrowType.Reflex); var profile = AbilityProfile.Get(fire);
                var action = profile.Damage.OfType<ContextActionDealDamage>().First(d => d.HalfIfSaved);
                void Capture(float weighted, bool evasion, bool improved, float failed, float passed)
                {
                    var read = Activator.CreateInstance(readType, true); readType.GetMethod("Reset", Hidden).Invoke(read, null);
                    capture.Invoke(null, new object[] { profile, new List<KeyValuePair<GameAction, float>> { new KeyValuePair<GameAction, float>(action, weighted) },
                        0.5f, evasion, improved, 1f, read });
                    float f = (float)readType.GetField("Failed", Hidden).GetValue(read), p = (float)readType.GetField("Passed", Hidden).GetValue(read);
                    Check((bool)readType.GetField("Known", Hidden).GetValue(read) && Math.Abs(f-failed)<0.001f && Math.Abs(p-passed)<0.001f,
                        $"native half-save action preserves conditional outcomes ({evasion}/{improved}: {f}/{p})");
                }
                Capture(7.5f, false, false, 10f, 5f);
                Capture(5f, true, false, 10f, 0f);
                Capture(2.5f, true, true, 5f, 0f);
                var noSave = Fuse("ConfusionSpell", "Blindness", SavingThrowType.Unknown); var noSaveProfile = AbilityProfile.Get(noSave);
                Check((string)typeof(AbilityProfile).GetProperty("FusionUnsupported",Hidden).GetValue(noSaveProfile) ==
                    "saved-result branch without a declared root save",
                    "injected missing-root configuration is withheld rather than inventing a failed save; independent-save forecasts are untouched");
                Check(SaveBoostCheck.ReferencedMembers(ownControl).Any(m => m.Name == "HasConditionImmunity"),
                    "per-effect classification reads the game's condition-immunity method");
                Check(SaveBoostCheck.ReferencedMembers(joint).Any(m => m.DeclaringType == typeof(ValueModel) && m.Name == "FusionValue") &&
                    !SaveBoostCheck.ReferencedMembers(joint).Any(m => m.Name.StartsWith("EstimateAbilityDamage", StringComparison.Ordinal)),
                    "live combined route calls checked shared math and never repeats the damage estimate");
                Check(SaveBoostCheck.ReferencedMembers(getRounds).Any(m => m.Name == "HasFact") &&
                      SaveBoostCheck.ReferencedMembers(getRounds).Any(m => m.Name == "get_Rounds" || m.Name == "Rounds"),
                    "live marginal filter and duration envelope are connected");
                var envelopeType = typeof(ValueModel).GetNestedType("ControlEnvelope", Hidden);
                var envelope = Activator.CreateInstance(envelopeType, true);
                var add = (Func<float,float,bool>)Delegate.CreateDelegate(typeof(Func<float,float,bool>), envelope, envelopeType.GetMethod("Add", Hidden));
                var clear = (Action)Delegate.CreateDelegate(typeof(Action), envelope, envelopeType.GetMethod("Clear", Hidden));
                var total = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), envelope, envelopeType.GetMethod("Rounds", Hidden));
                for (int i=0;i<128;i++) { clear();add(1,1);add(0.4f,3);total(); }
                long before=GC.GetAllocatedBytesForCurrentThread();
                for (int i=0;i<10000;i++) { clear();add(1,1);add(0.4f,3);total(); }
                long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
                Check(bytes==0 && Math.Abs(total()-1.8f)<0.0001f, "warm bounded envelope allocates zero bytes and keeps one hard plus two soft rounds");
                lines.Add($"FUSION-VALUE envelope: 10000 warm two-effect calls, {bytes} bytes; scalar scratch microcheck, not Unity timing");
            }
            catch(Exception ex) { bad++;lines.Add("FUSION-VALUE FAILED: "+ex.GetBaseException()); }
            failures=bad;lines.Insert(0,$"FUSION-VALUE {checks} checks, failures {bad}; actual source/apply metadata, native save contracts and compiled routes; no actor, Rulebook, cast, command or Unity timing");return lines;
        }
    }
}
