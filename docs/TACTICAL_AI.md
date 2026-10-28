# Turn-based tactical AI (v0.3.0, revised in v0.3.1-v0.3.7)

This document describes how companions decide what to do in **turn-based combat**. Real-time combat still uses
the older `UnifiedDecisionEngine`, which already handled weapon attacks. It now shares the consumable settings.

## 1. Why it was rewritten

Evidence from a v0.2.119 play session log:

| Symptom you saw | Log evidence | Cause in the old planner (`TurnPlanBuilder`) |
|---|---|---|
| Lann never shot, walked away | `Lann abilities: ... Attacks=0` on all 33 analyses, 16× `Retreat to safe position` | Only *abilities* counted as attacks. A plain weapon attack (the game's `UnitAttack`) was never an option, so "no attack" meant retreat. |
| Albedo wandered | move → `Replan needed: Position changed by 5.1m` → `Hittable: 0/6` → `Retreat - no targets` | Same as above, plus the plan invalidated itself because of the unit's *own* movement. |
| Orvath kept "appraising" | `Attack: Appraise Foe` chosen 9× | Enemy-targeted utility abilities were scored as attacks. |
| Buff spam | `Spirit Weapon Enchantment` 8× | "Already applied" detection returned *not applied* for effects it could not parse (enchantments, nested actions). |
| Wasted turns | 54× `Move target invalid: not walkable or occupied` | The planner computed melee squares itself and the mod's own grid rejected them. |
| No item use | – | Items in quick slots are abilities with a `SourceItem`; they were never valued. |
| Ridden pet followed the cursor | game code | `SaddledUnitController` creates the mount's command without a `ForcedPath`. |

## 2. How a decision is made

Every time the companion's command slots are empty during its turn, `TurnBasedController` asks
`Tactics/TacticalEngine.Decide` for **one** next action, executes it through `CommandDispatcher`, and waits for the
game to finish it. There is no whole-turn plan to invalidate. The next decision sees the new position, the remaining
actions, and the HP or death of the target.

1. **Context** (`TacticalContext`)
   - **Remaining actions:** standard, move, swift, full-round, and whether a full attack or a 5-foot step is still possible.
   - **Remaining move distance:** one move action's worth, from the game's `TurnController`.
   - **Units:** enemies, allies, and who is in melee with us (`EngagedBy`).
   - **Expected rounds left:** estimated from total enemy HP.
   - **Difficulty:** enemy threat compared with party strength.
2. **Candidates.** Every option gets one value in *expected damage-equivalent points* (below).
3. **Selection.** A free or swift option worth at least 2 points goes first, because it doesn't use the main action
   (for example Smite Evil before a full attack, or a 5-foot step out of melee). Otherwise the best affordable main
   action is chosen. Nothing worth at least 0.5 points means the turn ends.
4. **Learning within the turn.** An option is not retried in the same turn if it could not be issued, ended without
   acting, was force-finished by the game before it started (an approach that stalled), or ended with the action it
   was chosen for (standard, full-round, swift) still unused (except commands that legitimately spend none).
5. **Waiting.** The controller waits for the command while it makes progress (the unit moves or the command is
   executing). It gives up after 4 s without progress or 15 s in total, counting only time the turn is actually ticked
   (each tick counts at most 0.1 s, so pauses, dialogs and alt-tab don't count). An idle command the AI did not issue
   (left over from real time or an out-of-combat routine) is interrupted after 1 s, except the game's own ability
   toggles, attacks of opportunity, and commands the game marks as not interruptible.

## 3. Values

**Damage value** of `d` expected damage on enemy `e`: `min(d, HP)`, plus the enemy's threat if `d ≥ HP` (a likely
kill prevents its future damage), plus some threat-weighted progress otherwise. Threat ≈ `4 + 3·CR`. It is scaled down
for enemies that are asleep, paralysed or helpless, and up for enemies currently in melee with the party.

| Option | How its value is computed |
|---|---|
| **Weapon attack** (single, move + attack, or full attack) | Attack list from the game (`UnitAttack{ForceFullAttack, OnlyForInspect}.Init`, as the unit-description UI does). For each attack: hit chance from `RuleCalculateAttackBonus` vs `RuleCalculateAC`, damage from `RuleCalculateWeaponStats` dice, minus damage reduction, plus crit and sneak attack. A ranged attack while threatened subtracts an attack-of-opportunity penalty. Full vs single attack is set explicitly; otherwise the game uses the UI smart-cursor mode. |
| **Damage spell / ability / bomb** | Dice evaluated in the ability's own context (`RecalculateRanks`), × spell-resistance pass × attack-roll hit chance (touch spells) × failed-save chance (half on save if applicable), for every enemy in the area. Hitting allies costs twice the damage; if *Avoid friendly fire* is on, the option is dropped instead. Immune targets count as 0. |
| **Heal** | Only for allies below the heal threshold (potions: *Drink healing potions below HP %* setting). Value = `min(heal, missing HP) × 0.6 × urgency` (×3 below 25 % HP, ×2 below 50 %), more if the ally is in melee. Undead only when the ability's own branch heals them (Inflict Wounds). Never aimed at an enemy. Buffs that come with a heal (Lay On Hands) add nothing on their own. |
| **Control / debuff** | Failed-save chance × SR pass × target threat × strength (hard control such as sleep, stun or hold: 1.0; soft: 0.4; plain debuff: 0.25; lasting "mark" such as Smite or a judgement: 0.15; one-round mark such as Aid Another: 0.1, halved if no other party member is fighting that enemy; area buffs never count enemies in their radius as marks) × rounds (1 for one-round or one-attack effects, else up to 3). Skipped if the target already has the effect or is immune (`HasConditionImmunity`, spell immunity). Non-control debuffs fall off after two uses per combat. |
| **Buff** | Skipped if the target already has it, it would not stack (same bonus type already as high, e.g. Mage Armor over real armor), or it was cast on them this round. `(1 + 1.5·spell level) × rounds × quality`, where quality (0-1) comes from the buff's components (`BuffEffectAnalyzer`: AC/attack/damage 1.0, saves 0.9, ability scores 0.7-0.8, conditional saves 0.2, skills 0.1, difficult-terrain immunity 0.1; unrecognised 0.5; no components 0.05). Consumables need quality ≥ 0.5. A mode switch that removes one of the recipient's good buffs (Shift Form) only if better than what it replaces. Weighted down after round 2, in easy fights, and for allies who aren't fighting. One-round or one-attack buffs (Perfect Strike) count about 2 rounds; a one-round buff on oneself only if an attack is still possible after paying for it. Not for a recipient the ability also heals (Lay On Hands). |
| **Cleanse** (removes buffs by descriptor or a specific buff, also alongside a heal, e.g. Lay On Hands mercies) | Only if the target has a *harmful* buff (flagged harmful, or put on it by an enemy) that a removal whose branch applies to it would remove: `(2 + 2·spell level) × rounds`. A descriptor cleanse's own buffs add nothing on their own. |
| **Enchantments, summons, dispels, utility** (effects that can't be verified on the target) | Once per combat per target. Utility abilities such as Appraise Foe are worth 1 point, so they only happen when nothing useful is possible. Not for abilities with a verifiable effect (damage, heal, cleanse) that is worth nothing right now, and dispel/utility never for consumables. |
| **Charge** | Only when a normal move can't reach the target this turn: ~1.1 × single-attack value. |
| **5-foot step out of melee** | Ranged characters, casters, and anyone whose best action is a spell that provokes (not aimed at an adjacent enemy), in melee: 0.4 × threat of the enemies in melee with them. Free; it keeps the full attack available. |
| **Withdraw** | Below 30 % HP and in melee, when nothing better exists. |
| **Advance** | When nothing can be attacked from here. Melee advances to reach; ranged characters advance to ~85 % of weapon range with line of sight; casters without a ranged weapon stop at ~25 ft. |

**Weights.**
- **Roles:** DPS ×1.1 damage, ×0.7 buffs. Support ×1.6 heals, ×1.5 buffs, ×0.85 damage. Tank ×1.25 against enemies already in melee with allies.
- **Range preference:** Ranged ×1.1 for ranged attacks and ×0.7 for melee; Melee is the reverse.
- **Spell slots:** levelled spells count for less in easy fights, so weapons and cantrips are preferred there.
- **Consumables:** scrolls and wands ×0.7 × Use Magic Device success chance. Thrown and alchemical items ×0.85.
  Buff potions only in hard fights, at ×0.5. Healing potions only by urgency.

**Conditional effects.** Many abilities branch on the target: Cure Wounds heals the living and harms undead, Inflict
Wounds the reverse, Lay On Hands harms undead. `AbilityProfile` records which `Conditional` branch each damage, heal,
buff and removal sits in and evaluates it per target (or against the caster, for a Conditional inside
`ContextActionOnContextCaster`), in the ability's own execution context. Heals that run on the caster or on other
units (party members, pet, random units) are not heals of the target, except for personal-range abilities. Only conditions that read facts
of the target or caster are evaluated (`HasFact`, `HasBuff`, alignment, ally/enemy, ...). For any other condition the
log says once `[Profile] <ability>: branch condition <type> not evaluated - both branches counted`.

## 4. Consumables: what the game allows

- In combat, only items in a character's **quick slots** can be used. Equipping a usable item adds an ability with
  `SourceItem` to the unit (`ItemEntity.OnDidEquipped`). Inventory use (`TryUseFromInventory`) is disabled while
  `Player.IsInCombat`.
- **Animal companions cannot use items in combat** (`AbilityData.IsAvailableForCast`).
- **Scrolls and wands of spells not on the user's list need Use Magic Device.** The engine factors in the success
  chance, and gives zero if the character has no UMD skill.
- All of this is subject to the toggles in the mod menu (Ctrl+F10 → *Consumables*), in both combat modes.

## 5. Reading the log

One line per decision (`<game>\Wrath_Data\Managed\UnityModManager\Log.txt`):

```
[Tactics] Lann R2 HP100% [SMWF5] enemies=4 rounds~2.6 diff=0.71 :: FullAttack(2)->Zombie=21.4 | Attack->Zombie=13.2 | Move+Attack->Kobold=10.8 => FullAttack(2)->Zombie
```
- `[SMWF5]`: Standard, Move, sWift, Full attack and 5-foot step available (`-` = used).
- The five best affordable options with their values, then the choice.
- `[Cmd] ... ISSUED / ENDED ... result= moved=` shows what the game then did with it.

If a companion does something odd, that line shows which option won and by how much. Please send it.

## 6. v0.3.1: fixes from the first 0.3.0 play logs

| What the log showed | Line(s) ("Recent" = Recent log, otherwise the player-controlled-Orvath log) | Cause | Fix |
|---|---|---|---|
| `Item:Cure Light Wounds->Seelah(dispel)=2.0` drunk at 100 % HP (Seelah ×2, Camellia ×1) | 307, 644; Recent 301 | At full HP the heal is worth 0, and the potion fell through to the once-per-combat fallback for unverifiable effects ("dispel"). | The fallback is only for abilities without a verifiable effect; consumables never get dispel/utility value. |
| `Item:Inflict Light Wounds->Camellia(heal)`; `Item:Inflict Light Wounds->Skeletal Champion(dmg)` → `result=Fail` | 125, 343-345 | Both branches of the "undead?" `Conditional` were counted for every target. | Per-target branch evaluation (§3, Conditional effects). Heals are never aimed at enemies. |
| `Lay On Hands — Others->Cultist Champion(debuff+buff)`; `Lay On Hands — Self->Seelah(buff)` at 92 % HP | 583, 475 | Its heal branch was ignored on living enemies, and its secondary buffs counted as a buff. | Never aimed at a living enemy; buffs that come with a heal add no value of their own. |
| Albedo `Move+Attack->Dretch` 4× with `forcedPath=0.7m`, `Success acted=True`, standard action still unused | Recent 208-222 | `UnitCommandController.TickCommandTurnBased` force-finishes an approaching command whose unit stopped moving as "Success" (`IsActed`, never `IsStarted`). | Such a command, or one that ends with its standard/full-round/swift action still unused, counts as failed for this turn. |
| `Camellia: command wait timeout - ending turn` while her 7.6 m walk was still running (4.3 s) | 64 | Timeout of 300 *frames*, about 4.3 s at 70 FPS. | Time-based wait that resets on progress (4 s idle, 15 s cap, ≤ 0.1 s per tick). |
| Camellia's turn lost to a leftover command at turn start | Recent 204 | A command not issued this turn sat idle in her slot. | Interrupted after 1 s idle (the game's own toggles and attacks of opportunity excepted). |
| Lann: `Aid Another->Cultist Champion(mark)=7.8` beat his attacks; walked 9.1 m, interrupted | 570-577 | A +2 for one round was valued like a 3-round mark at 0.15 × threat. | One-round marks 0.1 × threat for 1 round, halved if nobody else fights that enemy; archers walking up to touch an enemy ×0.5. Lasting marks unchanged. |
| `Perfect Strike->Lann(buff)` cast after the full attack (5×) | 91, 171, 222, 284, 630 | A one-attack buff was valued like a 3-round buff. | One-round self-buffs need an attack left after paying for them. If Perfect Strike's buff instead lasts longer and is spent by the next attack (`RemoveBuffOnAttack`), casting it after the attack still helps next turn's first attack, so it is only valued lower (≈ 2 rounds). |
| `Treat Affliction->Albedo(buff)` with nothing to treat | 80 | Its applied buff counted as a buff. | Descriptor cleanses are valued by what they remove. This works only if the blueprint removes by descriptor; the next log will show. |

## 7. v0.3.2: fixes from the first 0.3.1 log

All units, Orvath included. Line numbers refer to that log.

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| Camellia: `Treat Affliction->Camellia(buff)=8.6` cast on herself at R1 | 62 | Any buff was worth `(1 + 1.5·level) × rounds`, whatever it does. | Buff value × quality from its components (§3). Its components are now logged once (`[Profile] Treat Affliction: ...`), so the next log shows what it is. |
| `Item:Mage Armor->Camellia/Seelah`, `Item:Feather Step->Camellia/Seelah` | 66, 131, 1046, 1114 | Same; Mage Armor's armor bonus does not stack with Seelah's armor. | Stacking check (descriptor-based); consumables need a clear combat effect (quality ≥ 0.5). |
| Orvath: `Shift Form: Overlord / Mortal Guise->Orvath(buff)` then `...(cleanse)` every turn, ending back in Mortal Guise | 39-43, 112-116, 184-188 and every later turn (27 casts) | Mortal Guise removes the Overlord buff, which counted as a cleanse. | Only harmful buffs count as something to remove; replacing a good buff must be an improvement. |
| Orvath: `Soul Power->Orvath(mark+buff)=26-38`, recast in consecutive rounds | 51, 546, 639, 704, 804, 1032, 1103 | A caster-centred area buff counted enemies in its radius as "marks". | Area abilities never get mark value; one-round buffs count one round. Components logged once. |
| `Lay On Hands — Others->Mite Scout(buff)=8.8` (living enemy) | 81, 84 | Only the paladin's own side buff was left, and it counted. | An ability whose target effects none apply to the target is worth 0 there. |
| Albedo / Seelah / Camellia: `Advance` interrupted after 6-9 m, then `END TURN` with the standard action unused | 327, 390, 1169 | The cut-short move was marked failed. | A move that made ≥ 1 m of progress is not marked failed; the next decision can keep moving. |
| `Lann: 'move:5ft' was force-finished before it started` after a successful 5-foot step | 95, 994 | The v0.3.1 "never started" rule also caught moves, which finish that way on arrival. | The rule applies to attacks and abilities only. |

## 8. v0.3.3: Soul Power, Treat Affliction, summons, toggles

Sources: the in-game descriptions (screenshots), and the decompiled IsekaiMod 6.5.0, Demonology 0.1.5, Tome of the
Firebird 1.4.18 (its BlueprintCore reference lists name the vanilla blueprints) and Tabletop Tweaks.

| Ability | What it really does | Was | Now |
|---|---|---|---|
| **Soul Power** (vanilla Dark Elementalist, `31a1e5b2...`) | Full round: draws on dead creatures within 30 ft whose HD sum ≥ caster level, lowers burn by 1; each success raises the day's elemental-overflow attack/damage bonus. Marks every creature in the burst (`DarkElementalistSoulPowerOnceBuff`). | Valued 26-38 as buff + "marks" on enemies; cast at round 1 with no corpses. | `ContextActionHealBurn` → worth `1.5 × rounds` (+4 if there is burn), only with enough unmarked dead HD in range. |
| **Treat Affliction** (vanilla, `b89dcf50...`) | Lore (Religion) check to remove one disease/poison; once per character per day (the 23h37m buff in the screenshot is that marker). | Its marker buff counted as a buff (8.6). | Day-long buffs with no recognised combat component are markers, worth 0; only the removal (if detectable) counts. |
| **Shift Form: Overlord / Mortal Guise** (IsekaiMod) | One free-action toggle: `Conditional(HasBuff Overlord) → RemoveBuff : ApplyBuff (permanent)`. | In combat: on, then "cleanse" back off every turn (fixed in 0.3.2). After every fight the out-of-combat routine recast it, flipping the form. | In combat: turned on when it is worth it, never off. Out of combat: toggles are left to the player. |
| **Demonic Art I-III** (Demonology) and vanilla summons | Summon; `AfterSpawn` applies a buff to the summoned creature. | That buff counted as a buff on the caster. | Effects after a spawn belong to the summon; the ability is valued as a summon. |
| **Appraise Foe** (IsekaiMod) | −2 AC/saves on the enemy, +2 insight to the caster's attack/AC/saves, 1 minute. | ~11, correct. | Unchanged. |

## 9. v0.3.4: fixes from the first 0.3.3 log

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| Camellia never cast Spirit Weapon Enchantment: `Spirit Weapon Enchantment->Camellia(buff)=0.1-0.3` | 547, 2011, 5499, 8626, 9619, 13105, 15238 | Its buff has no components (`[NoComponents]`); the enchantment is applied by the ability's actions. 0.3.2 rated component-less buffs as markers (0.05). | A component-less buff of an enchanting ability counts as an enchantment (0.8). |
| `Item:Mage Armor->Seelah(buff)=3.1` used (Seelah wears plate) | 548, 2249 | Mage Armor = AC+4 (Armor) + an AC-vs-weapon-type part of unreadable size; 0.3.2 let any unknown-size part make the buff "effective". | Superseded when every bonus of known size is superseded. |
| `Treat Affliction->Orvath(dispel)=2.0` listed with nothing to treat | 4566, 8516, 9460, 11698 | The once-per-combat "dispel" fallback did not check the target. | Needs an ally with a harmful effect, or an enemy with a spell buff (Dispel Magic). |
| Smite Evil's buff rated "unrecognised" | `[Profile] Smite Evil` | `AttackBonusAgainstTarget` / `DamageBonusAgainstTarget` unknown to the analyzer. | Recognised as attack/damage bonuses. |

Checked and fine: Shift Form cast 2× in the whole log (27× before), Soul Power and Treat Affliction no longer cast,
no wait timeouts, 5-foot steps no longer reported as failures. Orvath's quickened Sleep followed by a normal Sleep on
the same target only happens when the first did not take (the target has no Sleep buff afterwards: a failed save).

## 10. v0.3.5: fixes from the first 0.3.4 log

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| After the fight: `Out-of-combat permanent buff (Soul Power)` (succeeded), `(Perfect Strike)`, `(Inflict Light Wounds)` on Camellia (`result=Fail`) | 1204, 1217, 4199-4242 | The out-of-combat routine (shared by both modes) trusted the old classifier's "permanent buff" label; hiding the Shift Form toggle in 0.3.3 exposed the next entries. | `RealTimeController.IsWorthKeepingUp`: no items, toggles, one-shot buffs, burn/damage/heal/summon effects; needs a buff on the caster lasting hours or permanently, with a recognised combat effect (≥ 0.5), not already active. |
| `Camellia: command wait timeout (4.0s without progress)` with `CanAct=False` and no command | 795 | Nothing to wait for: she could not act. | Ends the turn after 1 s: `cannot act this turn`. |
| `Treat Affliction->...(utility)=1.0` on every unit | 18, 802, 1021, 2320 ... | 0.3.4's "nothing to remove" check fell through to the generic utility value, which is above the main-action threshold. | Removal/dispel abilities with nothing to remove are worth 0. |

Working as intended in this log: Spirit Weapon Enchantment cast at round 1 (line 1400), Camellia healing herself at
26-37 % HP (potion, then spell), Lann's Perfect Strike before his full attack, Albedo switching target after an
interrupted approach, Orvath's Soul Power listed only with corpses around (`Soul Power->Orvath(burn)=5.8`, line 3815)
and not chosen over Grasp Heart.

## 11. v0.3.6: fixes from the 0.3.5 log

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| `Out-of-combat permanent buff (Perfect Strike)` after every fight | 5878, 8747, 11091, 13386, 15899 | Its buff is permanent and removes itself after one reroll (`ModifyD20.DispellOnRerollFinished`); only `RemoveBuffOnAttack` counted as "spent by use". | Both count as one-shot: no out-of-combat casting, valued as ~2 rounds in combat. |
| Camellia's `Cure Light Wounds` → `result=Fail` while engaged | 12424-12456 | A standard-action spell in melee needs a defensive-casting check; failing provokes an attack of opportunity and can lose the spell (`UnitUseAbility.OnTick`, `AbilityData.ProvokeAttackOfOpportunity`). | 5-foot step first when the best action is such a spell (not aimed at an adjacent enemy); otherwise the spell's value × 0.85 while engaged. Potions and wands do not provoke. |
| 13,559 `IsReadyForNextAction` debug lines | throughout | Logged every frame while a command runs (debug logging on). | Logged only when its result changes. |

## 12. v0.3.7: Progressive spell policy and area spells

- **Progressive** (per character, off by default): after all options are valued and filtered, offensive spellbook
  spells above the lowest *usable* slot level are removed from the choice. Usable = worth at least the engine's
  minimum and at least a 25 % chance to land (spell resistance × failed save × hit). Slot level comes from the game
  (`Spellbook.GetSpellLevel`, metamagic included; level 0 = free and never gated). Each spellbook is its own ladder.
  A cast that would hit an enemy the game marks as a boss (`Experience.Encounter == Boss`) is not held back.
  Same rule in real time. Full design and evidence: [PROGRESSIVE_SPELLS.md](PROGRESSIVE_SPELLS.md).
- **Area spells**: the effects of spawned areas (`AbilityAreaEffectRunAction`, `AbilityAreaEffectBuff`) are now read,
  so Grease, Web, Stinking Cloud and similar spells are valued as control/damage with their real radius. A control or
  debuff area that would also catch allies is dropped when *Avoid friendly fire* is on (as damage areas already were),
  otherwise each caught ally is subtracted. Only allies the effect would actually reach count (per-target conditions,
  CC immunity, already affected); Selective metamagic spares allies. An area's radius is the ability's own AoE radius,
  else the area size for a cylinder (half for a wall). Area damage counts once per round (not on enter and every round,
  never on movement); buffs an area applies last while the area does.
- With Progressive on, each offensive spell also offers its best target with a fair (≥ 25 %) chance and its best
  boss-hitting target, since the tier gate judges those. Spells with different metamagic are separate options.

## 13. v0.3.8: fixes from the 0.3.7 log

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| Camellia recast `Spirit Weapon Enchantment(enchant)` at the start of four fights | 1291, 1495, 1833, 2169 | The buff check saw it active (0), then the once-per-combat fallback for unverifiable effects cast it anyway. | `AbilityProfile` reads `ContextActionEnchantWornItem` (enchantment + slot); `EnchantmentActive` = enchantment on the item, or (unknown enchant actions) every carrier buff present → 0, no fallback. |
| Seelah: `Smite Evil->X` then attacked Y | 283→291, 1314→1320, 1511→1517, 2210→2216 | The swift mark is decided before the attack, aimed at the most threatening evil enemy; the attack picked its own target. | `MarksForOwnAttacks` (buff with `AttackBonusAgainstTarget`/`DamageBonusAgainstTarget`, caster only); `CoupleOwnAttackMarks` keeps such marks only on the best main action's enemy (re-aimed there) and drops them when it attacks no one. |
| Orvath: `Sleep->Cultist Champion` twice in one turn, same value | 2237, 2245 | `AbilityTargetsAround.m_Condition` (who the area can affect) was never read. | Evaluated per unit like `AbilityTargetsAround.Select`; `ContextConditionHitDice` (fixed limit) is now a safe condition. Excluded units are logged once: `[Profile] <spell>: <unit> is outside its target filter`. |
| Albedo `Move+Attack` in round 0: moved 8.2 m, no attack | 739-746 | In a surprise round a move uses the standard action's budget (`ActionsState.UpdateMoveDistance`). | `SurpriseRound` (`unit.IsSurprising()`): options needing move + standard are not affordable. |
| Seelah: `Advance` rejected, "no movement left" | 541, 1603 | After a 5-foot step the game allows no other movement; the engine used a different measure than the dispatcher. | `NoMovementLeft` = `GetRemainingMovementRange(unit, true, false) <= 0` (same as the dispatcher): no movement options. |
| Progressive on, but no line said so | - | Lines were written only when a tier was held back or escalated. | Once per combat and caster: `[Progressive] <unit> (TB): policy on - gated: ...; left alone: ...`. |
| `Coup de Grace(utility)=1.0` vs a sleeping enemy | 1924 | No recognised effect: the blueprint is game data, not code. | Abilities that fall to the utility fallback with no detectable effect log their components/actions once (`no recognised effect - components=[...]`), so Coup de Grace can be valued from evidence next. |

## 14. v0.3.9: fixes from the 0.3.8 log

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| Lann: single `Move+Attack` after moving 0.1-0.7 m with his full attack available | 693, 2163, 2361, 2431, 2755 | "5-foot step + full attack" was offered for melee weapons only. | Offered for ranged weapons too (`FullAttack(n)->X+5ft`, ×0.95). The dispatcher refuses it when the path is longer than the step and the next best option is tried. |
| Lann at 25 % HP: full attack 7.6 over his potion 5.9; down at the end of the boss fight, Camellia too | 3119, 3261 | Heal value = HP restored × urgency; nothing about who can hit the wounded unit. Downed allies were in no list. | `IncomingDamage` (threat of each enemy that can reach the ally this round, shared among the allies it can reach) and P(drop) = exp(-HP/danger); the heal adds the drop in P(drop) × `AllyWorth` ((4 + 3·level) × up to 3 rounds). 25 % counts as "below 25 %". `DownedAllies` (unconscious, not dead) are heal targets: a heal that brings one above 0 HP is worth `AllyWorth`. |
| `Coup de Grace(utility)=1.0` vs a sleeping wizard; Seelah attacked it for 9.6 | 677 | Its effect is Tabletop Tweaks' `CoupDeGraceTTT` component (no actions; 0.3.8 dump, line 675). | Identified by the vanilla GUID (kept by TTT) and valued by the rules: automatic critical hit with the wielded weapon, then Fort DC 10 + damage or die (`CombatMath.EstimateCoupDeGrace`); minus attacks of opportunity from other enemies. |
| Orvath stunned (Grasp Heart) the Mite Scout he had just put to sleep; allies hit sleeping enemies | 2591, 677, 2217 | Control on an already disabled enemy was valued in full; damage on a sleeper ignored that it wakes it. | Control on a disabled enemy ×0.1; non-lethal damage on a sleeping enemy ×0.5 (`WakeFactor`). |
| ~20 `[Profile] Soul Power: X is outside its target filter` lines | 72-76, ... | Logged per unit. | Once per ability. |

## 15. v0.3.10: fixes from the 0.3.9 log

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| Albedo: `Advance->Dretch` issued 15 times, 0 m each, until the decision safety limit | 4700-4812 | His Move+Attack had been taken as a 5-foot step; afterwards the game forbids other movement (`TurnController.ShouldRestrictNormalMovement`), but `GetRemainingMovementRange` only says so outside five-foot-step mode. The interrupted move counted as "acted", so the no-repeat rule missed it. | Engine: `StepTaken` (`GetMetersMovedByFiveFootStep > 0`) caps movement at the step's remainder and drops movement options. Dispatcher: refuses paths longer than that remainder. TBController: a move that went < 0.1 m failed for this turn. |
| Lann: stepped out of melee, then three commands force-finished without acting | 3892-3932 | Same rule; and the step-out point did not keep any enemy in range and sight. | As above; a ranged unit's step-out point must keep an enemy in range with line of sight (`UnitCommand.IsUnitCloseEnough` from the point). |
| Seelah, Albedo, Camellia: short `Move+Attack` "force-finished before it started" | 4667, 4702, 5110, 5258 | The path's end was out of reach (trimmed because an ally stood at its end); native AI checks the end with `IsUnitCloseEnough` (`AiAction.ScorePath`), the dispatcher did not. | Attacks and abilities whose path ends out of reach are refused; the next best option is tried. |
| Lann at 20 % HP (no potion left) shot the Large Water Elemental and went down; Seelah at 12-15 % attacked first | 6609, 3380, 6642 | "Withdraw" needed an engaging enemy and had a fixed value. | Offered below 50 % HP whenever an enemy can reach the unit; value = drop in P(drop) between here and the destination (incoming damage there + attacks of opportunity for leaving) × `AllyWorth`. |

## 16. v0.3.11: fixes from the 0.3.10 log

| What the log showed | Line(s) | Cause | Fix |
|---|---|---|---|
| Camellia at 37 % HP cast Cure Light Wounds on Orvath's summoned Dretch (14.7) instead of herself; the Dretch left combat at once, Camellia went down soon after | 21580 | `DownedAllies` took any player-faction unit at 0 HP; summons die there, only companions and pets go unconscious. Summons were also valued like companions (summoned Cambion 11.6 at line 21430). | Downed targets: `Player.PartyAndPets` only, never `UnitPartSummonedMonster` units. `AllyWorth` of a summon ×0.5. |
| Albedo, three rounds: attack on the Skeletal Champion Archer refused ("path ends out of reach"), then a 1.6 m advance towards it that used his 5-foot step | 16044-16268 | The advance went to the same enemy the pathfinder could not reach. | A reach refusal marks the enemy `Unreachable` for the turn (`TurnMemory`); advances skip such enemies. |

## 17. v0.3.12: verified defects from the Progressive expansion audit

| What was wrong | Evidence | Fix |
|---|---|---|
| Orvath cast Entangle on himself as `(buff)=9.4` | 0.3.10 log line 18345; intent came only from `EffectOnEnemy`/`EffectOnAlly`, and an unrecognised buff counts 0.5 | `AbilityProfile.AppliesHarmfulBuffs`: a target buff flagged `Harmful` or imposing a controlling condition (`AddCondition`, and now `BuffStatusCondition`) makes the ability harmful (`HarmfulEffects`) unless it is flagged helpful to allies or personal. Used for target sides, the buff branch and the out-of-combat buff routine. `BuffStatusCondition` also counts for control strength. |
| CC immunity almost never detected | `HasCCImmunity` switched on single `[Flags]` values; spells carry e.g. `Sleep \| MindAffecting` | Each flag is checked; immune = immune to every condition the set maps to (Daze and Petrified added). |
| Real time: "Avoid friendly fire" had no effect | the area penalty went into `BaseScore`, which `HybridFinalScore` ignores; the setting was not read | `UnifiedDecisionEngine.VetoFriendlyFire` with the turn-based rule (`AbilityProfile.AllyCaught`) |
| Real time: queued units skipped the readiness checks | `ProcessPendingUnitsQueue` called `ExecuteAIDecision` directly | queued units go through `ProcessUnit` (and `ShouldControlUnit`) |
| Real time: one classification per blueprint | cache key = GUID; spell level, resource type and swift flag copied to wands, other casters, metamagic copies | key = blueprint + caster + spellbook + item + metamagic (bounded) |
| Real time: decision interval setting unused | constant 0.5 s | the setting (0.2-2 s, default 0.5) |
| Silent analysis failures | profile errors, per-ability evaluation errors and unreadable spell lists were debug-only or swallowed | logged once per ability / book in the normal log |
| Unreadable settings.json replaced by defaults on the next save | `ModSettings.Load` | the file is kept as `settings.json.unreadable-<time>` first |
| `UnitExit` area actions | not walked, undocumented | documented: exit lists usually undo the enter effect and would look like a cleanse |

## 18. v0.3.13: fixes from the 0.3.12 log

| What the log showed / was asked | Evidence | Fix |
|---|---|---|
| Seelah left enemies she was fighting: `Move+Attack->Spider Swarm` at 23 % HP over `Attack->Monitor Lizard`, `Move+Attack->Fiendish Spitting Giant Centipede` over `Attack->Small Earth Elemental`, `Charge->Young Giant Fly` over the centipede in reach | lines 2139, 4708, 3894; both moves stopped without an attack and the turn was lost (move-attacks otherwise land ~97 % of the time across five logs) | `DisengageRisk`: for the end point of the move, every engaging enemy whose reach it leaves (`IsReach`) and that would attack on disengage (`ShouldAttackOnDisengage` / `AttackOfOpportunity` with `simulate: true`, as the game's own `EngagementSimulator`) provokes. Value × (1 − chance one hits) − ((P(drop) after the attacks' damage − P(drop) now) × `AllyWorth` + 0.3 × damage). Applied to Move+Attack (melee and ranged), Charge, move-then-cast and Advance; skipped for moves that fit in the 5-foot step. Labels show `[AoO n]`. |
| Orvath: `Entangle->Young Giant Centipede` twice in one turn (quickened, then standard) | lines 824-837, 1122-1132 | `AbilityProfile.InOwnSideArea`: an enemy inside a still-active copy of the same area put down by our side gains nothing from another one (active areas from `Game.State.AreaEffects`). |
| Auto Selective / auto metamagic | IsekaiMod's auto metamagics are toggles with the game's `AutoMetamagic`; `AbilityData.HasMetamagic` includes them (`UnitPartSpecialMetamagic`) while the toggle is on | Selective (friendly fire), Reach (`GetApproachDistance`) and Quicken (`RuntimeActionType`) were already honoured through the game. Now also Empower (×1.5 dice + bonus) and Maximize (highest roll) in damage and heal estimates, the game's own formula (`RuleCalculateDamage`, `RuleHealDamage`). Extend and Bolster: not modelled (effect duration is capped by expected fight length; Bolster's spread is not estimated). |
| `[BuffStackingAnalyzer] ExtractFromAddStatBonus error: Specified cast is not valid` | line 868 (Shield of Faith) | `AddContextStatBonus` stores a `ContextValue`: its descriptor and stat are kept (amount = simple value, else 1), so its stacking with existing deflection bonuses is judged. |
| Animal companions never used belt items | `AbilityData.CanBeCastByCaster` refuses item abilities to `PetType.AnimalCompanion` in combat (game rule) | Optional (`PetsUseItems`, default on): a transpiler replaces that single `UnitPartPet.Type` read (verified: one `callvirt get_Type` in the real assembly); all other item rules still apply. |


## 19. v0.3.14: real time uses the tactical engine

The 0.3.14 test showed the real-time engine (`Core/DecisionEngine/UnifiedDecisionEngine` + `Scoring/`) could not be tuned
into shape: an option's score was the geometric mean of a dozen 0..1 factors, so an action with no effect but no
downside won. Every real-time action of Lann, Aeiluvetar and Orvath was "Aid Another" (melee range), the ranged
characters walked into melee, and no weapon attack was chosen in a whole session.

Real time is now decided by the same engine as turn-based (`Tactics/RealTimeTactics.Act`, called by
`RealTimeController.ProcessUnit` once the unit's command slots are empty):

| Turn-based concept | Real time |
|---|---|
| Actions left this turn | The game's real-time action cooldowns (`HasStandardAction` etc. read `UnitCombatState.Cooldown`) |
| Movement left | Unlimited walking (`UnitMoveTo` ignores cooldowns; attacks walk to their target); horizon = speed × 6 s |
| Round (`CombatMemory.Round`, per-round memory) | 6 s of game time since the combat's first decision |
| 5-foot step out of melee | "Back off out of melee": a 4 m move, priced with the attacks of opportunity it provokes |
| Full or single attack flag | Not set: the game makes a full attack whenever movement allows |
| END TURN | "wait": decided again after the decision interval (logged only in verbose mode) |

Learning from a command that did nothing and the attempt loop are shared (`Tactics/TacticalDriver`), unchanged for
turn-based. The old engine remains selectable ("Real time: use the old decision engine") as a fallback until the new
path is proven in play.

## 20. v0.3.14: statuses (investigation and plan: `docs/STATUS_AWARE_PLAN.md`)

The action economy already comes from the game (`HasStandardAction` etc. include Nauseated, Staggered and the surprise
round). What was missing were the game's rules on whether a command *starts*, and two gates that stopped units the game
still lets act:

| Status | Game rule | Mod (0.3.14) |
|---|---|---|
| Lying down, may stand up (TB) | `UnitProneController` stands a directly controlled unit up only while it has a command; `CanAct` reads false meanwhile | `TurnBasedController.StandUp` gives a short walk order (`TacticalEngine.IssueStandUp`), the game stands the unit up and cancels it, the next decision sees the real economy. Before, the turn ended as "cannot act" every round |
| Nauseated | Only `UnitMoveTo` starts (`UnitCommandController.ShouldStartCommand`) | Options other than plain moves are not offered (`ActionRules`); real time no longer stops a nauseated unit before the AoE escape and drops its commands that cannot start |
| Staggered (TB) | One move or one standard action (`IsMoveActionRestricted`) | No "walk there and act" options |
| CantMove / MovementBan | `CanMove` false | No option that has to walk |
| Standing in a harmful area | - | "Leave <area>": a move out, valued like entering it (danger × ½ × the unit's worth) less its attacks of opportunity; a melee character keeps an enemy in reach if it can |

`Tactics/ActionRules.cs` is the pure rule (unit-tested), `Tactics/UnitStatus.cs` reads the game; decision lines show the
unit's conditions (`{nauseated | nauseated: move only}`) and every "cannot act" line names them.

## 21. v0.3.14-test5: risk on the way, areas, damage actually taken

From the test4 logs (turn-based 67e90edc, real time 87aa4bc7; plan: `docs/archive/PLAN_0.3.14_test4_TB.md`).

| Change | Both modes unless noted |
|---|---|
| **Attacks of opportunity along the path** (`PathAttackers`): every enemy whose reach the straight walk enters and then leaves attacks, the rule of the game's own `EngagementSimulator`. Only current engagers at the destination were checked: Camellia charged past the Large Water Elemental "safely" and was dropped. | both |
| **Real time: a lone engager always attacks** (`AttacksOnLeaving`): the game's preview (`ShouldAttackOnDisengage(simulate)`) skips "engaged with only this unit / its last target", which its real check applies. Lann and Aeiluvetar backed off "safely" and were dropped. Turn-based unchanged (its preview already answers yes). | RT |
| **Drop chance** (`Tactics/RiskMath.cs`, unit-tested): an attack's chance to take the unit out in one blow (damage spread, crits), priced as the unit's worth, not only the average damage. Used for AoOs, standing up and revives. | both |
| **Exposure below half HP**: walking where more enemies can hit it raises the chance to drop before the next turn (withdraw model in reverse). | both |
| **Areas**: live areas only (`IsLive`: not ended, view loaded), the game's shape test with the unit's body (`Covers`), one exit finder (`AoeDangerAnalyzer.FindExit`) for "Leave" and the real-time emergency escape, which is not re-issued while the unit walks. An area's danger is weighted by the chance it affects the unit (descriptor/control immunity, its save DC, time left). "Leave" counts the chance a hit stops the walk. | both |
| **Cannot act but can walk** (nausea's animation): decided with movement only after a second of waiting (`UnitEntityData.IsAbleToAct`); no Advance while only moving. | TB (no Advance: both) |
| **Standing up** (`ShouldStandUp`): stays down only when standing (AoOs, chance to be dropped, best action after standing) is worse than lying down (+4 to be hit). **Revives** are worth the chance the ally stays up (`ReviveStaysUp`). | stand-up TB; revives both |
| **Damage estimates**: `AddEnergyDamageImmunity` counted (the game's character sheet reads it; only `AddEnergyImmunity` was), effects in opposite branches of one condition counted once (`AbilityProfile.Exclusive`; Fire Blast was counted twice), and what each enemy actually took per damage type (`Tactics/DamageObserver`, `RuleDealDamage`) overrides the model. | both |
| **Weapon options per enemy**: no attack listed against one enemy no longer drops all weapon options; a turn with no option at all says why. | both |
| **Real time: replan after heavy damage**: an engine command that has not started is dropped when the unit lost a quarter of its HP since and is below half. | RT |

## 22. v0.3.15 (test1): spells that did nothing, potions, control duration, saves, tanks

From the 0.3.14 turn-based log (e62751a4 + combat log 5ee51eba + GameLog c544dcb3) and real-time log 9c5c6df2.

| Change | Both modes unless noted |
|---|---|
| **Abilities only the game uses are left out** (`AbilityDiscovery`): the action bar's own rules (hidden facts, variants whose `IsVisible()` is false) and the kinetic blade internals (`WeaponKineticBlade.Blast` / `ActivationAbility`, `AbilityKineticBlade`). Orvath's blade carries its own "Fire Blast" (the damage the blade deals on a hit: no attack roll, so valued 9.1 against the real blast's 8.7); cast directly it does nothing. 6 of 6 Fire Blasts in the last fight were that one. What is left out is logged once per unit. | both |
| **Did the game do anything?** (`Tactics/CastOutcome`): every damage, heal or debuff/control ability the engine issues is followed until the game's execution of it ends; touch spells until the touch is delivered. Any attack roll, damage, heal, save, SR check, buff by the caster, or a cast refused with a reason (spell failure, UMD) counts. A silent refusal (`RuleCastSpell` returns before casting, e.g. "cannot target") or no event at all is logged `[Cast] ... did nothing`; after two on the same target in a fight the ability is not chosen there again. | both |
| **Held touch charges**: a new cast of the touch spell the unit already holds (another potion, scroll or slot) is not chosen while the held charge can reach that target (it is free; the new cast would replace it). A charge the game cannot deliver to its holder (a potion's charge after the potion healed nothing) is removed the way the game's own cleanup does (`RemoveUnitPartTouchOnAllCompanions`), so the next potion starts fresh. Seelah drank eight potions in a row, each leaving a charge. | both |
| **Diagnostics for the potion problem**: on such a failure the log names the game's own `CantTargetReason`, the charge's source, age, and whether its fact is on the unit; charges carried into a fight are listed at its start. The root cause (the manual Cure Light Wounds failed too) is not known yet. | both |
| **Control duration per target** (`AbilityProfile.EffectRoundsOn`): each buff application keeps its branch and expected duration. Color Spray on a 5+ HD enemy stuns one round, not the three the value assumed. | both |
| **Real save bonuses** (`Tactics/SaveObserver`, `RuleSavingThrow`): contextual bonuses the game applied (against mind-affecting, poison, a mod's bonus) and automatic passes, kept as the difference to the plain save stat, per target and ability, plus per kind of creature. | both |
| **Tanks accept more risk**: pay 60 % of the damage and exposure a move costs (being dropped outright stays at full price) and turn careful below a third of their HP instead of half. | both |
| Dead setting `UseBuffsBeforeAttack` removed (old settings files still load). | - |

### 0.3.15-test2 (turn-based log 25a1e132)

| Change | Modes |
|---|---|
| **Touch spells walk first**: a touch spell (scroll, wand or slot) on a target out of reach is cast where the caster stands and the game queues the touch as a walk without a path, which turn-based movement cancels. Both of Camellia's Inflict Light Wounds scrolls (5.6 m away, and a 5-foot step away) were cast, never touched, and the charge was lost. The engine now walks into reach itself (our path), then casts. | TB |
| **A held charge is worth keeping**: another spell cast drops it (`TouchSpellsController`), so such options pay the charge's current best value (`[drops held touch]`). A charge held for someone else still undelivered after two rounds counts as a silent failure. | both |
| **Areas cost per round of staying**: entering a harmful area cost its danger × half a fight's worth × one round's chance, so melee fighters charged into Grease to reach enemies lying in it (34 moves "into Grease" in one log) and fell too. Now: danger (already × the chance to fail that round's save) × the unit's per-round worth × the rounds it stays (until the fight or the area ends, up to 3), doubled when it stops inside (standing up provokes and enemies hit a prone unit at +4). Passing through costs one round. | both |

## 23. v0.3.16 (test1): fixes from the 0.3.15 turn-based log (648247bb)

| Change | Modes |
|---|---|
| **No approach loop for touch spells**: the walk into reach is issued only when it moves the caster at least 0.3 m (to two thirds of touch reach). Standing within reach on the ground while the game's range test still said "out of range" (most likely a height difference), Camellia issued a 0.0 m "Approach to touch" about 40 times until the decision limit ended her turn: three turns lost in one fight. Otherwise the game's own cast is used. | TB |
| **A walk into reach counts as a move**: it was recorded as the spell, so after a successful approach (5.5 m, 2.7 m, 0.7 m) the spell was marked "force-finished before it started" and dropped for the turn - the scroll was never cast. | TB |
| **No movement options after a 5-foot step**: Move+Attack and spells that need a walk are not offered once the step is taken (the game forbids it; the dispatcher refused Orvath's Move+Attack and Fire Blast). | TB |

### 0.3.16-test2 (turn-based log 4de5be87)

Touch spells now walk and cast as intended (Camellia: approach 3.7 m, 2.0 m, 3.4 m, each followed by the Inflict
Moderate Wounds cast; no "did nothing" lines).

| Change | Modes |
|---|---|
| ~~Touch target within reach on the ground counts as in reach~~ - replaced in test3, see below. | TB |
| **"Nothing worth doing" names the best options** and why they were not taken (value, not affordable, failed this turn, did nothing before). Seelah ended a round-1 turn with all actions and four enemies, and the log could not say why. | TB |

### 0.3.16-test3: the game's range rule (from the real `Owlcat.Runtime.Core.dll`)

`GeometryUtils.MechanicsDistance`, the distance behind every range and reach test (`UnitCommand.IsUnitCloseEnough`):
the flat (x/z) distance when the height difference is at most 1.5 m, otherwise the larger of the flat distance and half
the height difference. Touch reach is the weapon reach plus both bodies (`AbilityData.GetApproachDistance`, ~1.6 m or
more), so a target within reach on the ground but out of reach by this rule is over ~3 m above or below: a touch cannot
reach it from that level, and test2's "count it as in reach" would have spent the scroll on a touch that never lands.

| Change | Modes |
|---|---|
| **Touch targets out of reach by height alone are not offered** (within 90 % of reach on the ground, out of reach by the game's rule). | both |
| **The walk into reach goes to the target** with the touch reach (×0.8) as its arrival radius, so the dispatcher's path (stairs, obstacles) ends where the game's own test says the touch can be made, instead of a point on a straight line. | TB |

### 0.3.16-test4: checked against the real pathfinding library (`AstarPathfindingProject.dll`)

| Change | Modes |
|---|---|
| **Paths no longer end on an ally's square**: `CommandDispatcher.TrimAllyBlockedEnd` looked the end point up with `AstarPath.GetNearest(point, NNConstraint.Default)`, which returns the nearest node that *suits* the constraint - and `NNConstraint.Suitable` rejects ally-blocked nodes - so the end was never found blocked and the trim never ran. Now the cell itself (`BattlefieldGrid.CellAt`, as the game's AI reads it: `gridGraph.GetNearest(position).node`). | TB |
| **Walkability checks work**: `BattlefieldGrid.IsWalkable` had the same flaw (the default constraint swaps in the nearest walkable node), so every point passed. Used by the real-time emergency area escape and the old real-time engine. | RT |
| Path pooling (`Claim`/`Release`), `BlockUntilCalculated` and `ABPath.FakePath` were checked against the library: the dispatcher's use is correct. | - |

### 0.3.16 release: casting among enemies (log 0d742ea2)

| Change | Modes |
|---|---|
| **A provoking cast is priced by the game's rule** (`CastProvokeRisk`): the defensive-casting check (d20 + concentration vs 15 + 2 x spell level, `RuleCheckCastingDefensively`) and, on a failure, an attack of opportunity from every enemy that would engage the caster where it casts (`ProvokeAttackOfOpportunityAfterDefensivelyFail`), priced like any attack of opportunity (chance to be dropped, damage; the spell is lost when the caster drops, or half the time when hit). Replaces a flat 15 % that ignored how many enemies were there and how hard they hit. Camellia walked 3.4 m in among three Corrupted Mongrels to read Inflict Moderate Wounds; all three struck and she went down before the spell went off. Tagged `[casting provokes N]`. Potions never provoke. | both |
| **Walking up to cast prices standing among the enemies afterwards** (the raised chance to be dropped before the next turn) at any HP; weapon moves keep the half-HP rule. | both |

## 24. v0.3.17 (test1): diagnostics

- **"Nothing worth doing" also on movement-only turns**, and with why "Leave area" was not offered: not inside a
  harmful area (the condition outlasts it), no movement left, no way out within reach, or the walk out costs more than
  staying (attacks of opportunity, chance to be stopped). Seelah stayed nauseated next to the Corrupted Mongrels for
  three turns (log 0d742ea2) and the log could not say which. Logging only, no behaviour change.

### 0.3.17-test2: phase 0 of the tuning plan (docs/AI_TUNING_AUDIT.md) - diagnostics only

No decision changes; every item below only logs, or skips building a log line nobody reads.

- **`[Perf]`** at the end of each fight, per combat mode: number of engine decisions, median, 95th percentile and
  slowest time (with the unit and its choice), total (`Tactics/FightStats`).
- **`[Calib]`** at the end of each fight: how many rounds it lasted against the expected rounds at its start, the
  party's real damage per round against the `4 × levels` the estimate assumes, and the damage the enemies dealt per
  round against their `Threat` at the start (findings B2 and N5).
- **`[Save]`** for every saving throw against a party member's ability: roll, bonus, DC, result and the chance it had
  (passes on N+). Tells bad luck from a wrong estimate (Orvath's repeated Sleep casts, log 06aebf2d).
- **`[Cast] ... did nothing to its target`**: an ability aimed at an enemy (not a lasting area or a summon) after which
  no rule touched that enemy (attack roll, save, spell resistance, damage, buff), with the units it did reach.
- **`[Cast] ... Use Magic Device failed`**: the roll and DC of a failed item check. The game keeps the item's charge and
  the combat log shows only a skill roll, so the wand seemed to do nothing (Orvath's wand of Cure Light Wounds).
- **Decision line**: the chosen option's value, and its rank when it is not among the five listed.
- **"Nothing worth doing"** names marks that help only the unit's own attacks (Smite Evil) as held back because no
  attack follows this turn.
- **Noise**: each `BLACKLISTED` reason once per fight (it was logged on every decision); verbose messages on hot paths
  (spell resistance, damage reduction, resistance analysis, area danger) are no longer built when verbose logging is off.

## 25. v0.3.17 (test3): phase 1 of the tuning and active-ability plans

These come from `docs/AI_TUNING_AUDIT.md` (§8, §11–12) and `docs/ACTIVE_ABILITY_PLAN.md` (AA-0). Turn-based and real
time share all of them, except where marked.

### Approaching around harmful areas

- **Choosing the spot (`PlanApproach`).** It applies to Move+Attack, move-then-cast and Advance.
  - When the straight approach to a target enters a harmful area, the engine samples two rings around the target
    within reach: 85 % and 60 % of the reach, 16 directions each.
  - It keeps the spots that are walkable, unoccupied, within move range and reached without entering an area, and
    takes the cheapest by `MoveRisk`.
  - Advance also tries spots just outside reach (reach + 1.5 m / + 3 m). The unit then waits at the edge for enemies
    coming out.
  - With no such spot, the straight approach keeps its honest price. The label says `(around Grease)`.
- **Walking there first.** The unit walks to that exact spot (`Candidate.MoveFirst`, arrival within the reach left)
  and acts on its next decision, the same way the turn-based touch approach works. Valuation and execution use the same
  destination. Seelah stood idle for 11 turns while her enemies lay in Grease (log 06aebf2d).
- **Checking the real path (turn-based).** A move priced as entering no area (`Candidate.CheckPath`) is refused by the
  dispatcher when the game's computed path enters one, up to the point where the unit arrives
  (`CommandDispatcher.PathEntersArea`). Camellia walked into Grease along a bending path to touch a summon. In real time
  the game paths by itself, so there is no check.

### Conditions and the safety net

- **Hit Dice with a shared value (Sleep).** `ContextConditionHitDice` with `AddSharedValue` is evaluated with the
  shared value's range from the ability's own `ContextCalculateSharedValue`: dice count and bonus from the context, no
  rolling. If the lowest and highest give the same answer, that is the answer; otherwise the average decides. It was
  counted for every target before.
- **Per-target "reached nothing".** An ability aimed at an enemy is watched. If no rule touched that enemy (attack
  roll, save, spell resistance, damage, buff), that counts against that enemy; twice, and the ability is not aimed at
  it again this fight. Exceptions:
  - lasting areas, summons and Charge, which make their attack after the ability ends;
  - abilities with their own logic;
  - abilities with no effect on their target.
- **Diagnostics fix.** Charge no longer shows as "did nothing to its target" (that was a false alarm in test2).

### Values

- **Heal.** A summon's restoring part is halved, like its worth. At full HP, Camellia healed a Dretch instead of using
  Daze.
- **Spell-slot discount in easy fights.** It applies to the damage and control parts only, as the Spell Use setting
  says.
- **Caster stepping out of melee.**
  - The step keeps the spell's target in range.
  - It is worth the casting risk it avoids (now the whole loss in `CastRiskCost`) minus what the cast still risks at
    the new spot.
  - A caster with good Concentration casts defensively where it stands.

### Correctness

- **Immunity caches** (`CombatRulesAnalyzer`) last one frame. They used to be kept for the whole game session, which
  hid immunities gained later and kept ones that had ended.
- **Mod switched off in the mod manager:** trap avoidance of the player's moves, PreBuff ticking and the pet item rule
  stop.
- **Units the AI does not control** (main character, companions, per-character AI switch):
  - they are left out of the buffs after combat, of PreBuff's casters and of the leave-combat interrupt;
  - PreBuff goes through `CommandDispatcher` and the item settings.
- **Old real-time engine only:** its unit lists and team blackboard are updated only when it is selected, not every
  frame.

### Diagnostics

- `[Perf] slow decision` for any decision over 250 ms, with the number of ability profiles built in it.
- `[Coverage]`, once per character per session (and again when its abilities change):
  - the action-bar abilities by what the engine makes of them: understood (with kinds), not available, blacklisted,
    held back by item settings, or effect not understood (with component types);
  - every toggle with the game's facts: on or started, activation type and action (the game's `GetCommandType`),
    group, combat-end and default flags, resource and buff components.

### Removed

About 3.5k lines of the v0.2 planner, which had no callers: TurnPlanner and role plans, SequenceOptimizer,
ActionExecutor, TurnOrchestrator, and RealTimeController's role-based logic.

### Not done, and why

- **No performance work.** test2 measured a median decision of 2–4.5 ms and a 95th percentile of 9–38 ms.
- **`ValueModel` consolidation (C4)** moves to phase 2, where it is the base for the calibration changes.

### 0.3.17-test4: fixes from the test3 turn-based log (9a7bb1f9)

- **The "reached nothing" check counted effects of the caster's other areas.**
  - Orvath cast Sleep on a Cultist Champion seven rounds running and no save was ever rolled. The Champion stood in
    Orvath's Grease, and the Grease's saves on it counted as Sleep "reaching" it.
  - Now an effect whose context comes from a lasting area this cast did not create does not count
    (`CastOutcome.FromOtherArea`).
  - The log line gives the target's level, so a Hit Dice limit can be confirmed from it.
- **An area spell aimed at an immune enemy for the others around it** ("Sleep->Giant Spider" for the Summoned Wolves
  next to it). More value always wins: a centre on an immune enemy that catches more of the others is the better cast.
  Only on equal value, the same enemies covered, does a centre on an enemy the spell affects win
  (`TacticalEngine.Better`, `Candidate.AimAffected`).
  - test4 used a ×0.97 factor instead. That could prefer a centre catching fewer enemies when the difference was under
    3 %, so test5 replaced it.
  - The per-target safety net treats an area that reached other enemies as a success, so centring on an immune enemy
    on purpose is never blocked. Only an area that reached no one counts against its aim.
- **Walking into an area that impedes movement or applies hard control** (Grease, Entangle, Web, Stinking Cloud's
  nausea) can stop what the walk was for: on a failed save the unit falls, is held or cannot act. This chance now joins
  the attacks-of-opportunity stop chance in `MoveRisk` (`AoeDangerAnalyzer.EnteringDanger`, out `stopChance`).
  - It applies to Move+Attack, Charge and move-then-cast.
  - All four chosen walks into an area in the test3 log were priced without it: Seelah into Grease for the last
    Zombie, and into Entangle and Stinking Cloud; Camellia into Stinking Cloud. Each time the attack was lost to the
    save.
- **Advance around an area to where the unit already stands is not offered.** Seelah made two 0.0 m advances.
- **Coverage report:** Charge and Coup de Grace are listed as understood (valued by the game's rules), not as
  "effect not understood".
- **Facts from the coverage report:**
  - Fighting Defensively activates `OnUnitAction`: its AC applies after the unit attacks. This answers an open
    question in `docs/ACTIVE_ABILITY_PLAN.md` (AA-3).
  - Coup de Grace is TTT's `CoupDeGraceTTT` in this game.

### 0.3.17-test6: why the "reached nothing" check never fired for Sleep (test5 log ab78e9cf)

Orvath again cast Sleep on the Cultist Champion every round from R2 to R8. No save was rolled on it, and no `[Cast]`
line appeared, so the check counted every cast as having reached it. The cause is in the game code:
`AbilityExecutionProcess.DoApplyEffect` triggers `RuleSpellResistanceCheck` for **every** target of a spell before it
applies anything. That includes targets without spell resistance, and targets the effect then skips (Sleep's Hit Dice
limit). That check was counted as an outcome on the target.

- Now only a *resisted* spell counts from that rule.
- A buff counts only when it is one the ability is meant to apply (`AbilityProfile.TargetBuffs`), not a hidden marker.
- When an ability did nothing to its target, the log now also gives the target's level and the ability's Hit Dice
  conditions as the profile evaluates them, for example `level <= 0 + Damage (4..4, average 4)`. That shows whether the
  limit is read correctly.
- The Cultist Champion is an encounter enemy, not spawned by Random Reinforcements. That mod clones existing creature
  blueprints for its spawns and does not change their levels or immunities.

### 0.3.17-test7: a buff keeps its conditions in every branch (test6 log c2b4960c)

The safety net worked in test6: `Sleep did nothing to its target Cultist Champion (level 5) ... (2/2; not aimed at it
again this fight)`, and Orvath moved on. The Hit Dice line added in test6 never printed, though, and that showed why
the AI expected Sleep to work in the first place. `AbilityProfile` stored one set of conditions per target buff. When
the same buff was applied in a second branch, it cleared the conditions and treated the buff as unconditional. Sleep
applies its buff in more than one branch, so its Hit Dice limit was never checked.

- Each branch a buff is applied in is now kept. The buff applies to a target when any of those branches does
  (`BuffAppliesTo`: unconditional or yes in one branch decides; otherwise unknown, else no).
- The Hit Dice note and the `[Profile]` description read all branches (`A & B or C`).
- The same rule fixes any ability whose buff appears in several branches with different conditions, for example
  alignment- or creature-type-limited buffs.

## 26. v0.3.18 (test1): phase 2 of the tuning plan, calibration

The plan is in `docs/AI_TUNING_AUDIT.md` (§4 B2, B4b, B5, B8, B10, C3, C4, N5; §8 phase 2). The shared formulas are now in
`Tactics/ValueModel.cs`, which uses no game types, so they are unit-tested (`tests/CompanionAI.Tests/ValueModelTests.cs`,
46 tests in all). A combatant's worth per round (4 + 3 × level, or CR) stays the scale's convention, so options keep
their order. What changes are the quantities that must match the game: how long the fight lasts, how much damage the
enemies really deal, the chance to kill, and what a provoked attack costs.

### Fight length from the party's real damage (B2)

- Before: `1 + enemy HP / max(10, 4 × party levels)`. In the 28 turn-based fights of the 0.3.17 logs the party dealt
  1.0 / 1.4 / 1.8 damage per level per round (quartiles), not 4, and fights lasted about twice the estimate.
- Now: the prior is 1.5 per level (at least 4). From the second round on it moves towards the damage the party really
  dealt in the completed rounds of this fight: `(prior × 2 + dealt) / (2 + rounds)`. After two rounds what was seen
  counts as much as the prior. Enemies that join mid-fight add their HP as they appear. The 1–6 clamp and the per-term
  caps stay.
- With the prior, the measured fights' real length over the estimate is 1.1 at the median (0.8 / 1.1 / 1.5). It was
  about 1.9.
- Effect: horizons capped at 3 rounds stay at 3 longer as the fight goes on (ally worth, control, area costs), and
  long buffs, enchantments and summons count up to 6 rounds instead of about 4.

### The enemies' real damage (N5)

- Before: an enemy's `Threat` (4 + 3 × CR) was its damage per round in the drop-chance model (heals, withdraw,
  exposure, standing up, revives). In the logs the enemies' Threat at the start was 16–82 per round, and they dealt
  0–13 to the party.
- Now: at the first decision of each round the engine records its prediction, the sum of every ally's incoming damage.
  The rounds after compare it with the damage the enemies really dealt: `(dealt + 2 × prediction per round) /
  (predicted + 2 × prediction per round)`, from 0.4 to 2. That scale multiplies `IncomingDamage`.
- A party that takes more damage than predicted (late-game enemies hit harder than CR suggests) gets more careful. One
  that takes less relaxes, never below 0.4, because a big hitter may still be on its way. Reinforcements are counted
  from the round they appear, and the prior keeps the first rounds from counting too much.
- The worth of killing or controlling an enemy still uses its Threat. That is the scale's convention, the same as an
  ally's worth.

### The chance to kill (B5)

- Before: the kill bonus was the enemy's whole Threat once the expected damage reached its HP, and a quarter of Threat
  × the HP share below. That was a fourfold jump at one point, whatever the chance to kill.
- Now: `Threat × [P(kill) + 0.25 × (1 − P(kill)) × min(1, E / HP)]`. `P(kill)` comes from the chance the damage lands
  (any attack hits, `WeaponAttackEstimate.AnyHitChance`; the effect chance of a spell) and the damage spread of
  `RiskMath.ChanceAtLeast`.

| Expected damage / HP (Threat 10, HP 10) | 0.25 | 0.5 | 0.75 | 0.9 | 1.0 | 1.25 | 1.5 | 2.0 |
|---|---|---|---|---|---|---|---|---|
| 0.3.17 | 3.1 | 6.2 | 9.4 | 11.2 | **20.0** | 20.0 | 20.0 | 20.0 |
| 0.3.18, damage always lands | 3.1 | 6.2 | 10.7 | 14.3 | 16.2 | 17.8 | 18.8 | 20.0 |
| 0.3.18, lands half the time | 3.1 | 8.4 | 12.8 | 14.9 | 16.2 | 16.2 | 16.2 | 16.2 |

A big hit that lands half the time now kills half the time, and is valued that way above and below the HP mark.

### Healing: one curve, one urgency term (B4b)

- Before: each HP restored was worth 0.6 × 1 / 2 / 3 by HP band, × 1.25 when the ally was engaged, plus the
  drop-chance term. Urgency was counted twice. There was a jump from 0 to 1.2 at the threshold, and more at 50 % and
  25 %. Heals were the best-valued option in the logs (median 18–21, weapons 7–8).
- Now: `ValueModel.HealPerHp` is 0 at the threshold, rising over its last 15 %, then 0.6 + 2.4 × depth below it, at
  most 2.4. The danger of dropping is only in the drop-chance term. Downed allies keep the revive model unchanged.

| HP share (threshold 50 %) | 49 % | 45 % | 40 % | 30 % | 25 % | 20 % | 10 % | 0 % |
|---|---|---|---|---|---|---|---|---|
| 0.3.17 | 1.2 | 1.2 | 1.2 | 1.2 | 1.8 | 1.8 | 1.8 | 1.8 |
| 0.3.17, engaged | 1.5 | 1.5 | 1.5 | 1.5 | 2.25 | 2.25 | 2.25 | 2.25 |
| 0.3.18 | 0.09 | 0.56 | 1.08 | 1.56 | 1.8 | 2.04 | 2.4 | 2.4 |

The result is fewer top-ups of mildly hurt allies, and the same or more for badly hurt ones.

### Ranged attack rolls provoke (B10)

- The game provokes for every ranged and ranged-touch attack roll unless the rule's flag says no
  (`UnitCombatEngagementController.OnEventDidTrigger(RuleAttackRoll)`). Point Blank Master sets that flag for its
  weapon category, `IgnoreAttacksOfOpportunityForSpellList` for its spells, and mods may add more.
- `Tactics/ProvokeObserver` reads the flag from the party's real rolls. The answer is kept per attacker and weapon
  category, and per ability. The party also shoots outside melee, so this is known early. Before any roll, Point Blank
  Master in the attacker's facts answers for weapons. A fake roll cannot be used: its fortification branch rolls dice.
- `ProvokeRisk` is now the one pricing for every provoking action, at the place it happens. It covers casting in
  melee, a ranged attack roll, and Coup de Grace. The cost is the one leaving melee already used: dropped now, more
  likely to drop before the next turn, and the HP to heal. Casting left out the middle part before.
- Weapons: single, full and Move+Attack shots in reach of enemies pay it. Only the attacks still to come are lost if
  the unit drops. Before, shooting in melee cost 35 % of the engagers' Threat, flat.
- Rays and kinetic blasts (`AbilityProfile.RangedAttackRoll`) pay it too, cast defensively or not. An enemy makes one
  attack of opportunity a round, so a failed casting check and the roll share it.
- The step out of melee is worth what acting in place would lose by provoking, less what the new spot still risks.
  For archers that replaces 40 % of the engagers' Threat, and an archer with Point Blank Master no longer steps out.
  Casters with rays now step out as well.

### Real time

- **B8:** the pre-engine "Emergency AoE escape" now runs for the old engine only. The tactical engine's "Leave area"
  option already keeps a target in reach, prices attacks of opportunity and handles nausea. A command still waiting to
  start when the unit finds itself in a new harmful area is dropped once (`RealTimeTactics.ShouldReplanInArea`), and
  the engine decides again.
- **C3:** a cancelled stuck command marks the engine's option as failed for the round, and its target as unreachable
  when the target was the problem (`RealTimeTactics.RecordStuck`). The old tracker is written only for the old engine.

### Diagnostics

- `[Calib]` at the end of a fight now also gives the party damage per round used at the end, the predicted enemy
  damage, and the danger scale at the end.
- `[Provoke]`: a party member whose ranged rolls do not provoke, the first time it is seen.
- Decision labels show `[shot provokes N]` and `[ray provokes N]`.

### 0.3.18-test2: fixes from the test1 logs (turn-based 611fe6bf, real time 0f047a15)

**Grease aimed at an immune creature did nothing.**
- Cause: Orvath aimed Grease at an airborne Quasit, which is immune to ground effects. An area spell aimed at a
  creature is applied to that creature first. The game checks its spell resistance and immunities there
  (`AbilityExecutionProcess.DoApplyEffect` → `RuleSpellResistanceCheck`). When it is immune, nothing happens: the area
  is never created, so the enemies around the Quasit were not caught either.
- Bursts that pick each unit in the area (`AbilityTargetsAround`: Sleep, Color Spray) check every unit on its own, so
  aiming at an immune creature costs nothing there. The problem is the spawned areas (Grease, Entangle, Web, Stinking
  Cloud) and anything else applied once to the aimed target.
- Fix: an area ability that can be aimed at the ground is aimed at the spot where the creature stands
  (`AbilityProfile.TargetOn`). That is the same area in the same place, and the game makes no such check.
- An area that can only be aimed at a creature (and does not pick units itself, `AbilityProfile.SelectsTargets`) is
  valued by that creature's chance to let it through: 0 when immune, the spell-penetration chance against spell
  resistance.
- The test5 aim rule stays: with equal value, the area is centred where it catches the most. An immune creature at
  the best centre is now fine, because the area goes on the ground under it.

**Seelah idle for nine turns (R9–R17) next to Grease.**
- Cause: only the best enemy to close on was considered. That enemy lay in Grease, and no spot around the area was
  accepted, because only spots within this turn's movement counted.
- Fix: the three best enemies are now considered, each with its own approach. An advance may also aim at a spot
  around the area beyond this turn's movement. The unit walks towards it on a path checked to stay out of the area,
  and goes on next turn.
- When no spot is found, the label says why: `(no spot around: N in or through an area, N beyond this turn's move,
  N taken, N not walkable)`.

**Checked and fine.**
- `[Calib]`: the party's damage per round used at the end of each fight is within 1–3 of what it dealt (prior 15–23
  against observed 7–20). The enemies dealt 0.1–0.6 of the prediction, so the danger scale ends at its 0.4 floor in 9
  of 10 fights. That floor keeps a margin for enemies that have not attacked yet.
- `[Perf]`: median decision 3–6 ms, 95th percentile 12–42 ms. One 773 ms first decision in real time.
- Real time: commands waiting to start were decided again when the unit found itself in a Stinking Cloud (8 times),
  and units left it when that was worth it (10).
- Shots, rays and casts that provoke were priced and labelled (`[shot provokes]` 20, `[ray provokes]` 18).
- `UnitAttack ... result=Interrupt acted=True` is how the game ends every weapon attack command after its attacks. It
  is in every log since 0.3.15 and is not a failure.

### 0.3.18-test3: fixes from the test2 logs (turn-based 62b9dd8d, real time 2e3b2678)

**Real time: attacks of opportunity on the way were never priced.**
- In real time the game's `UnitCombatState.ShouldAttackOnDisengage` looks up when the enemy engaged the unit. For a
  reach that a planned move only walks into on the way, there is no such entry, and the game throws
  `KeyNotFoundException`. That happened 500–700 times in each real-time log. The exception was caught, and the enemy
  counted as not attacking.
- Turn-based never reaches that line (it answers "attacks" before it).
- Now, for an engagement that would begin on the way, the game's own rule is applied: an engagement younger than 6 s
  attacks, unless the enemy is moving itself, or a Duelist's No Retreat applies.
- The attack-of-opportunity check is also asked as a disengage, so features that let a unit leave without provoking
  (`DisengageWithoutAttackOfOpportunity`) now count in both modes.

**Real time: interrupted commands were counted as casts that did nothing.**
- Several times every party command was interrupted at once (manual move orders): `ENDED ... result=Interrupt
  acted=False`. The safety net counted each one as an ability that "did nothing to its target", and Doom was blocked on
  the Cultist Champion after two of them.
- Now a command that ended before the game started executing the ability is not judged at all (`CastOutcome.Settle`:
  no execution, not acted).
- In real time an area spell that ran and caught no one is logged and not counted: its targets may have walked out
  while it was cast (Sleep on a Neophyte Fighter).

**Turn-based: stepping from one spot by the Grease to another.**
- With a full turn left, Seelah stepped 1 m to another spot by the area, no nearer the enemy than before, and her turn
  ended there. Camellia's spot was where she already stood ("moved nowhere").
- Now a spot out of reach is only taken when it is nearer the enemy than the unit already is.
- Otherwise the unit waits where it stands. The label says `ring(s) no nearer than now`.

**Checked and fine.**
- `[Calib]` in both modes: the party damage per round used at the end is within 1–6 of what was dealt.
- Grease and Entangle were aimed at the ground (no immune aim in these logs).
- Advances went around Grease and Entangle, and paths that walked into Grease were refused and the next option taken.

## 27. v0.3.18 (test4): phase 3 of the tuning plan, consolidation

The plan is in `docs/AI_TUNING_AUDIT.md` §4 (C2, B9, B6, N5, D2) and §8 (phase 3). N5 was done in phase 2 (§26). D2, the
legacy real-time engine, stays until 0.4.0 (your decision).

### One resistance and hit-chance path (C2)

- **Spell immunity for every spell.** `HitChanceCalculator.CalculateSpellResistance` returned "passes" for any spell
  that does not allow spell resistance before it looked at immunity. So a spell without SR (Grease, Entangle, Web)
  never saw a target's spell immunity or descriptor immunity, such as an airborne creature against ground effects.
  It now follows the game's order (`RuleSpellResistanceCheck.IsSpellResisted`):
  1. Primal Magic Essence and `IgnoreSpellResistanceForSpells` let the spell through, immunity included.
  2. Otherwise an immune target (`UnitPartSpellResistance.IsImmune`) stops it, whether or not the spell allows SR.
  3. Only then comes the SR roll, with spell penetration.

  The order is a game-free function, `Tactics/ResistanceRule.PassChance`, unit-tested in
  `tests/CompanionAI.Tests/ResistanceRuleTests.cs`: immunity without SR, bypass over immunity, no roll without SR, and
  penetration (50 tests in all).
- **No second analyser in the engine.** `CombatRulesAnalyzer.AnalyzeResistance` hard-filtered damage and control
  options with its own rules: no spell penetration, immunity only for spells with SR, energy immunity worked out a third
  time. The engine now uses one path:
  - spell resistance and immunity in `CombatMath.EffectChance` (0 when immune);
  - energy immunity, resistance and observed damage in the damage estimate (`AfterTargetReduction`, `DamageObserver`);
  - condition immunity through `AbilityClassifier.HasCCImmunity`;
  - buff immunity through the new `CombatMath.BuffImmune`.

  The analyser stays for the legacy real-time engine.
- **Buff immunity (`CombatMath.BuffImmune`).** The game's `RuleCanApplyBuff` immunities, read from the components the
  way the game decides:
  - `BuffDescriptorImmunity` for the ability's descriptors, unless the caster has the feature that ignores it, or
    Mythic's reduce-resistances;
  - `SpecificBuffImmunity` for that buff, with its alignment and caster-fact conditions.

  The rule is not triggered, because one of its handlers (`BuffSubstitutionOnApply`) applies other buffs. A creature
  immune to a control's buff is not counted as controlled, and an immune ally is not counted as caught. Each unit's
  immunity components are gathered once per frame.
- **Spell attack rolls by the game's rules.** `CombatMath.SpellAttackHitChance` now uses the same rules as weapons:
  - the attack bonus (`RuleCalculateAttackBonus`) with the delivery's own weapon: the ray or touch weapon of
    `AbilityDeliverProjectile` / `AbilityDeliverTouch`, or the bow of an archer's blast;
  - the delivery's attack-stat replacement (kinetic blasts by Dexterity, Intelligence domains);
  - the AC for that attack type (`RuleCalculateAC`).

  Before, it was base attack + Strength or Dexterity against a hand-made touch AC, without Weapon Focus (Ray), buffs or
  flanking. The old estimate is the fallback.
- **One attack-roll test.** A line or cone projectile makes no attack roll in the game, whatever its flag says.
  `AbilityProfile.NeedsAttackRoll` and `RangedAttackRoll` follow that rule.

### Real time: who cannot act (B9)

The decompiled game settles what the plan left open:
- **Dazed, Stunned, prone:** these make `UnitState.IsAble` false, so the game starts nothing. In real time
  `UnitProneController` stands a prone unit up by itself after 3 s, so a mod stand-up order is not needed there.
- **Frightened:** a frightened unit that sees an enemy panics (`UnitFearController`). The game takes it out of direct
  control and runs it away, and the mod already leaves such units alone (`ShouldControlUnit`). Frightened without panic
  (no enemy in sight) may act, but the real-time pre-filter refused it.
- **Cowering:** the condition blocks nothing in the game's code by itself.
- Now the real-time pre-filter refuses a cowering unit only when the game says it cannot act, a panicked unit (as
  before, through control), and no longer a frightened one that is not panicked. Turn-based is unchanged.

### Repeated control (B6): no learning step

- 932 saves were logged against party abilities in the 0.3.17–0.3.18 logs. 276 passed, against 312 expected from the
  game's own chances.
- Enemies pass fewer saves than predicted (Sleep 35 of 173 against 48 expected). The estimate errs on the cautious
  side, so recasting control on a target that keeps failing its save is, if anything, undervalued.
- No per-target save learning is added.
- One note for later: the Fortitude save against Coup de Grace's death effect was passed 7 times in 25 against 3.5
  expected. The sample is too small to act on.

## 28. v0.3.18 (test5): active abilities, step AA-1 (foundations)

The plan is `docs/ACTIVE_ABILITY_PLAN.md` §H, AA-1. Its items 1 (shared-value Hit Dice) and 5 (the per-target "reached
nothing" safety net) shipped in 0.3.17. Nothing here names an ability or a mod. Each change reads the game's own
components and rules, so other mods' content is covered when it uses them.

### Damage, heals and kills the profile could not read (plan F)

- **Previewed damage and heals.** An action that is not the vanilla `ContextActionDealDamage` but implements the game's
  `IDealDamageProvider` is now valued by the game's own preview. That covers `ContextActionDealWeaponDamage`, Tabletop
  Tweaks' `ContextActionDealDamageTTT` (Oracle, Lich, mythic fixes) and Mystical Mayhem's corrupting ray and drain. It
  works as in the attack tooltip (`TacticalCombatAttackTooltipVM`): the action builds its damage rule in the ability's
  context without triggering it, `UnitHelper.CalculateAbilityDamage` (a fake rule) gives its least and most after the
  target's reductions, and the mean of the two is used. The same applies to `IHealDamageProvider` heals
  (`RuleHealDamage`, fake).
- **Kills.** `ContextActionKill` (Isekai's Instakill and Devour Prey, Power Word Kill) is valued as removing the enemy,
  with the chance that it lands. That chance is a failed save plus spell resistance and immunity, not "half on a save",
  through the branch the kill sits in. It replaces the damage value when it is higher.

### Options that were never offered

- **Conversions.** The action bar offers a spell's conversions (`GetConvertedAbilityData`): spontaneous cure and inflict
  from a prepared slot, shadow spells, and Tabletop Tweaks' Quick Channel and Quicken Blessing. Only variants were
  taken before, so a cleric with no Cure prepared could not heal.
  - Now all of a spell's conversions are offered, and an ability's only those the game shows (`AbilityDiscovery`).
  - Slot restores and slot-to-resource conversions are bookkeeping, not actions, and are left out.
  - A conversion is its own option (its key says it spends another spell's slot). Conversions of one spell from slots
    of the same level are one option: the one that can be cast now.
- **Point-only summons.** Summon Monster and Summon Nature's Ally can only be aimed at a point, so they had no target
  side and were never offered. They are now aimed at a spot towards the nearest enemy, a few metres short of it,
  within range and line of sight, on walkable ground outside harmful areas (`SummonPoint`). The label is
  `->a spot near the fight`. A summon counts as used once per fight, whatever it was aimed at.
- **Toggle-buff effects.** Tabletop Tweaks' `AbilityEffectToggleBuff` (Warrior Spirit, Black Blade attunements)
  switches a buff on the caster on or off. It was read as "utility", and a cast could switch the buff off. Now any
  effect component whose type ends in `ToggleBuff` and exposes its buff makes the profile a toggle: the engine casts it
  only while the buff is off and worth having.

### Corrections

- **Coup de Grace provokes only when it does.** Phase 2 priced its attacks of opportunity always. The vanilla ability
  does not provoke; Tabletop Tweaks' version does, through the game's `ContextActionProvokeAttackOfOpportunity` on the
  caster in its own component. `AbilityProfile.CasterProvokes` finds that action in any action list of the
  blueprint's components, and every such ability now pays the provoke (`ProvokeLoss`).
- **Enchant rule.** Only the game's own enchant actions count. Any type name with "Enchant" matched Mystical Mayhem's
  Enchant Item and counted Mage's Disjunction as an enchantment.
- **Fallback order.** The order is now: summon, consumables (never a fallback, as the project rule says), dispel and
  removal, then enchantment.
- **No "utility" value.** An ability with no recognised effect at all got "utility, once per fight". It was never
  chosen in seven logs, and it offered things like Isekai's Planar Coliseum Return in combat. Those abilities now get
  0, and the coverage report lists them as "effect not understood".
- **Blacklist.**
  - A cantrip is now judged by its spell-list level. A level-1 character's class touch power (no spellbook) counted
    as a cantrip through `AbilityData.SpellLevel`'s fallback.
  - A touch spell's damage is read from its sticky delivery (through the profile), so damaging touch powers are no
    longer blacklisted as "touch cantrip with no damage".
  - The "Daze cantrip - low HD limit" rule is removed. It never matched, and the Hit Dice limit is now read per target.
- **More conditions evaluated per target.** The game's read-only checks were added to the safe list. Each was checked
  in the decompiled game for side effects:
  - target and caster HP;
  - flanked, flat-footed, engaged;
  - weapon, shield and two-handed equipment;
  - buffs by school, rank or uniqueness;
  - stat values, arcane or divine caster, blueprint, see-invisible, main target, distance, in combat, gender, main
    character, touch charge, buff immunity.

  Spirit Weapon Enchantment's weapon check was "not evaluated - both branches counted" in every log. Favored Enemy is
  left out, because its check creates a game part on the caster.

### To check in the game

- A cleric or oracle out of Cure slots: `Cure ... ` options from other slots appear and are cast.
- A Summon Monster spell on the action bar: `->a spot near the fight`, and the creature appears near the enemies.
- With Tabletop Tweaks: Coup de Grace shows `[casting provokes N]` next to engaged enemies. Without it, it shows no
  provoke tag.
- The `[Coverage]` "effect not understood" lines: these abilities are no longer offered.

## 29. v0.3.18 (test6): active abilities, step AA-2 (command-activated toggles)

The plan is `docs/ACTIVE_ABILITY_PLAN.md` §H AA-2, with design §E1 (a) and §E3. The code is
`Tactics/TacticalEngine.Toggles.cs` (a part of the engine), with the setting "AI may start performances, rage and
similar abilities" (on by default).

**Which toggles.** Activatable abilities whose activation is a game command (`ActivateWithUnitCommand`): bardic and
Azata performances, the Rage family, shaman spirits such as Camellia's Battle Spirit, and judgments. A toggle is
offered only when it is on the action bar, off, and available by the game's resource and restriction checks.

Left as the player set them, with the reason in the `[Coverage]` toggle line ("AI: leaves it (...)"):
- attack modes and Mobility (AA-3);
- mounting (Saddle Up: a buff with `ShareFeaturesWithPet`);
- kineticist infusions and Gather Power (modifiers of a blast);
- toggles that need a target;
- "accept effect" preferences (they are not command-activated);
- a toggle the game's group rule would not start (`ActivatableAbility.TryStart`: the group's weights against its size).

**Action.** `UnitActivateAbility.GetCommandType`, the game's own rule, including Quicken Performance. A toggle competes
with attacks and spells for that action. A free one (Rage) worth 2 or more goes first, like any quick action.

**Value.** The buff it puts on its recipients, valued like every buff (`BuffEffectAnalyzer`, the stacking rule):
- **Recipients:** the caster for a self buff (Rage); the allies in its area for a performance (the area's
  `AbilityAreaEffectBuff`, radius from the area); the enemies in it for a harmful one (a dirge), at a quarter of each
  enemy's worth per round.
- **Rate:** per round, like a spell of half the character's level.
- **Duration:** the expected rounds, but no more than the resource lasts, at most 3. Engaged recipients count fully,
  the others at 0.6, summons at half.
- **Easy fights:** a resource spent per round is a daily resource, so it is discounted the way spell slots are.
- **What it forbids:** `ForbidSpellCasting` (Rage), unless the caster has the feature that ignores it, costs half the
  best spell (and item, when items are forbidden too) the caster could use instead, for those rounds.

**Issued** the way the action bar does it: `IsOn = true`, and the game queues its `UnitActivateAbility`, or starts it at
once. It does not go through the dispatcher, because there is no path to give it. Logged as
`[Toggle] Camellia: Battle Spirit on - ...`.

**Stopped** by the AI only when it started the toggle itself and nobody is left in its area. A performance with every
ally out of range spends a round of its resource for nothing. Rage and the rest end by the game's own rules
(`DeactivateIfCombatEnded`, the resource running out). Each toggle is switched at most once per round (`CombatMemory`),
so there is no thrashing.

**To check in the game:**
- Camellia's Battle Spirit: `Start Battle Spirit (N in range, 4 left)` competes with her spells in round 1–2, and the
  `[Toggle]` line follows.
- The `[Coverage]` toggle lines now end with "AI: may start it when worth it" or "AI: leaves it (reason)".

## 30. v0.3.18 (test7): active abilities, step AA-3 (attack modes and Mobility)

The plan is `docs/ACTIVE_ABILITY_PLAN.md` §H AA-3, with design §E1 (b)/(c), §E2 and §E3. The code is
`Tactics/TacticalEngine.Modes.cs`, with the setting "AI may switch combat modes" (on by default).

**Which modes.** Toggles that are not started by a command and whose buff only changes attack, damage or AC through the
game's own components:
- `WeaponParametersAttackBonus` / `WeaponParametersDamageBonus` (Power Attack, Deadly Aim, Piranha Strike): their weapon
  filters (ranged, two-handed, finessable, full attack only), the 1 + BAB/4 scaling, the mythic +1 and the two-handed
  Power Attack factor (x1.5, Greater x2, mythic x5/3), as `CalculatePowerAttackScaling` does;
- stat bonuses to the attack bonus or AC (`AddStatBonus`, `AddContextStatBonus`, `AddStatBonusAbilityValue`: Combat
  Expertise, Fighting Defensively). Their values come from the game: a mechanics context for the unit and the buff works
  out ranks and custom properties (Fighting Defensively's +2 / +3 AC and its attack penalty).

A buff with anything else, or a component setting the AI does not model (specific weapon, grip style, dual wielding,
off-hand only, a multiplier fact), is left as the player set it; the `[Coverage]` toggle line gives the reason, or
"AI: may switch it with attacks and moves".

**Decision.** Each weapon option (full attack, single attack, Move+Attack) is valued with every set of modes, one per
group (the game's group rule) and each ungrouped mode on or off, at most 32 sets:
- attack and damage by the game's own attack routine with the modes' changes (`CombatMath.EstimateWeaponAttack` with a
  per-attack delta; the routine's rule results are read once per frame and reused for every set);
- AC by the damage avoided until the unit's next turn where the attack is made from: 5 % of a d20 per point relative to
  the enemies' chance to hit, priced with the drop-chance model like a heal.
- The gain reaches the option through its own weights (wake factor, the chance a move or a shot is stopped).
- **Hysteresis (E3):** the best set is kept only when it beats the current state by 10 % of the option's value (at least
  0.5), and a mode is switched at most once per round.

Execute switches the modes the way the action bar does (`IsOn`) just before the attack, logged as `[Toggle] <unit>:
<mode> on|off for '<option>'`. Fighting Defensively starts on the attack itself (`ActivateOnUnitAction` Attack:
`TryStart` when the attack is about to trigger), so its penalty applies to that attack and its AC afterwards: the
model it is valued with.

**Mobility.** While it is on, an enemy's attack of opportunity on a unit leaving its reach is cancelled when the unit
passes a Mobility check against that enemy's CMD (`UnitCombatState.AttackOfOpportunity`: `RuleCalculateCMD`, then a
skill check), and speed is halved.
- Every move is priced with that chance when the unit has it on, from any source (`PriceAttacks` with the chance each
  attack is made).
- The AI switches it on (a free action) before one of its three best options that move and provoke, when the move still
  fits at half speed, the attacks avoided are worth 2 or more, and that option is then the best thing to do.
- It is switched off again, when the AI started it, once no conscious enemy engages the unit.

## 31. v0.3.18 (test8): active abilities, step AA-4 (the long tail)

The plan is `docs/ACTIVE_ABILITY_PLAN.md` §H AA-4: each item only with evidence from the coverage report and the logs,
or a confirmed game rule. The martial parts are in `Tactics/TacticalEngine.Martial.cs`.

**Evidence.** The `[Coverage]` lines of the 0.3.17-test3 to 0.3.18-test2 logs (5 party members):
- Demoralize was blacklisted for every one of them, and Touch of Fatigue for Camellia and Orvath;
- Hydraulic Push (a bull rush) was "not understood";
- Orvath's Burning Infusion toggle is off.

No metamagic rod, Spell Combat, style stance or unread mod buff component showed up.

**Demoralize and Dazzling Display.** Valued by the game's rule (`Demoralize.RunAction`):
- **Check:** d20 + Intimidate + the action's bonus against 10 + the target's character level + its Wisdom bonus, the DC
  adjusted by difficulty like every skill check (`CombatMath.IntimidateOdds`).
- **Duration:** on a success the Shaken buff lasts 1 + (result - DC) / 5 rounds, one more with Frightening Thug. The
  value uses that average over the successful rolls.
- **Rest of the model:** the debuff path as for any spell (Shaken is soft control), with fear and mind-affecting
  immunity from the buff's descriptors. An enemy already shaken is not demoralized again.

**Blacklist.** The tactical engine and its coverage report now value Demoralize and touch debuffs by what they do
(`IsBlacklistedForCombat(ability, valuedByEffect: true)`); only the legacy real-time engine keeps the old rules.
Fatigued (-2 Str / Dex) is a plain debuff (0.25 x threat), no longer soft control (0.4): otherwise Touch of Fatigue
would have beaten the damage cantrips.

**Combat maneuvers.** `ContextActionCombatManeuver`, its custom version and `ContextActionDisarm`, valued as chance x
worth x rounds.
- **Chance:** `RuleCombatManeuver`'s roll - d20 + CMB (with the action's stat replacement: casting stat, kineticist
  main stat, caster level as base attack) against the CMD; a natural 1 fails, a natural 20 succeeds; 0 against a target
  immune to maneuvers.
- **Duration:** margin / 5 + 1 rounds, as `ApplyManeuver` gives it.
- **Worth, by maneuver:**
  - trip, overrun and awesome blow knock the target prone: 0.4 for one round, if it can be knocked down;
  - disarm takes a manufactured weapon: 0.6;
  - dirty-trick blind or entangle: 0.4; sickened and sunder armor: 0.25 (not when the buff is already there or the
    target is immune);
  - bull rush (Hydraulic Push), in turn-based only: an enemy in melee with a party member walks back and loses its full
    attack, 0.2 for one round;
  - pull and grapple: 0.

**Vital Strike, Cleave, Pounce.**
- **Vital Strike** (`AbilityCustomVitalStrike`): the single attack plus (VitalStrikeMod - 1) more rolls of the weapon
  dice (the mythic feat adds the weapon bonus as many times). They are added after the critical multiplier, so only the
  hit chance counts (`CombatMath.VitalStrikeExtra`).
- **Cleave** (`AbilityCustomCleave`): the target, then the enemies in the melee hand's reach by distance to it, each
  attack made only while the attacks before it hit. Two attacks at most, all of them with Great Cleave.
- **Pounce:** a charge by a unit with Pounce that is not staggered is a full attack (`UnitAttack`), and is valued as
  one.
- **Shared parts:** the role and preference weight of plain attacks (`AttackWeight`), and the ranged-shot provoke rule.
  These abilities make attack rolls, so the silent-failure safeguard judges them.

**Metamagic and chains.**
- **Persistent:** a passed save is rolled again (`AbilityEffectRunAction`, `ContextActionSavingThrow`), so the fail
  chance becomes 1 - pass^2.
- **Extend:** doubles an extendable buff duration (`ContextDurationValue.IsExtendable`) in the per-target duration.
- **Chains** (`AbilityDeliverChain`: Chain Lightning, Chains of Fire, Eldritch Chain): they reach the units nearest
  their first target within the radius, up to the target count. Enemies, allies or both by their target type (Selective
  keeps allies out); an ally they catch costs friendly fire like an area.
- **Empower + Maximize:** stays as the vanilla rule (maximized x 1.5). Tabletop Tweaks' own setting gives less (about
  20 % less with both on the same spell); that difference was judged too small to read mod settings for.

**Cast outcome.** A skill check made by an ability's action (its target from the game's context data) and a combat
maneuver roll count as the game doing something. Otherwise a failed Demoralize or trip would have left no trace and
counted as a silent failure.

**Left as they are (no evidence yet):**
- metamagic rods;
- kineticist infusions: Burning Infusion costs burn with every blast, and the player had switched it off;
- Spell Combat (plan: investigate with a Magus first);
- style stances;
- resurrection;
- mod buff components with other names.

## 32. v0.3.18 (test9): code review before the playtest

The review covered the four parts of the engine: the core, the toggles and modes (AA-2/AA-3), the long tail (AA-4), and
the profile, math, observers and real-time path. Each finding was checked against the decompiled game before it was
fixed.

**Game side effects and wrong game rules.**
- A combat maneuver's CMD was asked with the maneuver's type. An enemy that strikes back at that maneuver
  (`CounterAttackOfOpportunityOnCombatManeuver`) made a real attack of opportunity on the AI unit just from the AI
  scoring Trip. Such an enemy is now asked without the type.
- The heal preview triggered a fake `RuleHealDamage`. Handlers that ignore `IsFake` (`RemovedByHeal`,
  `SufferFromHealing`, heal triggers) acted on it. The heal is now read from the rule without triggering it.
- Every spell-resistance check counted as "the cast did something", because the game runs it for every unit target. As
  a result the silent-failure safety net never judged an ability aimed at another unit. Now only a check that decides
  something counts: resisted, immune, or a real roll.
- Full-round casts (a spontaneous caster's metamagic, Summon Monster) also face the defensive-casting check. They were
  never priced for it and never stepped out of melee first.
- Spell resistance now uses the cast's context (`UnitPartSpellResistance` context overloads): SR bonuses and penalties,
  Piercing, the cast's descriptors.
- Buff immunity now also reads the buff's own descriptor (the game's buff context). Example: Demoralize's Shaken
  against a fear-immune target.

**Double counting and values that never fired.**
- **Coup de Grace:** paid its attacks of opportunity twice.
- **Withdraw:** priced its attacks of opportunity outside the danger scale. It is now priced like every other move
  (`MoveRisk`).
- **Friendly fire:**
  - each ally's own damage is estimated, not the first ally's for all;
  - a downed ally in the area costs its whole worth;
  - downed allies in an area count.
- **Control on an already disabled enemy:** was discounted twice.
- **Standing up from prone:** now compared with the whole turn that standing allows, not just the option chosen first.
- **A performance with no one left in its area:** is stopped now. The performer was always counted as a recipient.
- **Defense value of AC modes:** now per enemy, from its own attack roll with the d20's limits. An enemy without a
  weapon attack gains nothing from AC.

**Modes (AA-3).**
- **Switching:** modes switch only after the dispatcher has accepted the attack. A refused attack used to leave them
  switched and locked for the round.
- **Turning off:** done with the game's `TurnOffImmediately`. Otherwise the buff stays until the round ends. A mode
  whose immediate turn-off costs an action is never chosen off.
- **Baseline:** a mode that is on but not started (Fighting Defensively between attacks) is valued as it will apply.
- **Full-attack-only bonuses:** counted only for a real full attack.
- **Set limit:** past 32 sets, the remaining groups keep their state; they were treated as "off".
- **Excluded:** toggles that spend an action or a resource every round (`ActivatableAbilityUnitCommand`,
  `ActivatableAbilityResourceLogic`).
- **Mobility:**
  - no longer switched on for an option this turn had already ruled out;
  - speed x1.5 with Trickster's fast movement;
  - the skill DC adjusted for difficulty;
  - when the fight ends, the toggles the AI switched on that the game keeps on (Mobility) go off again.

**Combat memory.** A switch between turn-based and real time mid-fight no longer counts as a new fight. The round count
went backwards there and reset the casts, uses and toggle locks. Real time's rounds now continue from the turn-based
round reached.

**Per-fight observers.**
- The observers are also cleared at the start of a fight. A loaded save in real time raises no "combat ended", so a
  retried fight kept the last attempt's blocks.
- What one creature of a kind showed is learned per fight, not for the whole session.

**AA-4.**
- **Success and failure lists:** what a maneuver (or a Demoralize) does on success counts as an unknown outcome. Its
  failure list and Demoralize's Trickster list are not counted.
- **Maneuver chance:**
  - includes concealment and the attack bonuses the game copies into the CMB (flanking, the difficulty bonus);
  - an overrun only knocks prone by 5 or more;
  - a spell's resistance and attack roll come first.
- **Intimidate chance:** applies to the Shaken buff only, with the duration taken from the DC before the difficulty
  adjustment.
- **Chains:** jump to the targets the game would pick, downed party members and unconscious enemies included.
- **Cleave:** swings at unconscious enemies in reach too, as the game does.
- **Vital Strike:** adds dice to physical weapon damage only.
- **Extend:** only durations it can extend.

**Other estimates.**
- Dice of type Zero added half their count.
- Damage pre-rolled into a shared value, and shared-value dice counts, are averaged from the ability's own calculation.
- Evasion and Improved Evasion are counted on Reflex half-damage.
- The Kinetic Archer's blast is rolled against touch AC.
- Branch-condition checks are cached per ability copy (a scroll and the spell, metamagic copies).
- The full damage on a failed save and half on a passed one are one damage, not two.
- Self-damage is not damage to the target.

**Movement.**
- A 5-foot step before a full attack is priced like a move into a harmful area, and its path is checked.
- A spot around an area must be cheaper than walking straight through it.
- With Pounce, a charge is considered at any range the charge allows.
- A summon can be cast again once the caster's summons are gone.
- A one-round mark is not spent on an advance.
- Leaving a harmful area is valued per round on the scale entering one uses.

**Left as they are:**
- the main-target condition for each unit of an area (`ContextConditionIsMainTarget`), a rare case;
- the Swordlord / Hellknight / Frightening Thug upgrades of Demoralize;
- Rowdy Vital Strike;
- switching Mobility off while engaged when speed would matter more.

## 33. v0.3.18 (test10): what the test9 play-test logs showed

Evidence comes from the 0.3.18-test9 logs: turn-based (13 fights) and real time (11 fights). Each finding was traced to
the code and the decompiled game before it was changed. The player is fine with the long fights Orvath's Grease
produces (the party took little damage). So Grease keeps its value, and the rest of the party now plays around it.

**Grease and other areas that only hinder movement.**
- **Turn-based.** Entering one now costs what a fall costs: the move action spent standing up (a quarter of a round)
  plus the attacks of opportunity standing provokes there, times the chance to fall. It is priced for one round, because
  leaving is an option every turn.
  - Before, it was priced as a share of the whole duration, about 24 points. Seelah stood idle beside the party's own
    Grease for 37 turns (about a third of her turns) while the enemies inside it hit her.
- **Real time.** The game keeps a unit down while the area knocks it down again (`UnitProneController`: it stands only
  after 3 s without a fall). So the full price stays, and leaving one is valued for the rounds it still lasts. The
  front line lay prone in Grease for 164 checks.
- **Real-time path check.** The dispatcher now checks real-time walks against harmful areas too. It uses a path asked
  of the pathfinder on its own (not the unit's seeker) and never attaches it. The game's own walk bends around
  obstacles where the straight line priced for the move did not.
- **Real-time casts in progress.** An ally's area spell that is being cast counts as the area it will be while its
  command runs (8 s at most). Camellia walked at an archer while Orvath was being ordered to Grease it.

**Penalties are not control.** Shaken, Sickened, Dazzled and Fatigued (and a fear descriptor on its own) now count as
`ControlStrength.Minor`: 0.15 of the enemy's worth a round instead of 0.4. The anti-spam reduction now applies to
them as well. Demoralize had been cast 52 times (turn-based) and 62 times (real time), often over attacks on adjacent
enemies.

**Sleep.** A hit that wakes a sleeping enemy without killing it is worth a tenth while another enemy is still acting;
it used to be half. Camellia woke the sleeping Evoker every round and Orvath put it back to sleep each time. In real
time the archers woke the Spawn of the Hungry Moon after each of five Sleeps.

**Real time decides for everyone at once.**
- **Shared casts.** An ally's cast of the same ability on the same enemy that is still running makes that option worth
  0. This caught 19 duplicate Demoralizes and two Coups de Grace on one Cambion.
- **Touch spells.** These walk first in real time too. Cast from afar, the game queues its own touch delivery and chases
  the target with the held charge: Orvath followed his moving Dretch for about 75 s. This was the player's "stuck
  channelling" report: no Fire Blast was involved.
- **Walking without progress.** Walking counts as a reason to wait only while the unit gets 0.5 m nearer within a round.
  Otherwise the command ends as stuck and the engine decides again.

**Smaller fixes.**
- **Turn-based 5-foot step.** It must be one: a longer real path is refused. Before, a 3.3 m path became a normal move
  that provoked, and Aeiluvetar went down from full HP.
- **Prone.** A unit stays prone for one turn at most. Aeiluvetar lay prone for seven turns in a row.
- **Consumables.** None on summons, and only for a value of 2 or more.
- **Soul Power.** Not cast again while neither the burn nor the dead around have changed (seven casts in a row).
- **Buff values.** A stat penalty is not a benefit (Enlarge Person's -2 Dexterity), and a Strength-only buff is worth a
  fifth to a recipient that fights at range.
- **New diagnostics.**
  - A revive's final value and what reduced it.
  - What Fighting Defensively's `AddFactContextActions` runs, which is why it is still left as the player set it.
  - Progressive's "gated" line renamed to "on the spell-level ladder".

**Also in this build (asked for after the code review).**
- **Demoralize upgrades:** Swordlord's Dazzling Display bonus, Frightening Thug's greater buff on 4+ rounds,
  Hellknight Fearsomeness within 3 m, Shatter Confidence, and the mythic Trickster's paralysis. Each is valued on the
  control scale of the buff it applies, never against an immune target.
- **Rowdy Vital Strike:** (2 x sneak attack)d6 precision damage, nothing against precision-immune targets.
- **Branch conditions:** evaluated with the cast's real aim as the main target (`AbilityProfile.AimedAt`).
  `ContextConditionIsMainTarget` was true for every unit of an area (a bomb's direct hit against its splash). It is the
  only condition the AI evaluates that reads the main target.

**Regression review of this batch (fixed before the build).**
- **Movement-only areas** (priced as a fall in turn-based) are now strict: the spawning ability deals no damage of any
  kind, applies no hard control, and its buffs impose nothing but Prone. Entangle, Web, Spike Growth and blinding or
  sickening clouds keep the full price. A path through several areas is a fall only if every one of them is.
- **Real-time path check:** asks A* only when a harmful or pending area lies near the walk, on the walkable navmesh
  (graph mask 1), at the front of the queue.
- **Walking without progress:** time spent waiting on a cooldown or an animation does not count. A target that keeps
  moving is marked as out of reach; a fixed point the walk cannot get nearer to is only a path problem.
- **Shared casts:** only for outcomes that do not add up (debuffs, control, marks, Coup de Grace). Two allies' damage on
  one enemy is focus fire.
- **No consumables on summons:** only own-side summons (a Cure on an enemy undead summon is still an attack).
- **Demoralize upgrades:** reduced by the same repeat factor as Shaken, compared against the undiminished Shaken.
- **Soul Power:** blocked while no one has died around and the burn has not grown since the last cast.
- **Strength-only buffs:** reduced only when the recipient's weapon does not add Strength, by the game's weapon rule
  (a composite bow does). An `AddContextStatBonus` with a negative multiplier counts as a penalty.
- **Precision immunity:** incorporeal creatures (`GhostCriticalAndPrecisionImmunity`) count as immune too.
- **Condition cache:** one checker's results are dropped when the frame changes, so per-aim keys do not pile up.

**Checked and left as they are.**
- **Grease's value** to the caster: per the player's preference, see above.
- **Danger calibration** at its 0.4 floor in 12 of 13 fights: a deliberate safety margin, and the party took little
  damage.
- **Dismiss Spell, the Sleep Hit Dice pool, Arcane Reservoir's choice between caster level and DC, and Battle Spirit's
  area:** each needs more evidence or blueprint data.
- **Mobility switched off while engaged:** nothing in these logs showed the case.

## 34. v0.3.18 (test11): what the test10 play-test logs showed

Logs: turn-based (7 fights), a second turn-based session (3 fights), real time (8 fights). No errors in any of them.
Confirmed from test10: Demoralize is no longer spammed (8 casts against 62); touch spells walk first; no consumables on
summons; the turn-based 5-foot-step rule holds; Seelah now works around Orvath's Grease (1 idle turn against 37).

**Real time.**
- **The path check refused good walks (a test10 regression).** A path asked of A* without the unit's seeker is the
  centres of the navmesh triangles it crosses, a zigzag that cut into the Stinking Cloud beside spots the decision had
  put at its edge. 22 approaches were refused and Lann did nothing for two rounds. The check now uses the walk the game
  takes: the unit's own position, the closest point to the goal, and the funnel modifier the game's movement agent
  turns on in real time (`CommandDispatcher.GameWalk`).
- **Refused walks are remembered** for two rounds while the unit stays within 1 m (`CombatMemory.WalkRefused`). Lann's
  same refusals repeated 18 times over three rounds.
- **Re-deciding on entering an area** skips walks that already counted on it: a walk out of an area, an advance or
  withdrawal, an attack priced "[into ...]". Two leave-walks were dropped part-way through.
- **Stuck rule:** a unit within its approach radius, with clear sight, that keeps walking for 2 s is stopped (it was 6 s).
  A walk still looking for line of sight is left alone.

**Both modes.**
- **Summons that bring an ally-catching area** (a Dretch's Stinking Cloud, read from the creature's own facts) are worth
  0.7 while the party has melee fighters. Orvath summoned a Dretch at the start of all 8 real-time fights; its cloud
  nauseated Camellia for five rounds and Seelah for six. Cambion and Quasit had the same value and now win.
- **An effect that outlasts its area** (Stinking Cloud's nausea, 1d4 rounds after leaving) is priced for its own duration
  when walking in, not one round. Orvath stepped into the cloud for a Color Spray and lost three turns.
- **Sleep's Hit Dice pool.** The game spends Sleep's Hit Dice target by target (`ContextActionChangeSharedValue` SubHD); the
  profile checks each target against the whole pool. Orvath cast Sleep centred on a Kobold Evoker six times, each
  re-sleeping the same Mongrel. After two casts that reach others but never their centre, that enemy is no longer
  counted in the caster's area of that spell this fight (`CastOutcome.AreaSkips`); the cast stays allowed for the others.
- **Enlarge Person on an archer** whose bow adds Strength: worth a fifth when the buff also lowers Dexterity (+1 damage
  against -1 to hit from Dexterity and -1 from size).
- **Coup de Grace:** damage reduction comes off the whole hit once, after the critical multiplier and with the sneak
  attack in it.

**Turn-based.**
- **Standing up:** when no safe spot is found, spots right beside the unit are tried without the area filter (the walk
  is only the trigger; the game cancels it as the unit stands). Revived Aeiluvetar lay prone two turns, "no spot to walk
  to". The log now says how many spots were in an area or not walkable.
- **5-foot step:** a step spot refused because its real path is longer than a step is not offered again that turn; up to
  three other directions are.

**Checked and left as they are.**
- **Holy Water "thrown beside" the enemy** (player's question): the throw was aimed at the Skeletal Champion. Splash
  weapons make a ranged touch attack, and on a miss the game sends the flask to a miss point beside the target
  (`Projectile.CalculateMissTarget`). The AI already prices that chance. Holy Water's direct-hit/splash condition is an
  `OrAndLogic` the profile does not evaluate, so both branches are counted for everyone; left for now, as asked.
- **Seelah charging into nine enemies at 77 % HP:** pricing the drop chance at any HP was tried and dropped. With her HP
  and the fight's danger scale it would not have changed that charge, and it could make low-level melee builds timid.
- **Melee allies waiting beside the party's own Entangle:** the enemies inside are held, and the archers work on them.
- **Slow decisions** (400-490 ms, about one per fight, no new profiles built): most likely garbage collection.

## 35. v0.3.19 (test1): Fighting Defensively, toggle ownership, the player's orders in turn-based combat

**Fighting Defensively.** Every test10 coverage line left it alone: its buff has an `AddFactContextActions` that only
removes a buff when the mode ends (`off ContextActionRemoveBuff`). Such a component changes nothing while the mode is
on, which is what the mode is valued for, so it is accepted now (`OnlyRemovesOnEnd`). The coverage line names the buffs
it removes, to check they are harmless.
- **Sanity check of the values the game computes.** Each mode's AC and attack change is logged once per fight
  (for example `[Modes] Seelah: Fighting Defensively = AC +2, attack -4`). A mode that adds AC and whose attack part reads as no
  penalty (or a bonus) is not used: it would look like free AC and stay on all the time.
- **Mobility.** Why it was not switched on before a move that would provoke is logged once per round: the move does not
  fit at half speed, it saves too little, or the move is still not the best option. test10 had no Mobility switch at all
  and no reason.

**Who may switch a toggle.**
- **The AI switches every toggle it can value**, also those the player set before the fight (until 0.3.18 it only
  switched off what it had switched on itself).
- **When the fight ends, every toggle the AI switched goes back to its state before the fight**
  (`CombatMemory.SwitchOffAiToggles`). Toggles the game ends with the fight are left to the game.
- **New setting "Leave a toggle alone for the rest of the fight once you switch it yourself" (on).** The action bar's
  toggle clicks (`MechanicActionBarSlotActivableAbility.OnClick`, `MechanicActionBarSlotItem.OnClick`) are noted while the
  owner is in combat (`GameInterface/PlayerTogglePatch`). The engine then leaves that toggle alone until the fight ends, and
  it is not put back afterwards.

**The player's orders in turn-based combat (investigated, not changed).** The player cannot use a spell, ability or
item for an AI-controlled companion during its turn; in real time they can. This is not a setting of the mod:
- The game greys out the action bar in turn-based combat unless the current unit has no command queued
  (`MechanicActionBarSlot.CanUseIfTurnBased` → `TurnController.CanEndTurnAndNoActing`).
- The mod gives its first order on the frame the turn starts and the next one on the frame the last ends, so the
  queue is never empty when the bar is checked.
- Real time has no such check: the player's order replaces the AI's (`UnitCommands.Run`), and the AI waits while a
  command runs.

A way to let the player take over a turn is in the plan for a later version (a click on the companion's action bar
pauses the AI for that turn, the AI waits for the player's command and then uses what is left).

### 0.3.19-test2: what the test1 logs showed (turn-based 6c49226d, real time 38d120f3)

Fighting Defensively worked as a mode: 9 switches in turn-based and 1 in real time, on when an enemy stood next to the
unit, off for attacks on enemies that were no threat. The "off" gains show its -4 is read. Its switch-off action removes
`CraneStyleWingBuff` (Crane Wing needs it): harmless. Orvath summoned Cambion or Quasit rather than the Dretch while the
melee fighters stood. Enlarge Person went only to melee characters.

**Fixed.**
- **Toggles left on after a fight.** Fighting Defensively "ends with combat" by its blueprint, yet the AI's switch-on
  was still on at the next fight's start (Seelah, Aeiluvetar). A toggle that ends with combat is now switched off after
  the fight when the player had it off. It is switched back on only when it starts with an attack (`ActivateOnUnitAction`),
  never a performance or Rage.
- **Mode values in the coverage line of every fight** (the once-per-session line fell outside a log that starts
  mid-session).
- **One mark at a time.** Camellia's Ring of the Mad Duelist (a free action) marked a new enemy at every decision, the
  same Neophyte Fighter twice in one turn: its buff has no component, so "already marked" could not be seen on the
  enemy. A mark is not applied again while the caster's last mark from that ability still runs on a living enemy that can
  fight. What such a mark's ability holds is logged once, as evidence for valuing it.
- **Smite Evil with Vital Strike, Cleave and Coup de Grace.** The own-attack mark was kept only for weapon attacks, moves
  and Charge; Seelah never smote with her seven Vital Strikes.
- **Real time: stuck options were never learnt from.** The round's memory starts afresh every 6 s and a walk is called
  stuck after 6 s, so `RecordStuck` always found the wrong memory (no "was stuck" line in the log). The option now
  travels with its command, and is not offered again while the unit stands where it got stuck. A walk in range with no
  line of sight that makes no progress counts as a problem with that target. Aeiluvetar got the same no-sight
  Move+Attack six times and attacked once in a 16-round fight.
- **Fighting Defensively's value bias.** The option's value came from the rules as they stand, which lack a mode that is
  on but not started: kept on, the attack had no -4; switched off, the -4 counted as a gain on top. The value is brought
  to the state the attack is really made in first.
- **The Dretch's lower value applies whoever is left** (with the melee fighters down it was summoned at full value and
  its cloud caught Orvath, Lann and Aeiluvetar).
- **Only disabled enemies left:** their threat is no longer cut to a quarter. A sleeping, entangled Dretch was the last
  enemy for four rounds while Camellia and Seelah found nothing worth doing.

**Diagnostics for the next step.**
- **Power Attack, Deadly Aim and Combat Expertise** reach the AI rebuilt by another mod: Power Attack's buff has only an
  `AddInitiatorAttackWithWeaponTrigger` (the game's own has the weapon bonus components the AI reads), and Combat
  Expertise has Tabletop Tweaks' `AddStatBonusIfHasFactTTT`. The coverage line now says what an on-attack trigger runs.
  `blueprints.zip` holds the game's own data, not what mods change at runtime, so the log is the source for this.

**Code review before the playtest (0.3.19-test3).**
- **Disabled enemies keep their danger discount.** The "only disabled enemies left" rule raised the threat used for
  incoming damage, Fighting Defensively's AC value and control values too; it now applies only to the worth of damage and
  kills (`CombatMath.TargetWorth`).
- **One mark at a time only for marks the AI cannot see** (no component on the mark buff). A visible mark is already
  checked on the enemy; blocking it by cast memory kept Smite off a second enemy for the whole fight, and off every enemy
  after a Smite whose command failed.
- **End-of-fight restore never starts anything:** a toggle is put back on only when that starts nothing now (it starts
  with combat or an attack, or simply applies its buff), never one switched on by a command or with a resource. The state
  recorded is the real one before the AI's switch.
- **Fighting Defensively's value correction** only for a mode that really starts with the attack (`ActivateOnUnitAction`,
  available).
- **Real time: a target the unit got stuck on** (no sight, keeps moving) stays out of reach while the unit stands there,
  for every option on it, not only the one that got stuck.
- **Combat memory reset once per fight:** the end-of-fight reset was followed by a second one at the next fight's first
  decision, wiping a toggle click noted in between.
- **Player clicks** count during the party's fight even for a companion that has not joined it yet (real time).
- **Hot-path log text** is built only the first time it is written.
- Left as is: a mode switched on by the AI turns off the player's other mode in the same group (the game's group rule);
  it is not put back after the fight.

**Next (not in this build).**
- Real time: a spot with line of sight for archers whose target is in range but out of sight (the game's own search
  stalls).
- Coup de Grace and other full-round abilities after a 5-foot step (the step is offered for full attacks only).
- Sleep's Hit Dice pool in the game's target order.
- A healer walking to an ally that is withdrawing; a frightened healer's touch spells.
- An own area spell over downed allies that blocks their revival.
- Mobility in real time (half speed costs time, not the move).

## 36. v0.3.19 (test4): one owner for toggles - compatibility with Buff It 2 The Limit

**What the other mod does** (its source, 1.22.0, Id `BuffIt2TheLimit`): it switches activatable abilities by setting
`IsOn` itself and calling `TryStart` (`BuffExecutor.Execute`, phase 0), at combat start (`ExecuteCombatStart`), after a
configured round limit and when combat ends (`RoundLimitWatcher`), and handles activation groups and form chains (Shifter's
Fury, Chimeric Aspect, mounts). It also casts buff routines from spell slots and items, on demand or at combat start. It
offers no public API, and which abilities a player gave it lives in its own save state.

**Where this mod changes the same state.** Five places switch a toggle: attack modes with an attack
(`ApplyModes`), Mobility off when unthreatened (`MobilityHousekeeping`), starting a performance or Rage (`ExecuteToggle`),
stopping a pointless performance (`StopIfPointless`), and the end-of-fight restore (`CombatMemory.SwitchOffAiToggles`).
No other path issues `UnitActivateAbility` or sets `IsOn`. The "player switched it" lock sees only action-bar clicks, so
the other mod's changes were open to being undone. One more overlap: the out-of-combat self-buff routine
(`RealTimeController.ApplyPermanentBuffsOutOfCombat`, after every fight and between real-time decisions) casts missing
hour-long self buffs from the same spell slots, with no setting. The "Pre-Buff Party" button runs only when clicked.

**Design.** Since the other mod cannot say which toggles it owns, the owner is chosen globally by the player:
- **"AI manages activatable abilities (toggles)"** (`AiManagesToggles`, on) replaces "AI may start performances..." and
  "AI may switch combat modes...". Off: none of the five paths switches anything, in or out of combat; the AI still reads
  what is on (attack routines, AC, Mobility in move pricing). On: as before, and "Leave a toggle alone..." still protects
  the player's own clicks. A settings file from before keeps the user's choice: on only if both old switches were on.
- **"After fights, AI companions recast their long-lasting self buffs"** (`AiRecastsSelfBuffs`, on): one gate at the
  entry of the routine covers all its callers and clears its busy-unit queue.
- **Detection is advisory only** (`GameInterface/ModInterop`): Unity Mod Manager's own list (`FindMod("BuffIt2TheLimit")`,
  `Enabled`), looked up at most every 5 s, logged once when it changes. While it is active and either setting is on, the
  settings page says to switch off the matching setting. Settings are never changed for the player.

**Not verified in the game yet:** both mods together (combat-start songs, round-limit switch-off, activation groups).


## 37. v0.3.19 (test5): kinetic blasts priced by what the game deals

**Feedback** (players of 0.3.18): "kinetic blasts, and stuff like a warlock invoker blast from mystical mayhem are not
properly used"; "kineticists aren't blasting as much as they should, especially kinetic shooter just does standard attack
instead of a blast".

**What the game adds that the estimate missed** (decompiled game code):
- **Elemental Overflow** (`AddKineticistElementalOverflow`): on `RuleDealDamage` of the kineticist's own blasts
  (`UnitPartKineticist.Blasts`, the variant's parent), min(Bonus, class level / 3) - limited by the burn accepted unless
  `IgnoreBurn` - times 2 (3 for a fire blast with Fire's Fury, +2 with Elemental Engine at full burn). Its attack bonus
  already counted: the hit chance runs the game's attack rule with the blast weapon. Now in every blast damage estimate
  (`CombatMath.ElementalOverflowDamage`, read from the component's fields: the game adds it inside a damage rule the
  estimate does not run). So accepted burn now has a modelled benefit; no burn cost was added (burn makes blasts
  stronger through Overflow, and its hit-point effect is blueprint data not in the code).
- **Kinetic archer blasts** (`AbilityKineticist.IsArcherBlast`: the archetype's flag and a ranged weapon in hand): the
  blast is shot through the bow (`AbilityDeliverProjectile`, `RuleAttackWithWeapon`, against touch AC) and adds the bow's
  damage modifiers and enhancement (`ContextActionDealDamage.GetDamageInfo`: `WeaponStats.DamageModifiers` + `Enhancement`).
  Hit chance and range were already the game's; the damage now is too (`CombatMath.KineticDamageBonus`).
- **Kinetic Blade** (`WeaponKineticBlade`): every hit casts the blade's blast with the hit's attack roll
  (`KineticistController.TryHandleKineticBladeAttack`); the blade weapon itself deals little. Each attack line of the
  routine now carries that blast's damage (`AttackLine.BladeBlast`, with the blade's damage modifiers for
  `UseWeaponDamageModifiers`, Overflow, the target's resistances and spell resistance), multiplied on a critical like the
  game does.
- **Gather Power** (turn-based): with a gather mode set, the game puts Gather Power in front of an infused blast whose
  burn it would lower (`KineticistController.WillGatherPower`, private: called by reflection) and the gather takes the Move
  action, a second one the Standard too (`TurnController.CalculateKineticistPrediction`). Such a blast is affordable only
  as a standard action with the Move action still there and no walk; it is never the follow-up of a step out of melee.
  The candidate label shows `[gathers power N]`. Real time unchanged (no action economy there). The blast queued behind the
  gather was counted as refused before - fixed in the same build (`CommandDispatcher`, `GameAPI.HasPendingCommands`).

**Diagnostics.** `[Coverage] <name> kineticist:` once per ability set - class level, burn (accepted / max, this round),
archer blasts, the weapon (kinetic blade, its activation action, activated), gather mode and the gather action, and each
burn ability with its burn now (`AbilityKineticist.CalculateAbilityBurnCost`) and Overflow's damage.

**Not done, waiting for a log:** Mystical Mayhem's Invoker blast has no structural blocker in the code (ray with attack
roll, `ContextActionDealDamage`); a log of an Invoker shows how it is valued. The Kinetic Blade activation's own action
cost before an attack is logged, not yet priced.

## 38. v0.3.19 (test6): burn, the Kinetic Sharpshooter's quiver, infusions that block every blast

**Source:** the game's Kineticist blueprints (the player's `Blueprints/Classes/Kineticist` folder), read with the decompiled
code. "Kinetic shooter" is the **Kinetic Sharpshooter** archetype (`KineticSharpshooterArchetype`:
`AddKineticistMechanicalPart ArcherBlast`, so its blasts are shot through the bow).

**What burn is.** `BurnFeature` → `AddKineticistBurnValueChangedTrigger` → `BurnEffectBuff`: `AddContextStatBonus` on
`DamageNonLethal` of `KineticNonLethalDamageProperty` = burn x character level. The game knocks a unit out when damage +
nonlethal reach its hit points (`UnitLifeController`); burn stays until rest (`RemoveOnRest`). Burn per round:
`BurnPerRoundResource` 1, +1 at kineticist level 6 and every 3 after; the day's burn `BurnResource` 3 + Constitution.
Elemental Overflow (`ElementalOverflowBonusFeature`): Bonus = class level / 3, limited by the burn accepted - so burn also
raises blast damage (§37).

**Why kineticists stopped blasting.** A blast that costs burn passes the game's restriction only with that much left this
round (`IsAbilityRestrictionPassed`: `LeftBurnThisRound` = min(left today, left this round)). Substance infusions are on
by default once learned (`IsOnByDefault`) and add burn to every blast (`AddKineticistBurnModifier` on the infusion's buff;
Cluster Arrows +3, applied to all 17 base blasts). So a low-level Kinetic Sharpshooter could never blast, and any
kineticist with an infusion on had no blast left once the day's burn was spent - only the weapon. The Sharpshooter's
answer is its quiver (`KineticSharpshooterCreateQuiverAbility`: 1 burn, standard, class level + Constitution bonus ranks
of `KineticSharpshooterObtainQuiverBuff` = `UnitPartKineticist.QuiverBuff`), which pays a blast's burn first when a ranged
weapon is in hand (`AbilityKineticist.CalculateCost` / `Spend`). Its buff has no effect the analyser reads: never cast.

**Changes** (`Tactics/TacticalEngine.Kinetic.cs`):
- **Hit points before going down** (`CombatMath.HpBeforeDown` = HP left - nonlethal): the unit's HP share (caution,
  heal threshold) and every drop-chance estimate (exposure, path attacks, withdraw, heal danger) use it.
- **Burn priced** (`BurnCost`): the drop chance the nonlethal damage adds before the next turn (the heal model's term,
  mirrored), 0.1 per burn hit point as a day cost, less the Overflow it adds to the blasts left; a burn that would knock
  the unit out costs twice its worth. Applied to each kineticist ability candidate by the game's own cost after the quiver
  (`PriceBurn`, label `[burn N: -x]`), and to weapon attacks with a kinetic blade not yet activated this round (the game
  activates it first: free action, 1 infusion burn; label `[blade burn N]`). With too little burn left to activate it, the
  blade's blast counts nothing.
- **When burn blocks every blast** (none available, at least one failing the burn restriction), against the best other
  standard / full-round option:
  1. *Create Quiver* when the arrows make a blast affordable from next round: (blast - best other) x min(rounds left - 1,
     arrows / burn per blast), less its own burn. Not while the quiver still holds arrows.
  2. Otherwise, with "AI manages toggles" on, *switch off* (free, at once) the active infusion whose burn modifier blocks
     them: (blast - its remaining burn cost - best other) x rounds left. Not a toggle the player switched in this fight;
     back on when the fight ends (`CombatMemory.SwitchOffAiToggles`: an infusion changes the next blast and nothing else).
  Logged once per round as `[Kinetic] <name>: blasts blocked by burn - ...` with what was done or why nothing.

**Not verified in the game yet.** The quiver's rank stacking when cast again with arrows left (not done: it could repeat
every turn if ranks do not add up).

## 39. v0.3.19 (test7): what the test6 logs showed (2 turn-based, 3 real-time fights)

- **Fallen companions left down.** Real-time fight 2: Seelah down from RT2 to the end (RT9); Camellia's Cure Light Wounds
  (~7) could not lift her alone (needed 9), a partial heal counts 0.6 x its share, and an attack (11.5-12.6) beat the
  revive (7-10.5) every round. Fight 3: Seelah down at RT1, Camellia at RT2, both for ~17 rounds. A revive is now worth the
  ally's output for the rest of the fight, up to 6 rounds (`ValueModel.ReviveWorth`, unit-tested), not 3.
- **Grease on a downed ally.** Fight 3: Orvath's Grease went down on the unconscious Camellia; every later revive was priced
  as walking into Grease ("the walk x0.00"). A lasting area harmful to allies now costs half the revive worth of each
  downed ally it covers. `[AoeDanger] <name>: <area> affects it at N% (inputs)` is logged once per party member and area when
  the chance is 90 % or more, to see whether such a price comes from the save or from something unread.
- **The last enemy in our own Grease.** Turn-based fight 1: a sleeping goblin in Orvath's Grease lasted R4-R8; Seelah stood
  beside it every turn (entering cost more than hitting a disabled enemy was worth). When no enemy can act, a movement-only
  area (Grease, Entangle, Web) costs only the attack a fall stops (`MoveRisk`), so she steps in and can Coup de Grace.
- **Sleep on the last active enemy** counts one round at most (the allies' attacks wake it): Orvath re-slept that goblin
  while the archers shot it awake.
- **Unread buffs that spend a limited use or burn** get no "unknown effect" value (`BuffQuality`, `SpendsLimitedUse`):
  Searing Flesh (1 burn, a fire aura against natural / unarmed attackers - `SearingFleshEffectFeature`) was cast in all five
  fights by a crossbowman, and a full-round Soul Power then took the burn back three times. Also: Arcane Reservoir's
  caster-level / DC buffs (unread effect, reservoir points) are no longer used as filler. Burn accepted on cast
  (`AbilityAcceptBurnOnCast`) is priced like a blast's burn (`BurnOf`).
- **Ring of Foe Focus ("Ring of the Mad Duelist").** From its blueprints: a free action puts a `UniqueBuff` mark (one enemy at
  a time) and the wearer's feature gives +2 AC against that enemy (`ACBonusAgainstBuffOwner`) and +2 on saves against it
  (`SavingThrowBonusAgainstFactMultiple`). The mark's buff has components (`UniqueBuff`, `RemoveBuffIfCasterIsMissing`), so the
  test2 "empty mark" guard never applied, and the AI marked enemies it did not fight, twice in one turn. Caster-side marks are
  now valued by what they guard (`TacticalEngine.Marks.cs`): that enemy's share of the incoming threat x (AC x 5 % / 55 % +
  saves x 2.5 %), minus what the mark gives where it is now; 0 ("already marked", "X is the better mark") otherwise.
- **Smaller.** A "+5ft" full attack is offered only when a step towards the target gives reach and line of sight
  (`StepGivesReach`; Lann's four refused attacks: in range, out of sight, a 3.7 m path). No "Leave <area>" to where the unit
  stands (Seelah's 0.0 m path, 4 s stall). An item that is used up needs a value of 5 (a potion was drunk at 64 % HP for 3.7).
- **Not changed:** the real-time decision interval (0.5 s: decisions take 18-33 ms and mostly wait for a running command).

## 40. v0.3.19 (test8): danger from what hits the unit, heal items, caster-side reading of buffs

- **Danger where a unit stands** (`IncomingDamage`) is at least the most damage it really took in one round of the last
  three (`FightStats.RecentWorstTaken`), capped by the uncalibrated prediction there (a hit from an enemy now dead or gone
  does not count). The party-wide calibration (`DangerScale`, 0.4 at its floor in every test6 fight) had cut the danger of
  the one the enemies focus: Seelah lost 55 % of her HP in one round and 50 % in another. Every drop-chance estimate uses it:
  potions and heals (their drop-chance term), withdraw, exposure, revives staying up, burn.
- **Heal items** are judged by the player's potion threshold (when) and the drop chance (whether now); the test7 bar for
  used-up items (value 5) applies only to items that do not heal. The threshold is relative: `ValueModel.HealPerHp` is 0 at
  any threshold and rises towards 0 HP, the drop-chance term ignores the threshold.
- **A lasting area over a downed ally** costs its half revive worth only when a conscious party member has a heal it can
  aim at an ally (`PartyCanRevive`).
- **Buff reading** (`BuffEffectAnalyzer`, shared with real time and the pre-buff routine): `AddFacts` is followed into the
  facts it grants (two levels); caster level and spell DC boosts (`AddCasterLevelForAbility`, `AddCasterLevelForSpellbook`,
  `IncreaseCasterLevel`, `IncreaseSpellDescriptorCasterLevel`, `IncreaseSpellSpellbookDC`, `IncreaseAllSpellsDC`,
  `IncreaseSpellDescriptorDC`, `IncreaseSpellDC`) are worth 0.4, to a recipient with a spellbook only
  (`SpellcastingOnly`); critical-hit and damage-reduction bypass components (`CriticalConfirmationBonus`,
  `InitiatorCritAutoconfirm`, `IgnoreDamageReductionOnAttack`, `IgnoreTargetDR`) 0.4. The counts came from the unread
  components of the three test6 logs (`AddCasterLevelForAbility` 48 times).
- **Pre-buff button** skips buffs that last a round or end with the next attack.

## 41. v0.3.19 (test9): what the test8 logs showed (3 turn-based, 3 real-time fights)

- **Bigger bodies and harmful areas.** The game holds a unit in an area by its body (`AreaEffectView.Contains`:
  `Shape.Contains(position, Corpulence)`), and `Corpulence` grows with size (`UnitEntityView.GetSizeScale`). Seelah's walk
  ended just outside Orvath's Grease ("around Grease"); Camellia then cast Enlarge Person on her and her bigger body lay in
  it. A buff that makes its recipient bigger (`ChangeUnitSize`, read from the buff: `CombatMath.GrownCorpulence`) now pays
  for the harmful area the bigger body would overlap where the recipient stands (`AoeDangerAnalyzer.GrowingDanger`),
  priced as staying inside one (`GrowthIntoAreaCost`); label "grows into <area> -x".
- **A knockdown area costs the turn it knocks down at its start (turn-based).** Seelah's rounds 2 and 3 ended at once with
  `CanAct=False`: a unit an area knocks down at the start of its own turn loses that turn - `TurnController.ContinueActing`
  ends it (not able to act, and `UnitCanGetUpOnCommand` is taken while the fall is still being applied). 14 earlier logs
  show the same skipped turns (mostly Seelah's). The 0.3.18-test10 price (a quarter of a round for standing up) is replaced
  by `ValueModel.KnockdownAreaCost` (unit-tested): staying inside, the next turn is lost at the chance to fall; passing
  through, at that chance squared; a fall whose turn is not lost still costs standing up and its attacks of opportunity.
  Entering (`MoveRisk`), leaving ("Leave Grease") and growing into one use it (`AreaStayCost`). The exemption when only
  disabled enemies are left stays.
- **Grease's save.** It lives in the area buff's own actions, which the ability analysis does not read: everyone was
  "affected at 100 % (save Unknown)". `SaveObserver.SaveTypeOf` remembers the save the game rolled against each ability (by
  anyone); `AoeDangerAnalyzer.AffectChance` uses it when the analysis has none ("(save as seen)").
- **Ring of the Mad Duelist (caster-side marks).** No wasted marks any more, but none placed either: in rounds 19-20 the
  Cultist Champion took a fifth of Camellia's HP a round. Its guard is now that enemy's share of what really comes at the
  caster (`IncomingDamage`), not the party-wide danger scale alone (x0.40 in that fight); and a free action that spends
  nothing (no spell slot, item charge, use or burn: `CostsNothing`) needs a value of 1, not the quick bar of 2
  (`MinCostlessFreeValue`, `QuickBar`). Its value is logged once a round per target ("guards ~x a round").
- **Revive check** (`PartyCanRevive`): a heal counts only when it heals one of the downed allies concerned
  (`AbilityProfile.AppliesTo`: Inflict Light Wounds heals undead only), aimed at an ally or a burst that reaches allies
  (Channel Positive Energy). Belt scrolls and wands count (action-bar items); potions do not (drinker only). Frame-cached.
- **One-roll buffs.** Guidance removes itself on its owner's next attack roll, save or skill check
  (`AddInitiator...RollTrigger` / `SavingThrowTrigger` running `ContextActionRemoveSelf`): `AbilityProfile.BuffsSpentByRoll`,
  one round's worth (`BuffRounds`). Camellia recast it on Orvath in rounds 2, 3 and 4 (2.4-3.9, over attacks). A
  weapon-attack trigger that removes its buff counts as spent by the next attack. The pre-buff button skips both, and
  next-cast buffs.
- **Next-spell boosts** (`TacticalEngine.Boosters.cs`). Arcane Reservoir's "Increase Caster Level" / "DC" (blueprints:
  free action, 1 reservoir point, 2-round buff removed after the next spellbook spell by `AddAbilityUseTrigger`): Orvath
  spent seven points in the test8 logs, mostly before Entangle, Grease or Sleep. A buff spent by the next cast
  (`BuffsSpentByCast`) with caster-level or DC components (`SpellBoostOf`) is worth only what it adds to the best main action
  when that is a spell it applies to (`CoupleSpellBoosts`): DC - the rise in the chance the save is failed (5 % a point,
  up to the cap; half for half-on-save damage); caster level - the extra damage dice of a caster-level `ContextRankConfig`
  up to its cap (stepped progressions half), x0.7 of the spell's value. Otherwise dropped. Logged: "<boost> = x (for ...)".
- **Real time, no line of sight.** After a walk to see a target got stuck (in range, no sight: an enemy in another room
  behind a doorway someone stands in), every option on that target is left out while neither unit has moved 2 m, the unit
  still cannot see it, for up to 18 s (`CombatMemory.NoteNoSight` / `StillNoSight`). The refused-walk memory (1 m, 12 s)
  let Aeiluvetar walk at the same unseen archer three times, 6-10 s each.
- **Not the AI:** the 30-round fight was an IsekaiMod "Rival" Cultist Champion (party damage 4 a round).
- **Review (test10).** The no-sight memory tests sight the way the game's walk did (`LineOfSightGeometry.HasObstacle`
  from the eyes, as `UnitCommand.SightIsClear`), once a frame: `LineOfSightChecker` is another test and logs a line on
  every call, and it was asked for every option on the target. The Ring of the Mad Duelist moves to another enemy at most
  once a round and only for a guard 25 % better than the current one's: with the free-action bar at 1 it could have hopped
  between two moving enemies (every half second in real time).

## 42. v0.3.19 (test11): helpful buffs valued by what they do for who gets them

Audit, evidence and root causes: `docs/SHORT_BUFFS_AUDIT.md`. Checked against the game's spell and mythic blueprints
(Spells, Mythic folders supplied by the player).

- **Parts with sizes** (`Analysis/BuffParts.cs`, static, cached): attack, damage (incl. extra dice on hit), AC by bonus
  type, saves, Strength / Dexterity / Constitution, temporary HP, concealment, miss chance, mirror images, physical DR,
  energy resistance, extra attacks, a bigger size, no spells (Rage), AC / saves against an alignment; through `AddFacts`
  and through buffs a buff switches on (`AddFactContextActions` / `AddBuffActions`: Haste's `HasteBuff` only switches on
  `HasteBuffEffect`). Bookkeeping components are not "unread"; everything else is reported as unread.
- **Real numbers**: each part is evaluated with the buff's `MechanicsContext` under the ability's execution context (the
  game's own construction when the spell lands: caster level, ranks; shared values by their average, never rolled); the
  duration from the ability's `DurationValue` the same way, doubled by Extend (`EffectiveRounds`).
- **Stacking**: a non-stacking bonus counts only above the recipient's best bonus of that type
  (`ModifierDescriptorHelper.IsStackable`); `BuffStackingAnalyzer` no longer reads a rank-based bonus as +1 (it marked
  Shield of Faith - (CL + 6) / 6 deflection - "superseded" by any +1 deflection item).
- **Value** (`TacticalEngine.Buffs.cs`, formulas in `ValueModel`, unit-tested): offence on the recipient's own weapon attack
  against the enemy it would attack (`AttackBonusGain`, `DamageBonusGain`, extra attack = its first attack's expected
  damage; half for a recipient that casts spells); defence on what comes at it (`IncomingDamage`, or its share of the
  enemies' threat before contact) and the strongest enemy's hit chance against it (`ArmorGain`, concealment, saves x
  0.025, DR per hit, `TempHpAbsorbed`, `MirrorImagesAbsorbed`) priced by `PreventionValue` (0.3 a HP kept + the lower
  chance to drop x the ally's worth); over `UsefulRounds` (real duration within the expected rounds; one attack / one roll
  for buffs spent by it). Role weight kept; the round-2 x0.6 and difficulty factors dropped (they double-counted the rounds
  left and the danger). Summons half. Penalties count (Enlarge Person's -2 Dexterity and size: an archer loses by it).
- **Not read**: a buff with no valued part keeps the old formula, at 60 % when it takes a main action.
- **Restraint**: a Standard / full-round action that only buffs is taken over the best immediate main action only when it
  is at least 15 % better (`ValueModel.BuffBeatsImmediate`); logged "[Buff] ... not cast - ... is within 15 %".
- **Guidance** removes itself by name (`ContextActionRemoveBuff` of `GuidanceBuff`, not `ContextActionRemoveSelf`): the
  one-roll rule of test9 now sees it.
- **Log**: "[Buff] <caster>: <buff> = value (slot) - <parts, recipient numbers, rounds>; best other: ..." once a round per
  cast and recipient.
- Unchanged: the pre-buff button and out-of-combat keep-up (`BuffWorth`), toggles, the legacy real-time engine.

## 43. v0.3.19 (test12): test11 play-test review

Evidence: the turn-based and real-time logs of 0.3.19-test11 (4 + 6 fights). The new "[Buff]" lines worked: the 15 %
restraint held Enlarge Person back twice for Divine Zap, Guidance was valued 0.3 and never chosen, each fight cast Enlarge at
most once. Fixes:
- **Size-up dice**: a weapon is its wielder's size (`ItemEntityWeapon.Size` = `Wielder.State.Size` for the party), so a
  size step scales its dice by the game's `WeaponDamageScaleTable` - bows too. The appraisal used +1 a hit; it now uses the
  weapon's real step (longsword / longbow 1d8 -> 2d6: +2.5). Enlarge Person on an archer is therefore mildly positive
  (-2 attack, +2.5 damage a hit), as in the game, not negative as section 42's worked example said.
- **Arcane Reservoir DC**: the ability is a Conditional on the caster's Potent Magic fact (+1 buff, else +2 buff);
  `SpellBoostOf` counted both buffs (+3: "lands 65% -> 80%") where the game gave +1 (Sleep DC 19 -> 20 in the log). It now
  keeps only the branch the caster gets (`BuffAppliesTo` with the caster).
- **Log**: the "[Buff]" line of a next-spell boost shows the boost's own note (it showed the buff formula the boost had
  replaced); the "60 % on a main action" note only where that cut was applied.
- Seen, unchanged: Seelah waits beside the party's own Grease when the last enemies stand in it (walking in is priced
  higher than her attack with about 1.4 rounds left; the ranged allies finish them).

## 44. v0.3.19 (test13): class competence audit, Stage B1 - area toggles read and valued like buffs

Audit: `docs/CLASS_COMPETENCE_AUDIT.md` (Stage A: baseline, pipeline, evidence, class matrix, findings F1-F6, plan).
Stage B1 fixes F1-F3 in `TacticalEngine.Toggles.cs`:
- **F1, areas that give their buff by actions** (`AbilityAreaEffectRunAction` UnitEnter / Round -> `Conditional`s ->
  `ContextActionApplyBuff`, not to the caster; UnitExit not read) are read as grants next to `AbilityAreaEffectBuff`.
  Before: "its area applies nothing the AI can read" - Camellia's Battle Spirit (4 rounds in every fight of every log)
  was never considered; Inspired Rage the same. Checked against the supplied Mythic blueprints: the 19 Aeon gaze areas
  and Angel Halo give their buffs this way, each behind IsAlly / CasterHasFact / HasFact conditions.
- **F3, conditions per recipient**: each grant's conditions (the area buff's `Condition`, the Conditionals on the way)
  are checked with the game's own `ConditionsChecker.Check` under the performer's context (`AbilityProfile.CheckSafe`,
  the same safe-condition list; unevaluable: counted at half). An ally whose acceptance toggle is off is not a recipient;
  the AI never switches that toggle. The stop rule ("nobody left in its area") uses the same check.
- **F2, value**: each recipient's share is the test11 appraisal (`AppraiseToggleBuffs` -> `AppraiseParts`, shared with
  spells: the recipient's own attacks and incoming damage, stacking increment, ranks in the performer's context) over
  the rounds the resource and the fight last (`ValueModel.UsefulRounds`). Unread effects keep the old formula, at 60 %
  when the toggle takes a main action. A spell prohibition keeps the toggle's own rule (`ForbidSpellCasting` with its
  ignore feature). A toggle that only helps allies is a buff for the 15 % restraint rule and gets the "[Buff]" line.
- Coverage: "may start it when worth it (area R m gives <buff> if <conditions>)".
- Not changed: acceptance toggles, attack modes, infusions, Gather Power, `AiManagesToggles`, `RespectPlayerToggles`,
  the legacy real-time engine.

## 45. v0.3.19 (test14): class competence Stage B2, from the Classes blueprints

See `docs/CLASS_COMPETENCE_AUDIT.md` §8. Changes:
- **Warpriest Fervor**: the game offers each Warpriest spell as a quickened, self-only conversion paid with Fervor
  (`AbilityData.GetConversions`, `FervorQuickenedSpell`). `AbilityDiscovery.AddConversions` skipped every conversion with
  the original's blueprint, so it was never an option; now a same-blueprint conversion with other metamagic is kept
  (Fervor, Combat Healer). Same-spell copies without metamagic (Spell Fool) stay out.
- **BuffParts** reads `AttackTypeAttackBonus` / `WeaponAttackTypeDamageBonus` (Rage, Bloodrage, Inspired Rage, stances:
  Value x AttackBonus, melee or ranged), `ACBonusAgainstAttacks`, `TemporaryHitPointsPerLevel` (x the recipient's level),
  `ModifyD20` take-best (Bit of Luck, Fortune, Lion's Call: about +3 on attacks and saves; conditional variants half), and
  saving throw bonuses against descriptors (conditional).
- **Toggles**: an ally who accepts a rage aura loses its spells (the effect's `ForbidSpellCasting`): counted for that
  recipient; the performer's own prohibition keeps the toggle's rule.
- **Witch Cackle / Chant** (`ContextActionReduceBuffDuration` Increase, behind `HasBuffFromCaster`): read as
  `AbilityProfile.Extensions`; valued by `ExtensionValue` - each unit within reach carrying the caster's copy, one more
  round of that hex's share of its worth (`ControlShareOf`; a hex that disables keeps its full worth), only the part of
  the extra time the fight still lasts.
- Rage and Bloodrage are started by the game at combat start while on (Immediately + DeactivateIfCombatEnded): the
  coverage line says so instead of "not activated by a command".

## 46. v0.3.20 (test1): playtest feedback 1 - blasts against a full attack on a Rat Swarm

**Report.** Three units full-attacked a Rat Swarm with a bow instead of blasting: Lann (Kinetic Sharpshooter, FullAttack(5)
35.6, no blast in the top five), Ember (Psychokineticist, FullAttack(3) 21.7 over Fire Blast 11.4) and Camellia
(FullAttack(3) 20.8 over Sleep 14.1 and Eldritch Blast 8.6).

**Cause (game code).** Swarms carry `SwarmDamageResistance`: on `RuleCalculateDamage` every physical damage of a weapon
that is not a kinetic blast (`WeaponCategory.KineticBlast`) is declined by half, unless bludgeoning (Tiny swarms), or
totally (`DiminutiveOrLower`). The weapon estimate (`CombatMath.ReadRoutine`) took a hit as the weapon's average damage
minus damage reduction, so arrows against the swarm counted twice what they deal: Ember's full attack is about 10.9, below
her Fire Blast; Camellia's about 10.4, below Sleep. The same estimate counted critical hits on targets immune to them
(`AddImmunityToCriticalHits` sets `RuleCalculateDamage.CritImmunity`) and sneak attack on targets immune to precision
damage. Swarms also take x1.5 from area damage (`SwarmAoeVulnerability`: a projectile that is not single, or
`AbilityTargetsAround`, or a location hazard - a spell's own area effect does not count); spell estimates left it out.

**Changes.**
- **Weapon hits by the game's damage rule** (`ReadHitOnTarget`): the weapon's damage bundle (as `UnitHelper.ApplyDamage`
  builds it) through a fake `RuleCalculateDamage` at its least and most, as `UnitHelper.EstimateAttackDamage` /
  `CalculateAbilityDamage` do - nothing dealt, no log. It applies every target handler: damage reduction, energy
  resistance on a weapon's energy dice, swarm resistance, vulnerabilities, the difficulty setting's declines. Read from
  the same rule: critical-hit immunity (no critical part) and what the target keeps of physical damage (attack-mode
  damage changes, sneak attack and Vital Strike dice are scaled by it). Once a frame per unit, target and weapon. When it
  cannot be read, the old estimate stands.
- **Sneak attack** needs a target not immune to precision damage.
- **Swarm area bonus** (`SwarmAreaFactor`, the game's test) in spell damage estimates; the damage observer divides it
  out of what it records (the bonus belongs to that hit, not to the damage type).
- **[DmgCheck]** at the end of a fight: an AI unit's damaging ability whose hits dealt more than a quarter more or less than
  estimated (at least two hits; criticals and save halves left out). Silent when the estimates were right. For effects the
  estimate does not read, such as a mod's blast bonus.

**Not settled from the snippet.** Lann had no blast among his five best options; the reason (burn, weapon, availability)
is in the full log (`[Kinetic]`, `[Coverage]` lines). A physical archer blast shot through the bow against a swarm is
halved by the game (the bow is the damage's weapon); the blast estimate does not apply that.

## 47. v0.3.20 (test2): the 0.3.19 release logs (6 turn-based, 3 real-time fights), DarkCodex, archer blasts

**Lost turns: a walk longer than one move action (turn-based).** Fight 3, rounds 4-5: Aeiluvetar's Move+Attack (path
15.0 m), Lann's (16.3 m), Orvath's (15.7 m) and Camellia's Grease (13.0 m) were "force-finished before it started": the
navmesh path to the target was far longer than the line the option was priced on, the dispatcher set the two-action
movement limit, the walk used the standard action, and the game finished the attack unstarted - four turns gone. A
command with a standard action after the walk now needs the path to fit one move action
(`TurnController.GetRemainingMovementTime(total: false, singleActionMove: true)`); a 5-foot step, a full attack (checked
already), full-round abilities and Charge are left as they were. Refused, the engine takes its next best option.
"path ends out of reach (0.0m short)" now says "path ends without line of sight to the target".

**Revives.** A heal too small to bring a downed ally up counted 0.6 x its share of the way, without the chance the ally
stays up once up; a heal that brings it up counted that chance. Real-time fight 3: with the Cultist Champion next to
Camellia, bringing her up was worth 1-3 ("stays up ~3 %") and a part-way heal 15-17, chosen round after round. The
part-way heal now has the same factor, for the ally brought up by a next heal of the same size.

**Danger calibration.** All nine fights ended at the danger scale's floor (x0.40): the enemies dealt 0-25 % of the
prediction (3 of 226, 0 of 248, 73 of 566, 96 of 389), the smoothed share 0.2-0.3. The floor is 0.25
(`ValueModel.MinDangerScale`, unit-tested). What the unit itself took in its worst recent round still sets its own
danger (`FightStats.RecentWorstTaken`), so the one the enemies focus is not made careless by a quiet party.

**Real time: in range, no line of sight.** An Advance with the weapon's range as its radius has already arrived when the
unit is in range: the game did not move it (Lann, 12.2 m from a Dretch, range 13.8 m; then 11 s at "-9.1 m to go").
Real time walks to the nearest free, walkable spot outside harmful areas within this move that sees the enemy
(`FindSightSpot`, the game's `IsUnitCloseEnough` with line of sight), label "(to see it)"; with none there is no
Advance on that enemy. Turn-based finds such a spot in its own path search and is unchanged.

**DarkCodex Elemental Scion** (source 1.9.6, `KineticBlastDiceIncrease`): on `RuleDealDamage` of the caster's blasts the
dice step up (d6 -> d8 -> d10 -> d12; once at level 1 for simple blasts, once more at 15 for all), inside the roll where
no estimate can read it. Kinetic blasts now learn: each AI unit's blast, over the session, what its hits dealt against
the estimate (critical hits and save halves left out, the rolls of one cast on one target within a second as one hit,
compared after the hit's resistances are recorded), from three hits "as estimated", 0.67..1.6
(`DamageObserver.BlastFactor`); every blast estimate is multiplied by it. Logged once when it first moves 10 %:
`[DmgCheck] <unit>'s <blast> deals x1.24 of its estimate (5 hits so far): its estimates use this`. Kinetic blasts only:
one cast, one target, one comparison. Other abilities keep the per-fight `[DmgCheck]` report only.

**Kinetic archer, physical blasts against swarms.** The bow is the damage's weapon, so `SwarmDamageResistance` declines a
physical archer blast (metal: half against a Tiny swarm; earth, bludgeoning: nothing). Read from the game's damage rule
with the bow and the blast's damage type (`ArcherBlastDecline`, the decline only).

**Log.** Removed from the verbose log: real-time "=> wait" lines with no option at all (615 in one log), zero next-spell
boosts with no spell to boost, the trap patch's line for every player move, the trap cache line and its per-trap list on
every refresh (now once per trap), the turn tick's "EndTurn - Nothing worth doing" (the decision line says it);
"[HitChance] SR: X is IMMUNE to Y" once per pair.

**Seen, not changed.** Most real-time "stuck" commands were the game's walk (a flying Quasit's position, doorways); they
are cancelled after 6 s as designed. The Isekai "Rival" Cultist Champion fight (19 spans) is the enemy, not the AI.

## 48. v0.3.20 (test3): advance after a refused long walk; review of test1-test2

**Correction to §47.** The "lost turns" of fight 3 were not lost: each unit walked 13-15.5 m (the game spent the
standard action on the walk) and attacked from there the next round - in effect a double move towards a far enemy. The
flaw was the price: the option was valued as walking and attacking this turn, which could not happen.

**Advance beyond one move.** After the dispatcher refuses a walk-then-act as longer than one move action, the target is
remembered for the turn with the refused option's value (`TurnMemory.TooFar`, `TacticalDriver.RecordRejected`), and the
next decision offers "Advance-><enemy> (beyond one move)" next to whatever can be done from where the unit stands. It is
worth `AdvanceShare` (0.35, the share every advance has) of the larger of the weapon attack on that enemy and the refused
option - a caster closing to cast is worth a share of its spell, not of its dagger. An advance walks one move action; the
next decision then has the standard action: to attack from there, or to advance again (the double move, when nothing
better appears). Before test2 the double move happened blind; in test2 it only happened when nothing could be done from
here. Fight 3 with the change: Orvath's Color Spray (14.8) beats an advance (about 5); Lann's shot at the Kobold Evoker
(2.1) beats an advance (about 1.4); a melee unit whose only option here is weak walks up.

**Review fixes.**
- The test2 failure text "path ends without line of sight" no longer marked the target as out of reach for the turn
  (the check matched on "path ends out of reach"); both texts are constants of the dispatcher, read by the driver.
- Learned blast damage (`DamageObserver`): critical hits are also recognised by the damage's own multiplier, not only by
  the attack roll; damage over time and lasting areas that carry the blast (Burning Infusion's burn, Deadly Earth) are
  left out (each tick would read as a weak hit); infusion variants share what is learned (keyed by the blast); the share
  is `ValueModel.LearnedShare` (unit-tested).
- Buff appraisal: a buff's damage bonus counts at what the enemy keeps of weapon damage (`Offence.PhysicalFactor`: half
  against a Tiny swarm), as the attack's own damage already did.
- The game-rule hit cache is cleared with the attack routines (`ClearAttackCache`: a preview while "standing").
- "no spell to boost next" stays out of the verbose log also when the best action is an attack (most of the 372).

**Enemy importance (reviewed, not changed).** `CombatMath.Threat` is 4 + 3 x CR, x0.25 disabled, x1.2 in melee with
someone; every option is then valued by what this unit can do to that enemy (its hit chance and damage by the game's
rules, resistances, kill chance capped at the HP, control's share of the threat). The importance itself knows only the
CR: not what the enemy can do (a caster and a brute of one CR are alike), not what it did this fight, not whom it hits.

## 49. v0.3.20 (test4): enemy importance by what each enemy can do and did

`Tactics/EnemyThreat.cs`, pure formulas in `ValueModel` (`ThreatShare`, `ObservedOffence`, `Urgency`, unit-tested).

**Before.** `CombatMath.Threat` - the importance behind kill bonuses, control values, debuffs and predicted danger - was
4 + 3 x CR (x0.25 disabled, x1.2 in melee): a CR 2 wizard with Color Spray left and a CR 2 dretch counted the same.

**Offence, once a round per enemy** (again when one joins or falls), against the party member it is most likely to
attack (the weakest it engages, else the nearest):
- weapons: the full attack by the game's rules (`CombatMath.EstimateWeaponAttack`: its attack bonus against that ally's
  AC, damage after the ally's reduction, criticals) - the estimate the party's own attacks use;
- spells and abilities with a use left that the game flags as harmful to enemies (spellbook spells it can still cast,
  `Spellbook.CanSpend`; spell-like and supernatural abilities, `IsAvailableForCast`), the best of its six highest-level:
  damage x the chance it lands (`CombatMath.EffectChance`: spell resistance, attack roll, save, evasion), control as the
  ally's worth a round for up to two rounds x the chance (none against an immunity), x(1 + half the other party members
  in its area);
- heals with a use left (`EffectOnAlly` helpful): what the best restores on its most hurt ally - a healer undoes the
  party's damage;
- what it really dealt to the party in the fight's completed rounds, blended with the weapon prediction (two rounds of
  prior, `ObservedOffence`).
Offence = the largest of spell, heal and blended weapon value (one of them a round).

**Share, not inflation.** `ThreatShare` = (offence / CR worth) against the fight's average, 0.5..2: the fight's total stays
what the CRs give, so the balance of damage against control, heals and buffs (and the danger calibration) is unchanged;
the order between enemies changes. `Threat` = CR worth x share (x0.25 disabled, x1.2 in melee as before).

**Urgency.** For the worth of taking an enemy down or holding it (`CombatMath.ControlWorth`, used by kill bonuses,
control, debuffs, maneuvers, hex extensions, harmful auras) - not for the danger it poses, which already drives heals:
x(1 + 0.5 x the chance the ally it attacks drops to it this round), a quarter for a summon; only while that ally is within
its reach. The ally's HP is read each frame.

**Log.** `[Threat] R1: most dangerous first - <enemy> x1.8 (<spell> 9.8 on <ally>), <enemy> x1.2 (attacks 6.1 on <ally>),
...; N more x0.6-0.9` at the first decision with two or more enemies; `now the most dangerous - ...` when another enemy
leads by 15 % or more; at the end `[Threat] fight: dealt the most to the party - <enemy> 24 (rated up to x1.6), ...`.

**Cost.** Once a round per enemy: one attack estimate and up to twelve ability estimates (profiles are cached per
blueprint; a new enemy spell's profile is built once per session).

## 50. v0.3.20 (test5): the test4 release logs (4 turn-based, 4 real-time fights); attacks of opportunity checked

**Attacks of opportunity against the game code.** The engine prices them in every place the game makes them:
leaving an enemy's reach (`DisengageRisk`, the game's `EngagementSimulator` check, Mobility per enemy), a provoking cast
(`CastProvokeRisk`, the defensive-casting check), a ranged attack roll (`AttackRollProvokeRisk`, `ProvokeObserver`),
standing up, walking out of an area (`MoveRisk`), reviving next to an enemy (`ReviveStaysUp`). Their cost - the chance
to be dropped, the extra chance to drop before the next turn, the HP to heal - was right. What they stop was not:
- **a hit does not stop a walk.** `UnitAttackOfOpportunity` does nothing to its target's move, and
  `UnitMoveController` interrupts a turn-based walk only when the unit can no longer move (prone, dropped, held). Since
  0.3.13 every hit counted as a lost move and action: leaving an enemy that hits half the time halved a Move+Attack's
  worth. Now the stop chance is the drop chance plus the chance a hit knocks the unit down - an enemy whose weapon
  trigger trips on a hit (wolves' bites: `AbstractWeaponTrigger` with a Trip maneuver or `ContextActionKnockdownTarget`,
  from its facts or its weapon's enchantments), by its CMB against the unit's CMD (`CombatMath.TripOnHitChance`), none
  for a unit that cannot be knocked down;
- **a hit spoils a spell by the concentration rule.** A hit while the spell is being cast calls for a concentration
  check against 10 + spell level + half the damage (`UnitConcentrationController`, `RuleCheckConcentration`), and none
  when the game says damage cannot interrupt it (`AbilityData.CanBeInterruptedWithDamage`: wands, held touch charges).
  It was a flat half. The walk before a cast cannot spoil it: the check is made only for a cast already started.

**Bane counted as taking an enemy out.** `AbilityClassifier` maps the Compulsion descriptor to Charm, and Charm is
hard control: Bane (Compulsion, Fear, Emotion, Mind-Affecting; -1 to attacks and against fear) was valued like Hold
Person, by the party and in the new threat assessment ("Neophyte Cleric x2.0 - Bane 24 on Seelah"). Compulsion says how a
spell works, not what it does: the profile now reads control from the other descriptors, the buffs' conditions, and a
faction change (`ChangeFaction`: Dominate Person, whose descriptor is only Compulsion). The immunity checks still see
Compulsion.

**Save bonuses against one kind of effect.** Remove Fear's +4 against fear counted as +2 to every save for each ally in
its burst - up to 8.6 in fights with no fear, and it was cast once on that value. A save bonus against descriptors
(`SavingThrowBonusAgainstDescriptor`, its context variant) now counts for the share of enemies that can use such an
effect (`EnemyThreat.ShareUsing`: the descriptors of each enemy's harmful abilities with a use left and of its
features, at least 0.1); a bonus against an alignment (Protection from Evil's AC and saves) for the share of enemies of
it (`Alignment.HasComponent`). Other conditional bonuses stay at half.

**Blast learning from resisted hits.** `[DmgCheck] Orvath's Fire Blast: 2 hits dealt 0 a hit, estimated 1` - a fire
blast on a Dretch and a Quasit (fire resistance 6-7). What is left after a flat reduction says nothing about the blast,
and the learned share fell. Only hits whose every roll reached the target as rolled (no reduction, decline,
vulnerability or immunity) are learned and checked; a hit with a later reduced roll is taken back whole. The per-fight
report also needs the difference to be at least 2 a hit.

**Slow decisions.** Four decisions took about 1 s with little to weigh and nothing new to build (median 20-57 ms), one
or two a fight, at any point in it: most likely the game's garbage collection on a low-end laptop. The `[Perf] slow
decision` line now says when a garbage collection ran during it, and when the enemies were assessed, so the next log
tells.

**Looked at, left as is.**
- Turn-based refusals "path ends without line of sight" (10, all Camellia or Seelah) are per option: Daze on the
  Goblin Stirrer went through right after Move+Attack on it was refused. One turn was lost (Camellia R4, fight 3: three
  options on a Ghoul Drifter in Grease, all refused, the rest worth under 0.5).
- Real-time, fight 1: all five first commands ended 0.4-0.6 s after they were issued, with no AI line between - not
  the AI's doing (its own drops are logged, as in fight 2: "now in a harmful area", a Dretch's Stinking Cloud).
- Shield of Faith 15 on Seelah (real time, fight 2): +2 AC when enemies hit her on 17+ halves what reaches her
  (9.4 a round expected) - right by the model.

## 51. v0.3.20 (test8): attacks of opportunity in target choice and movement (play-tester concern, test7 logs)

**The concern.** Companions sometimes pick a farther enemy and walk off, provoking from the enemy beside them or from
enemies on the way. Is the risk weighed, or ignored?

**What was already there (0.3.13-0.3.20-test5).** Every option that moves is priced with the attacks of opportunity
the game would make (`DisengageRisk`): the enemies engaging the unit now and every enemy whose reach the walk passes
through (sampled every 0.75 m), each asked the game's own disengage question (`ShouldAttackOnDisengage`,
`AttackOfOpportunity` with `simulate`). Each attack is priced by its hit chance, damage, critical range and trip chance
against this unit (`PriceAttacks`), giving the chance to be dropped on the way, the chance the walk is stopped (dropped
or knocked down), the extra chance to drop before the next turn, and the HP to heal, times the unit's worth (tanks pay
60 % of the damage part). The option loses that: Move+Attack, Charge, move-then-cast, the walk to touch, Advance,
Withdraw, Leave area and the real-time back-off. Mobility is switched on when it saves more than it costs. Provoking
casts, shots and standing up are priced by their own rules (§50). In the test7 logs only 2 of the chosen turn-based
options provoked (Camellia's Move+Attack on a Zombie, Orvath walking past two enemies to revive Seelah), none in real
time: the pricing works, and provoking stays a choice when it pays.

**What was missing.**
- **No other spot was looked for.** The approach was always the straight line to the target. Only a harmful area on
  that line made the engine look at spots around the target (§33); the attacks were priced but never avoided. A spot on
  another side of the target can provoke nothing - still inside the reach of the enemy the unit would leave (the game
  makes the attack only when the unit leaves an enemy's reach), or clear of one the line passes.
- **Where the walk ends was priced only below half HP.** Camellia, at full HP beside the Skeletal Champion, walked off to
  a Zombie for one attack of opportunity priced 0.8 ("Mobility would save only 0.8") and was down before her next turn
  (test7 turn-based, R2). Leaving an enemy is a choice of where to stand; for a healthy fighter walking up to an enemy
  without provoking, the old rule stays (its place is in melee).
- **The straight line, not the real walk.** A path that bends around an ally or a corner can pass enemies the line did
  not, as the 5-foot step (§34) and harmful areas (§33) showed before.

**Changes.**
- `PlanApproach`: when the straight approach provokes attacks that cost at least 0.5 (`AttackDetourCost`; a near-sure
  miss is not worth a search), the 16 spots of one ring inside reach of the target are priced the same way (walkable,
  free, within this turn's move, out of harmful areas) and the cheapest replaces the straight approach when it costs
  less (a short walk breaks ties). The unit walks there first and acts on the next decision, as around an area. The log
  says `(from a spot that provokes 0, not 1)`. Areas keep their two rings and the advance's spots beyond reach; an
  attacks-only search uses one ring (16 spots, each priced once per decision and cached per target and range).
- `MoveRisk`: a move that provokes also pays for where it ends against where the unit is (the higher chance to drop
  before the next turn there, `ExposureCost`), at any HP. Nothing changes for a move that provokes nothing, or one to a
  spot no more dangerous than here.
- **The real walk is counted.** For the option chosen, `CommandDispatcher.Run` counts the attacks of opportunity along
  the walk the game will take (`TacticalEngine.PathAttackers` on any path; turn-based: the attached `ForcedPath` up to
  where the unit arrives and as far as this turn's movement goes, not for a walk the game makes as a 5-foot step; real
  time: the pathfinder's walk, asked only when an enemy is near the line, together with the area check). A walk that
  provokes more than the option was priced for is refused (`the walk provokes more attacks of opportunity (2, priced for
  1: ...)`), the attackers are kept for the turn (`TurnMemory.PathAttackers`), and the next decision prices the option
  again with them (`RepriceRealPaths`, `[its path: AoO 2]`, Mobility too) - not dropped: it competes at its true price.
  Refused a second time, it is dropped for the turn like any other refusal. Advance, Withdraw and Leave area are
  counted too.

**Not overly cautious.** Nothing forbids provoking. The spot search only changes where the unit stands for the same
action on the same target, and only to a spot that costs less; the destination price is the unit's real extra chance to
drop there (at full HP against a weak hitter it is under the 2 % floor and costs nothing); the real walk only corrects
an under-count. A fighter that gains more than the attacks cost still walks off and takes them.

**Real time, test7.** The quiet waits for the standard action's cooldown (PERF-001) skipped 56, 33, 54 and 82 decisions
in the four fights (97, 62, 80 and 102 made; 5.0, 3.8, 4.0 and 5.7 s of decision time). The median decision is longer
(37-47 ms) because the skipped ones were the quick "wait" decisions. The two slow decisions (628 and 744 ms) chose
nothing and ran during the game's garbage collection. Every refused-or-interrupted command was the game's (all five
first commands interrupted at once, then commands on a target that died).

## 52. v0.3.20 (release): the test8 logs (3 turn-based, 3 real-time fights)

**5-foot steps around a unit.** Lann's "5ft step out of melee" was refused three times in each of rounds 6 and 7 of
the first turn-based fight (the game's path was 2.5 m and 4.1 m: around the units beside him); he then shot from melee,
provoking, and dropped to 40 %. The step's spots were checked only against harmful areas (turn-based) and the refused
spots themselves, so the next spot in the same blocked direction was offered again. A turn-based step spot now needs a
free line (`StepIsClear`): no other body on the spot or across the line, and the navmesh trace reaching it within 0.3 m.
The dispatcher's path check still refuses a step that is not one.

**Looked at, left as is.**
- No chosen option provoked an attack of opportunity in either log, and no approach needed a spot search or a recount
  of the real walk: the party fought from where it stood or walked up without leaving anyone's reach.
- Seelah went down twice (turn-based fight 3: the Isekai Mod reinforcement Vermleks' Inflict Light Wounds, Mass; real
  time fight 3: the Large Water Elemental while her Mage Armor was being cast) - enemy damage, not a walk or a missed
  heal.
- Real-time quiet waits skipped 97, 77 and 75 decisions; the slow decisions (566-913 ms) all ran during the game's
  garbage collection.

## 53. v0.3.21: real time with Wrath Tactics - its rules first, AI Control fills the gaps

**Request.** A player using Wrath Tactics (Gh05d, player-written rules: "heal an ally below 50 %", "buff at combat
start") wanted both mods to work together: Wrath Tactics' rules carried out when they fire, AI Control in charge
otherwise, never a companion left idle.

**Why they clashed.** AI Control replaces the game's auto-attack at the same place (`AiBrainController.TickBrain`)
and decides only for a unit with no command, so it never cut into a running Wrath Tactics command. But it decides
within 0.5 s of a unit going idle, and Wrath Tactics evaluates its rules every 3 s (`TacticsConfig.TickIntervalSeconds`):
AI Control filled every gap first. Wrath Tactics skips a unit while a cast or walk without an `AiAction` is running
(`PlayerCommandGuard.HasForeignActiveCommand`) - AI Control's own casts and walks - so it waited for AI Control in turn.

**Handoff (`Tactics/WrathHandoff`, unit-tested; `RealTimeController.WrathHadItsTurn`).** With the setting on and Wrath
Tactics active, a unit Wrath Tactics manages (enabled for it, with an enabled global or own rule) that has no command
is offered to Wrath Tactics first (`TacticsEvaluator.RequestTick`). AI Control waits until Wrath Tactics' evaluation
counter has moved (it looked at its rules) or 0.6 s, then decides itself if no command came. Once per idle stretch: a
unit AI Control then leaves waiting is not offered again until it has carried out a command; Wrath Tactics' own 3 s tick
still covers it. A Wrath Tactics command is never cancelled by AI Control's stuck-command check.

**Isolation.** Every new path starts with the setting and a cached "Wrath Tactics active" check (the mod manager's
list, every 5 s); off, or without the mod, nothing else runs and AI Control behaves as in 0.3.20. Turn-based combat
never reaches `RealTimeController`. Wrath Tactics is reached only by reflection (`ModInterop`, checked against 1.34.0):
a missing or throwing member switches the handoff off for the session, logged once.

**Limits.** A rule whose action is "Do nothing" gives no command and is invisible to AI Control. Wrath Tactics may still
replace one of AI Control's weapon attacks when a rule fires (its own priority). The handoff costs at most about 0.6 s
each time a managed companion becomes free (normally a frame or two).

## 54. v0.3.21 (test2): the 0.3.20 release logs (3 turn-based, 4 real-time fights)

**A turn left unused beside the last enemy.** Turn-based fight 3, round 7: Aeiluvetar had every action left and the
only enemy, a Skeletal Champion Archer her arrows hardly hurt, in range - `Attack=0.5 => END TURN` (0.49, under the 0.5
bar every main action must clear). When nothing clears the bar, a weapon attack from where the unit stands with any
worth (more than 0.05, after its own costs) is taken now - unless the enemy is disabled: there the bar keeps the party
from waking a sleeping or held enemy for a sliver of damage (the sleeping Vermlek's attacks were worth 0.2-0.4 in fight
2). Both modes; nothing else changes.

**Looked at, left as is.**
- Orvath cast Sleep on "Vermlek" four rounds running (fight 2): there were two; each cast slept the one awake (the
  party's attacks woke them), and the aimed one, already asleep, was dropped from the area count after two casts.
- Fight 3 switched from turn-based to real time and back (rounds counted from 1 again): the player's switch.
- Real time, fight 3: Seelah's potion was dropped as she was nauseated by the Rat Swarm right after choosing it (the
  game starts only walking then); her next one went through. Fight 2: two Medium Earth Elementals took her from full
  HP to 6 % in a round after a Move+Attack that provoked nothing - the healthy fighter's place is in melee.
- No refusals, no lost turns, no errors in either log; slow decisions only during the game's garbage collection.

## 55. v0.3.21 (test3): Power Attack, Deadly Aim, Piranha Strike and Combat Expertise as attack modes

The coverage lines of every log said "AI: leaves it" for Power Attack and Deadly Aim: their buff holds only an
`AddInitiatorAttackWithWeaponTrigger`. A 0.3.19 comment put that down to another mod rebuilding them; it is the base
game's own form - `PowerAttackBuffEffect`, `DeadlyAimBuffEffect`, `PiranhaStrikeBuffEffect` are game blueprints. Before
each suitable attack the trigger applies the effect buff that carries the `WeaponParametersAttackBonus` /
`WeaponParametersDamageBonus`. So the per-attack mode choice of 0.3.18 (AA-3) never ran for them, for anyone; they
stayed as the player set them, and the attack estimate never counted their -hit/+damage (the effect buff exists only
during the swing).

`ModeOf` now reads through such a trigger when it fires before the attack, asks nothing the AI does not model (at most
melee or ranged: `CheckWeaponRangeType`) and only applies buffs; their parts are read as before, each valued in its own
effect buff's context (its ranks). A triggered mode is never "in the estimate" (`UnitMode.InEstimate`): kept on, its
change is added to the option's value; switched, the change is weighed as for any mode. Combat Expertise was left
for its `AttackTypeAttackBonus` (the melee penalty): read now, for the weapon in hand, and part of the free-AC sanity
check. The `[Modes]` line says "(applied with each attack)" for a triggered mode.

## 56. v0.3.21 (test4): coverage gaps - invisibility, energy protection, weapon buffs, melee attack actions, Command, Poison

From the offline audit (`docs/COVERAGE_AUDIT.md`, gaps 2-7); each reader follows the game component it reads.
- **Invisibility** (`BuffInvisibility`: Greater Invisibility, Invisibility, Vanish). In the game an invisible unit has
  total concealment (`UnitPartConcealment.Calculate`) against attackers without See Invisibility or True Seeing; the
  attacks it makes against such a target get +2 (`RuleCalculateAttackBonus`, concealment bonus) and the target is
  flat-footed against it (`RuleCheckTargetFlatFooted`: no Dexterity to AC, sneak attack applies). New part
  `Invisibility`. It counts for the share of the enemies still fighting that cannot see it (`IsSeeInvisibility`, no
  blindsense, as `UnitMemoryController` judges it):
  - defence: a 50 % miss chance;
  - offence: +2 plus the nearest enemy's Dexterity bonus on attacks, and the recipient's sneak attack dice per hit.
  The kind an offensive action ends (`NotDispellAfterOffensiveAction` false: Invisibility, Vanish) counts once, for its
  first attack and one round of cover. Nothing is counted when the recipient is invisible that way already.
- **Protection from Energy** (`ProtectionFromEnergy`, subclass of `AddDamageResistanceEnergy`) absorbs every point of
  its type until its pool is spent (`CalculateValue` returns the pool left). Read before as resistance 5 per hit with
  no pool. Now the pool is both the amount and the cap, and any energy-resistance pool is counted over the whole buff
  (pool / rounds per round), not again each round.
- **Weapon buffs**, judged on the recipient's weapon in hand (`BuffPart.Weapon`) instead of being counted at half:
  - `AdditionalDamageOnHit` (Scaled Fist Draconic Fury): energy dice per hit, natural/unarmed/melee/specific-weapon
    checks as in `OnEventAboutToTrigger`. They are not reduced like weapon damage against swarms (`BuffPart.Energy`).
  - `EquipmentWeaponTypeEnhancement` (Magic Fang, Greater Magic Fang): enhancement bonus to attack and damage, natural
    and unarmed attacks or one category. Typed Enhancement when not `IsStackable`.
  - `IncreaseDiceSizeOnAttack` (Lead Blades): the weapon's dice that many sizes up, by the game's
    `WeaponDamageScaleTable` (`SizeUpDiceGain` with steps), for the categories and subcategories it names.
  - **Weapon enchantments** (from the Equipment / Weapons blueprints the player supplied):
    - Which abilities: `EnhanceWeapon` (Magic Weapon, Greater Magic Weapon, Keen Edge, and their off-hand versions)
      and `ContextActionEnchantWornItem` on the main hand (oils, Holy Sword, the Greater domain powers, Black Blade,
      Bane). They are recorded in `AbilityProfile.WeaponEnchants`.
    - How each enchantment is read: `BuffPartsReader.ReadEnchantment`, for the weapon it goes on (Magic Fang: natural
      and unarmed; Magic Weapon: the main hand's; the off-hand version: a two-handed main weapon only).
    - `WeaponEnhancementBonus` counts only above the weapon's enhancement now (`RuleCalculateWeaponStats.Enhancement`).
    - `WeaponCriticalEdgeIncrease` (keen) adds the base threat range again, but not on a weapon whose range is doubled
      already (`DoubleCriticalEdge`: keen or Improved Critical, the game's own flag).
    - `WeaponEnergyDamageDice` adds energy dice per hit.
    - `WeaponDamageAgainstAlignment` (Holy, Unholy, Axiomatic, Anarchic) counts for the share of enemies of that
      alignment.
    - Bane (`WeaponConditional*`) counts at half.
    - The greater versions take the enchantment at their caster-level step, as `EnhanceWeapon.RunAction` does.
    - An enchanting ability with no buff of its own uses this appraisal instead of the spell-level guess; "nothing to
      gain" is reported when, for example, keen goes on a weapon that is keen already.
- **Melee attack actions**:
  - `AbilityCustomMeleeAttack` is Devastating Blow (its only type, read by reflection): one melee attack at -5 that
    always threatens a critical hit. Valued by the attack estimate with the -5 and the threat chance raised to 1;
    nothing extra against a target immune to critical hits.
  - `ContextActionMeleeAttack` (Piledriver, Death Attack, Blade Whirlwind): one attack or the full attack with the
    melee hand, with auto-hit and auto-threat. `SelectNewTarget` attacks the enemy in reach nearest to the target.
    Around the caster (Blade Whirlwind) it attacks each enemy the area reaches. An ability that only attacks is a
    weapon attack (`OnlyMeleeAttack`, `IsWeaponAttack`); one with other effects (Death Attack) adds the attack as an
    "attack" part.
  - **Piledriver's trip or bull rush** comes from the caster's feature: an `AddAbilityUseTrigger` for this ability puts a
    buff on the caster whose `AddInitiatorAttackWithWeaponTrigger` (on a hit) runs `ContextActionCombatManeuver`.
    `OnHitManeuvers` follows that chain. It values the maneuver as maneuvers are valued (`ManeuverStrength`, CMB
    against CMD), times the hit chance.
- **Command** (Flee, Approach: `SetFleeOrApproachLogic`). The game walks the bearer away from or to the caster and
  takes it out of its own control (`UnitPartFleeOrApproach`, `PreventDirectControl`): hard control for its one round.
  `IsHarmfulBuff` now uses the same control reading (`BuffControl`), so a faction change counts there too.
- **Poison** (`BuffPoisonStatDamage`, Constitution).
  - What the game does: one dose when it lands (`OnActivate`, whatever `NoEffectOnFirstTick` says), then each round
    a save; a failed save is another dose, until `SuccesfullSaves` passes or `Ticks` rounds.
  - How it is valued: the expected doses are `ValueModel.PoisonDoses` (unit-tested). Two points of Constitution are
    one hit point per Hit Die (`CombatMath.PoisonHpLoss`), valued as damage. With no other control it replaces the
    plain-debuff guess (0.25 of the enemy's worth per round), which it was valued at before. Other stats stay a plain
    debuff.

The audit script now also counts what the engine already did for harmful-flagged abilities whose buffs it cannot read:
a plain debuff when the ability can be aimed at an enemy. The first audit listed Command and Poison as "never cast";
they were cast, valued only by that guess.

## 57. v0.3.21 (test6): ally hexes, Protective Luck, Tactician teamwork feats, free metamagic

- **Ally hexes were never cast.** The game's blueprints flag the witch and shaman hexes aimed at allies "Harmful"
  for enemies and allies alike: Ward, Fortune, Protective Luck, Fury, Battle Ward, Draconic Resilience. Read as harmful,
  they were offered only against enemies, which they cannot target. `AbilityProfile.HarmfulToEnemies` now needs an
  ability that can reach an enemy (`AimsAtFriendsOnly`: no enemy, point, area or chain target, not personal). Their buffs
  are valued like any buff on an ally. Enemy threat (`EnemyThreat.IsHarmful`) uses the same rule, so an enemy witch's
  Ward no longer counts as an attack spell. The healing hexes were cast already, because heals may be aimed at friends.
- **Protective Luck** (`AddTargetBeforeAttackRollTrigger`): before each attack roll against the bearer, the attacker
  gets a buff whose `ModifyD20` rolls twice and keeps the worse (misfortune). Read as +3 AC against every attacker
  (`MisfortuneOnAttacker`); at half when limited to melee, non-reach or a descriptor.
- **Tactician teamwork feats** (Vanguard and Forester: `AddFactsFromCaster`; the Cavalier's buffs:
  `AddFeatureIfHasFact`). The granted feat's own components are read and marked `BuffPart.Granted`, so an ally that
  has the feat already gains nothing:
  - Precise Strike (+1d6 in melee when flanking);
  - Outflank (+2 more to hit when flanking);
  - Back to Back (+2 AC when flanked);
  - Shake It Off (+1 saves).
  Each needs a partner with the feat (the Tactician gives it to everyone around), so it counts at half. The other
  teamwork feats (Coordinated Defense, Shield Wall, Allied Spellcaster, Seize the Moment...) stay unread.
- **Free metamagic on the next spell** (`AutoMetamagic`, through the next-spell boosters of
  `TacticalEngine.Boosters.cs`). A buff whose metamagic ends with the spell it changes (`Once`), or that is spent by
  the next cast, is now a booster: Phantasmal Mage, Emergency Help, Healing Blessing, Life Spirit.
  - Where it applies: the game's own `AutoMetamagic.ShouldApplyTo` decides which spells it goes to.
  - What it adds to the main action chosen with it:
    - Empower: half again of the damage or healing;
    - Maximize: about +60 % of the dice;
    - both together: +140 %;
    - Persistent: a second save, so the effect lands at `1 - (1 - fail)^2`.
  - What it never counts: metamagic the spell has already. Reach, Extend, Quicken and Selective change no number in
    the value and are not counted.

## 58. v0.3.21 (test7): mythic buffs; the Units and creature folders

- **Edict of Impenetrable Fortress / Edict of Invulnerability** (Aeon, `AddPhysicalImmunity`): immune to physical
  damage (every form, `RuleCalculateDamage`). Read as damage reduction that no weapon passes, which keeps what
  physical damage takes (`PhysicalDR`, at most 80 % of what comes at the recipient). Its log note says "immune to
  physical damage".
- **Golden Dragon Might** (`OutcomingDamageAndHealingModifier`): +N % to the damage the bearer deals (the
  `ModifierBonus` of the damage rule). New part `DamagePercent`: that share of the recipient's weapon damage per round,
  limited to melee or ranged by its private weapon type. Only against allies, or only for or never for area damage:
  at half.
- **Thousand Bites, Dragon Pride** (`BuffExtraEffects`): a second buff the game adds while another one is on the
  bearer (`CheckAndReapply`). Its parts are read at half.
- Still unread: Edict of Perseverance (`SuppressBuffs`), Equal Force, Light of Creation, Joy of Life, Ode to
  Miraculous Magic, Second Breath, Angel Rekindle (resurrection).
- **Units and creature blueprints** (supplied by the player). Of the 464 abilities in the Units folder, 416 are
  valued; the 48 others are enemy or NPC only (teleports, golem attacks, test spells). Every companion ability is read.
  Of the creature-ability folder (540 blueprints, mostly passive monster features), 20 of its 23 active abilities are
  read, among them the summoned templates' Smite. The audit script now follows `ContextActionCastSpell` (`m_Spell`) as
  `AbilityProfile` does; Dragon Wrath and Demon Charge were wrongly listed as not understood.

## 59. v0.3.21 (test8): verification of what is valued, with the AI's own readers

The coverage script (§56-58) judged abilities by component names. This audit runs the mod's real readers
(`AbilityProfile.Get`, `BuffPartsReader.Read`, `BuffEffectAnalyzer.Analyze`, `TacticalEngine.ModeOf` / `ProfileOf`)
on every blueprint the player supplied, outside the game (`tools/ProfileAudit`, `docs/COVERAGE_AUDIT.md`
"Verification with the real readers"). For each ability it mirrors the engine's target sides (`EvaluateAbility`) and
value paths (`ScoreAbility`) and says whether the ability is valued by what it does, only by a guess, by nothing, or
never offered at all.

**False positives found and fixed** (looked valued, but the engine never offered them or valued them by a guess):
- *Never offered - no side to aim at.* Each kind had its own cause:
  - personal abilities that reach only the caster but are flagged harmful to enemies (Wizard Abjuration Resistance /
    Protective Ward, Arcanist Energy Shield, Transmutation school powers, Frightful Aspect, Firebelly, Battle Spirit):
    `HarmfulToEnemies` now excludes them (`OnlyOnCaster`), as it excludes friends-only abilities;
  - areas that reach only allies, flagged harmful (Holy / Unholy Aura, Shield of Law, Cloak of Chaos): excluded the same
    way when they deal no damage;
  - friends-only abilities whose drawback buff was read as an attack (Lesser Restoration turns Exhausted into Fatigued,
    Hell's Decree dazzles the ally it empowers): `AppliesHarmfulBuffs` leaves friends-only abilities out;
  - enemy-only abilities flagged helpful to allies (the Assassin's Death Attack): `HelpfulToAllies` needs an ally it can
    reach;
  - helpful areas that can only be aimed at the ground (Rainbow Dome, Circle of Clarity, Pillar of Life): aimed where an
    ally stands; the area form of Dispel Magic where an enemy stands;
  - harmful areas that can only be centred on the caster without being personal (Rolling Thunder, Explosive Fervor,
    Crushing Presence, Rousing Retort): aimed at the caster, like a personal area; unique-weapon self-centred areas too.
- *Debuffs by a flat 0.25.* A debuff whose buff the parts reader understands (Bestow Curse, hex
  penalties) is now valued by what its penalties take from the enemy's offence and defence
  (`ValueModel.DebuffShare`: attack, damage, AC, saves, Strength and Dexterity penalties, capped at 0.6 of the
  enemy's worth, unit-tested). Debuffs with nothing readable keep the flat 0.25.
- *Immunities read as nothing.* Freedom of Movement, Death Ward and other immunities (`BuffDescriptorImmunity`, `SpellImmunityToSpellDescriptor`, `AddConditionImmunity`) are now
  conditional saves against that descriptor; energy immunity (`AddEnergyDamageImmunity`, `AddEnergyImmunity`) is
  energy resistance 30. Weapon enchant carriers (`BuffEnchantWornItem`, `BuffEnchantAnyWeapon`) are read by their
  enchantments; `AddEffectFastHealing` is a heal per round, limited to the damage the recipient takes.
- *Pits* (Create Pit, Spiked / Acid / Hungry Pit, `AreaEffectPit`): read as a control area (at least soft control),
  with the fall and every-round actions walked and a Reflex save; units already in a pit, too large, or immune
  (evasion / effect-immunity facts the pit names) are not counted as caught.
- Buff saves are capped at +20 in the worth; bookkeeping components (`ReplaceSourceBone`, `ReplaceCastSource`) no
  longer make a buff look unread.

**Confirmed correct by spot checks:** Bless, Haste, Heroism, Shield of Faith, Barkskin, Mage Armor, Bull's Strength,
Divine Favor, Blur, Displacement, Stoneskin, Divine Power, Invisibility, Magic Weapon. The offline run cannot apply
per-unit branch conditions (Enlarge Person's size branches); the engine checks those per target in the game.

**Still unread** (valued by the old quality guess, or not at all): polymorph forms, auras carried by buffs
(`AddAreaEffect`), on-hit triggers (`AddInitiatorAttackWithWeaponTrigger`), `SuppressBuffs`, Reach / Extend / Quicken
next-spell metamagic, teamwork feats other than §57's four, natural-attack toggles (claws, `EmptyHandWeaponOverride`),
spellcasting buffs (`IncreaseAllSpellsDC`, `IncreaseCasterLevel`), Shadow Evocation / Conjuration, Black Hole, Rift of
Ruin, Haunting Mists, Raise Dead and resurrections, Ray of Enfeeblement (`AddFactContextActions`, flat 0.25),
movement-speed buffs, enemy regeneration. Magic Deceiver's Magic Hack slots are internal and correctly never offered.

**Review of this audit (same build).** A second pass with the harness found mistakes in the first:
- The new "helpful ground area" rule also aimed Symbol of Revelation (strips concealment) and Symbol of Vulnerability
  (-4 saves) at the party: neither its blueprint nor its buff carries a harmful flag. A buff whose read parts are all
  penalties, or that strips concealment (`DoNotBenefitFromConcealment`), now counts as harmful
  (`AbilityProfile.TakesAwayOnly`). Both symbols are aimed at enemies.
- The same blind spot made Crushing Despair, Prayer's enemy half, Mind Fog, the hag Evil Eye, Soul Siphon, the Peer
  Through spells and Trickster's Bear's Grace (-8 Dexterity) count as 0.15 "marks" on enemies, and some as buffs on
  allies caught in them. The mark test and the friendly-fire test now use everything the ability applies
  (`HarmfulEffects`), not only the blueprint flag: they are debuffs valued by their penalties.
- A debuff read only in part (Bestow Curse's Deterioration, Plague Storm, Symbol of Vulnerability's lower spell
  resistance and damage reduction) kept only the share of the penalties read, which could be below the old flat 0.25:
  the flat share is now its floor.
- An immunity carried by a debuff no longer cancels its penalties in that share.

All folders after the review: 2 942 valued, 468 guessed, 364 nothing, 52 never offered (the ones listed in
COVERAGE_AUDIT.md, all correct to leave).


## 60. v0.3.21 (test9): auras on buffs, See Invisibility / True Seeing, claws, harmful performances

Gaps from §59's unread list, checked with `tools/ProfileAudit` before and after.
- **Auras carried by a buff** (`AddAreaEffect`): Archon's Aura, Aura of Greater Courage, Frightful Aspect's fear aura,
  Repulsion, Cloak of Dreams, the Cleric domain and Warpriest blessing greater powers (Glory, Madness, Protection,
  Liberation, Charm), Celestial Aura of Heaven, Judgment Aura, the Godclaw Hellknight tiers, Power of Faith, Ghoul Touch's
  stench. `AbilityProfile.Auras` records the carrier and its area; `TacticalEngine.Auras.cs` values what the area gives
  to the units around the bearer, with the same grant reading as performances (`ReadAreaGrants`, now shared and
  following failed saves for auras), the area's conditions per unit (`GrantReaches`), allies by the buff appraisal and
  enemies as debuffs or control times the chance it lands (spell resistance, the area's save). A carrier with no part of
  its own is no buff on the caster any more (Archon's Aura was counted twice). Time Stop's freeze makes its bearers
  untouchable: worth nothing as control. 34 party abilities.
- **See Invisibility, True Seeing** (`AddCondition`): the miss chance it takes away from the recipient's attacks on the
  enemies now - invisible ones, and Blur / Displacement for True Seeing (the game's exceptions in
  `UnitPartConcealment.Calculate`). Nothing hidden: not cast (was a 0.5 guess every fight).
- **Claws for empty hands** (`EmptyHandWeaponOverride`: bloodline claws, Feral Mutagen and the like): the dice they add
  over the empty-hand attack; nothing while the hands hold a weapon (the game arms only empty hands). 64 toggles.
- **Harmful performances by what they apply**: the toggle code tested the harmful flag only; Dirge of Doom's Shaken
  buff carries none and was never valued on enemies. It uses `AbilityProfile.IsHarmfulBuff` now (§59).

Party abilities (enemy, NPC, cutscene folders left out), test8 → test9: valued 2 539 → 2 586, guessed 415 → 387,
nothing 269 → 250; toggles valued 138 → 202, guessed 84 → 37, nothing 74 → 57.

Still unread: polymorph forms, on-hit triggers (`AddInitiatorAttackWithWeaponTrigger`: mostly special effects - a
debuff on hit, Master Slayer's and True Judgment's kill on a failed save, Feint), `SuppressBuffs`, Reach / Extend /
Quicken next-spell metamagic, the other teamwork feats, spell DC and caster level buffs (still the old analyzer reading,
for casters only), Shadow Evocation / Conjuration, Black Hole, Rift of Ruin, Haunting Mists, resurrections, Ray of
Enfeeblement's Strength penalty, movement speed, enemy regeneration.

## 61. v0.3.21 (test10): stat changes at a buff's start, regeneration, spellcasting boosts

- **Stat changes made when a debuff starts** (`AddFactContextActions` → `BuffActionAddStatBonus`, behind a save's failed
  branch or a Conditional): Ray of Enfeeblement's Strength penalty (a shared value, 1d6 + 1 per 2 levels, x-1), Touch of
  Gracelessness. Valued by their penalties (`ValueModel.DebuffShare`), not the flat 0.25. The failed branch is the full
  penalty; a passed save halves it, so this is the upper end.
- **Regeneration** (`AddEffectRegeneration`, `UnitPartRegeneration`): at 0 HP a regenerating enemy only falls
  unconscious and gets up once it has healed; damage of a kind it names (a troll: fire, acid) stops it for a round, and
  then 0 HP is death (`UnitLifeController`). A kill of a regenerating enemy whose regeneration runs is a knock-down,
  0.35 of a real one (`ValueModel.RegeneratingKill`, unit-tested), unless the damage stops it: weapons by the game's
  weapon rule (`RuleCalculateWeaponStats` damage types: flaming dice, material, magic; `WeaponAlignment`), abilities by
  their damage actions' types (`CombatMath.EndsRegenerationOf`). A downed regenerating enemy stays a target, so fire
  or acid can finish it; before, it was dropped as unconscious and stood up again.
- **Spellcasting boosts** (`IncreaseAllSpellsDC`, `IncreaseSpellSchoolDC`, `IncreaseSpellDescriptorDC`,
  `IncreaseCasterLevel`, `IncreaseSpellSchoolCasterLevel`, `SpellPenetrationBonus`) and the stat a recipient's DCs come
  from (its spellbook's casting attribute: Fox's Cunning on a wizard): for a recipient that casts, its casting worth per
  round by caster level times how much better its spells land (`ValueModel.SpellcastingGain`: 5 % per DC point on about
  0.6 of its spells, 2 % per caster level, 5 % per point against the share of enemies with spell resistance; at most
  half). Nothing for a recipient that casts no spells. Hell's Decree (magic), Overwhelming Spell, Ode to Miraculous Magic,
  the Aeon DC and CL gazes, Chronomancy, Focused Infusion among 22 newly read.

## 62. v0.3.21 (test11): on-hit effects, Quicken / Extend for the next spell, shadow spells, clicked-target effects, teamwork feats

- **On-hit effects** (`AddInitiatorAttackWithWeaponTrigger` on hit): a harmful buff put on the target (Stunning Fist and
  its fatigue / sickened forms, Frightful Strikes, the Assassin's poisons, the Death blessing), extra damage, and a kill
  on a failed save (Master Slayer, True Judgment) - `BuffPartKind.OnHitBuff` / `OnHitKill`, damage parts. Valued in the
  buff appraisal against the enemy the recipient would attack: the chance at least one hit lands each round (a natural
  20, a critical hit, the first attack only where the trigger says so), a save at half, the debuff as the control
  section values one (`HarmShareIn`), a kill once at the enemy's worth. Triggers on a miss, before the attack, on the
  attacker, on attacks of opportunity or on a kill stay unread; a trigger that only removes its buff is no effect.
- **Next-spell metamagic** (`MetamagicOnNextSpell`: Magus arcana, Universalist school powers; with `AutoMetamagic`):
  read as next-spell boosts, removed by the game when that spell is cast (`AbilityProfile.IsSpentByCast`). Quicken is
  worth the best other standard action it leaves room for - only for a booster that costs no action while a swift action
  is left (a swift booster takes the swift the quickened spell needs). Extend is worth the useful rounds a doubled
  duration adds within the fight. Reach and Selective change no number the value is made of: recognised, worth nothing.
- **Shadow Evocation / Conjuration**: their copies of other spells were already offered (the action bar's conversions)
  but valued as the real spell. Now each target's share of the effect is the game's: it disbelieves on a passed Will save
  against the DC, and then damage is cut to the shadow factor and other effects land with that chance
  (`MechanicsContext.TryAffectByShadow`; `CombatMath.ShadowShare`, inside `EffectChance`); a shadow summon at the factor
  with an even save.
- **Clicked-target effects** (`AdditionalAbilityEffectRunActionOnClickedTarget`): Haunting Mists' whole effect, the
  Dark Lurker's Blade from Shadows, Elemental Vortex, the lingering areas of Ice Storm, Stone Call, Volcanic Storm, Tar
  Pool. Their difficult terrain is minor control (`UnitCondition.DifficultTerrain`), not the flat 0.25 of an unread debuff.
  Black Hole and Rift of Ruin were already valued (damage, soft control); only the old script listed them as unread.
- **Teamwork feats a Tactician shares**: Allied Spellcaster (+2 against spell resistance), Shield Wall (+1 shield AC),
  Volley Fire's attack bonus (`AttackBonusAgainstFactOwner`), all conditional; Coordinated Defense / Maneuvers, Shielded
  Caster and Seize the Moment are known but change nothing the appraisal models. Prerequisites, tags and recommendations
  of a granted feat no longer count as unread effects.

Party abilities, test9 → test11: valued 2 586 → 2 658, guessed 387 → 352, nothing 250 → 213; toggles valued 202 → 206.

## 63. v0.3.21 (test12): review of §61-62

- **Regeneration, damage types**: materials and alignments are flags (a weapon can be cold iron and silver); the test
  matched only the exact value. Any one flag the regeneration names now counts, as in the game's own test.
- **Downed regenerating enemies** stay in the enemy list (§61); the fight-length estimate summed their HP below 0 and
  shortened the expected fight. HP counts from 0. Threat, attacks of opportunity and incoming danger already required an
  enemy that can act, or apply the disabled discount.
- **True Judgment, Master Hunter**: their hit puts a buff on the target that kills as it starts (`AddFactContextActions` →
  `ContextActionKill`, `AbilityProfile.KillsOnStart`); not read, so True Judgment stayed a guess although §62 listed it.
  Read as an on-hit kill now; such a buff is harmful wherever it is applied; an empty cooldown buff put on the target is
  no effect. `ContextCalculateAbilityParamsBasedOnClass` (the DC by class level) is bookkeeping.
- **First-hit-only damage on a buff spent by one attack** was divided by the hits per round twice. Once now.
- **Shield Wall** force-stacks in the game; read as a shield bonus it was cancelled by a shield already worn. Untyped now.
- **Checked and correct**: Quicken by the game's rule (`AbilityData.GetDefaultActionType` makes a quickened spell
  swift, `RuntimeActionType` falls back to standard when the swift is spent, `UnitPartSpecialMetamagic` includes
  next-spell metamagic), next-spell boosters skipped by the pre-combat buff routine, shadow copies' disbelief rule
  (`MechanicsContext.TryAffectByShadow`), Ray of Enfeeblement's shared value, the spellcasting gain only for casters.
- Doc comment of `CombatMath.Threat` restored (it had ended up on `ShadowShare`), and `ShadowShare`'s note on summons
  corrected (an even save: half real, half at the factor).

## 64. v0.3.21 (test13): the 0.3.21-test12 logs

- **Facts a buff takes from its caster** (`AddFactsFromCaster`): the game adds only the facts the caster has and the
  bearer lacks (`AddFactsFromCaster.OnActivate`). The reader counted every fact on the list: Camellia's Inspired Rage
  carried "hits hinder" and three unread ability components from rage powers she does not have, on every ally, in both
  logs. Each part now keeps the caster's fact (`BuffPart.CasterFact`) and counts only for a caster that has it, and so do
  its unread components (`BuffPartList.CasterUnread`, `UnreadFor`). The facts of a selection too (`FeatureFromSelection`:
  the Bladebound's black blade feats), which were skipped. A granted ability or toggle is something to use, not an effect
  on its bearer: no longer read as buff parts (`AddFacts` too). Same for the Tactician's teamwork feats (§62), the Judge's
  judgement aura, Battle Prowess, Sneaky Tricks; the offline check marks such parts "(caster's)". No verdict changed.
- **Checked, not changed**:
  - Aid Another against the Cultist Champion (the first real-time fight): the party's attacks hit it only on high rolls
    (0.4-1.6), so +2 for an ally's next attack or AC (one-round mark, 0.1 of the enemy's worth) came out ahead (0.9-2.0).
    That is the old mark rule (§3, §6), not one of the new readers.
  - Orvath's decisions took 250-370 ms in the fight with ten enemies (2-3 slow decisions in the earlier test logs, with
    five or six enemies): the cost grows with abilities × targets. Spells are still scored while the Standard action is
    on cooldown, on purpose: in real time an option usable "from here" keeps a unit from walking off while it waits.
  - The new readers in the logs: Grease (mean 17 real time, 14 turn-based) as before the difficult-terrain change, the
    Arcane Reservoir DC / caster-level boosts at 0.5-4.4 for the spell cast next, Inspired Rage 1.6-7.7.

## 65. Kineticist infusion value: a new, restricted slice on the restored v0.3.21 base

**Task plan and proposal assessment.** Branch `codex/kineticist-infusion-value` starts at development
`f51189fa93ffebc791489c6761e05ccbbdea0014`. Before edits, runtime and test sources matched stable tag
`v0.3.21` (`3d280bd3e3800f1db671112f1be1460ba05d7de5`); Info.json remains 0.3.21. This is new work after
the [withdrawal](V0_3_22_POSTMORTEM.md), with no cherry-picks or copied implementation from that attempt.
The author's intended next version is v0.3.22; this task does not publish or bump a release.

The supplied game Managed assemblies exist locally. Matching native method bodies can be inspected from those
assemblies; the Windows decompile directory in CLAUDE.md does not exist here. Supplied blueprint exports include
Bowling, Pushing, Burning and their referenced actions. No running Unity game or live Mono profiler is available.

Four proposal details need refinement before implementation:

- The stable AbilityProfile reader has no public action-list entry point. Its private walker cannot safely be
  reused on arbitrary infusion buffs without changing a shared reader, fabricating a blueprint or using reflection.
  **Support only one direct ContextActionCombatManeuver (Trip or BullRush) with no success actions.** Bowling and
  Pushing fit; Burning's carrier/activation-save/damage chain and Entangling's nested conditions do not. No second
  recursive walker, shared-reader edit, or name/GUID-specific exception is introduced.
- A correct maneuver chance needs the existing target-dependent attack-bonus/CMB/CMD calculation rules. It cannot
  be precomputed once per blueprint. Explicitly allow those calculations for supported active effects against an
  actual scored recipient, reusing the blast's already-calculated hit/resistance probability. There are **zero
  additional burn, gather, restriction, parameter, hit-chance or SR queries**. This is not advertised as arithmetic
  only, nor as a frame-time guarantee. The added rule-handler cost still needs live testing.
- PriceBurn runs after each ability has selected its best aim and after walking/casting risks were applied.
  Adding raw control there would miss target competition and over-credit risky delivery. Instead add the narrowly
  supported value in ScoreAbility's existing damage-recipient pass, separately from damage role weighting, before
  target selection and ordinary risk discounts. PriceBurn and AddKineticCandidates's control flow remain unchanged.
  Their existing BestBlast recovery estimate explicitly omits the new infusion contribution, since it may estimate
  switching that infusion off. It retains its stable estimate rather than crediting an effect it would remove.
- A source constant cannot equal the Git hash of the commit that contains that constant. The single startup
  identity line uses the module MVID; the PR records the source revision and DLL SHA-256. The initial proposal
  excluded packaging; the author subsequently requested a downloadable test ZIP. That packages the same verified
  DLL without a version bump or changes to loading/toggling behavior.

**Native shape and applicability.** The actual component is
`Kingmaker.UnitLogic.Mechanics.Components.AddKineticistInfusionDamageTrigger`, with `Actions.Actions`.
It handles damage using the **active infusion buff's** context. Its fields gate direct damage, weapon type,
descriptor and allowed ability/parent; it deduplicates recipients at the same game time. The restricted reader
requires direct damage and rejects weapon/descriptor filters and context-dependent maneuver variants. It requires
an on/started SubstanceInfusion toggle, a genuinely active matching buff owned by this caster, a supported action
and an allowed actual blast/parent. No context is manufactured and no action is executed while scoring.
The native CalculateCost -> CalculateBurnCost -> AddKineticistBurnModifier path already charges the infusion;
the new contribution is not gated on positive burn and adds no second burn penalty.

**Supported delivery.** Ordinary direct single-target blast ability candidates only. Point targets, areas,
chains, shadow spells, saves, weapon/blade delivery, units with the ArcherBlast mechanical flag, and blasts already containing a maneuver stay outside
this slice. Unsupported layouts contribute zero extra value; their existing blast damage and burn remain.
Optional ricochet components on otherwise ordinary projectiles are allowed, but no secondary recipient or bounce
receives added value. Hybrid target effects (buffs/heals/removals) are excluded so the existing target-effect gate
proves that a damage branch may run. Unknown guards retain the stable reader's existing uncertainty policy;
they are not asserted to be definitely true. No additional guard/context evaluation is introduced.
No hypothetical configuration is scored. Infusions which are off or not actually active contribute nothing.
Native maneuver immunities and the existing ManeuverStrength/ManeuverOdds scale remain authoritative. Trip must
also skip an already prone/getting-up recipient. Pushing retains the existing BullRush convention: only an enemy
engaging the party in TB has valued movement loss; RT pushes are not assigned an invented benefit.

**Gather Power trace: not offered.** Supplied GatherPower ability
`6dcbffb8012ba2a4cb4ac374a33e2d9a` has Hidden=true; AbilityDiscovery excludes hidden abilities. Its grant by
GatherPowerAbilitiesFeature does not bypass that filter. Gather mode activatables remain excluded by ModifierGroups.
No Gather ability/value change is needed on this evidence. This does not claim every third-party mod preserves it.

**Verification plan.** Keep shared readers, native math/cost sites, controllers, dispatch, toggle ownership and all
104 stable tests intact. Test the new pure on-hit control arithmetic; build Release against real supplied references.
Run ordinary ProfileAudit on identical baseline/current input, with no withdrawn kinetic checks: all verdict/detail
rows must match. Review the added runtime lines for any hooks, state mutation or extra burn/gather calls. A chosen
supported blast gets one [Kinetic] diagnostic with its infusion contribution, target and existing burn label.

**Gameplay gate, both modes.** No active infusion should behave as stable v0.3.21; compare cold and warm turns and
idle portrait selection with the same save/settings/mods. With Bowling/Pushing already active, compare eligible,
immune, prone/getting-up and non-applicable targets, including a zero-burn blast after native reductions. Confirm
the added control participates in target selection and pays normal movement/provoke/concentration risks. Check
AI toggle management off/on and end-of-fight ownership/restoration: this slice adds no toggle writer. Check a
non-kineticist and ordinary summons. Record the startup MVID and chosen [Kinetic] lines. Compilation and offline
reader parity are not in-game acceptance, command proof or measured FPS gains. No later roadmap slice belongs here.

**Completed checks (implementation `0a7ed97`).** All 110 pure tests passed (104 stable tests retained, six new
on-hit control arithmetic cases). Full Release build against the supplied real references: zero warnings/errors.
Protected shared readers, CombatMath, controllers/dispatch, toggle management, Info.json and tools are unchanged.
PriceBurn, BurnOf, BurnBlocked, TotalBurn and AddKineticCandidates's bodies match the restored baseline;
only BestBlast's call explicitly opts out of the added contribution. The diff adds no patch, fact substitute or
native burn/gather/restriction/parameter/hit/SR call site. Main's only change is the startup identity line.

The preserved current ProfileAudit cannot compile against the restored runtime: its ordinary audit and optional
check sources still reference removed types/members. It was not modified or described as passing. An isolated copy
of the actual **v0.3.21 tag's** ordinary ProfileAudit ran against both restored and new DLLs on identical input,
without the withdrawn feature checks. The final reports are byte-for-byte identical: **1,220 ability rows, zero
reader errors; 4,097 loaded blueprints and the same one failed AchaekekSacredWeaponFeature export** on both runs.
Coverage is PARTIAL; the old runner's zero exit status does not make that missing export complete coverage.

An additional local check called the production blueprint parser on the real loaded exports (no fake actor,
fact, context or rule): Bowling/Trip and Pushing/BullRush were accepted, Burning and Entangling rejected, and the
supported parse was reused. The actual readers' static delivery guards admit Earth/Air Blast and their Extended
Range variants. Bowling's allowed family matches Earth and rejects Air; Pushing matches both; both reject Fire.
Thus the conservative guards do not silently exclude those shipped ordinary blast shapes. No diagnostic tool,
game DLL, rewritten dependency or blueprint export is committed or packaged.

Tested DLL SHA-256: `cf0fb2a3746e1f27d782afd8d3b2bb0269d614d80ca58b677b8d48f93d202a66`.
Startup module MVID: `4abd89c4-7b64-4e6f-8ae5-5d96f61ed556`.
The author subsequently requested a UMM test archive: see [download and build identity](../test-builds/README.md).
This packages the same checked DLL. No stable release is published; packaging/documentation changes no runtime source.

**Still unverified:** initialized actors' active-buff/context ownership, actual CMB/CMD and third-party handlers,
command outcomes, idle selection/UI and combat frame times in Unity/Mono. The inherited ManeuverOdds reader still
has its existing 0.5 exception fallback; this task neither changes it nor claims exact parity when that path fails.
Unknown damage guards retain the stable reader's policy. The old recovery estimate remains conservative and
omits infusion value even for a quiver plan that might retain it. Duplicate same-type infusion effects receive at
most one contribution. Secondary ricochets and all unsupported action graphs remain unpriced. Keep the PR draft
for review and the targeted gameplay gate; lower timing medians or passing arithmetic tests are insufficient.

**Test[1] review:** [budget-laptop evidence, decision findings and diagnostic correction](INFUSION_VALUE_TEST1_REVIEW.md).
The author reports smooth overall play. The recorded state did not exercise Bowling/Pushing; supported-infusion
gameplay acceptance remains pending. The follow-up changes Progressive mode reporting only. Sleep's shared-HD
budget, path/boost feasibility, energy-specific protection and cone-recipient coverage remain separate open issues.

## 66. Test[1] follow-up: shared HD, feasible boosts, typed protection and cones

The author requested fixes for those four findings on a separate task branch.
`codex/test1-scoring-fixes` includes pending PR #6 as an explicit dependency;
main and shared development remain unchanged. This is an unmerged test, intended
for v0.3.22, retaining UMM version 0.3.21 until the coordinated release process.

Sleep now forecasts a consuming HD pool in native recipient order, with earlier
SR failures preserving budget and either save outcome consuming it. Static
shared-HD thresholds without a matching SubHD mutation keep their old scoring.
Deep Slumber's native spreading delivery is deliberately unsupported: its control
credit is withheld, with an `[HD]` explanation, rather than assuming Sleep order.

Selected main approaches are prepared before marks/boosts, using the dispatcher's
native path/reach/LoS rules, a three-check decision limit, and already-scored
fallbacks. Silent Progressive views allow escalation after a blocked lower tier.
UI previews do not pathfind. Inspection-mode attacks avoid animation setup;
temporary paths are released and actual commands still pass through `Run`.

Energy protection is priced against matching typed threat, with native pools and
current protection kept separate. Existing spell estimates and completed damage
events supply conservative packet sizes, avoiding weapon-hit frequency for spells.
Cone casts use native recipients at their actual aim/planned origin, including
allies, with the same set used for friendly-fire and Progressive/boss decisions.

The detailed [plan, review corrections, checks, limits and gameplay checklist](TEST1_SCORING_FIXES.md)
and [test download/identity](../test-builds/README.md) are the handover. Offline
checks do not establish live stability or laptop performance parity.
## 67. Intended v0.3.22: bounded substance-infusion selection and Test[2] fixes

Test[2] remained smooth overall on the author's budget laptop, but exposed the
previous slice's deliberate limitation: inactive infusions were never selected.
[Log evidence and corrections](INFUSION_SELECTION_TEST2_REVIEW.md) and
[complete implementation/support matrix](INFUSION_SELECTION_PLAN.md) distinguish
source inspection, offline checks and pending gameplay verification.

- Supported learned substance options now compete with retaining/off configurations
  and the best ordinary action. Their actual effect graphs, native buff DC/ranks,
  applicability, immunity, existing effects and marginal native burn cost determine
  value on the shared `ValueModel` scale. There is no fixed infusion priority.
- Eighteen of twenty supplied buff layouts are recognized: maneuvers, periodic
  damage, save-based debuffs, concealment removal, matching magnetic penalties,
  native SR/immunity/material/physical-DR effects, ranked Cluster Arrows and the
  conditional Vampiric healer discount. Unraveling and Chain Arrows are explicitly
  withheld from automatic selection because safe read-only valuation of their
  dispel/secondary-delivery paths was not established. Weapon/blade/shadow delivery
  and a new Gather/metakinesis/form optimization are outside this selector.
- Search: four reachable blast aims, eight complete recipients per aim, 24
  comparisons, 8 ms cooperative deadline. A native call can overrun it. This limit
  excludes ordinary decision work and appraisal of already-active effects; it is
  not a whole-decision latency guarantee. Ordinary blast damage/burn remains when
  an unsupported effect cannot be valued. Form variants retain ordinary scoring.
- No gameplay action executes during appraisal, no fake facts are installed and
  no new global/Harmony/UI hook exists. Native cost copies are reconstructed only
  through native constructor/reducer APIs for exact constant marginal modifiers.
  Unknown layouts fail closed. Actual post-switch cost/availability owns execution.
- **Activation ordering corrected in §68 after Test[3]:** the dispatcher requests
  native `IsOn` once and adopts the native-created command without reissuing it.
  The next decision waits for commands and requires the real owned `AppliedBuff`.
  Global management, whole-group player ownership, capacity and per-round locks
  apply. Duplicate entries do not multiply effects. Restoration stops alternatives
  before restoring original states and preserves player-owned groups.
- Test[2] fixes preserve a reached touch cast after its separate approach is
  cancelled, omit unrelated unavailable heals/buffs during cooldown while keeping
  future offensive/held-touch/spell-forbidding dependencies, and remove the advance
  value floor implicated in Lann's low-HP withdraw/return loop. No combat clock or
  animation setting is changed.

Verification: 154 pure tests pass; Release compiles against supplied real game
references without warnings/errors. A partial native-data run (4,097 loaded, one
known unrelated failed export) recognizes the eighteen layouts, verifies 486 native
cost/quiver vectors and bindings, and produces 1,220 ordinary reader rows with zero
reader errors, identical to the pending PR #7 baseline. These checks do not run
live actor activation, third-party handlers, commands or Unity/Mono frame profiling.
The draft task branch includes PR #7/#6 dependencies; main/development are unchanged.

## 68. Test[3]: native infusion commands, resistance evidence and RT walk diagnostics

[Full evidence, corrections and verification](TEST3_REVIEW.md). The separate native
CombatLog establishes actual misses, damage/resistance, opportunity hits, healing
and encounter numbering that the AI logs alone cannot supply.

Five TB Burning activations were selected and immediately interrupted. The §67
pre-queue assumption was wrong: native OnDidTurnOn's matching predicate is
Judgment-only. The dispatcher now requests IsOn once, adopts/tracks the native-created
current/queued command and never constructs/reissues a second activation. Native
DidAct, actual owned buff, management/ownership/group locks and restoration remain
in control. Validated resource-free switches use the existing costless Free bar;
ordinary Toggle/Swift thresholds are unchanged and future burn remains priced.

A fully resisted six-point hit recorded six absorbed against Fire Resistance 10.
That observation previously bypassed known native defence on later estimates.
For fully absorbed packets with positive reduction, AfterTargetReduction keeps
observed factors/custom reduction and applies the native remaining-damage bound,
without subtracting both reductions. Successful-hit observations retain their old
authority, including evidence of caster-specific bypass. The initial
pre-observation discrepancy is still unverified; no complete damage-model parity
is claimed.

Encounter #10's Seelah issue includes a completed 13.5m attack, ally-heal detours,
opportunity damage and mod-requested emergency interruption. Continuous target-score
thrashing is not established. The author approved diagnostics only: chosen RT walk
score versus in-reach alternative, existing movement risks and the existing native
path query's result/length. No added pathfinding, target lock, movement score or
cancellation-policy change. Replay before proposing a behavioral remedy.

154 pure tests pass; Release has zero warnings/errors. Actual native metadata
checks expose the Judgment-only predicate for all 20 supplied substances; compiled
route checks guard against manual construction/reissue, and candidate fixtures
check Free/Swift thresholds. Existing cost/layout/cone checks and 1,220 ordinary
reader rows pass (partial exports: 4,097 loaded, one failed). These are not live
actor command, restoration, first-hit damage or Unity/Mono performance tests.

## 69. Test[4]: proven Burning execution, committed-approach spell safety and timings

[Full evidence, changes, limitations and replay checklist](TEST4_REVIEW.md).
All eight logged native infusion activations succeed across TB/RT. CombatLog
confirms Burning DC19 and a recurring six-fire tick. Accepted burn, stop recovery
and original-off restoration are exercised. Only Burning is gameplay-demonstrated;
learned Bowling/Pushing are not thereby applicable to Fire Blast. Preserve valid
Sleep centres benefiting other recipients and partial cures that accumulate
toward revival; their labels alone do not establish broken decisions.

RT7 exposes a coordination gap: Seelah starts an elemental approach before allied
Grease lands, then fails the native Grease save. The author approved a narrow
spell-safety guard, keeping movement/target commitment/navigation/cancellation
policy unchanged. With AvoidFriendlyFire enabled, new harmful lasting control
circles are withheld when a registered unfinished ordinary attack approach proves
the ally's entire native arrival/body disk lies strictly inside. The guard reuses
existing Selective, side, condition and immunity rules, inspects bounded current
command slots and performs no pathfinding or gameplay actions. It initially
supports a single direct fixed native cylindrical spawn and equal destination/
centre elevation. Unknown shapes, slopes, mere overlap, boundaries and many
large-target/ranged approaches retain existing appraisal. No specific live veto
outcome is proven by the supplied positions-free logs or pure geometry tests.

Spell-boost descriptions are reused only within one coupling pass, by actual
ability identity. Identical periodic reduction reads are reused only within one
infusion-effect appraisal, keyed by action/context/recipient identities and
freshly calculated amount. Native contexts/ranks, dice, saves, survival, durations
and existing limits remain; no score weights or decision scope are cut. These
small deduplications do not establish a large performance improvement.

Both Test[3] and Test[4] ran on the same budget laptop; the author reports broader
Test[4] slowdown, much milder than the withdrawn preview. Cold profile work, warm
control/boost appraisal and collection-overlap pauses are visible, without enough
stage attribution to name a sole cause. Existing >250ms logs now show scalar stage
times and nested active-infusion time; expensive TB stand-up previews are logged
separately from ordinary decision summaries. No per-frame trace or animation-speed
change is added.

The initial optimistic Fire Blast/resistance estimate remains unresolved. Native
inspection rejects missing chunk initialization as its explanation. Failed native
reduction reads now produce a bounded diagnostic while preserving the old fallback;
the earlier censored-observation fix remains. No damage-math parity claim is made.

164 pure tests pass and Release builds against supplied game references with zero
warnings/errors. Compiled memo identity, Grease metadata, existing activation/cost/
cone checks and all 1,220 ordinary reader rows pass; the partial export corpus
retains one known unrelated failed file. Live guard effectiveness and Unity/Mono
performance need replay.
The isolated task includes PR #9/#8/#7/#6 dependencies; it does not update main or
shared development and retains release metadata 0.3.21 for intended v0.3.22.

## 70. Test[5]: concentration outcome correction and bounded control/energy evidence

[Full review, evidence, limits and replay checklist](TEST5_REVIEW.md). The author
reports 8/10 smoothness with definite improvement over Test[4]. Ordinary samples
are mostly lower, but encounter/state/work differences prevent a controlled
speedup claim. Seven warm long decisions overlap collection in weapons/modes or
ordinary ability work; active infusion/search does not dominate. Preserve current
local reuse and decision scope; no further performance pruning is introduced.

A confirmed outcome-tracking defect treated visible native concentration failures
as silent/no-reach failures: Seelah's failed Cure and Orvath's failed Sleep each
received a strike despite native CombatLog explaining the failed roll. The
existing CastOutcome subscriber now observes failed RuleCheckConcentration for
the matching pending caster/root/touch/ConvertedFrom spell, records native refusal
evidence and disables target-reach judgment without claiming an effect reached.
Successful/unrelated checks and genuine silent-effect safety remain unchanged.
No rule is triggered and no casting-risk, command, resource or movement policy
changes. Live settlement/event ordering still requires replay.

The reported two Quasit Sleeps in TB7 are real; both saves pass. All ordinary
centres are considered and shared-HD recipients are weighted by native eligibility,
chance, threat/state and existing risks. Missing competing-centre values/positions
prevent proving that a visible human cluster had greater legal expected value.
Multi-target and useful immune-centred Sleep choices remain demonstrably present.
No cluster bonus, weights, minimum-recipient rule or save-based blacklist is added.
A selected-action `[HD-Aim]` diagnostic samples twelve already-appraised centres,
existing reasons/risks and up to three existing HD-eligible IDs per centre. It
adds no appraisal/query; capture stays off for previews/counterfactuals and cones.
Its cap limits only diagnostics, never scoring, and its labels distinguish HD
eligibility from positive benefit and pre-policy rows from the selected value.

Five Burning activations succeed, native periodic damage is visible, and burn
recovery/restoration remain exercised. Optimistic Fire Blast/resistance estimates
persist without a native-query exception; actual criticals sometimes penetrate.
No new damage formula or blanket blacklist is justified. A 24-row/fight ID-keyed
`[Math] energy estimate` trace exposes existing raw/native/observed/final values
and return routes without new defence queries or changed returns. Prior censored-
observation and successful-hit bypass behavior remain; the root discrepancy is
not claimed fixed. Samples can be exhausted before every later case is recorded.

Six committed-approach Grease vetoes confirm the §69 gates are exercised, without
proving six prevented knockdowns. Existing Selective protection goes through the
possibly mod-patched native area check; genuinely protected allies bypass the
new veto. No broader movement/commitment/navigation/cancellation change is made.
The second-blast report was corrected by the author: its external grant failed
and manual casting was unavailable too. No discovery change is warranted.

164 existing pure tests pass; Release has zero warnings/errors. Retained native
activation/cost/memo/cone checks and all 1,220 ordinary reader rows pass, with the
same partial export coverage. Compiled observer/Selective/HD diagnostic checks
pass, as do detached native Grease Selective off/on/off assertions; the energy
trace has an independent source review. These do not run live actors, AutoMetamagic
or Tabletop Tweaks handlers. No new build has been run in game. The isolated branch
includes PR #10/#9/#8/#7/#6 and leaves main/development unchanged, retaining release
metadata 0.3.21 for intended v0.3.22.

## 71. Released v0.3.22 logs: optional TTT reduction provider and reach evidence

[Full evidence, compatibility limits and replay checklist](RELEASE_0322_LOG_REVIEW.md).
The new energy sample distinguishes the previous mystery:353 of356 rows use the
missing-vanilla-part fallback, while native Quasit damage explicitly meets Fire
Resistance10. Raw blast7.50 and literal Burning3.50 are plausible; no native
estimate/exception route appears in those samples.

TTT's DR rework replaces native resistance components and registers a separate
TTUnitPartDamageReduction. The supplied Core0.7.15 binary confirms the expected
part type, registration, lazy initialization, immunity and preview API. The new
optional adapter queries an already installed TT part only when the vanilla part
is absent. It uses native Get, the provider's own initialization/IsImmune/
EstimateDamage, and the existing observed-factor/minimum-remaining bound. It
adds no hard TTT dependency, installed fact/part, damage application, pool spend,
manual chunk reset, copied game table or Rulebook trigger. Only API metadata and
delegates are cached; no actor/context/result survives an appraisal. Unknown or
failed optional support retains the existing fallback with bounded diagnostics.

This repairs the missing-provider compatibility path, not every damage-model
case. TTT's preview itself is approximate; mixed vanilla+TTT providers retain
the vanilla route rather than an invented stacking rule. Initialized actors,
actual settings/custom handlers, pools and live first-hit values remain replay
checks. The supplied Core/game DLLs are not committed or packaged.

Native combat13 is RT5. Hand of Hosilla's actual glaive attacks total91 damage;
companions use utilities/heals and Camellia does attempt an80%-stop advance into
Grease, so the event is not simply idle actors or absent pathfinding. The source
lacks a specific shorter-reach response at a hostile-area edge. A lure/back-off
would need honest future-access value, native reach/intent and explicit anti-loop
scope; stepping outside present reach cannot erase all potential incoming threat.
The author approved **diagnostics only**. No movement score, back-off candidate,
retreat threshold, target lock, navigation or cancellation change is made.
The new `[Reach]` row prints already-scored action/risk/area/retreat data and,
where available, a current accepted foe UnitAttack's native approach radius and
distance-only check. Up to eight command slots and one row per actor/round are
sampled; weak TurnMemory keys retain scalar bookkeeping only. No new forecast,
LoS/path query, idle/UI hook or candidate mutation is introduced.

New HD rows show legal range exclusions and a group Sleep46.2 beating Quasit12.6;
no new targeting defect or weight change is justified. Six Burning activations
succeed, healing is real, and no false silent strike appears. Native concentration
checks both succeed, leaving the failed-event branch unexercised here. Other-mod
blueprint warnings and ordinary acted TB interruptions are not AI exceptions.

Sampled means are39.10ms TB/48.59ms RT; no current smoothness rating or controlled
benchmark is available. Warm slow stages are ordinary abilities, not infusion
work. Preserve decision quality and budgets. Integrated Release has zero
warnings/errors and all 164 unit tests pass. Actual Core binding, absent/mismatched
API fallbacks, detached empty-provider previews, compiled/native boundaries and
retained infusion/cone checks pass. All 1,220 ordinary reader details remain
unchanged (4,097 loaded exports/one known failure, partial coverage). No live
new-adapter or reach-diagnostic result is claimed. The isolated task retains0.3.22 metadata and the published ZIP/tag;
it does not update main, publish or automatically merge development.
