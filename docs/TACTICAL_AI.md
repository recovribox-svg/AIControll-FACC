# Turn-based tactical AI (v0.3.0, revised in v0.3.1)

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
4. **Learning within the turn.** An option that could not be issued, that ended without acting, or that ended while
   the action it was chosen for (standard, full-round, swift) is still unused, is not retried in the same turn.
5. **Waiting.** The controller waits for the command while it makes progress (the unit moves or the command is
   executing). It gives up after 4 s without progress or 15 s in total. An idle command the AI did not issue (left over
   from real time or an out-of-combat routine) is interrupted after 1 s, except the game's own ability toggles and
   attacks of opportunity.

## 3. Values

**Damage value** of `d` expected damage on enemy `e`: `min(d, HP)`, plus the enemy's threat if `d ≥ HP` (a likely
kill prevents its future damage), plus some threat-weighted progress otherwise. Threat ≈ `4 + 3·CR`. It is scaled down
for enemies that are asleep, paralysed or helpless, and up for enemies currently in melee with the party.

| Option | How its value is computed |
|---|---|
| **Weapon attack** (single, move + attack, or full attack) | Attack list from the game (`UnitAttack{ForceFullAttack, OnlyForInspect}.Init`, as the unit-description UI does). For each attack: hit chance from `RuleCalculateAttackBonus` vs `RuleCalculateAC`, damage from `RuleCalculateWeaponStats` dice, minus damage reduction, plus crit and sneak attack. A ranged attack while threatened subtracts an attack-of-opportunity penalty. Full vs single attack is set explicitly; otherwise the game uses the UI smart-cursor mode. |
| **Damage spell / ability / bomb** | Dice evaluated in the ability's own context (`RecalculateRanks`), × spell-resistance pass × attack-roll hit chance (touch spells) × failed-save chance (half on save if applicable), for every enemy in the area. Hitting allies costs twice the damage; if *Avoid friendly fire* is on, the option is dropped instead. Immune targets count as 0. |
| **Heal** | Only for allies below the heal threshold (potions: *Drink healing potions below HP %* setting). Value = `min(heal, missing HP) × 0.6 × urgency` (×3 below 25 % HP, ×2 below 50 %), more if the ally is in melee. Undead only when the ability's own branch heals them (Inflict Wounds). Never aimed at an enemy. Buffs that come with a heal (Lay On Hands) add nothing on their own. |
| **Control / debuff** | Failed-save chance × SR pass × target threat × strength (hard control such as sleep, stun or hold: 1.0; soft: 0.4; plain debuff: 0.25; "mark" such as Aid Another: 0.1, halved if no other party member is fighting that enemy) × rounds (1 for one-round effects, else up to 3). Skipped if the target already has the effect or is immune (`HasConditionImmunity`, spell immunity). Non-control debuffs fall off after two uses per combat. |
| **Buff** | Skipped if the target already has it or it was cast on them this round. `(1 + 1.5·spell level) × rounds`. Weighted down after round 2, in easy fights, and for allies who aren't fighting. Buffs spent by the next attack or lasting one round (Perfect Strike) only on oneself, and only while an attack is still possible this turn. |
| **Cleanse** (removes buffs by descriptor or a specific buff, no damage or heal) | Only if the target has something it removes: `(2 + 2·spell level) × rounds`. A descriptor cleanse's own buffs add nothing on their own. |
| **Enchantments, summons, dispels, utility** (effects that can't be verified on the target) | Once per combat per target. Utility abilities such as Appraise Foe are worth 1 point, so they only happen when nothing useful is possible. Not for abilities with a verifiable effect (damage, heal, cleanse) that is worth nothing right now, and dispel/utility never for consumables. |
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

**Conditional effects.** Many abilities branch on the target: Cure Wounds heals the living and harms undead, Inflict
Wounds the reverse, Lay On Hands harms undead. `AbilityProfile` records which `Conditional` branch each damage, heal
and buff sits in and evaluates it per target, in the ability's own execution context. Only conditions that read facts
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
| Albedo `Move+Attack->Dretch` 4× with `forcedPath=0.7m`, `Success acted=True`, standard action still unused | Recent 208-222 | The game force-finishes a turn-based command whose movement ran out as "acted" without attacking. | A command that ends with its standard/full-round/swift action still unused counts as failed for this turn. |
| `Camellia: command wait timeout - ending turn` while her 7.6 m walk was still running (4.3 s) | 64 | Timeout of 300 *frames*, about 4.3 s at 70 FPS. | Time-based wait that resets on progress (4 s idle, 15 s cap). |
| Camellia's turn lost to a leftover command at turn start | Recent 204 | A command not issued this turn sat idle in her slot. | Interrupted after 1 s idle (the game's own toggles and attacks of opportunity excepted). |
| Lann: `Aid Another->Cultist Champion(mark)=7.8` beat his attacks; walked 9.1 m, interrupted | 570-577 | A +2 for one round was valued like a 3-round mark at 0.15 × threat. | Marks 0.1 × threat, one-round effects 1 round, halved if nobody else fights that enemy; archers walking up to touch an enemy ×0.5. |
| `Perfect Strike->Lann(buff)` cast after the full attack (5×) | 91, 171, 222, 284, 630 | A buff for the next attack was valued like a 3-round buff. | Next-attack / one-round buffs (`RemoveBuffOnAttack`, duration ≤ 1 round) need an attack left this turn. |
| `Treat Affliction->Albedo(buff)` with nothing to treat | 80 | Its applied buff counted as a buff. | Descriptor cleanses are valued by what they remove. This works only if the blueprint removes by descriptor; the next log will show. |
