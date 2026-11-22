// ★ Fusion coverage, the remaining groups (docs/FUSION_COVERAGE_REST.md): C7 fused dispels, C11 Vampiric Touch's caster-side
// temporary HP, and the later slices. Native contracts, the actual source graphs, the admission rule and the compiled routes.
// Actor-free: no Game/Player, unit, context, Rulebook, cast or random roll.
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
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class FusionRestCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name)
        {
            var members = new List<MemberInfo>();
            foreach (var m in type.GetMethods(Hidden).Where(m => m.Name == name && m.GetMethodBody() != null))
                members.AddRange(SaveBoostCheck.ReferencedMembers(m));
            string prefix = "<" + name + ">";
            foreach (var holder in new[] { type }.Concat(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)))
            {
                bool machine = holder.Name.StartsWith(prefix, StringComparison.Ordinal);
                foreach (var m in holder.GetMethods(Hidden | BindingFlags.DeclaredOnly))
                    if ((machine || m.Name.StartsWith(prefix, StringComparison.Ordinal)) && m.GetMethodBody() != null)
                        members.AddRange(SaveBoostCheck.ReferencedMembers(m));
            }
            return members;
        }

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>(); string info = "";
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION-REST FAILED: " + why); } }
            try
            {
                var all = loaded.ToList();
                var abilities = all.OfType<BlueprintAbility>().ToList();
                var buffs = all.OfType<BlueprintBuff>().ToList();
                BlueprintAbility Spell(string name) => abilities.FirstOrDefault(b => b.name == name);
                var holder = abilities.FirstOrDefault(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc") ?? Spell("MagicHackDefaultSlot1");
                if (holder == null) { failures = 0; return new List<string> { "FUSION-REST SKIPPED: native MagicHack slot outside this scope" }; }
                AbilityData Fuse(string a, string b, string delivery, SavingThrowType save, int level = 4)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(delivery),
                        SavingThrowType = save, SpellSchool = SpellSchool.Evocation, SpellLevel = level };
                    return data;
                }
                var engine = typeof(TacticalEngine);
                string Unsupported(AbilityProfile p) => (string)typeof(AbilityProfile).GetProperty("FusionUnsupported", Hidden).GetValue(p);
                var model = engine.GetMethod("FusionOutcomeModel", Hidden);
                var reasonOf = typeof(AbilityProfile).GetField("FusionOutcomeMetadataReason", Hidden);
                string Joint(AbilityData data)
                {
                    var p = AbilityProfile.Get(data);
                    bool ok = (bool)model.Invoke(null, new object[] { data, p });
                    return ok ? null : (string)reasonOf.GetValue(p) ?? "unqualified";
                }
                var refusal = engine.GetMethod("FusionDispelRefusal", Hidden);
                string DispelRefusal(AbilityData data) => (string)refusal.Invoke(null, new object[] { AbilityProfile.Get(data) });

                // --- C7: fused dispels ---
                var tryRemove = typeof(ContextActionDispelMagic).GetMethods(Hidden | BindingFlags.DeclaredOnly).Where(m => m.GetMethodBody() != null)
                    .Select(m => SaveBoostCheck.ReferencedMembers(m).ToList())
                    .Any(b => b.Any(x => x.Name == "OnlyTargetEnemyBuffs") && b.Any(x => x.Name == "IsEnemy"));
                Check(tryRemove, "native: a dispel with OnlyTargetEnemyBuffs skips a buff whose caster is not the dispeller's enemy");
                var theft = Spell("ArcanaTheftEffect")?.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>()?
                    .Actions?.Actions?.OfType<ContextActionDispelMagic>().FirstOrDefault();
                Check(theft != null && theft.OnlyTargetEnemyBuffs, "actual Arcana Theft: its dispel takes only enemies' buffs");
                var target = Spell("DispelMagicTarget")?.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>()?
                    .Actions?.Actions?.OfType<ContextActionDispelMagic>().FirstOrDefault();
                Check(target != null && !target.OnlyTargetEnemyBuffs, "actual Dispel Magic: its dispel takes the party's own buffs too");
                Check(DispelRefusal(Fuse("DispelMagicTarget", "Fireball", "Fireball", SavingThrowType.Reflex)) == null &&
                      Joint(Fuse("DispelMagicTarget", "Fireball", "Fireball", SavingThrowType.Reflex)) == null,
                    "Dispel Magic with Fireball: damage and a dispel the dispel order prices, in the joint value");
                Check(DispelRefusal(Fuse("DispelMagicTarget", "Slow", "Fireball", SavingThrowType.Will)) == null,
                    "a dispel before the cast's own buff (Spell 1 Dispel Magic, Spell 2 Slow) cannot take it off");
                Check(DispelRefusal(Fuse("Slow", "DispelMagicTarget", "Fireball", SavingThrowType.Will)) == "dispel after the cast's own buff" &&
                      (Joint(Fuse("Slow", "DispelMagicTarget", "Fireball", SavingThrowType.Will)) ?? "").Contains("dispel after the cast's own buff"),
                    "a dispel after the cast's own Slow could take it off again: kept on the simpler value");
                Check(DispelRefusal(Fuse("Slow", "ArcanaTheft", "Fireball", SavingThrowType.Will)) == null,
                    "Arcana Theft after Slow takes only enemies' buffs: it never takes the cast's own");
                Check(Calls(Body(engine, "FusionOutcomeModel"), engine, "FusionDispelRefusal"),
                    "the joint value admits a dispel through FusionDispelRefusal");
                Check(Calls(Body(engine, "ScoreAbility"), engine, "TryDispelValue"), "a fused dispel is priced on every unit it reaches (TryDispelValue)");

                // --- C11: Vampiric Touch's caster-side temporary HP ---
                var own = typeof(AbilityProfile).GetMethod("OwnResultTemporaryHp", Hidden);
                bool Own(string name) => (bool)own.Invoke(null, new object[] { buffs.FirstOrDefault(b => b.name == name) });
                Check(Own("VampiricTouchBuff"), "actual VampiricTouchBuff: temporary HP from the cast's own shared value only");
                Check(!Own("HasteBuff") && !Own("BlessBuff") && !Own("FalseLifeBuff"),
                    "Haste, Bless and False Life are not such a buff (a caster payload of theirs stays withheld)");
                var vampiric = AbilityProfile.Get(Fuse("VampiricTouchCast", "Fireball", "Fireball", SavingThrowType.Reflex));
                Check(Unsupported(vampiric) == null && vampiric.CasterBuffs.Count > 0 && vampiric.DealsDamage,
                    "Vampiric Touch with Fireball is offered: its caster temporary HP is left uncredited, as an ordinary one's");
                Check(Calls(Body(typeof(AbilityProfile), "GetFusion"), typeof(AbilityProfile), "OwnResultTemporaryHp"),
                    "the fusion admission reads the caster payload through OwnResultTemporaryHp");

                // --- C9: nested saves (Phantasmal Killer, Banshee Blast), ordinary and fused ---
                var nestedRun = Body(typeof(ContextActionSavingThrow), "RunAction");
                Check(nestedRun.Any(m => m.Name == "MakeSaveAlsoForMagicHack") && nestedRun.Any(m => m.Name == "get_MagicHackData") &&
                      nestedRun.Any(m => m.Name == "Setup" && m.DeclaringType?.Name == "SavingThrowData"),
                    "native: a nested save rolls in a fused cast only with MakeSaveAlsoForMagicHack, and sets the save its actions read");
                Check(Body(typeof(Kingmaker.UnitLogic.Mechanics.MechanicsContext), "get_SavingThrow").Any(m => m.Name == "get_Current"),
                    "native: a ConditionalSaved reads the current save data (the nested one inside it)");
                var tryNested = typeof(AbilityProfile).GetMethod("TryNestedSave", Hidden);
                string Nested(AbilityProfile p, Kingmaker.ElementsSystem.GameAction a)
                {
                    var args = new object[] { a, null, null };
                    if (!(bool)tryNested.Invoke(p, args)) return null;
                    return ((ContextActionSavingThrow)args[1]).Type + "/" + args[2];
                }
                var killer = AbilityProfile.Get(CasterPolicyCheck.Data(Spell("PhantasmalKiller")));
                var kill = killer.Damage.FirstOrDefault(a => a is ContextActionKill);
                var shock = killer.Damage.OfType<ContextActionDealDamage>().FirstOrDefault();
                Check(killer.Save == SavingThrowType.Will && Nested(killer, kill) == "Fortitude/Failed" && Nested(killer, shock) == "Fortitude/Passed",
                    "actual Phantasmal Killer: Will, then Fortitude - the kill on a failed Fortitude save, 3d6 on a passed one");
                var banshee = AbilityProfile.Get(CasterPolicyCheck.Data(Spell("BansheeBlast")));
                var applications = (System.Collections.IEnumerable)typeof(AbilityProfile).GetProperty("BuffApplications", Hidden).GetValue(banshee);
                var firstApplication = applications.Cast<object>().FirstOrDefault();
                var fright = (Kingmaker.ElementsSystem.GameAction)firstApplication?.GetType().GetField("Action", Hidden).GetValue(firstApplication);
                Check(banshee.Save == SavingThrowType.Reflex && Nested(banshee, fright) == "Will/Failed" &&
                      Nested(banshee, banshee.Damage.First()) == null,
                    "actual Banshee Blast: its damage on the Reflex save, its fear behind a failed Will save");
                var fusedKiller = AbilityProfile.Get(Fuse("PhantasmalKiller", "Fireball", "Fireball", SavingThrowType.Reflex));
                Check(Unsupported(fusedKiller) == null && Nested(fusedKiller, fusedKiller.Damage.FirstOrDefault(a => a is ContextActionKill)) == "Fortitude/Failed",
                    "fused Phantasmal Killer is offered: its Fortitude save is read (MakeSaveAlsoForMagicHack)");
                var fusedBanshee = AbilityProfile.Get(Fuse("BansheeBlast", "Fireball", "BansheeBlast", SavingThrowType.Reflex));
                Check(Unsupported(fusedBanshee) == null && fusedBanshee.IsProjectileCone, "fused Banshee Blast (its cone) is offered");
                var math = typeof(CombatMath);
                Check(Calls(Body(math, "EstimateAbilityDamageRaw"), math, "NestedSaveFactor") &&
                      Calls(Body(engine, "ScoreAbility"), math, "KillNestedFactor") && Calls(Body(engine, "ScoreAbility"), math, "NestedBuffFactor") &&
                      Calls(Body(engine, "TryFusionControlRounds"), math, "NestedSaveFactor"),
                    "damage, kills and control behind a nested save are weighted by its fail or pass chance, ordinary and fused");
                // --- C10: lingering areas (Obsidian Flow, Haunting Mists) ---
                var process = typeof(Kingmaker.Controllers.AbilityExecutionProcess);
                var clickedType = typeof(Kingmaker.UnitLogic.Abilities.Components.AdditionalAbilityEffectRunActionOnClickedTarget);
                bool runsClicked = process.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Concat(new[] { process })
                    .SelectMany(t => t.GetMethods(Hidden | BindingFlags.DeclaredOnly)).Where(m => m.GetMethodBody() != null)
                    .Select(m => SaveBoostCheck.ReferencedMembers(m).ToList())
                    .Any(b => b.Any(x => x.DeclaringType == clickedType && x.Name == "Apply") && b.Any(x => x.Name == "get_Spell1") && b.Any(x => x.Name == "get_Spell2"));
                Check(runsClicked, "native: a fused cast runs each source spell's clicked-target payload (once, at the main target)");
                var friends = Body(typeof(AbilityData), "get_CanTargetAlly");
                Check(Calls(friends, typeof(AbilityData), "GetDeliverBlueprint"), "native: the additional AoE blueprint shapes targeting (CanTargetAlly)");
                var lingering = typeof(AbilityProfile).GetMethod("LingeringArea", Hidden);
                object Area(string name) => lingering.Invoke(null, new object[] { Spell(name)?.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AdditionalAbilityEffectRunActionOnClickedTarget>() });
                Check(Area("ObsidianFlow") != null && Area("HauntingMists") != null &&
                      Spell("ObsidianFlow").ComponentsArray.Any(c => c?.GetType().Name == "AdditionalAoeForMagicHack") &&
                      Spell("HauntingMists").ComponentsArray.Any(c => c?.GetType().Name == "AdditionalAoeForMagicHack"),
                    "actual Obsidian Flow and Haunting Mists: one lingering area each, marked as the fused cast's additional AoE");
                var areas = typeof(AbilityProfile).GetField("FusionLingeringAreas", Hidden);
                var mists = Fuse("HauntingMists", "Fireball", "Fireball", SavingThrowType.Reflex);
                mists.MagicHackData.AdditionalAoeBlueprint = Spell("HauntingMists");
                var mistsProfile = AbilityProfile.Get(mists);
                Check(Unsupported(mistsProfile) == null && ((System.Collections.ICollection)areas.GetValue(mistsProfile)).Count == 1,
                    "Haunting Mists with Fireball is offered: no RunAction of its own, its mists recorded as a lingering area");
                var obsidian = Fuse("ObsidianFlow", "Snowball", "Snowball", SavingThrowType.Reflex);
                obsidian.MagicHackData.AdditionalAoeBlueprint = Spell("ObsidianFlow");
                Check(Unsupported(AbilityProfile.Get(obsidian)) == null, "Obsidian Flow with Snowball is offered, its additional AoE read");
                var other = Fuse("Fireball", "Snowball", "Snowball", SavingThrowType.Reflex);
                other.MagicHackData.AdditionalAoeBlueprint = Spell("Fireball");
                Check(Unsupported(AbilityProfile.Get(other)) == "additional area delivery", "an additional AoE that is no lingering area stays withheld");
                var catches = Body(engine, "LingeringAreaCatches");
                Check(Calls(Body(engine, "ScoreAbility"), engine, "LingeringAreaCatches") && Calls(catches, engine, "AlliesWalkingInto") &&
                      Calls(catches, engine, "UsesRangedWeapon"),
                    "an ally the lingering area would cover, a melee ally at its edge or walking in, refuses the cast");

                int nestedRead = 0, nestedUnread = 0; var names = new List<string>();
                foreach (var a in abilities)
                {
                    var run = a.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>();
                    if (run == null || run.SavingThrowType == SavingThrowType.Unknown) continue;
                    AbilityProfile p;
                    try { p = AbilityProfile.Get(CasterPolicyCheck.Data(a)); } catch { continue; }
                    var map = typeof(AbilityProfile).GetProperty("NestedSaves", Hidden).GetValue(p);
                    if (map == null) continue;
                    if (typeof(AbilityProfile).GetProperty("NestedSaveRefusal", Hidden).GetValue(p) == null) { nestedRead++; if (names.Count < 10) names.Add(a.name); }
                    else nestedUnread++;
                }
                info = $"; ordinary nested saves read on {nestedRead} abilities ({string.Join(", ", names)}{(nestedRead > names.Count ? ", ..." : "")}), {nestedUnread} left on the root save";
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"FUSION-REST {checks} checks, {bad} failures{info}");
            return lines;
        }
    }
}
