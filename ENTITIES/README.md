# Entity System

## Purpose

This folder contains the shared foundation for living world actors:
robots, wildlife, passive creatures, aggressive creatures, and future
species such as dinosaurs, birds, and other alien life.

The project originally used an enemy-only system built around robots.
We are migrating its reusable behaviour into shared entity components.

An entity's species, aggression, group membership, and movement behaviour
should be separate choices. Being an entity does not automatically mean
being an enemy.

## Design Direction

Keep the actor responsible for coordinating its components.

Shared components should handle specific capabilities:
- Target selection and awareness.
- Navigation and movement.
- Combat and attack execution.
- Action sequences.
- Wandering.
- Group membership and threat communication.
- Optional herd roaming.

Species definitions supply settings and resources. Species-specific code
should handle behaviour that cannot reasonably be shared.

Shared systems must not assume that every entity is a robot, attacks the
player, or belongs to a moving herd.

## Groups and Herds

Group membership and herd movement are separate features.

A robot squad can share threats and group identity without following a
wandering herd anchor.

Wildlife can use group membership together with a moving herd anchor.

Future species can choose either behaviour independently.

## Current Shared Components

- `EntityBody`: shared actor foundation and component access.
- `EntityTargeting`: target selection and sight checks.
- `EntityCombat`: shared attack execution.
- `EntityCombatMovement`: chasing and ranged positioning.
- `EntityWandering`: configurable wandering behaviour.
- `EntityGroupMember`: group membership and threat communication.
- `EntitySequence`: execution of configurable action sequences.
- `EntitySequenceDefinition`: sequence actions and timing.
- `EntityAction`: base resource for reusable sequence actions.

Sequence actions currently include:
- `FireEntityAction`
- `DodgeEntityAction`
- `WaitEntityAction`

These actions are shared capabilities, not robot-specific behaviours.

## Migration Status

The sequence system now uses entity types directly.
Its old enemy sequence adapters have been removed.

Some existing actor, definition, motor, combat, and presentation files
still use `Enemy` names. These are remaining migration work, not the
intended final shared architecture.

The existing robot actor and wildlife actor still need further
consolidation around the shared entity foundation.

## Migration Rules

For each migration pass:

1. Identify the reusable behaviour in the existing enemy code.
2. Move that behaviour into an appropriate shared entity component.
3. Update its consumers, scenes, and resources.
4. Verify existing robot and wildlife behaviour.
5. Remove obsolete implementations and temporary adapters once unused.

Avoid keeping duplicate systems or empty compatibility wrappers after
their consumers have migrated.

Renaming a file alone is not enough: shared code must also remove
unnecessary assumptions about enemies, robots, and player-only targets.

## Organisation

Keep reusable capabilities in shared entity folders.

Keep species-specific definitions, artwork, scenes, and behaviour with
their species.

Prefer focused components with clear responsibilities. Do not create
extra files solely to forward calls or preserve obsolete names.

## Performance

Prefer one coordinated actor update loop over independent processing
callbacks on every helper.

Use configurable intervals and staggered updates for expensive work.
Cache stable component references.

Target searches, sight checks, path requests, and destination searches
should run when required rather than automatically every frame.

## Future Species

A new species should reuse existing capabilities where appropriate,
then supply its own settings, visuals, attacks, and optional behaviours.

The goal is to add new species without copying the robot implementation
or rebuilding targeting, movement, combat, and group logic each time.
