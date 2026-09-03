# Vehicle Adaptations 1.0.0

Vehicle Adaptations changes the stationary vehicles generated with the world. It
does not affect minibikes, motorcycles, 4x4s, gyrocopters, or other drivable
`EntityVehicle` instances.

## Burning and explosions

Static cars, vans, SUVs, pickups, emergency vehicles, buses, trucks, tractors,
forklifts, and construction vehicles record damage across their visual damage
stages. At 75% of the durability available when the vehicle first loads, the
vehicle catches fire. Five seconds later it uses that vehicle block's existing
vanilla explosion profile, damaging blocks and entities around it.

An explosion ignites every other adapted static vehicle inside its explosion
radius. Those vehicles receive their own fire warning and delayed explosion,
allowing visible chain reactions instead of vanilla's instantaneous cascade.
Damage and burning state are persisted in the vehicle's composite tile entity.

All tuning values except the underlying vanilla explosion profiles are in
`VehicleAdaptationsConfig.xml`.

## Regeneration

When a naturally generated vehicle detonates, the server records its exact block
variant, rotation, metadata, position, and destruction world time in the current
save. Its due time uses the live `LootRespawnDays` sandbox value. If loot respawn
is disabled, vehicle regeneration is disabled too.

After the interval, the vehicle returns with fresh durability and loot when:

- every chunk touched by its original multiblock footprint is loaded;
- no player is within the configured clear radius (32 m by default); and
- every cell in that footprint is still air.

The mod never overwrites a player-built block. Any occupied footprint remains pending
and is retried later. Player-placed static vehicle blocks are not registered for
regeneration. Pending records survive server restarts in
`VehicleAdaptationsRespawns.xml` under the active save directory.

This mod is required on both the dedicated server and clients because it supplies
a custom composite tile-entity feature and synchronization package.
