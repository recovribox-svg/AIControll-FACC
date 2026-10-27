// ★ v0.3.20 (test4): how dangerous each enemy is, by what it can do and what it did.
//
// Until 0.3.20-test3 an enemy's importance (CombatMath.Threat) was its CR alone: a CR 2 wizard with Color Spray left and a
// CR 2 dretch counted the same, whatever either did in the fight. Once a round, for every enemy of the party in combat:
//   - its weapons: the full attack against the party member it is most likely to attack (the one it engages, the weakest
//     of those; else the nearest), by the game's rules - attack bonus against that ally's AC, damage after its damage
//     reduction, criticals (CombatMath.EstimateWeaponAttack, the estimate the party's own attacks use);
//   - its spells and abilities with a use left that the game flags as harmful to enemies (spellbook spells it can still
//     cast, spell-like and supernatural abilities): the best one against that ally - damage times the chance it lands,
//     control as the ally's worth for the rounds it lasts, more for an area that catches more of the party - up to six,
//     highest spell level first (AbilityProfile, cached per blueprint);
//   - its heals with a use left (spells, abilities): what the best restores on its most hurt ally - an enemy healer
//     undoes the party's damage, and would otherwise read as harmless;
//   - what it really dealt to the party in this fight's completed rounds, blended with the weapon prediction
//     (ValueModel.ObservedOffence: two rounds of prior).
// Its offence is the largest of the spell, the heal and the blended weapon value (one of them a round). Its share of the fight (ValueModel.ThreatShare,
// 0.5..2) redistributes the CR-based total by offence: the order between enemies changes, the fight's weight does not.
// On top, for the worth of taking an enemy down or holding it (not for the danger it poses - that would count twice), the
// chance the ally it attacks drops to it this round (ValueModel.Urgency).
// Logged: who leads at the start of a fight, when the lead changes, and who really dealt the most at its end.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics.Actions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class EnemyThreat
    {
        private sealed class Assessment
        {
            public string Name;
            public float Worth;          // ValueModel.PerRound(CR)
            public float Weapon;         // full attack on its likely victim, by the rules
            public float Spell;          // its best castable harmful ability on that victim (and those around)
            public string SpellName;
            public float Heal;           // its best castable heal on its most hurt ally
            public string HealName, HealTarget;
            public float Offence;        // max(spell, weapon blended with what it dealt)
            public float Share = 1f;
            public float PeakShare;
            public UnitEntityData Victim;
            public string VictimName;
            public float Zone;           // how far it reaches a victim (IncomingParts' zone)
            /// <summary>★ v0.3.20 (test5): the descriptors of its harmful abilities with a use left and of its features
            /// (Frightful Presence): what kinds of effect it can use on the party.</summary>
            public Kingmaker.Blueprints.Classes.Spells.SpellDescriptor Descriptors;
            // ★ test1 fixes: retain typed damage from the existing assessment, without another spell/rule query.
            public DamageEnergyType? EnergyType;
            public float EnergyDamage, EnergyPerCast, PredictedOffence;
        }

        /// <summary>The enemies of the last assessment.</summary>
        private static readonly List<string> Living = new List<string>();

        private static readonly Dictionary<string, Assessment> Assessed = new Dictionary<string, Assessment>();
        private static readonly Dictionary<string, float> Dealt = new Dictionary<string, float>();
        private static readonly Dictionary<string, Dictionary<DamageEnergyType, float>> EnergyDealt =
            new Dictionary<string, Dictionary<DamageEnergyType, float>>();
        private static readonly Dictionary<string, Dictionary<DamageEnergyType, int>> EnergyEvents =
            new Dictionary<string, Dictionary<DamageEnergyType, int>>();
        private static readonly Dictionary<string, int> FirstRound = new Dictionary<string, int>();
        private static readonly Dictionary<string, KeyValuePair<int, float>> UrgencyCache = new Dictionary<string, KeyValuePair<int, float>>();
        private static int _round = int.MinValue, _setHash;
        private static bool _startReported;
        private static Assessment _leader;

        /// <summary>Most harmful abilities assessed per enemy (highest spell level first).</summary>
        private const int MaxAbilities = 6;

        /// <summary>A new fight (and the end of one, after <see cref="ReportFight"/>).</summary>
        public static void Clear()
        {
            Assessed.Clear();
            Living.Clear();
            Dealt.Clear();
            EnergyDealt.Clear();
            EnergyEvents.Clear();
            FirstRound.Clear();
            UrgencyCache.Clear();
            _round = int.MinValue;
            _setHash = 0;
            _startReported = false;
            _leader = null;
        }

        /// <summary>Damage an enemy dealt to the party (FightStats sees every damage roll).</summary>
        public static void NoteDealt(UnitEntityData enemy, int dealt, RuleDealDamage damage = null)
        {
            if (enemy == null || dealt <= 0) return;
            Dealt.TryGetValue(enemy.UniqueId, out float sum);
            Dealt[enemy.UniqueId] = sum + dealt;
            // Attribute only the native completed result's known energy components. Normalizing to Result preserves
            // native halving/redirection/caps without copying their rules. Unknown source data stays unattributed.
            if (damage?.ResultList == null || damage.ResultList.Any(r => r.Source == null)) return;
            float total = damage.ResultList.Sum(r => Math.Max(0, r.FinalValue));
            if (total <= 0f) return;
            var eventEnergy = new Dictionary<DamageEnergyType, float>();
            foreach (var result in damage.ResultList)
            {
                if (!(result.Source is EnergyDamage energy) || result.FinalValue <= 0) continue;
                eventEnergy.TryGetValue(energy.EnergyType, out float previous);
                eventEnergy[energy.EnergyType] = previous + dealt * result.FinalValue / total;
            }
            foreach (var entry in eventEnergy)
            {
                if (!EnergyDealt.TryGetValue(enemy.UniqueId, out var types))
                    EnergyDealt[enemy.UniqueId] = types = new Dictionary<DamageEnergyType, float>();
                if (!EnergyEvents.TryGetValue(enemy.UniqueId, out var events))
                    EnergyEvents[enemy.UniqueId] = events = new Dictionary<DamageEnergyType, int>();
                types.TryGetValue(entry.Key, out float previous);
                types[entry.Key] = previous + entry.Value;
                // One completed native damage event, not one count per same-energy result-list entry.
                events.TryGetValue(entry.Key, out int count);
                events[entry.Key] = count + 1;
            }
        }

        /// <summary>
        /// ★ test1 fixes: add known matching-energy shares to an existing incoming-threat weighting. Unknown energy
        /// contributes zero. Uses the same observed-offence prior as threat assessment, not a generic 15% allowance.
        /// </summary>
        internal static void AddEnergyExposure(UnitEntityData enemy, float weight, Dictionary<DamageEnergyType, float> totals,
            Dictionary<DamageEnergyType, float> packetSizeTotals)
        {
            if (enemy == null || weight <= 0f) return;
            Assessed.TryGetValue(enemy.UniqueId, out var assessment);
            Dealt.TryGetValue(enemy.UniqueId, out float dealt);
            EnergyDealt.TryGetValue(enemy.UniqueId, out var observed);
            EnergyEvents.TryGetValue(enemy.UniqueId, out var events);
            if (observed != null)
                foreach (var entry in observed)
                {
                    float predicted = assessment?.EnergyType == entry.Key ? assessment.EnergyDamage : 0f;
                    float share = ValueModel.EnergyExposureShare(entry.Value, dealt, predicted, assessment?.PredictedOffence ?? 0f);
                    totals.TryGetValue(entry.Key, out float previous);
                    totals[entry.Key] = previous + weight * share;
                    int count = 0;
                    events?.TryGetValue(entry.Key, out count);
                    float size = ValueModel.EnergyPacketSize(assessment?.EnergyType == entry.Key ? assessment.EnergyPerCast : 0f,
                        entry.Value, count);
                    packetSizeTotals.TryGetValue(entry.Key, out float previousSize);
                    packetSizeTotals[entry.Key] = previousSize + weight * share * size;
                }
            if (assessment?.EnergyType is DamageEnergyType type && (observed == null || !observed.ContainsKey(type)))
            {
                float share = ValueModel.EnergyExposureShare(0f, dealt, assessment.EnergyDamage, assessment.PredictedOffence);
                totals.TryGetValue(type, out float previous);
                totals[type] = previous + weight * share;
                packetSizeTotals.TryGetValue(type, out float previousSize);
                packetSizeTotals[type] = previousSize + weight * share * ValueModel.EnergyPacketSize(assessment.EnergyPerCast, 0f, 0);
            }
        }

        /// <summary>The enemy's share of the fight's importance (1 when unknown).</summary>
        public static float Share(UnitEntityData enemy) =>
            enemy != null && Assessed.TryGetValue(enemy.UniqueId, out var a) ? a.Share : 1f;

        /// <summary>
        /// What taking this enemy down or holding it gains from the ally it may drop this round (1..1.5): the chance that
        /// ally drops to its offence (scaled like all predicted damage by the fight's danger calibration) while within its
        /// reach. Once a frame.
        /// </summary>
        public static float Urgency(UnitEntityData enemy)
        {
            if (enemy == null || !Assessed.TryGetValue(enemy.UniqueId, out var a) || a.Victim == null) return 1f;
            int frame = Time.frameCount;
            if (UrgencyCache.TryGetValue(enemy.UniqueId, out var cached) && cached.Key == frame) return cached.Value;
            float urgency = 1f;
            try
            {
                var victim = a.Victim;
                if (victim.Descriptor.State.IsConscious &&
                    Vector3.Distance(enemy.Position, victim.Position) <= a.Zone + victim.Corpulence)
                {
                    float drop = RiskMath.RoundDropChance(CombatMath.HpBeforeDown(victim), a.Offence * FightStats.LastDangerScale);
                    urgency = ValueModel.Urgency(drop, victim.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() != null);
                }
            }
            catch
            {
                urgency = 1f;
            }
            if (UrgencyCache.Count > 256) UrgencyCache.Clear();
            UrgencyCache[enemy.UniqueId] = new KeyValuePair<int, float>(frame, urgency);
            return urgency;
        }

        /// <summary>
        /// Assess the enemies once a round, and again when the set of enemies changes (one joins, one falls). Called by every
        /// decision (TacticalEngine.BuildContext); cheap when nothing changed.
        /// </summary>
        /// <summary>★ v0.3.20 (test5): how often the enemies were assessed (the slow-decision line says whether this one did).</summary>
        internal static int Assessments;

        public static void Refresh(int round)
        {
            List<UnitEntityData> enemies, party;
            try
            {
                enemies = new List<UnitEntityData>();
                party = new List<UnitEntityData>();
                foreach (var u in Game.Instance.State.Units)
                {
                    if (u == null || u.Descriptor?.State == null || u.Descriptor.State.IsDead || !u.Descriptor.State.IsConscious) continue;
                    if (!(u.CombatState?.IsInCombat ?? false)) continue;
                    if (u.IsPlayerFaction) party.Add(u);
                    else if (u.IsPlayersEnemy) enemies.Add(u);
                }
            }
            catch
            {
                return;
            }
            int hash = enemies.Count;
            foreach (var e in enemies) hash = hash * 31 + e.UniqueId.GetHashCode();
            if (round == _round && hash == _setHash) return;
            _round = round;
            _setHash = hash;
            if (enemies.Count == 0 || party.Count == 0) return;
            Assessments++;

            Living.Clear();
            foreach (var enemy in enemies)
            {
                Living.Add(enemy.UniqueId);
                if (!FirstRound.ContainsKey(enemy.UniqueId)) FirstRound[enemy.UniqueId] = round;
                if (!Assessed.TryGetValue(enemy.UniqueId, out var a)) Assessed[enemy.UniqueId] = a = new Assessment();
                Assess(enemy, a, party, enemies, round);
            }

            float totalOffence = 0f, totalWorth = 0f;
            foreach (var enemy in enemies)
            {
                var a = Assessed[enemy.UniqueId];
                totalOffence += a.Offence;
                totalWorth += a.Worth;
            }
            foreach (var enemy in enemies)
            {
                var a = Assessed[enemy.UniqueId];
                a.Share = ValueModel.ThreatShare(a.Offence, a.Worth, totalOffence, totalWorth);
                a.PeakShare = Math.Max(a.PeakShare, a.Share);
            }
            Report(enemies, round);
        }

        private static void Assess(UnitEntityData enemy, Assessment a, List<UnitEntityData> party, List<UnitEntityData> enemies, int round)
        {
            a.Name = enemy.CharacterName;
            float cr = 1f;
            try { cr = Math.Max(1, enemy.Blueprint?.CR ?? 1); } catch { }
            a.Worth = ValueModel.PerRound(cr);
            a.Weapon = 0f;
            a.Spell = a.Heal = 0f;
            a.EnergyType = null;
            a.EnergyDamage = 0f;
            a.EnergyPerCast = 0f;
            a.SpellName = a.HealName = a.HealTarget = null;
            try
            {
                bool ranged = (enemy.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false) || (enemy.Descriptor?.Spellbooks?.Any() ?? false);
                a.Zone = ranged ? 20f : enemy.CurrentSpeedMps * 3f + 2f + enemy.Corpulence;
                var victim = LikelyVictim(enemy, party);
                a.Victim = victim;
                a.VictimName = victim?.CharacterName;
                var usable = Usable(enemy);
                a.Descriptors = DescriptorsOf(enemy, usable);
                if (victim != null)
                {
                    a.Weapon = CombatMath.EstimateWeaponAttack(enemy, victim, full: true)?.ExpectedDamage ?? 0f;
                    a.Spell = BestAbility(enemy, victim, party, usable, out a.SpellName, out a.EnergyType, out a.EnergyDamage,
                        out a.EnergyPerCast);
                }
                a.Heal = BestHeal(enemy, enemies, usable, out a.HealName, out a.HealTarget);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Threat] {enemy.CharacterName}: {ex.Message}");
            }
            Dealt.TryGetValue(enemy.UniqueId, out float dealt);
            a.PredictedOffence = Math.Max(Math.Max(a.Spell, a.Heal), a.Weapon);
            int rounds = Math.Max(0, round - (FirstRound.TryGetValue(enemy.UniqueId, out int first) ? first : round));
            a.Offence = Math.Max(Math.Max(a.Spell, a.Heal), ValueModel.ObservedOffence(dealt, rounds, a.Weapon));
        }

        /// <summary>
        /// ★ v0.3.20 (test5): the share of the enemies in the fight that can use an effect of <paramref name="descriptor"/>
        /// on the party (a harmful ability with a use left, or a feature of that kind), at least 0.1 (an aura, a trap or a
        /// newcomer the assessment cannot see); 0.5 before the first assessment. What a save bonus against that kind of
        /// effect is worth (Remove Fear's +4 against fear).
        /// </summary>
        public static float ShareUsing(Kingmaker.Blueprints.Classes.Spells.SpellDescriptor descriptor)
        {
            if (Living.Count == 0) return 0.5f;
            int using_ = Living.Count(id => Assessed.TryGetValue(id, out var a) && (a.Descriptors & descriptor) != 0);
            return Math.Max(0.1f, using_ / (float)Living.Count);
        }

        private static Kingmaker.Blueprints.Classes.Spells.SpellDescriptor DescriptorsOf(UnitEntityData enemy, List<AbilityData> usable)
        {
            var found = Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None;
            try
            {
                foreach (var ability in usable)
                    if (IsHarmful(ability)) found |= ability.Blueprint.SpellDescriptor;
                foreach (var feature in enemy.Descriptor.Progression.Features.Enumerable)
                {
                    var component = feature?.Blueprint == null ? null
                        : Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.Blueprints.Classes.Spells.SpellDescriptorComponent>(feature.Blueprint);
                    if (component != null) found |= component.Descriptor.Value;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Threat] {enemy.CharacterName}'s descriptors: {ex.Message}");
            }
            return found;
        }

        /// <summary>The party member the enemy is most likely to attack: the weakest it engages, else the nearest.</summary>
        private static UnitEntityData LikelyVictim(UnitEntityData enemy, List<UnitEntityData> party)
        {
            UnitEntityData engaged = null;
            try
            {
                engaged = enemy.CombatState?.EngagedUnits?
                    .Where(u => u != null && u.IsPlayerFaction && u.Descriptor.State.IsConscious)
                    .OrderBy(CombatMath.HpPercent).FirstOrDefault();
            }
            catch { }
            return engaged ?? party.OrderBy(p => Vector3.Distance(p.Position, enemy.Position)).FirstOrDefault();
        }

        /// <summary>
        /// The best harmful ability the enemy can still use on <paramref name="victim"/>: damage times the chance it lands;
        /// control as the victim's worth a round for up to two rounds, times the chance; x(1 + half the other party members
        /// in its area).
        /// </summary>
        private static float BestAbility(UnitEntityData enemy, UnitEntityData victim, List<UnitEntityData> party, List<AbilityData> usable,
                                         out string name, out DamageEnergyType? energyType, out float energyDamage, out float energyPerCast)
        {
            name = null;
            energyType = null;
            energyDamage = 0f;
            energyPerCast = 0f;
            float best = 0f;
            foreach (var ability in usable.Where(IsHarmful).Take(MaxAbilities))
            {
                try
                {
                    var profile = AbilityProfile.Get(ability);
                    if (profile == null) continue;
                    float value = 0f;
                    float damage = 0f;
                    float damageIfLands = 0f;
                    if (profile.DealsDamage)
                    {
                        damageIfLands = CombatMath.EstimateAbilityDamage(ability, profile, victim);
                        value += damage = damageIfLands * CombatMath.EffectChance(enemy, victim, ability, profile, true);
                    }
                    float control = profile.ControlStrength == ControlStrength.Hard ? 1f : profile.ControlStrength == ControlStrength.Soft ? 0.4f : 0f;
                    if (control > 0f && !Analysis.AbilityClassifier.HasCCImmunity(victim, profile.Control))
                    {
                        float rounds = Mathf.Clamp(profile.EffectRoundsOn(ability, victim), 1f, 2f);
                        int level = Math.Max(1, victim.Descriptor?.Progression?.CharacterLevel ?? 1);
                        value += control * ValueModel.PerRound(level) * rounds * CombatMath.EffectChance(enemy, victim, ability, profile, false);
                    }
                    if (value <= 0f) continue;
                    float area = profile.IsAoE
                        ? 1f + 0.5f * party.Count(p => p != victim && Vector3.Distance(p.Position, victim.Position) <= profile.AoERadius)
                        : 1f;
                    value *= area;
                    if (value > best)
                    {
                        best = value;
                        name = ability.Name;
                        energyType = SingleEnergy(profile);
                        energyDamage = energyType.HasValue ? damage * area : 0f;
                        // Aggregate one cast's same-energy damage on one victim as a single packet. Multi-packet
                        // delivery can only make this resistance-rate estimate conservative; never use weapon hit size.
                        energyPerCast = energyType.HasValue ? damageIfLands : 0f;
                    }
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Threat] {enemy.CharacterName}'s {ability?.Name}: {ex.Message}");
                }
            }
            return best;
        }

        /// <summary>Only known, single-energy native damage is attributed before observation; mixed/custom stays unknown.</summary>
        private static DamageEnergyType? SingleEnergy(AbilityProfile profile)
        {
            DamageEnergyType? type = null;
            foreach (var action in profile.Damage)
            {
                if (action?.GetType() != typeof(ContextActionDealDamage)) return null;
                var damage = (ContextActionDealDamage)action;
                if (damage.DamageType?.Type != DamageType.Energy) return null;
                if (type.HasValue && type.Value != damage.DamageType.Energy) return null;
                type = damage.DamageType.Energy;
            }
            return type;
        }

        /// <summary>
        /// The heal it can still use that restores most on its most hurt ally (the HP it is missing at most); of its six
        /// highest-level helpful abilities. 0 when nobody on its side is hurt.
        /// </summary>
        private static float BestHeal(UnitEntityData enemy, List<UnitEntityData> enemies, List<AbilityData> usable, out string name, out string target)
        {
            name = target = null;
            var hurt = enemies.Where(e => e.HPLeft < e.MaxHP).OrderBy(CombatMath.HpPercent).FirstOrDefault();
            if (hurt == null) return 0f;
            float best = 0f;
            foreach (var ability in usable.Where(IsHelpful).Take(MaxAbilities))
            {
                try
                {
                    var profile = AbilityProfile.Get(ability);
                    if (profile == null || profile.Heals.Count == 0) continue;
                    float heal = Math.Min(CombatMath.EstimateAbilityHeal(ability, profile, hurt), hurt.MaxHP - hurt.HPLeft);
                    if (heal > best)
                    {
                        best = heal;
                        name = ability.Name;
                        target = hurt.CharacterName;
                    }
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Threat] {enemy.CharacterName}'s {ability?.Name}: {ex.Message}");
                }
            }
            return best;
        }

        /// <summary>
        /// Spells it can still cast and abilities with a use left that the game flags as harmful to its enemies or helpful
        /// to its allies, highest spell level first.
        /// </summary>
        private static List<AbilityData> Usable(UnitEntityData enemy)
        {
            var found = new List<KeyValuePair<int, AbilityData>>();
            try
            {
                foreach (var book in enemy.Descriptor.Spellbooks)
                {
                    foreach (var spell in book.GetAllKnownSpells().Concat(book.GetAllCustomSpells()))
                    {
                        if (spell?.Blueprint == null || !(IsHarmful(spell) || IsHelpful(spell))) continue;
                        if (!book.CanSpend(spell)) continue;
                        found.Add(new KeyValuePair<int, AbilityData>(book.GetSpellLevel(spell), spell));
                    }
                }
                foreach (var fact in enemy.Abilities.Enumerable)
                {
                    var data = fact?.Data;
                    if (data?.Blueprint == null || !(IsHarmful(data) || IsHelpful(data)) || !data.IsAvailableForCast) continue;
                    found.Add(new KeyValuePair<int, AbilityData>(data.SpellLevel, data));
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Threat] {enemy.CharacterName}'s abilities: {ex.Message}");
            }
            return found.OrderByDescending(f => f.Key).Select(f => f.Value).ToList();
        }

        // ★ v0.3.21: not an ally hex flagged harmful (AbilityProfile.AimsAtFriendsOnly).
        private static bool IsHarmful(AbilityData ability) =>
            ability.Blueprint.EffectOnEnemy == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityEffectOnUnit.Harmful &&
            !AbilityProfile.AimsAtFriendsOnly(ability.Blueprint);

        private static bool IsHelpful(AbilityData ability) =>
            ability.Blueprint.EffectOnAlly == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityEffectOnUnit.Helpful;

        private static string Describe(Assessment a)
        {
            string why;
            if (a.Spell > 0f && a.Spell >= a.Offence - 0.01f) why = $"{a.SpellName} {a.Spell:0.#} on {a.VictimName}";
            else if (a.Heal > 0f && a.Heal >= a.Offence - 0.01f) why = $"heals: {a.HealName} {a.Heal:0.#} on {a.HealTarget}";
            else why = $"attacks {a.Weapon:0.#} on {a.VictimName}" +
                       (Math.Abs(a.Offence - a.Weapon) > 0.5f ? $", {a.Offence:0.#} a round with what it dealt" : "");
            return $"{a.Name} x{a.Share:0.0} ({why})";
        }

        private static void Report(List<UnitEntityData> enemies, int round)
        {
            if (enemies.Count < 2) return;
            var ranked = enemies.Select(e => Assessed[e.UniqueId]).OrderByDescending(a => a.Share * a.Worth).ToList();
            string tag = $"{(CombatMemory.IsRealTime ? "RT" : "R")}{round}";
            if (!_startReported)
            {
                _startReported = true;
                _leader = ranked[0];
                string rest = ranked.Count > 3 ? $"; {ranked.Count - 3} more x{ranked.Skip(3).Min(a => a.Share):0.0}-{ranked.Skip(3).Max(a => a.Share):0.0}" : "";
                Main.Log($"[Threat] {tag}: most dangerous first - {string.Join(", ", ranked.Take(3).Select(Describe))}{rest}");
                return;
            }
            // A new lead is logged when it is clearly ahead (15 %), not each time two alike enemies swap.
            var leader = ranked.Contains(_leader) ? _leader : null;
            if (ranked[0] != _leader && (leader == null || ranked[0].Share * ranked[0].Worth > 1.15f * leader.Share * leader.Worth))
            {
                _leader = ranked[0];
                if (leader != null) Main.Log($"[Threat] {tag}: now the most dangerous - {Describe(ranked[0])}");
                else Main.Verbose($"[Threat] {tag}: the most dangerous now - {Describe(ranked[0])}");
            }
        }

        /// <summary>At the end of a fight: who really dealt the most to the party, against how it was rated.</summary>
        public static void ReportFight()
        {
            var top = Dealt.Where(d => d.Value > 0f).OrderByDescending(d => d.Value).Take(3).ToList();
            if (top.Count == 0 || Assessed.Count < 2) return;
            var parts = top.Select(d =>
            {
                Assessed.TryGetValue(d.Key, out var a);
                string name = a?.Name ?? "?";
                return $"{name} {d.Value:0} (rated up to x{(a != null && a.PeakShare > 0f ? a.PeakShare : 1f):0.0})";
            });
            Main.Log($"[Threat] fight: dealt the most to the party - {string.Join(", ", parts)}");
        }
    }
}
