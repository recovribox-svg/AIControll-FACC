# Turn-based tactical AI (v0.3.0)

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
4. **Learning within the turn.** An option that could not be issued, or that ended without acting, is not retried in
   the same turn.

## 3. Values

**Damage value** of `d` expected damage on enemy `e`: `min(d, HP)`, plus the enemy's threat if `d ≥ HP` (a likely
kill prevents its future damage), plus some threat-weighted progress otherwise. Threat ≈ `4 + 3·CR`. It is scaled down
for enemies that are asleep, paralysed or helpless, and up for enemies currently in melee with the party.

| Option | How its value is computed |
|---|---|
| **Weapon attack** (single, move + attack, or full attack) | Attack list from the game (`UnitAttack{ForceFullAttack, OnlyForInspect}.Init`, as the unit-description UI does). For each attack: hit chance from `RuleCalculateAttackBonus` vs `RuleCalculateAC`, damage from `RuleCalculateWeaponStats` dice, minus damage reduction, plus crit and sneak attack. A ranged attack while threatened subtracts an attack-of-opportunity penalty. Full vs single attack is set explicitly; otherwise the game uses the UI smart-cursor mode. |
| **Damage spell / ability / bomb** | Dice evaluated in the ability's own context (`RecalculateRanks`), × spell-resistance pass × attack-roll hit chance (touch spells) × failed-save chance (half on save if applicable), for every enemy in the area. Hitting allies costs twice the damage; if *Avoid friendly fire* is on, the option is dropped instead. Immune targets count as 0. |
| **Heal** | Only for allies below the heal threshold (potions: *Drink healing potions below HP %* setting). Value = `min(heal, missing HP) × 0.6 × urgency` (×3 below 25 % HP, ×2 below 50 %), more if the ally is in melee. Never used on undead. |
| **Control / debuff** | Failed-save chance × SR pass × target threat × strength (hard control such as sleep, stun or hold: 1.0; soft: 0.4; plain debuff: 0.25; "mark" such as Smite: 0.15) × rounds. Skipped if the target already has the effect or is immune (`HasConditionImmunity`, spell immunity). Non-control debuffs fall off after two uses per combat. |
| **Buff** | Skipped if the target already has it or it was cast on them this round. `(1 + 1.5·spell level) × rounds`. Weighted down after round 2, in easy fights, and for allies who aren't fighting. |
| **Enchantments, summons, dispels, utility** (effects that can't be verified on the target) | Once per combat per target. Utility abilities such as Appraise Foe are worth 1 point, so they only happen when nothing useful is possible. |
| **Charge** | Only when a normal move can't reach the target this turn: ~1.1 × single-attack value. |
| **5-foot step out of melee** | Ranged characters and casters in melee: 0.4 × threat of the enemies in melee with them. Free; it keeps the full attack available. |
| **Withdraw** | Below 30 % HP and in melee, when nothing better exists. |
| **Advance** | When nothing can be attacked from here. Melee advances to reach; ranged characters advance to ~85 % of weapon range with line of sight; casters without a ranged weapon stop at ~25 ft. |

**Weights.**
- **Roles:** DPS ×1.1 damage, ×0.7 buffs. Support ×1.6 heals, ×1.5 buffs, ×0.85 damage. Tank ×1.25 against enemies already in melee with allies.
- **Range preference:** Ranged ×1.1 for ranged attacks and ×0.7 for melee; Melee is the reverse.
- **Spell slots:** levelled spells count for less in easy fights, so weapons and cantrips are preferred there.
- **Consumables:** scrolls and wands ×0.7 × Use Magic Device success chance. Thrown and alchemical items ×0.85.
  Buff potions only in hard fights, at ×0.5. Healing potions only by urgency.

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
