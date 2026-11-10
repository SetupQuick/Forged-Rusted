using System;
using Gameplay;
using Gameplay.Entities;
using UnityEngine;

namespace Networking
{
    /// <summary>
    /// Classes, skills, abilities (invisibility, fireball) and the movement
    /// abilities handed to each player's motor. Effects other players must
    /// see (invisible) are broadcast with PlayerEffects.
    /// </summary>
    public sealed partial class ServerGameplayModule
    {
        /// <summary>Server copy of a player's progress (tests, debugging).</summary>
        public PlayerProgress GetProgress(uint peerId) =>
            _players.TryGetValue(peerId, out PlayerState state) ? state.Progress : null;

        private void TryChooseClass(PlayerState state, int index)
        {
            if (index < 0 || index >= ClassDefinitions.All.Count)
                return;

            if (state.Progress.ChooseClass(ClassDefinitions.All[index].Id))
            {
                state.StatusDirty = true;
                PlayersVersion++;
            }
        }

        private void TryUnlockSkill(PlayerState state, int index)
        {
            ClassDefinition definition = state.Progress.Class;
            if (definition == null || index < 0 || index >= definition.Skills.Count)
                return;

            if (state.Progress.Unlock(definition.Skills[index].Id))
            {
                state.StatusDirty = true;
                PlayersVersion++;
            }
        }

        private void TryUseAbility(PlayerState state, AbilityCode ability, Vector3 direction)
        {
            if (state.Dead)
                return;

            switch (ability)
            {
                case AbilityCode.Invisibility:
                    if (state.Progress.TryUse(Abilities.Invisibility, PlayerProgress.InvisibilityCost, PlayerProgress.InvisibilityCooldown, ref state.StatusDirty))
                        BroadcastEffects(state);
                    break;

                case AbilityCode.Fireball:
                    if (direction.sqrMagnitude < 0.5f || direction.sqrMagnitude > 1.5f)
                        return;

                    if (state.Progress.TryUse(Abilities.Fireball, PlayerProgress.FireballCost, PlayerProgress.FireballCooldown, ref state.StatusDirty))
                    {
                        CastFireball(state, direction.normalized);
                        BreakInvisibility(state);
                    }
                    break;
            }
        }

        /// <summary>Fireball: hits the first creature along the aim (blocks stop it).</summary>
        private void CastFireball(PlayerState state, Vector3 direction)
        {
            if (_entities == null || !_world.TryGetPlayerEye(state.Peer.Id, out Vector3 eye))
                return;

            float range = PlayerProgress.FireballRange;

            // Blocks in the way shorten the shot.
            if (VoxelRaycast.Cast(eye, direction, range, _world.VoxelSize,
                    (x, y, z) => _world.GetBlock(new Vector3Int(x, y, z)), out VoxelHit hit) &&
                World.Blocks.BlockRegistry.IsSolid(hit.Block))
            {
                range = hit.Distance;
            }

            ServerEntity best = null;
            float bestDistance = range;

            foreach (ServerEntity entity in _entities.Entities)
            {
                if (entity.Dead || entity.Kind == EntityKind.DroppedItem)
                    continue;

                EntityDefinition def = entity.Definition;
                Vector3 center = entity.Position + Vector3.up * (def.Height * 0.5f);
                float along = Vector3.Dot(center - eye, direction);
                if (along < 0f || along > bestDistance)
                    continue;

                // Distance from the ray to the creature's centre.
                Vector3 closest = eye + direction * along;
                float radius = Math.Max(def.Width, def.Height) * 0.5f + 0.25f;
                if ((closest - center).sqrMagnitude <= radius * radius)
                {
                    best = entity;
                    bestDistance = along;
                }
            }

            if (best != null)
                _entities.Damage(best, PlayerProgress.FireballDamage, state.Peer.Id, eye);
        }

        private void BreakInvisibility(PlayerState state)
        {
            bool wasInvisible = state.Progress.Invisible;
            state.Progress.BreakInvisibility(ref state.StatusDirty);
            if (wasInvisible)
                BroadcastEffects(state);
        }

        /// <summary>Tells every client whether this player is invisible (hide their model).</summary>
        private void BroadcastEffects(PlayerState state)
        {
            uint peerId = state.Peer.Id;
            bool invisible = state.Progress.Invisible;
            state.BroadcastInvisible = invisible;

            foreach (PlayerState other in _players.Values)
            {
                if (other.Peer.State != ServerPeerState.Connected)
                    continue;

                _server.Send(
                    other.Peer,
                    NetworkMessageType.PlayerEffects,
                    writer => { writer.Write(peerId); writer.Write(invisible); },
                    NetworkDelivery.ReliableOrdered);
            }
        }

        /// <summary>Every tick: motor abilities from the skill tree, invisibility running out.</summary>
        private void UpdateAbilities(PlayerState state)
        {
            var flags = CharacterMotor.MovementAbilities.None;
            if (state.Progress.Has(Abilities.Dash)) flags |= CharacterMotor.MovementAbilities.Dash;
            if (state.Progress.Has(Abilities.DoubleJump)) flags |= CharacterMotor.MovementAbilities.DoubleJump;
            if (state.Progress.Has(Abilities.Grapple)) flags |= CharacterMotor.MovementAbilities.Grapple;
            _world.SetMovementAbilities(state.Peer.Id, flags);

            if (state.BroadcastInvisible != state.Progress.Invisible)
                BroadcastEffects(state);
        }
    }
}
