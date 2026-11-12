# Eldritch Archer damage-sample identity

The shifter1 mixed-mode playtest review found a misleading comparison at AI log line 2959:
`Drow Magus's Scorching Ray: 2 hits dealt 33.5 a hit, estimated 15`.
The original inputs and their checksums are recorded in
[the broad review](SHIFTER1_PLAYTEST_REVIEW.md) and [its manifest](competence/SHIFTER1_PLAYTEST_INPUTS.json).
This focused task starts at PR #43 `d144b80`, including refreshed development `1f58150`.

The supplied native game assembly proves that Eldritch Archer delivers a spell using the first arrow's
`RuleAttackRoll`. All rays can therefore share the same roll object. `DamageObserver.NoteEstimate` groups
projectile packets by that object before comparing the group with a one-projectile estimate. The identity
distinguishes ordinary independent ray rolls, but cannot distinguish separate rays of this weapon-delivered
spell from multiple damage actions of one ray. Dividing by a guessed ray count would misattribute packets.

The sampler now withholds this ambiguous case: an actual projectile **Spell**, an actual Eldritch Archer
actor, and a physical ranged weapon on the event's attack roll. The event remains identifiable after the
native pending payload has cleared; neither the pending spell nor the current Spellstrike switch is used.
Ordinary independent projectile casts and spell-like kinetic abilities retain their existing sampling.
The conservative exclusion also covers a single weapon-delivered ray: the packet stream does not supply
the general ray identity needed to prove it safe.

Damage and resistance observations run before this gate and remain intact. The correction changes neither
native damage nor spell/weapon valuation. The non-kinetic comparison is diagnostic; no new damage multiplier
is learned. The playtest also contains vulnerability and other packet effects, so this repair does not claim
that every difference between 33.5 and 15 was caused by the grouping bug.

Native evidence is the supplied `Assembly-CSharp.dll`, SHA-256
`2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`, MVID
`90a9869c-2792-4c7b-bfb7-5a8b33da7c82`. Existing Eldritch Archer checks inspect launch/cleanup and first-arrow
semantics. Four additional compiled-route checks cover this gate, its event identity and defense-observation
ordering; eight unit cases cover the three admission boundaries. These are offline checks, without live actors,
casts, clocks or Unity execution.

Replay both combat modes with ranged Spellstrike Scorching Ray, an ordinary separately rolled ray cast,
a missed delivery arrow and a kinetic spell-like projectile. Weapon-delivered rays should no longer produce
the ambiguous per-ray comparison; actual damage and learned target defenses must still be observed. This
adds only constant-time event checks and no target scan, path query, persistent cache or extra forecast.
Movement is a separate author-approved follow-up. No main/development push, merge, release or version bump.
