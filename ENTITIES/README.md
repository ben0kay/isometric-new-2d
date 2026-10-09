# Entity System

## Purpose

This folder contains the shared foundation for living world actors:
robots, wildlife, passive creatures, aggressive creatures, and future
species such as dinosaurs, birds, and other alien life.

The original robot-focused enemy system has been consolidated into
shared entity components.

Species, aggression, group membership, and movement behaviour are
separate choices. Being an entity does not automatically mean being
an enemy.

## Design Direction

The shared Entity actor coordinates its components. Components provide
focused capabilities, while definitions supply settings and resources.

Shared systems must not assume that every entity is a robot, attacks
the player, or belongs to a moving herd.

Species-specific code should only handle behaviour that cannot
reasonably be shared.

## Folder Organisation

- Core: shared actor, body, definitions, and behaviour settings.
- Movement: navigation, movement, and configurable wandering.
- Combat: targeting, attacks, combat positioning, and threat responses.
- Groups: membership, threat communication, and optional group roaming.
- Grazing: grazing behaviour and supporting world queries.
- Sequences: configurable action sequences and their action resources.
- Presentation: shared visual presentation.
- Death: death handling and loot delivery.
- Species: species-specific definitions, artwork, and related resources.

Population remains in Core for now. Its organisation will be revisited
when population settings move into biome-specific content.

Keep related species resources together. Shared capabilities belong in
their corresponding shared folders.

## Shared Actor and Definitions

Entity is the common actor used by robots and wildlife.

EntityBody provides the shared actor foundation and component access.
EntityDefinition supplies the species settings and resource references.
EntityBehaviorSettings provides shared behaviour configuration.

Definitions select the capabilities an entity uses. Optional behaviours
should remain optional rather than becoming requirements for every
species.

## Movement and Behaviour

EntityMotor handles shared movement and navigation.
EntityWandering handles configurable wandering behaviour.

Movement settings can vary between species without copying the actor
implementation.

Grazing and herd roaming are optional capabilities. Robots do not need
to graze or follow a moving herd anchor.

## Targeting and Combat

EntityTargeting handles target selection and sight checks.

The shared combat components handle attack execution, chasing, ranged
positioning, and threat responses.

EntityCombatSettings supplies shared combat distances.
MeleeCombatSettings and RangedCombatSettings provide attack-specific
configuration.

These capabilities can be used by robots, aggressive wildlife, defensive
creatures, and future species. Combat code should avoid unnecessary
robot-specific or player-only assumptions.

## Groups and Herds

Group membership and herd movement are separate features.

EntityGroup and EntityGroupMember provide group identity, membership,
and threat communication.

GroupRoaming provides optional movement around a roaming group anchor.

A robot squad can share threats without following a wandering anchor.
Wildlife can combine group membership with herd roaming.

Future species can choose these behaviours independently.

## Sequences

Sequences live outside Combat because a sequence does not have to
represent combat behaviour.

- EntitySequence executes configured sequences.
- EntitySequenceDefinition stores the sequence configuration.
- EntityAction is the base resource for reusable actions.

Current actions include:

- FireEntityAction
- DodgeEntityAction
- WaitEntityAction

These actions are shared capabilities rather than robot-specific
behaviours. Future actions can support non-combat sequences.

## Presentation and Death

EntityPresentation handles shared visual presentation.

EntityDeathLoot handles loot delivery when an entity dies. Definitions
select the appropriate delivery behaviour, such as ground drops or
robot wreckage.

Species-specific artwork and loot resources remain with their species.

## Migration Status

The main enemy-to-entity consolidation is complete:

- Robots and wildlife use the shared Entity actor.
- Shared definitions, movement, combat, and presentation use Entity names.
- Sequences use entity types directly.
- Obsolete enemy actors, definitions, and sequence adapters have been removed.
- Shared capabilities have been organised into focused folders.

EnemyPopulation retains its existing name and location intentionally
until the biome-content population pass.

Further changes should address a concrete behaviour or requirement
rather than continue renaming or splitting files without a purpose.

## Maintenance Rules

When changing shared behaviour:

1. Identify which capability owns the behaviour.
2. Update the shared implementation and its consumers together.
3. Update affected scenes and resources.
4. Verify both robot and wildlife behaviour where relevant.
5. Remove obsolete implementations and unused adapters.

Avoid duplicate systems, empty compatibility wrappers, and files that
only forward calls.

Preserve resource connections when moving files. Move associated UID
files and update explicit paths in scenes, resources, and code.

## Performance

Prefer one coordinated actor update loop over independent processing
callbacks on every helper.

Cache stable component references. Use configurable intervals and
staggered updates for expensive work.

Target searches, sight checks, path requests, and destination searches
should run when required rather than automatically every frame.

Optional capabilities should not perform ongoing work when disabled.

## Verification

After changing shared entity behaviour, check:

- Melee robots approach and attack.
- Ranged robots position themselves and fire.
- Elite robots execute their shooting and dodging sequence.
- Wildlife grazes, wanders, and follows its herd where configured.
- Group threat communication still works.
- Entity deaths deliver the configured loot or wreckage.

## Future Species

A new species should reuse existing capabilities where appropriate,
then supply its own settings, visuals, attacks, and optional behaviours.

Add new shared capabilities when a species needs them. Keep genuinely
unique behaviour with that species.

The goal is to add species without copying the robot implementation
or rebuilding targeting, movement, combat, and group logic.
