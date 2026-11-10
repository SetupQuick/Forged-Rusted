using System;
using System.Collections.Generic;
using UnityEngine;
using World.Blocks;

namespace Gameplay.Entities
{
    /// <summary>What the simulation needs to know about a player.</summary>
    public struct PlayerTarget
    {
        public uint PeerId;
        public Vector3 Feet;
        public bool Alive;
        public bool Invisible;
    }

    /// <summary>
    /// Server-side creatures and dropped items: movement with collision
    /// against the voxel world, simple behaviour (animals wander and flee
    /// when hit, zombies chase and hit players), spawning around players
    /// and removing far ones. Pure code with callbacks, so it is tested
    /// without Unity; ServerGameplayModule connects it to the network.
    /// </summary>
    public sealed class EntitySimulation
    {
        public const float Gravity = 22f;
        public const float JumpSpeed = 6.2f;
        public const float ItemLifetime = 300f;
        public const float DespawnDistance = 96f;

        private readonly Func<Vector3Int, BlockId> _getBlock;
        private readonly float _voxel;
        private readonly System.Random _random;
        private readonly List<ServerEntity> _entities = new List<ServerEntity>();
        private readonly List<ServerEntity> _removed = new List<ServerEntity>();

        /// <summary>Creates the network object for a new entity and returns its id (0 = failed).</summary>
        public Func<ServerEntity, uint> Spawned;

        /// <summary>The entity is gone (died, picked up, despawned).</summary>
        public Action<ServerEntity> Removed;

        /// <summary>A creature hit a player (peer, damage, attacker).</summary>
        public Action<uint, float, ServerEntity> PlayerHit;

        /// <summary>A creature died (entity, killer peer or 0).</summary>
        public Action<ServerEntity, uint> Died;

        public bool SpawnCreatures = true;
        public int AnimalsPerPlayer = 6;
        public int ZombiesPerPlayerDay = 2;
        public int ZombiesPerPlayerNight = 6;

        public IReadOnlyList<ServerEntity> Entities => _entities;

        public EntitySimulation(Func<Vector3Int, BlockId> getBlock, float voxelSize, int seed = 1234)
        {
            _getBlock = getBlock ?? throw new ArgumentNullException(nameof(getBlock));
            _voxel = voxelSize;
            _random = new System.Random(seed);
        }

        public ServerEntity Find(uint id)
        {
            foreach (ServerEntity entity in _entities)
                if (entity.Id == id && !entity.Dead)
                    return entity;
            return null;
        }

        public ServerEntity Spawn(EntityKind kind, Vector3 feet)
        {
            var entity = new ServerEntity
            {
                Kind = kind,
                Definition = EntityDefinitions.Get(kind),
                Position = feet,
                Yaw = (float)(_random.NextDouble() * 360.0)
            };
            entity.Health = entity.Definition.MaxHealth;

            entity.Id = Spawned != null ? Spawned(entity) : (uint)(_entities.Count + 1000);
            if (entity.Id == 0)
                return null;

            _entities.Add(entity);
            return entity;
        }

        public ServerEntity SpawnItem(string itemId, int count, Vector3 position, Vector3 velocity, uint droppedBy, float pickupDelay)
        {
            var entity = new ServerEntity
            {
                Kind = EntityKind.DroppedItem,
                Definition = EntityDefinitions.DroppedItem,
                Position = position,
                Velocity = velocity,
                ItemId = itemId,
                Count = count,
                DroppedBy = droppedBy,
                PickupDelay = pickupDelay,
                Health = 1f
            };

            entity.Id = Spawned != null ? Spawned(entity) : (uint)(_entities.Count + 1000);
            if (entity.Id == 0)
                return null;

            _entities.Add(entity);
            return entity;
        }

        public void Remove(ServerEntity entity)
        {
            if (entity == null || entity.Dead)
                return;

            entity.Dead = true;
            _removed.Add(entity);
        }

        /// <summary>
        /// A player hit a creature. Returns true if it took damage.
        /// Knocks it back and makes animals run away.
        /// </summary>
        public bool Damage(ServerEntity entity, float damage, uint attackerPeer, Vector3 attackerPosition)
        {
            if (entity == null || entity.Dead || entity.Kind == EntityKind.DroppedItem || entity.HurtTimer > 0.4f)
                return false;

            entity.Health -= damage;
            entity.HurtTimer = 0.5f;
            entity.LastAttacker = attackerPeer;
            entity.StateDirty = true;

            Vector3 away = entity.Position - attackerPosition;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
            entity.Velocity = away * 5f + Vector3.up * 4.5f;

            if (entity.Definition.Hostile)
                entity.TargetPeer = attackerPeer;

            if (entity.Health <= 0f)
            {
                Died?.Invoke(entity, attackerPeer);
                DropLoot(entity);
                Remove(entity);
            }

            return true;
        }

        private void DropLoot(ServerEntity entity)
        {
            EntityDefinition def = entity.Definition;
            if (string.IsNullOrEmpty(def.DropItemId))
                return;

            int count = _random.Next(def.DropMin, def.DropMax + 1);
            if (count > 0)
            {
                var velocity = new Vector3((float)(_random.NextDouble() - 0.5) * 2f, 4f, (float)(_random.NextDouble() - 0.5) * 2f);
                SpawnItem(def.DropItemId, count, entity.Position + Vector3.up * 0.4f, velocity, 0, 0.5f);
            }
        }

        // ------------------------------------------------------------
        // Tick
        // ------------------------------------------------------------

        public void Tick(float dt, IReadOnlyList<PlayerTarget> players, bool night)
        {
            if (SpawnCreatures)
                SpawnAround(dt, players, night);

            for (int i = 0; i < _entities.Count; i++)
            {
                ServerEntity entity = _entities[i];
                if (entity.Dead)
                    continue;

                entity.Age += dt;
                entity.HurtTimer = Math.Max(0f, entity.HurtTimer - dt);
                entity.AttackTimer = Math.Max(0f, entity.AttackTimer - dt);
                entity.PickupDelay = Math.Max(0f, entity.PickupDelay - dt);

                if (entity.Kind == EntityKind.DroppedItem)
                {
                    if (entity.Age > ItemLifetime)
                    {
                        Remove(entity);
                        continue;
                    }
                }
                else
                {
                    if (FarFromEveryone(entity, players))
                    {
                        Remove(entity);
                        continue;
                    }

                    Think(entity, dt, players);
                }

                Move(entity, dt);
            }

            foreach (ServerEntity entity in _removed)
            {
                _entities.Remove(entity);
                Removed?.Invoke(entity);
            }

            _removed.Clear();
        }

        private bool FarFromEveryone(ServerEntity entity, IReadOnlyList<PlayerTarget> players)
        {
            foreach (PlayerTarget player in players)
                if ((player.Feet - entity.Position).sqrMagnitude < DespawnDistance * DespawnDistance)
                    return false;
            return true;
        }

        private void Think(ServerEntity entity, float dt, IReadOnlyList<PlayerTarget> players)
        {
            EntityDefinition def = entity.Definition;
            Vector3 desired = Vector3.zero;
            float speed = def.WalkSpeed;

            if (def.Hostile && TryFindTarget(entity, players, out PlayerTarget target))
            {
                Vector3 to = target.Feet - entity.Position;
                float flat = new Vector2(to.x, to.z).magnitude;

                if (flat > def.AttackRange * 0.8f)
                {
                    desired = new Vector3(to.x, 0f, to.z).normalized;
                    speed = def.RunSpeed;
                }

                if (flat <= def.AttackRange && Math.Abs(to.y) < 1.6f && entity.AttackTimer <= 0f)
                {
                    entity.AttackTimer = def.AttackCooldown;
                    PlayerHit?.Invoke(target.PeerId, def.AttackDamage, entity);
                }

                entity.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            }
            else
            {
                entity.TargetPeer = 0;

                // Hurt animals run away from whoever hit them.
                if (!def.Hostile && entity.HurtTimer > 0f)
                    entity.WanderTimer = Math.Min(entity.WanderTimer, 0.01f);

                entity.WanderTimer -= dt;
                if (entity.WanderTimer <= 0f)
                {
                    bool walk = _random.NextDouble() < 0.55;
                    float angle = (float)(_random.NextDouble() * Math.PI * 2.0);
                    entity.WanderDirection = walk ? new Vector3((float)Math.Sin(angle), 0f, (float)Math.Cos(angle)) : Vector3.zero;
                    entity.WanderTimer = 2f + (float)_random.NextDouble() * 4f;
                }

                desired = entity.WanderDirection;
                if (!def.Hostile && entity.HurtTimer > 0f)
                    speed = def.RunSpeed;

                if (desired.sqrMagnitude > 0f)
                    entity.Yaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
            }

            // Steer horizontal velocity (knockback fades out).
            Vector3 horizontal = new Vector3(entity.Velocity.x, 0f, entity.Velocity.z);
            Vector3 goal = desired * speed;
            float accel = entity.OnGround ? 18f : 4f;
            horizontal = Vector3.MoveTowards(horizontal, goal, accel * dt);
            entity.Velocity = new Vector3(horizontal.x, entity.Velocity.y, horizontal.z);
        }

        private bool TryFindTarget(ServerEntity entity, IReadOnlyList<PlayerTarget> players, out PlayerTarget target)
        {
            target = default;
            float best = entity.Definition.SightRange * entity.Definition.SightRange;
            bool found = false;

            foreach (PlayerTarget player in players)
            {
                if (!player.Alive)
                    continue;

                // Invisible players are only noticed right next to them.
                float range = player.Invisible ? 2f * 2f : best;
                float distance = (player.Feet - entity.Position).sqrMagnitude;

                if (distance < range && distance < best + 0.001f)
                {
                    best = distance;
                    target = player;
                    found = true;
                }
            }

            if (found)
                entity.TargetPeer = target.PeerId;

            return found;
        }

        // ------------------------------------------------------------
        // Movement with voxel collision
        // ------------------------------------------------------------

        private void Move(ServerEntity entity, float dt)
        {
            EntityDefinition def = entity.Definition;
            Vector3 v = entity.Velocity;

            entity.InWater = IsLiquidAt(entity.Position + Vector3.up * (def.Height * 0.4f));

            if (entity.InWater)
            {
                // Swim up gently; items float.
                v.y = Mathf.MoveTowards(v.y, 1.2f, 12f * dt);
                v.x *= 1f - Math.Min(1f, 2f * dt);
                v.z *= 1f - Math.Min(1f, 2f * dt);
            }
            else
            {
                v.y -= Gravity * dt;
            }

            if (entity.Kind == EntityKind.DroppedItem && entity.OnGround)
            {
                v.x *= 1f - Math.Min(1f, 8f * dt);
                v.z *= 1f - Math.Min(1f, 8f * dt);
            }

            Vector3 p = entity.Position;
            bool blockedX = !TryMoveAxis(ref p, 0, v.x * dt, def);
            bool blockedZ = !TryMoveAxis(ref p, 2, v.z * dt, def);
            bool movedY = TryMoveAxis(ref p, 1, v.y * dt, def);

            entity.OnGround = !movedY && v.y <= 0f;
            if (!movedY)
                v.y = 0f;

            // Creatures hop up one-block steps they walk into.
            if ((blockedX || blockedZ) && entity.OnGround && entity.Kind != EntityKind.DroppedItem &&
                new Vector2(v.x, v.z).sqrMagnitude > 0.01f)
            {
                v.y = JumpSpeed;
            }

            if (blockedX) v.x = 0f;
            if (blockedZ) v.z = 0f;

            // Never fall out of the world.
            if (p.y < -10f)
            {
                Remove(entity);
            }

            entity.Position = p;
            entity.Velocity = v;
        }

        /// <summary>Moves along one axis; false if a block stopped it.</summary>
        private bool TryMoveAxis(ref Vector3 feet, int axis, float delta, EntityDefinition def)
        {
            if (Math.Abs(delta) < 1e-6f)
                return true;

            // Sub-steps so fast movement cannot tunnel through a voxel.
            int steps = (int)Math.Ceiling(Math.Abs(delta) / (_voxel * 0.45f));
            float step = delta / steps;

            for (int i = 0; i < steps; i++)
            {
                Vector3 next = feet;
                next[axis] += step;

                if (Overlaps(next, def))
                    return false;

                feet = next;
            }

            return true;
        }

        public bool Overlaps(Vector3 feet, EntityDefinition def)
        {
            float half = def.Width * 0.5f - 0.001f;
            Vector3 min = new Vector3(feet.x - half, feet.y + 0.001f, feet.z - half);
            Vector3 max = new Vector3(feet.x + half, feet.y + def.Height - 0.001f, feet.z + half);

            int x0 = Mathf.FloorToInt(min.x / _voxel), x1 = Mathf.FloorToInt(max.x / _voxel);
            int y0 = Mathf.FloorToInt(min.y / _voxel), y1 = Mathf.FloorToInt(max.y / _voxel);
            int z0 = Mathf.FloorToInt(min.z / _voxel), z1 = Mathf.FloorToInt(max.z / _voxel);

            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (BlockRegistry.IsSolid(_getBlock(new Vector3Int(x, y, z))))
                    return true;
            }

            return false;
        }

        private bool IsLiquidAt(Vector3 position)
        {
            var voxel = new Vector3Int(
                Mathf.FloorToInt(position.x / _voxel),
                Mathf.FloorToInt(position.y / _voxel),
                Mathf.FloorToInt(position.z / _voxel));
            return BlockRegistry.IsLiquid(_getBlock(voxel));
        }

        // ------------------------------------------------------------
        // Spawning around players
        // ------------------------------------------------------------

        private float _spawnTimer;

        private void SpawnAround(float dt, IReadOnlyList<PlayerTarget> players, bool night)
        {
            _spawnTimer -= dt;
            if (_spawnTimer > 0f || players.Count == 0)
                return;
            _spawnTimer = 0.6f; // one attempt per player every 0.6 s

            foreach (PlayerTarget player in players)
            {
                if (!player.Alive)
                    continue;

                int animals = 0, zombies = 0;
                foreach (ServerEntity entity in _entities)
                {
                    if (entity.Dead || (entity.Position - player.Feet).sqrMagnitude > 48f * 48f)
                        continue;
                    if (entity.Kind == EntityKind.Zombie) zombies++;
                    else if (entity.Kind == EntityKind.Pig || entity.Kind == EntityKind.Chicken) animals++;
                }

                int zombieCap = night ? ZombiesPerPlayerNight : ZombiesPerPlayerDay;
                EntityKind kind;

                if (zombies < zombieCap && _random.NextDouble() < (night ? 0.6 : 0.25))
                    kind = EntityKind.Zombie;
                else if (animals < AnimalsPerPlayer)
                    kind = _random.NextDouble() < 0.5 ? EntityKind.Pig : EntityKind.Chicken;
                else
                    continue;

                if (TryFindSpawnSpot(player.Feet, EntityDefinitions.Get(kind), out Vector3 spot))
                    Spawn(kind, spot);
            }
        }

        /// <summary>Ground with room above, 12-26 m from the player, not in water.</summary>
        public bool TryFindSpawnSpot(Vector3 around, EntityDefinition def, out Vector3 spot)
        {
            spot = default;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                float angle = (float)(_random.NextDouble() * Math.PI * 2.0);
                float distance = 12f + (float)_random.NextDouble() * 14f;
                float x = around.x + (float)Math.Sin(angle) * distance;
                float z = around.z + (float)Math.Cos(angle) * distance;

                int vx = Mathf.FloorToInt(x / _voxel), vz = Mathf.FloorToInt(z / _voxel);
                int top = Mathf.FloorToInt((around.y + 12f) / _voxel);
                int bottom = Mathf.FloorToInt((around.y - 12f) / _voxel);

                for (int vy = top; vy > bottom; vy--)
                {
                    BlockId below = _getBlock(new Vector3Int(vx, vy - 1, vz));
                    if (!BlockRegistry.IsSolid(below))
                        continue;

                    var feet = new Vector3((vx + 0.5f) * _voxel, vy * _voxel, (vz + 0.5f) * _voxel);
                    if (!Overlaps(feet, def) && !IsLiquidAt(feet + Vector3.up * 0.1f))
                    {
                        spot = feet;
                        return true;
                    }

                    break;
                }
            }

            return false;
        }
    }
}
