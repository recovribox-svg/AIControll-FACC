// The AI's own readers (AbilityProfile, BuffPartsReader, BuffEffectAnalyzer) run on every ability blueprint, and the
// engine's choices that depend only on the blueprint are repeated here (TacticalEngine.EvaluateAbility's target sides,
// ScoreAbility's value paths, BuffQuality's fallback). What depends on a unit or the fight (immunity, range, HP) is not.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class Audit
    {
        /// <summary>Where each blueprint was read from (relative to its dump folder).</summary>
        public static readonly Dictionary<SimpleBlueprint, string> Paths = new Dictionary<SimpleBlueprint, string>();

        internal sealed class Row
        {
            public string Name, Path, Verdict, Detail;
            public BlueprintAbility Bp;
        }

        public static void Run(List<SimpleBlueprint> all, string output)
        {
            var rows = new List<Row>();
            int errors = 0;
            foreach (var bp in all.OfType<BlueprintAbility>())
            {
                if (bp.name != null && bp.name.Contains("Cutscene")) continue;
                if (bp.HasVariants) continue;   // its variants are counted (AbilityDiscovery.Gather)
                var row = new Row { Name = bp.name, Bp = bp, Path = Paths.TryGetValue(bp, out var path) ? path : "?" };
                try { Classify(bp, row); }
                catch (Exception ex)
                {
                    row.Verdict = "error"; row.Detail = $"{ex.GetType().Name}: {ex.Message}"; errors++;
                    if (errors <= 3) Console.WriteLine($"{bp.name}: {ex}");
                }
                rows.Add(row);
            }
            // Activatable abilities: an attack mode (TacticalEngine.ModeOf) or a toggle (TacticalEngine.ProfileOf), as the engine reads them.
            var engine = typeof(TacticalEngine);
            var modeOf = engine.GetMethod("ModeOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var profileOf = engine.GetMethod("ProfileOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            foreach (var bp in all.OfType<Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility>())
            {
                var row = new Row { Name = bp.name, Path = Paths.TryGetValue(bp, out var path) ? path : "?" };
                try { ClassifyToggle(bp, row, modeOf, profileOf); }
                catch (Exception ex) { row.Verdict = "toggle error"; row.Detail = (ex.InnerException ?? ex).Message; errors++; }
                rows.Add(row);
            }
            using (var w = new StreamWriter(output))
            {
                w.WriteLine($"abilities: {rows.Count}, errors: {errors}");
                foreach (var g in rows.GroupBy(r => r.Verdict.Split(':')[0]).OrderByDescending(g => g.Count()))
                    w.WriteLine($"{g.Count(),6}  {g.Key}");
                w.WriteLine();
                foreach (var g in rows.GroupBy(r => r.Verdict).OrderBy(g => g.Key))
                    w.WriteLine($"{g.Count(),6}  {g.Key}");
                w.WriteLine();
                foreach (var r in rows.OrderBy(r => r.Verdict).ThenBy(r => r.Name))
                    w.WriteLine($"{r.Verdict} | {r.Path} | {r.Name} | {r.Detail}");
            }
            Console.WriteLine($"abilities {rows.Count}, errors {errors} -> {output}");
        }

        private static object Field(object o, string name) => o?.GetType().GetField(name)?.GetValue(o);

        private static void ClassifyToggle(Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility bp, Row row,
                                           System.Reflection.MethodInfo modeOf, System.Reflection.MethodInfo profileOf)
        {
            if (bp.HiddenInUI) { row.Verdict = "toggle not managed: hidden"; return; }
            var mode = modeOf.Invoke(null, new object[] { bp });
            var modeExcluded = Field(mode, "Excluded") as string;
            if (modeExcluded == null) { row.Verdict = "toggle valued: attack/move mode " + Field(mode, "Kind"); return; }
            var toggle = profileOf.Invoke(null, new object[] { bp });
            var excluded = Field(toggle, "Excluded") as string;
            if (excluded != null) { row.Verdict = "toggle not managed: " + excluded; row.Detail = "as a mode: " + modeExcluded; return; }
            var buffs = new List<BlueprintBuff>();
            if (Field(toggle, "SelfBuff") is BlueprintBuff self) buffs.Add(self);
            if (Field(toggle, "AreaBuffs") is List<BlueprintBuff> area) buffs.AddRange(area);
            var parts = buffs.SelectMany(b => BuffPartsReader.Read(b).Parts).ToList();
            var unread = buffs.SelectMany(b => BuffPartsReader.Read(b).Unread).Distinct().ToList();
            row.Detail = "parts: " + string.Join("; ", parts.Select(x => x.ToString()).Distinct().Take(8)) +
                         (unread.Count > 0 ? " | unread: " + string.Join(",", unread.Take(5)) : "");
            if (parts.Any(x => x.Kind != BuffPartKind.MentalStat && x.Kind != BuffPartKind.ForbidsSpells)) row.Verdict = "toggle valued: buff parts";
            else
            {
                var best = buffs.Select(BuffEffectAnalyzer.Analyze).Where(a => a.Recognized).Select(a => a.CombatValue).DefaultIfEmpty(0f).Max();
                row.Verdict = best > 0f ? "toggle GUESSED: buff by old quality" : "toggle nothing: buff not read";
            }
        }

        private static void Classify(BlueprintAbility bp, Row row)
        {
            if (bp.GetComponent<AbilityKineticBlade>() != null) { row.Verdict = "not offered: kinetic blade internal"; return; }
            var p = AbilityProfile.Get(bp);
            if (p == null) { row.Verdict = "error"; row.Detail = "no profile"; return; }
            var parts = new List<string>();

            // --- EvaluateAbility: the sides it may be aimed at ---
            if (p.IsCharge) { row.Verdict = "valued: by the rules (charge)"; return; }
            bool selfOnly = bp.Range == AbilityRange.Personal;
            bool helpfulOnly = p.HelpfulToAllies && !p.HarmfulEffects && !p.DealsDamage;
            bool harmfulOnly = (p.HarmfulEffects || p.DealsDamage || p.IsWeaponAttack || p.Maneuvers.Count > 0) && !p.HelpfulToAllies && p.Heals.Count == 0;
            bool enemies, allies, self;
            if (selfOnly)
            {
                self = !harmfulOnly || p.IsAoE;
                enemies = false;
                allies = self && p.IsAoE;
            }
            else
            {
                enemies = !helpfulOnly && (bp.CanTargetEnemies || (bp.CanTargetPoint && (p.HarmfulEffects || p.DealsDamage || p.Dispels)));
                allies = !harmfulOnly && (bp.CanTargetFriends || bp.CanTargetSelf);
                if (!allies && !harmfulOnly && bp.CanTargetPoint && !bp.CanTargetEnemies && p.IsAoE &&
                    (p.TargetBuffs.Count > 0 || p.Heals.Count > 0 || p.RemovesAnything))
                {
                    allies = true;
                    if (System.Environment.GetEnvironmentVariable("PA_POINTALLY") != null)
                        System.Console.Error.WriteLine($"POINTALLY {bp.name} aoe={p.AoETargets} buffs=[{string.Join(",", p.TargetBuffs.Select(b => b.name))}]");
                }
                self = !harmfulOnly && bp.CanTargetSelf;
                if (p.Heals.Count > 0 && bp.CanTargetFriends) allies = true;
                if (harmfulOnly && p.IsAoE && bp.CanTargetSelf && !bp.CanTargetEnemies && !bp.CanTargetPoint) { self = true; selfOnly = true; }
            }
            if (!enemies && !allies && !self && p.Summons && bp.CanTargetPoint) self = true;   // SummonPoint
            if (selfOnly && self && p.IsAoE)
            {
                // an area around the caster reaches both sides it is meant for
                enemies = p.AoETargets != TargetType.Ally && (p.HarmfulEffects || p.DealsDamage || p.Maneuvers.Count > 0 || p.IsWeaponAttack);
                allies = p.AoETargets != TargetType.Enemy;
            }
            if (!enemies && !allies && !self)
            {
                row.Verdict = "NEVER OFFERED: no side to aim at";
                row.Detail = $"flags: toEnemy={p.HarmfulToEnemies} harmfulBuffs={p.AppliesHarmfulBuffs} helpful={p.HelpfulToAllies} dmg={p.DealsDamage} aoe={p.IsAoE} " +
                             $"onEnemy={bp.EffectOnEnemy} onAlly={bp.EffectOnAlly} tbuffs=[{string.Join(",", p.TargetBuffs.Select(b => b.name + (b.Harmful ? "(H)" : "")).Take(3))}] " +
                             $"cbuffs=[{string.Join(",", p.CasterBuffs.Select(b => b.name).Take(2))}] harmfulOnly={harmfulOnly} helpfulOnly={helpfulOnly} enemies?{bp.CanTargetEnemies} friends?{bp.CanTargetFriends} self?{bp.CanTargetSelf} point?{bp.CanTargetPoint} range={bp.Range}";
                return;
            }

            // --- ScoreAbility: what each side values ---
            var modelled = new List<string>();
            var guessed = new List<string>();
            zeroBuff = null;
            if (enemies)
            {
                if (p.DealsDamage) modelled.Add("damage");
                if (p.Maneuvers.Count > 0) modelled.Add("maneuver");
                if (p.IsWeaponAttack) modelled.Add("weapon attack");
                if (p.MeleeAttack != null && !p.OnlyMeleeAttack) modelled.Add("melee attack");
                if (p.TargetBuffs.Count > 0 || p.Pit != null)
                {
                    bool mark = !p.HarmfulEffects && p.ControlStrength == ControlStrength.None;
                    if (mark && p.IsAoE) { }
                    else if (p.ControlStrength != ControlStrength.None) modelled.Add($"control {p.ControlStrength}");
                    else if (!mark)
                    {
                        bool poison = p.TargetBuffs.Any(b => b.GetComponents<Kingmaker.Designers.Mechanics.Buffs.BuffPoisonStatDamage>()
                                                                .Any(c => c.Stat == Kingmaker.EntitySystem.Stats.StatType.Constitution));
                        var penalties = new[] { BuffPartKind.Attack, BuffPartKind.Damage, BuffPartKind.ArmorClass, BuffPartKind.Saves,
                                                BuffPartKind.ConditionalSaves, BuffPartKind.Strength, BuffPartKind.Dexterity,
                                                BuffPartKind.Constitution, BuffPartKind.MentalStat };
                        bool readable = p.TargetBuffs.SelectMany(b => BuffPartsReader.Read(b).Parts).Any(x => penalties.Contains(x.Kind));
                        if (poison) modelled.Add("poison (Con as HP)");
                        else if (readable && p.TargetBuffs.Any(b => BuffPartsReader.Read(b).Unread.Count > 0))
                            modelled.Add("debuff by its penalties (at least 0.25: some unread)");
                        else if (readable) modelled.Add("debuff by its penalties");
                        else guessed.Add("debuff (0.25 of worth)");
                    }
                    else guessed.Add("mark (0.15 of worth)");
                    if (p.ControlStrength == ControlStrength.None)
                        parts.Add("enemy-buff components: " + string.Join(",", p.TargetBuffs.SelectMany(b => b.ComponentsArray ?? new BlueprintComponent[0])
                                                                                .Where(c => c != null).Select(c => c.GetType().Name).Distinct()));
                }
            }
            {
                if ((allies || self) && p.Heals.Count > 0) modelled.Add("heal");
                // ScoreAbility: a buff on the caster counts whatever the target; the target's buffs only on a friend
                bool appliesHelpful = !p.HarmfulEffects || p.HelpfulToAllies
                    ? ((allies || self) && p.TargetBuffs.Count > 0) || p.CasterBuffs.Count > 0
                    : p.CasterBuffs.Count > 0;
                if (appliesHelpful && !p.DealsDamage && !p.IsCleanse && !p.HealsBurn)
                {
                    var buffs = (p.TargetBuffs.Count > 0 && (allies || self) ? p.TargetBuffs : p.CasterBuffs).ToList();
                    BuffVerdict(bp, p, buffs, modelled, guessed, parts);
                }
                if ((allies || self) && p.RemovesAnything) modelled.Add("cleanse");
                if (p.Extensions.Count > 0) modelled.Add("extends");
            }
            if (p.Summons) modelled.Add("summon");
            if (p.Dispels || p.RemovesBuffs) modelled.Add(p.Dispels ? "dispel" : "removal");
            bool verifiable = p.DealsDamage || p.Heals.Count > 0;
            if (modelled.Count == 0 && guessed.Count == 0 && !verifiable && p.Enchants)
                guessed.Add(p.WeaponEnchants.Count > 0 ? "enchantment (read: see parts)" : "enchant (spell-level guess)");
            if (p.WeaponEnchants.Count > 0 && (allies || self))
            {
                var ep = EnchantmentParts(p);
                if (ep.Count > 0) { modelled.Add("weapon enchantment"); parts.AddRange(ep); guessed.Remove("enchantment (read: see parts)"); }
            }

            row.Detail = (parts.Count > 0 ? "parts: " + string.Join("; ", parts.Distinct().Take(8)) + " | " : "") +
                         $"sides: {(enemies ? "enemy " : "")}{(allies ? "ally " : "")}{(self ? "self" : "")}";
            if (modelled.Count > 0)
                row.Verdict = "valued: " + string.Join(", ", modelled.Distinct()) + (guessed.Count > 0 ? " (+ guessed " + string.Join(", ", guessed) + ")" : "");
            else if (guessed.Count > 0)
                row.Verdict = "GUESSED: " + string.Join(", ", guessed.Distinct());
            else if (zeroBuff != null)
                row.Verdict = "nothing: " + zeroBuff;
            else if (!p.HasDetectableEffect)
                row.Verdict = "nothing: effect not understood";
            else
                row.Verdict = "nothing: its effects are on a side it is not aimed at";
        }

        /// <summary>TacticalEngine: AppraiseBuff when any part is read (not only mental stats / spell prohibition), else
        /// BuffQuality (BuffEffectAnalyzer, enchant 0.8, unrecognised 0.5 unless a limited use, 0.05 a marker).</summary>
        [ThreadStatic] private static string zeroBuff;

        private static void BuffVerdict(BlueprintAbility bp, AbilityProfile p, List<BlueprintBuff> buffs, List<string> modelled,
                                        List<string> guessed, List<string> parts)
        {
            var read = buffs.Select(BuffPartsReader.Read).ToList();
            var all = read.SelectMany(r => r.Parts).ToList();
            all.AddRange(EnchantmentPartObjects(p));
            if (all.Any(x => x.Kind != BuffPartKind.MentalStat && x.Kind != BuffPartKind.ForbidsSpells))
            {
                modelled.Add("buff parts");
                parts.AddRange(all.Select(x => x.ToString()));
                var unread = read.SelectMany(r => r.Unread).Distinct().ToList();
                if (unread.Count > 0) parts.Add("unread: " + string.Join(",", unread.Take(4)));
                return;
            }
            bool limited = bp.GetComponents<AbilityResourceLogic>().Any(l => l.IsSpendResource && l.RequiredResource != null);
            float best = 0f;
            string why = "";
            foreach (var b in buffs)
            {
                var a = BuffEffectAnalyzer.Analyze(b);
                float q = a.Recognized ? a.CombatValue : p.Enchants ? 0.8f : a.OnlyUnrecognized ? (limited ? 0f : 0.5f) : 0.05f;
                if (q > best) { best = q; why = a.Recognized ? $"analyzer {a.CombatValue:0.##}" : p.Enchants ? "enchant carrier 0.8" : a.OnlyUnrecognized ? "unread 0.5" : "marker 0.05"; }
            }
            parts.Add("unread: " + string.Join(",", read.SelectMany(r => r.Unread).Distinct().Take(5)));
            parts.Add("components: " + string.Join(",", buffs.SelectMany(b => b.ComponentsArray ?? new BlueprintComponent[0])
                                                            .Where(c => c != null).Select(c => c.GetType().Name).Distinct()));
            if (best >= 0.5f || (best > 0.05f)) guessed.Add($"buff by old quality ({why})");
            else if (best > 0f) guessed.Add($"buff worth ~0 ({why})");
            else zeroBuff = limited ? "unreadable buff on a limited-use ability (worth 0)" : "buff worth 0";
        }

        private static List<string> EnchantmentParts(AbilityProfile p) => EnchantmentPartObjects(p).Select(x => x.ToString()).ToList();

        private static List<BuffPart> EnchantmentPartObjects(AbilityProfile p)
        {
            var list = new List<BuffPart>();
            foreach (var e in p.WeaponEnchants)
                foreach (var en in e.Enchantments)
                    list.AddRange(BuffPartsReader.ReadEnchantment(en).Parts);
            return list;
        }
    }
}
