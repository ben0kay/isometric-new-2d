# Fractured Horizons — Combat Architecture Plan & Future Work Audit

> **Status:** PLANNED / NOT IMPLEMENTED
>
> **Purpose:** Hand-off plan for a future Codex implementation. This document records the
> architecture agreed in discussion; it is **not** evidence that the described new
> classes, resources, scenes, or mechanics already exist.
>
> **Baseline reviewed:** `main` at `7e348493db8c16876fa6fbc638243d7bae661126` (2026-10-10).
> **Read the current branch and relevant READMEs again before coding.**
>
> **Execution rule:** Do not implement the entire plan in one large refactor.
> Work in small, independently testable passes, obtain approval on the open gameplay
> decisions below, and preserve existing working systems and saves.

## 1. Objectives

Create **one reusable combat pipeline** shared by the player, wildlife, robots,
bosses, structures, and potential later vehicles. Its attack delivery and
feedback systems are independent and composable.

- Optional defence pools in this order: **Shield → Armour → Health/Hull**.
  Health and Hull are **the same underlying core vitality**, with only different
  labels and presentation. Core vitality is mandatory; Shield and Armour are optional.
  A dinosaur usually has only Health; a robot could have Shield, Armour and Hull.
- Distinct **damage channels** (Kinetic, Energy, Explosive, Electric, Thermal,
  Corrosive, Neutral; potential Toxic in future). Melee is a *delivery*, not a
  damage channel. Preserve existing enum numeric values for saved `.tres` resources.
- Separate **damage amounts/types**, **physical impact/knockback**,
  **ongoing status effects**, and **audio/visual feedback**. None should infer
  its full behaviour from a mandatory Light/Regular/Heavy projectile enum.
- Deliveries: melee/contact, projectile, beam, area, deployable. Mining/digging
  remain specialized tools and must continue working.
- Instant-radius damage, **expanding one-hit shockwave fronts**, and later
  timed/persistent hazard fields.
- Homing projectiles; target penetration with per-target falloff; distinct
  armour penetration and cover penetration; optional explosions and split shots.
- Instant or sustained **damage-capable beams** with width, blocking rules,
  multiple target piercing, per-target falloff, and damage-per-second / pulse
  intervals when a moving target crosses the beam.
- Mines/deployables with arming delay, **proximity**, **timer**, **contact**,
  and future remote triggers; the trigger selects *when* to detonate, whereas
  the shared area system determines *what* the detonation damages.
- Future status effects: Poison, Corrosion, Shock/Disruption, Stun, Burn,
  Slow and short Stagger; stacking limits and target-specific resistance.
- Explicit handling of world layers, terrain elevation, cover, friendly fire,
  streaming/unloading, pooled projectile reuse, and save compatibility.

## 2. Existing code — preserve and extend

The following files existed at baseline; their exact APIs must be rechecked
before starting any phase.

| Existing file(s) | Current job / migration direction |
|---|---|
| `COMBAT/DamageTypes.cs` | `DamageType` enum and `VitalityKind` (Health/Hull). Keep serialization-compatible. |
| `COMBAT/Health.cs` | Current single core vitality, damage immunity, damage/heal and death signals. Keep legacy `Damage(...)` entry points working. |
| `COMBAT/DefenseDefinition.cs` | Single defence profile with type multipliers and core vitality label. Adapt/migrate; **do not create two conflicting resistance authorities**. |
| `COMBAT/CombatLife.cs` | Death/respawn and existing hit flash/HUD; must not regress. |
| `COMBAT/Attacks/AttackDefinition.cs` | Attack delivery abstraction, cooldown, muzzle offsets. |
| `COMBAT/Attacks/ProjectileAttack.cs` | Current projectile damage, type, speed, lifetime, tint, spread/cone/ring count. |
| `COMBAT/Attacks/ContactAttack.cs` | Legacy contact attack. |
| `COMBAT/Attacks/MiningAttack.cs`, `DiggingAttack.cs` | Noncombat tools; preserve. |
| `COMBAT/Projectiles/Projectile.cs` | Straight-moving pooled shots, swept actor hitbox ray and terrain/cover test. Currently stops after first collision. |
| `COMBAT/Projectiles/ProjectilePool.cs` | Prewarm and fixed capacity; keep inactive shots physics-disabled and reset every launch attribute on recycle. |
| `COMBAT/Hitboxes/CombatHitbox.cs`, `CombatHitboxDefinition.cs` | Visible actor hitboxes positioned with terrain-adjusted artwork. |
| `COMBAT/Cover/CombatCover.cs` | Cover tests and height-based obstruction. Reuse in ray/beam and explosion rules. |
| `COMBAT/Weapons/Weapon.cs`, `MiningEmitter.cs` | Weapon delivery/cooldown/aim; mining beam is **not** a combat laser-beam damage system. |
| `ENTITIES/Combat/*` | Enemy AI, targeting, chase/melee/ranged decisions. Remains under `ENTITIES/`; new generic damage mechanics belong under `COMBAT/`. |
| `ENTITIES/Core/EntityDefinition.cs` | MaxHealth and optional Defense; future reference to layered defence and status resistances. |
| `SYSTEMS/Saving/EntitySaves.cs`, `PlayerSaveSections.cs`, `CampaignData.cs` | Currently persist core health; require a compatible extension for new defence pools. |
| `VISUALS/Feedback/*` | Health bars, damage numbers, camera shake. Keep rendering independent of combat outcomes. |
| `AUDIO/AudioManager.cs`, `AudioCatalog.tres` | Existing audio catalog, positional playback, cooldowns and pooling; reuse. |

**Important current limitations:** `Health.DamageApplied(int)` communicates only
actual **core Health/Hull loss**; it cannot describe a shield-only hit. Player
movement and `EntityMotor` both assign velocity each update, so a new
knockback mechanism cannot simply write `Velocity` once and expect it to
persist. Entity saves presently contain only a single health integer. These
are deliberate migration checkpoints.

## 3. Proposed target folder structure

The entries below are a **design map**. `[E]` means existing now; `[P]`
means planned. Names can be consolidated if implementation finds adjacent
single-responsibility files unnecessarily fragmented, but preserve the
boundaries between the subsystems.

```text
COMBAT/
├── COMBAT_ARCHITECTURE_PLAN.md           [E: this document]
├── README.md                            [P: runtime documentation]
├── DamageTypes.cs                       [E]
├── Health.cs                            [E: shared Health/Hull]
├── DefenseDefinition.cs                 [E: migrate/adapt safely]
├── CombatLife.cs                        [E]
│
├── Damage/
│   ├── DamageProfile.cs                 [P: reusable attack configuration]
│   ├── DamagePacket.cs                  [P: one resolved incoming hit context]
│   ├── DamageResult.cs                  [P: actual outcomes per defence layer]
│   └── DamageResolver.cs                [P: canonical damage path]
│
├── Defenses/
│   ├── DefenseController.cs             [P: runtime Shield/Armour/Core state]
│   ├── DefenseLayerDefinition.cs        [P: optional pool settings]
│   ├── DefenseState.cs                  [P: per-instance mutable values]
│   └── ResistanceProfile.cs             [P: only if needed; no duplication]
│
├── Impacts/
│   ├── ImpactProfile.cs                 [P: recoil, knockback, stagger, cues]
│   ├── KnockbackController.cs           [P: integrates with movement]
│   └── Presets/
│       ├── Light.tres                   [P]
│       ├── Regular.tres                 [P]
│       └── Heavy.tres                   [P]
│
├── StatusEffects/
│   ├── StatusEffectDefinition.cs        [P]
│   ├── StatusEffectController.cs        [P]
│   ├── StatusResistanceProfile.cs       [P]
│   └── Presets/
│       ├── Poison.tres                  [P]
│       ├── Corrosion.tres               [P]
│       ├── Shock.tres                   [P]
│       ├── Stun.tres                    [P]
│       ├── Burn.tres                    [P]
│       └── Slow.tres                    [P]
│
├── Attacks/
│   ├── AttackDefinition.cs              [E]
│   ├── ProjectileAttack.cs              [E]
│   ├── ContactAttack.cs                 [E]
│   ├── MiningAttack.cs                  [E]
│   ├── DiggingAttack.cs                 [E]
│   ├── BeamAttack.cs                    [P: delivery adapter]
│   ├── AreaAttack.cs                    [P: delivery adapter]
│   └── DeployableAttack.cs              [P: delivery adapter]
│
├── Projectiles/
│   ├── Projectile.cs                    [E]
│   ├── Projectile.tscn                  [E]
│   ├── ProjectilePool.cs                [E]
│   ├── ProjectileDefinition.cs          [P: visual/flight/optional behaviours]
│   └── Guidance/
│       └── HomingGuidance.cs            [P]
│
├── Beams/
│   ├── BeamDefinition.cs                [P: width/range/tracking/tick/pierce]
│   ├── BeamController.cs                [P: sustained or pulsed beam lifetime]
│   ├── BeamCollision.cs                 [P: ordered line/width target queries]
│   └── BeamVisual.tscn                  [P: presentation; optional pooled visuals]
│
├── Penetration/
│   ├── PenetrationProfile.cs            [P: target/armour/cover policies]
│   └── PenetrationResolver.cs           [P: per-contact budget/falloff]
│
├── Areas/
│   ├── AreaDamageDefinition.cs          [P]
│   ├── AreaDamageResolver.cs            [P: filtering, distance, cover, damage]
│   ├── ShockwaveController.cs           [P: outward moving damaging ring]
│   └── HazardFieldController.cs         [P: future periodic hazard area]
│
├── Deployables/
│   ├── DeployableDefinition.cs          [P: placement/ownership/lifetime]
│   ├── DeployableController.cs          [P: arming + shared trigger state]
│   ├── Triggers/
│   │   ├── ProximityTrigger.cs          [P]
│   │   ├── TimedTrigger.cs              [P]
│   │   └── ContactTrigger.cs            [P]
│   └── Mines/
│       ├── MineDefinition.cs            [P]
│       ├── MineController.cs            [P]
│       └── Mine.tscn                    [P]
│
├── Hitboxes/
│   ├── CombatHitbox.cs                  [E]
│   └── CombatHitboxDefinition.cs        [E]
├── Cover/
│   └── CombatCover.cs                   [E]
├── Weapons/
│   ├── Weapon.cs                        [E]
│   └── MiningEmitter.cs                 [E]
└── Debug/
    └── CombatDebug.cs                   [P: gated inspector/diagnostics]
```

Other projects remain authoritative for their own concerns:

- `ENTITIES/Species/**`: species-specific attacks, defence profile references,
  AI preferences, and optional audio/profile IDs, **not copies of shared logic**.
- `AUDIO/Sounds/Combat/**`: actual audio clips and AudioDefinition `.tres`.
  Keep unique species vocalizations under `AUDIO/Sounds/Entities/**`.
- `VISUALS/Feedback/**`: sparks, shield-break effects, beam appearance,
  damage-number styles and camera shake. Combat emits data/events; presentation
  subscribes. A beam's gameplay owner remains in `COMBAT/Beams/`.
- `PLAYER/Equipment/Weapons/**`: player weapon/item `.tres` references;
  `ENTITIES/Species/**` owns enemy-specific attack resources.

## 4. Behaviour contracts and important separations

### Damage pipeline and result

`AttackDefinition / Area / Projectile / Beam / Mine`
→ build an **attack payload**
→ `DamageResolver`
→ `DefenseController` (present Shield → Armour → core Health/Hull)
→ structured `DamageResult`
→ post-hit Impact, Status and Audio/Visual observers.

- `DamageProfile` is immutable Godot resource tuning, **not** mutable per-hit
  runtime state. `DamagePacket` captures amount/type, source/owner/team,
  contact point and direction, world layer, and references to any allowed
  impact/status application. Avoid holding stale source-node references.
- `DamageResult` should distinguish **miss**, **blocked by cover/team/layer**,
  **immunity**, **fully absorbed**, **actual per-pool depletion**, **shield break**,
  **armour break**, **core vitality loss**, **kill**, and optional effect outcomes.
  Do not conflate raw input damage with actual HP removed.
- Preserve the old `Health.Damage(...)`, `DamageEnvironment(...)`, signals and
  legacy source attribution behind migration adapters. Do not double-apply
  damage or emit duplicate death/hit events.
- Multipliers can be configured **per defence layer and damage type**, with
  separate status immunities. The current `DefenseDefinition` already resolves
  typed damage: migrate this to one clear authority instead of retaining two.
- Shield/Armour runtime values are **per actor instance**, never mutable on a
  shared `.tres`. Disabled/absent optional layers are skipped.
- Shield recharge/armour repair are optional mechanics; status, knockback and
  critical feedback remain separate from the core damage calculation.
- Decide whether a zero-damage shield hit still produces shield hit effects
  without accidentally triggering combat HP events.
- Preserve current `VitalityKind` Health/Hull naming, death handling and
  health bar support; later layered UI may show separate bars.

### Beams

A combat beam is **not** the existing `MiningEmitter`. It needs a
**line/segment or capsule-shaped hit volume**, including configurable width.
`BeamAttack` launches/maintains the delivery; `BeamController` manages
lifetime/aim and damage intervals; `BeamCollision` finds *ordered* contacts.
Use `DamageResolver` and shared target filters, not beam-specific health logic.

Support **instant/pulsed** and **sustained** beams, direction fixed/rotating/
target-tracked, first-contact blocking or multiple-target piercing,
per-target attenuation, and separately configurable interactions with walls,
shields and armoured targets. Continuous beam hits should be applied at a
defined cadence/DPS (not every render frame). Track per-target hit timers or
pulses, including actors **walking into** the beam after it activates.

The visual length **must end at the actual blocking contact**, not always
the weapon's maximum range. Respect same-layer targeting, terrain elevation,
cover and physics hitbox geometry. Handle self-hit exclusions and moving targets.

### Area damage, shockwaves, hazards

`AreaDamageResolver` owns target selection, deduplication, friendly fire,
damage falloff, line-of-sight / cover options, hit attribution and world-layer
checks. Different behaviours use the same resolver:

- **Instant**: resolve targets within a radius once at detonation.
- **Expanding shockwave**: each physics update advances the damaging
  **wavefront** from previous radius to new radius. Damage each valid target
  **once as the front crosses it**, with configurable wave thickness,
  expansion speed and falloff. Track contacted actor IDs; don't repeatedly
  damage the same entity each frame or tunnel past targets on low FPS.
- **Hazard field (future)**: optional periodic damage/status over a duration,
  explicitly different from a one-hit shockwave.

Explosion visual/audio can outlive its single damage evaluation. Do not allow
an explosion on Surface to hit cave/deep-layer actors at the same XY location.
Define height/terrain projection and what cover/structures block.

### Projectiles, homing and piercing

Retain pooling and swept collision. Add optional, composable flight/impact
behaviours without subclassing every combination:

- Homing: turn rate, acquire/reacquire window, FOV, lock persistence,
  optional prediction, target-loss behaviour (straight/reacquire/expire),
  and optional obstacle avoidance; never target unloaded/invalid actors,
  own team or other world layers.
- **Target piercing**: number of distinct targets allowed, optional remaining
  impact/damage budget and attenuation, plus per-projectile hit history.
- **Armour penetration**: how a damage packet interacts with defence pools;
  independent from piercing multiple actors.
- **Cover penetration**: interacts with physical material/obstacle rules,
  independently from armour or actor hit count.
- Explosion/proximity detonation and potential cluster/split emissions
  call the shared area/attack path; prevent recursive projectile chains.

Important: `ProjectileAttack.cs` already owns speed/lifetime/tint/damage.
Before adding `ProjectileDefinition.cs`, decide which resource is the single
owner of each field (attack payload vs reusable flight/visual archetype).
Avoid contradictory duplicate Inspector controls. Capture all parameters at
launch and **fully clear** guidance target, hit history, layer and damage
context on recycle. Define behaviour when pool capacity is reached.

### Mines and other deployables

A mine has a **placement/arming/trigger state machine**, not custom area
damage code. Trigger options include delayed arming, proximity enter with
optional hold/delay, fixed timer, contact, and later owner/remote trigger.
Detection radius and **damage radius are independent**. Mine detonation
dispatches an `AreaDamageDefinition` (instant, shockwave, etc.).

Configure allegiance/friendly fire, owner attribution, max live mines per owner,
arming VFX/SFX, expiration, whether mines can be shot/disarmed, and optional
chain-reaction detonation. Chain reactions need double-detonation guards and
bounded queued propagation (not unlimited recursive callbacks).
Decide separately whether player-placed persistent mines are saved while
short-lived enemy traps are discarded on chunk retirement.

### Physical impacts and status effects

An optional `ImpactProfile` controls movement impulse, recoil, stagger
threshold/strength and impact feedback request. Light/Regular/Heavy are
convenient presets, **not mandatory gameplay classes**. Target mass, immunity
and resisting impact can be configured separately from HP resistance.
Integrate impulses with `Player.cs` and `ENTITIES/Movement/EntityMotor.cs`,
which both overwrite velocity during their normal movement ticks.

`StatusEffectController` runs per actor only while active; definitions own
chance, duration, interval, potency, max stacks and refresh/replace/stack
policy. Support both biological and robotic resistance/immunity. Poison,
corrosion and burn may tick damage; Shock may impair systems; Stun should
not permanently lock bosses. Keep elemental **damage type** independent
from any optional status applied by the same hit. Clarify whether repeated
damage ticks interact with shields or only core vitality (open decision).

Statuses have clear source credit for delayed kills, cancellation on death,
and deterministic unload behaviour. Avoid one node/timer per individual
stack, and avoid per-frame global scans.

## 5. Open gameplay decisions — ask before implementation

**Do not silently choose these while implementing a major phase.** The
discussion established the architecture, not all gameplay balance rules.

1. **Armour model:** destructible Armour pool, permanent damage reduction,
   or both as independently configurable defence-layer features?
   *Initial proposal*: optional destructible pool with configurable type
   resistances; passive mitigation remains possible through resistance profile.
2. **Damage overflow:** after a Shield or Armour layer is depleted by a
   single attack, does leftover damage continue to the next layer or does
   breaking that layer stop the entire hit? *Proposed*: carry remaining
   damage forward, with optional per-attack override later.
3. **Shockwave default:** one hit on expanding wavefront, or periodic
   damage over an expanding filled area? *Proposed*: front contact once;
   use HazardField for periodic effects.
4. **Defence resistance and penetration math:** how to translate damage
   between layers with different multipliers, partial bypass, break-on-hit,
   regen delay and layer overflow without exploiting multipliers.
5. **Status application gate:** on contact, successful per-layer damage,
   core vitality damage only, or configurable by effect? Decide which
   statuses can be blocked by Shield.
6. **Mines and persistent zones:** save player-owned deployables, what
   countdown basis to use across unloaded chunks, and whether status
   timers pause or advance offscreen. First implementation can deliberately
   keep only temporary/same-session effects, but document the choice.
7. **Target filtering:** keep current Player/Enemy teams for baseline or
   expand to a broader faction/reputation model later? Do not bake assumptions
   into every delivery implementation.
8. **Beam occlusion:** width-aware capsule query, shield collision rules
   and whether cover penetration is material-driven or uses a generic budget.

## 6. Phased Codex implementation plan

**Workflow for each phase:** inspect latest HEAD and existing docs, state
exactly which files will change, make the *smallest working patch*, build the
Godot C# project if runtime available, run focused tests, update this checklist
and stop for review before beginning the next phase. One coherent commit per
reviewed phase is preferable; don't mix unrelated cleanup.

### Phase 0 — review and proof of baseline

- [ ] Reinspect current combat, entity, player, save, world-layer, audio and
      feedback code; compare with this dated plan.
- [ ] Identify all direct `Health.Damage`/environment/survival call sites,
      `DamageApplied` subscribers and `.tres` uses of `DamageType`.
- [ ] Record successful Godot build and a working existing blaster/melee/mining
      baseline. Note any preexisting errors rather than attributing to this work.
- [ ] Confirm gameplay decisions in §5 with the user before relevant phases.

### Phase 1 — common damage packet and hit result

- [ ] Add immutable/configured `DamageProfile` and small runtime
      `DamagePacket` and `DamageResult` with source, hit position and type.
- [ ] Add a `DamageResolver` path retaining legacy health and typed resistance
      behaviour; ensure blocked hits vs accepted health loss are distinguishable.
- [ ] Adapt projectile hitboxes, player weapon and enemy melee incrementally.
      Do not break old entry points or change balance while migrating.
- [ ] Emit suitable new *result* notifications for visuals/audio without
      changing the meaning of current `DamageApplied(int)`.
- [ ] Test immunity, zero damage, resistances, death once and source credit.

### Phase 2 — optional layered defence

- [ ] Define optional Shield and Armour pools with single mandatory core
      vitality (Health/Hull presentation only), each with per-instance state.
- [ ] Reuse/convert existing `DefenseDefinition`; settle overflow and
      resistance arithmetic; ensure pools do not share mutable resources.
- [ ] Add optional shield recharge delay/rate and explicit break/regen signals
      as selected. Keep dinosaur health-only and robot examples.
- [ ] Extend player/entity save data in a backwards-compatible manner; older
      saves with just Health must still load. Validate optional missing fields.
- [ ] Test shield-only hits, armour-only hits, shield depletion, overflow,
      death only at core zero, reload/stream-out/reload and HUD compatibility.

### Phase 3 — impact and knockback

- [ ] Add optional impact profiles and light/regular/heavy `.tres` presets.
- [ ] Integrate time-limited impulse with player and entity movement so
      ordinary velocity writes do not cancel knockback immediately.
- [ ] Distinguish physical stagger from multi-second stun/status effects;
      add mass/resistance and cap impulse against bosses/terrain.
- [ ] Connect accepted impacts to existing `CameraShakeBus` only through
      a bounded presentation hook, not hardcoded damage class rules.
- [ ] Test moving/idle/blocked actors, jump, navigation, multiple hits and death.

### Phase 4 — status effect framework

- [ ] Add one idle-when-empty controller per affected actor.
- [ ] Implement definitions, immunity/resistance, stack policy, interval
      ticking, independent status types and eventual cleanup.
- [ ] Start with a small set (e.g. Poison, Burn, Shock, Stun) and add the
      rest through data presets once behaviour is reusable.
- [ ] Decide status application gate and DOT layer interaction before
      shipping; prevent unintended DamageImmunity bypass exploits.
- [ ] Test repeated application, effect expiration, boss stun immunity,
      attribution, death, unload/retirement, respawn and save policy.

### Phase 5 — area damage and shockwaves

- [ ] Add shared area target query, radius/falloff, height/layer filtering,
      friendly-fire, damage packets and configurable cover occlusion.
- [ ] Implement instantaneous circular explosion with one damage decision
      per target, independent of visual duration.
- [ ] Implement sweeping expanding wavefront with hit-once registry and
      frame-rate-independent crossing detection; optional per-wave thickness.
- [ ] Prove surface explosions cannot hit entities in underground layers.
- [ ] Test multiple moving targets, adjacent obstacles, fast expansion,
      low FPS, chain reactions, friendly fire and zero targets.

### Phase 6 — projectile enhancements

- [ ] Add homing guidance data/runtime, loss-of-target policies, tracking
      update and pool reset safety.
- [ ] Add actor-target piercing and falloff, plus separate armour and cover
      penetration definitions if approved.
- [ ] Preserve accurate swept collision ordering (nearest encountered hit
      first) and no repeat hit on same actor within a projectile lifetime.
- [ ] Add optional proximity/impact detonation through the area system.
- [ ] Test fast movers, tracking across chunk unload, obstacle occlusion,
      layer transitions, pool capacity and zero-allocation idle pooling.

### Phase 7 — combat beams

- [ ] Add `BeamAttack`, beam definition/runtime, collision querying and
      beam visual; do not replace `MiningEmitter`.
- [ ] Support instant/pulsed and continuous damage when targets enter an
      already-active line-shaped volume.
- [ ] Implement ordered contacts, configurable first-hit blocking, target
      piercing, per-target falloff and cover rules shared with Penetration.
- [ ] Enforce per-target pulse cadence, source ownership and cleanup when
      weapon/target unloads, disables or changes layers.
- [ ] Test moving through beam, turning while firing, zero-width edge cases,
      several aligned targets, cover clipping and immediate turn-off.

### Phase 8 — mines and deployables

- [ ] Implement a minimal arming + state machine and proximity/timed/contact
      triggers, using the existing area system for damage.
- [ ] Keep trigger/detection radius independent of detonation radius.
- [ ] Add max live count, owner cleanup, optional destruction/disarm and
      explicit chain reaction protection.
- [ ] Decide persistence of placed mines in chunk saves; test loading
      and retiring chunks, paused world, owner death and world-layer changes.

### Phase 9 — feedback, debugging and final cleanup

- [ ] Hook `DamageResult` to existing world damage numbers, health bars,
      optional layer bars, hit flashes, camera shake and status indicators.
- [ ] Reuse the existing `AUDIO/` catalog/manager for species attacks,
      impacts, shield break, loops and status ticks. No audio files in
      `COMBAT/` or `VISUALS/Feedback/`.
- [ ] Add optional debug inspection of incoming type, shield/armour/core
      amounts, block reason, effect application and piercing/beam history;
      ship with debugging disabled by default.
- [ ] Remove old redundant code only after all dependent resource paths,
      save formats, AI and player references have been migrated/tested.
- [ ] Update `COMBAT/README.md` and this plan with what is **implemented**,
      what is postponed, and the actual tested commit SHAs.

## 7. Engineering constraints and regression checklist

- **Do not** change enum ordering, rename/move existing Godot `.cs`/
  `.tres`/`.tscn` resources casually, or break existing exported paths,
  scenes and player inventories. Preserve Godot C# global class/resource UID
  imports wherever relevant.
- Keep immutable definitions in Godot `Resource` and mutable timers,
  shield/armour pools, and hit histories in each actor/shot instance.
- Minimize node count; pooled projectiles remain reused; no per-stack
  `Timer` node, global per-frame entity scans or always-active components
  when idle. Expensive target queries should be bounded/cadenced.
- No effect may damage targets on an unrelated world layer. Respect terrain
  elevation, projectile flight height, cover and layer epoch invalidation.
- Do not assume a hitbox being contacted means core vitality damage was
  accepted; status, piercing continuation and sound should use results.
- Physics controls the authoritative hit detection. Separate gameplay
  collisions/damage cadence from GPU particle/Line2D/audio lifetimes.
- Preserve existing survival/environment damage semantics deliberately;
  damage-over-time needs a specified immunity policy, not silent bypasses.
- Validate component lifetimes and clear references across actor death,
  despawn, streamed chunk retirement, paused/unpaused world and save load.
- Keep baseline test scenarios working: basic robot blaster, player blaster,
  Tallowback melee, mining laser, digging, damage numbers, damaged enemy
  health bars, death/respawn, persistence and world-layer transitions.
- Add tests for multi-target beams, wavefront crossing, shield-only impacts,
  different armour resistance, target penetration, homing target loss, mine
  arming, multi-hit prevention and chain reaction guards.
- Prefer Godot Inspector-friendly `[ExportGroup]` presets; make methods clear
  with short header comments and `#region` groups, per
  `NOTES/CodePreferences.md`.
- Don't add global tuning knobs to `CONFIG/GlobalConfig.cs` without asking;
  prefer localized configurable `.tres` resources.
- Make integration changes additive and easy to roll back; when Godot cannot
  be run, explicitly record that and provide a focused manual test list.

## 8. A Codex hand-off prompt

> Read `COMBAT/COMBAT_ARCHITECTURE_PLAN.md` and
> `NOTES/CodePreferences.md` first. Inspect the current repository and identify
> which checklist items are already implemented. Do **Phase 0** first and report
> risks/compatibility dependencies. Ask for any open decisions before work that
> depends on them. When authorized, implement **only the next approved phase**,
> using small isolated patches that preserve current combat, physics, world
> streaming and saves. Update this MD's checkbox/status section with evidence
> and supply exact in-Godot testing instructions. Do not start all phases at once.

---

**Planning note:** the names/tree above intentionally describe a long-term
architecture. Actual source should stay *as simple as possible*; combine files
if doing so improves clarity without coupling otherwise isolated systems.
