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

From the test4 logs (turn-based 67e90edc, real time 87aa4bc7; plan: `docs/PLAN_0.3.14_test4_TB.md`).

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
