using System;
using System.Collections.Generic;
using Gameplay;
using Gameplay.Entities;
using Items;
using UnityEngine;

namespace Networking
{
    /// <summary>
    /// Health, damage, death and respawn of players; creatures and dropped
    /// items (EntitySimulation); attacking, dropping (Q), picking up and
    /// eating. All decided by the server; each player gets a PlayerStatus
    /// message when their health (or progress) changes.
    /// </summary>
    public sealed partial class ServerGameplayModule
    {
        public const float PlayerMaxHealth = 20f;
        public const float RespawnDelay = 3f;
        public const float AttackReach = 4.5f;
        public const float AttackCooldown = 0.35f;
        public const float PickupRadius = 1.5f;
        public const float SafeFallHeight = 3.2f;   // metres
        public const float FallDamagePerMetre = 2f;

        private EntitySimulation _entities;
        private readonly List<PlayerTarget> _targets = new List<PlayerTarget>();

        /// <summary>Creatures and dropped items (null until the world is ready).</summary>
        public EntitySimulation Entities => _entities;

        public float GetHealth(uint peerId) =>
            _players.TryGetValue(peerId, out PlayerState state) ? state.Health : 0f;

        public bool IsDead(uint peerId) =>
            _players.TryGetValue(peerId, out PlayerState state) && state.Dead;

        private void EnsureEntities()
        {
            if (_entities != null || !_world.IsReady)
                return;

            _entities = new EntitySimulation(v => _world.GetBlock(v), _world.VoxelSize)
            {
                Spawned = entity => _world.SpawnEntity(entity),
                Removed = entity => _world.DespawnEntity(entity.Id),
                PlayerHit = (peer, damage, attacker) => DamagePlayer(peer, damage),
                Died = OnEntityDied
            };
        }

        private void TickSurvival(float dt)
        {
            if (dt <= 0f)
                return;

            EnsureEntities();
            _targets.Clear();

            foreach (PlayerState state in _players.Values)
            {
                bool hasBody = _world.TryGetPlayerBody(state.Peer.Id, out Vector3 feet, out _, out float vy, out bool grounded, out bool inWater);

                if (state.Dead)
                {
                    // Tell the countdown once per second, not every tick.
                    float before = Mathf.Ceil(state.RespawnTimer);
                    state.RespawnTimer -= dt;
                    if (Mathf.Ceil(state.RespawnTimer) != before)
                        state.StatusDirty = true;

                    if (state.RespawnTimer <= 0f)
                        Respawn(state);
                }
                else if (hasBody)
                {
                    UpdateFall(state, vy, grounded, inWater);
                    Regenerate(state, dt);
                }

                state.Progress.Tick(dt, ref state.StatusDirty);
                UpdateAbilities(state);

                if (hasBody)
                {
                    _targets.Add(new PlayerTarget
                    {
                        PeerId = state.Peer.Id,
                        Feet = feet,
                        Alive = !state.Dead,
                        Invisible = state.Progress.Invisible
                    });
                }
            }

            if (_entities != null)
            {
                _entities.Tick(dt, _targets, _world.IsNight);
                PickUpItems();

                foreach (ServerEntity entity in _entities.Entities)
                    _world.UpdateEntity(entity);
            }

            foreach (PlayerState state in _players.Values)
                if (state.StatusDirty)
                    SendStatus(state);
        }

        // ------------------------------------------------------------
        // Health
        // ------------------------------------------------------------

        private void UpdateFall(PlayerState state, float verticalVelocity, bool grounded, bool inWater)
        {
            if (inWater)
            {
                state.FallSpeed = 0f; // water breaks the fall
            }
            else if (!grounded)
            {
                state.FallSpeed = Math.Max(state.FallSpeed, -verticalVelocity);
            }
            else if (!state.WasGrounded)
            {
                // Landed: height from speed (v^2 = 2 g h, g = 9.81).
                float height = state.FallSpeed * state.FallSpeed / (2f * 9.81f);
                float damage = (height - SafeFallHeight) * FallDamagePerMetre;

                if (damage >= 1f)
                    DamagePlayer(state.Peer.Id, Mathf.Floor(damage));

                state.FallSpeed = 0f;
            }

            state.WasGrounded = grounded;
        }

        private void Regenerate(PlayerState state, float dt)
        {
            state.SinceDamage += dt;

            if (state.Health >= PlayerMaxHealth || state.SinceDamage < 6f)
                return;

            state.RegenTimer += dt;
            if (state.RegenTimer >= 3f)
            {
                state.RegenTimer = 0f;
                state.Health = Math.Min(PlayerMaxHealth, state.Health + 1f);
                state.StatusDirty = true;
            }
        }

        /// <summary>Damages a player (monsters, falls). Returns false if ignored (dead, just hit).</summary>
        public bool DamagePlayer(uint peerId, float damage)
        {
            if (!_players.TryGetValue(peerId, out PlayerState state) || state.Dead || damage <= 0f)
                return false;

            state.Health = Math.Max(0f, state.Health - damage);
            state.SinceDamage = 0f;
            state.RegenTimer = 0f;
            state.StatusDirty = true;

            if (state.Health <= 0f)
            {
                // Inventory is kept for now (easier to test); dropping it is
                // a one-line change once the design decides.
                state.Dead = true;
                state.RespawnTimer = RespawnDelay;
            }

            return true;
        }

        private void Respawn(PlayerState state)
        {
            state.Dead = false;
            state.Health = PlayerMaxHealth;
            state.FallSpeed = 0f;
            state.SinceDamage = 99f;
            state.StatusDirty = true;
            _world.TeleportPlayer(state.Peer.Id, _world.SpawnPoint);
        }

        // ------------------------------------------------------------
        // Combat
        // ------------------------------------------------------------

        private void TryAttack(PlayerState state, uint target)
        {
            if (state.Dead || _entities == null || _now - state.LastAttack < AttackCooldown)
                return;

            ServerEntity entity = _entities.Find(target);
            if (entity == null || entity.Kind == EntityKind.DroppedItem)
                return;

            if (!_world.TryGetPlayerEye(state.Peer.Id, out Vector3 eye))
                return;

            Vector3 center = entity.Position + Vector3.up * (entity.Definition.Height * 0.5f);
            float reach = AttackReach + entity.Definition.Width * 0.5f;
            if ((center - eye).sqrMagnitude > reach * reach)
                return;

            ItemData held = state.Inventory[state.SelectedSlot].Item;
            float damage = held != null ? Math.Max(1f, held.attackDamage) : 1f;
            damage *= state.Progress.DamageMultiplier;

            state.LastAttack = _now;
            _entities.Damage(entity, damage, state.Peer.Id, eye);
            BreakInvisibility(state);
        }

        private void OnEntityDied(ServerEntity entity, uint killer)
        {
            if (killer != 0 && _players.TryGetValue(killer, out PlayerState state))
            {
                state.Progress.AddExperience(entity.Definition.Experience, ref state.StatusDirty);
                state.Progress.OnKilled(entity.Kind, ref state.StatusDirty);
                PlayersVersion++;
            }
        }

        // ------------------------------------------------------------
        // Items on the ground
        // ------------------------------------------------------------

        private void TryDrop(PlayerState state, int slot, bool wholeStack)
        {
            if (state.Dead || _entities == null || slot < 0 || slot >= state.Inventory.Count)
                return;

            ItemStack stack = state.Inventory[slot];
            if (stack.IsEmpty)
                return;

            if (!_world.TryGetPlayerEye(state.Peer.Id, out Vector3 eye) ||
                !_world.TryGetPlayerBody(state.Peer.Id, out _, out float yaw, out _, out _, out _))
            {
                return;
            }

            int count = wholeStack ? stack.Count : 1;
            state.Inventory.RemoveFromSlot(slot, count);
            state.InventoryDirty = true;
            PlayersVersion++;

            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _entities.SpawnItem(stack.Item.Id, count, eye - Vector3.up * 0.35f + forward * 0.5f, forward * 4f + Vector3.up * 2f, state.Peer.Id, 1.5f);
        }

        private void PickUpItems()
        {
            foreach (ServerEntity item in _entities.Entities)
            {
                if (item.Dead || item.Kind != EntityKind.DroppedItem || item.PickupDelay > 0f)
                    continue;

                foreach (PlayerTarget player in _targets)
                {
                    if (!player.Alive)
                        continue;

                    Vector3 body = player.Feet + Vector3.up * 0.9f;
                    if ((item.Position - body).sqrMagnitude > PickupRadius * PickupRadius + 0.81f)
                        continue;

                    if (!_players.TryGetValue(player.PeerId, out PlayerState state) ||
                        !ItemDatabase.TryGet(item.ItemId, out ItemData data))
                    {
                        continue;
                    }

                    int leftover = state.Inventory.Add(data, item.Count);
                    if (leftover == item.Count)
                        continue; // inventory full

                    state.InventoryDirty = true;
                    PlayersVersion++;

                    if (leftover == 0)
                    {
                        _entities.Remove(item);
                    }
                    else
                    {
                        item.Count = leftover;
                        item.StateDirty = true;
                    }

                    break;
                }
            }
        }

        private void TryUse(PlayerState state, int slot)
        {
            if (state.Dead || slot < 0 || slot >= state.Inventory.Count)
                return;

            ItemStack stack = state.Inventory[slot];
            if (stack.IsEmpty || stack.Item.healAmount <= 0f || state.Health >= PlayerMaxHealth)
                return;

            state.Inventory.RemoveFromSlot(slot, 1);
            state.Health = Math.Min(PlayerMaxHealth, state.Health + stack.Item.healAmount);
            state.InventoryDirty = true;
            state.StatusDirty = true;
            PlayersVersion++;
        }

        // ------------------------------------------------------------
        // Status message
        // ------------------------------------------------------------

        private void SendStatus(PlayerState state)
        {
            if (state.Peer.State != ServerPeerState.Connected)
                return;

            PlayerStatus status = state.Progress.ToStatus();
            status.Health = state.Health;
            status.MaxHealth = PlayerMaxHealth;
            status.Dead = state.Dead;
            status.RespawnSeconds = Math.Max(0f, state.RespawnTimer);

            bool sent = _server.Send(
                state.Peer,
                NetworkMessageType.PlayerStatus,
                writer => GameplayMessages.WriteStatus(writer, status),
                NetworkDelivery.ReliableOrdered);

            if (sent)
                state.StatusDirty = false;
        }
    }
}
