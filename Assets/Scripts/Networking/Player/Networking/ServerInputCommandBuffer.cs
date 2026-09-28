using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    internal sealed class ServerInputCommandBuffer
    {
        public const int MaximumBufferedCommands = 128;
        public const uint MaximumCommandLead = 256;
        public const int MissingCommandGraceTicks = 2;
        public const int MaximumHeldInputTicks = 2;

        private readonly Dictionary<uint, PlayerInputMessage>
            _commands =
                new Dictionary<uint, PlayerInputMessage>(
                    MaximumBufferedCommands);

        private readonly uint _networkId;

        private PlayerInputMessage _heldCommand;
        private uint _lastSimulatedSequence;
        private int _missingCommandTicks;
        private int _heldInputTicks;

        public int Count => _commands.Count;

        public uint LastSimulatedSequence =>
            _lastSimulatedSequence;

        public ServerInputCommandBuffer(
            uint networkId,
            float initialYaw)
        {
            if (networkId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(networkId));
            }

            _networkId = networkId;

            _heldCommand = new PlayerInputMessage(
                networkId,
                0,
                Vector2.zero,
                initialYaw,
                PlayerInputButtons.None);
        }

        public bool TryInsert(
            PlayerInputMessage command)
        {
            if (command.NetworkId != _networkId ||
                command.InputSequence == 0 ||
                !IsNewer(
                    command.InputSequence,
                    _lastSimulatedSequence))
            {
                return false;
            }

            uint distance = ForwardDistance(
                command.InputSequence,
                _lastSimulatedSequence);

            if (distance > MaximumCommandLead ||
                _commands.ContainsKey(
                    command.InputSequence))
            {
                return false;
            }

            if (_commands.Count >=
                MaximumBufferedCommands)
            {
                uint farthestSequence = 0;
                uint farthestDistance = 0;

                foreach (uint sequence in _commands.Keys)
                {
                    uint bufferedDistance =
                        ForwardDistance(
                            sequence,
                            _lastSimulatedSequence);

                    if (bufferedDistance >
                        farthestDistance)
                    {
                        farthestSequence = sequence;
                        farthestDistance = bufferedDistance;
                    }
                }

                if (distance >= farthestDistance)
                    return false;

                _commands.Remove(farthestSequence);
            }

            _commands.Add(
                command.InputSequence,
                command);

            return true;
        }

        public PlayerInputMessage GetCommandForTick(
            out bool consumesSequence)
        {
            uint expectedSequence =
                NextSequence(_lastSimulatedSequence);

            if (_commands.TryGetValue(
                    expectedSequence,
                    out PlayerInputMessage command))
            {
                _commands.Remove(expectedSequence);
                _missingCommandTicks = 0;
                consumesSequence = true;
                return command;
            }

            if (_commands.Count > 0)
            {
                _missingCommandTicks++;

                if (_missingCommandTicks >
                    MissingCommandGraceTicks)
                {
                    uint nearestSequence =
                        FindNearestSequence();

                    command = _commands[nearestSequence];
                    _commands.Remove(nearestSequence);
                    _missingCommandTicks = 0;
                    consumesSequence = true;
                    return command;
                }
            }
            else
            {
                _missingCommandTicks = 0;
            }

            consumesSequence = false;
            return CreateFallbackCommand();
        }

        public void MarkSimulated(
            PlayerInputMessage command)
        {
            if (command.NetworkId != _networkId ||
                command.InputSequence == 0 ||
                !IsNewer(
                    command.InputSequence,
                    _lastSimulatedSequence))
            {
                throw new InvalidOperationException(
                    "Only a newly consumed command can advance " +
                    "the server input acknowledgement.");
            }

            _lastSimulatedSequence =
                command.InputSequence;

            // Jump is a pressed edge and must never be repeated.
            _heldCommand = command.WithoutJump();
            _heldInputTicks = 0;
        }

        private PlayerInputMessage CreateFallbackCommand()
        {
            if (_heldInputTicks <
                MaximumHeldInputTicks)
            {
                _heldInputTicks++;
                return _heldCommand;
            }

            return new PlayerInputMessage(
                _networkId,
                _lastSimulatedSequence,
                Vector2.zero,
                _heldCommand.Yaw,
                PlayerInputButtons.None);
        }

        private uint FindNearestSequence()
        {
            uint nearestSequence = 0;
            uint nearestDistance = uint.MaxValue;

            foreach (uint sequence in _commands.Keys)
            {
                uint distance = ForwardDistance(
                    sequence,
                    _lastSimulatedSequence);

                if (distance < nearestDistance)
                {
                    nearestSequence = sequence;
                    nearestDistance = distance;
                }
            }

            return nearestSequence;
        }

        private static uint ForwardDistance(
            uint candidate,
            uint reference)
        {
            return unchecked(candidate - reference);
        }

        private static uint NextSequence(uint sequence)
        {
            return sequence == uint.MaxValue
                ? 1
                : sequence + 1;
        }

        private static bool IsNewer(
            uint candidate,
            uint reference)
        {
            return candidate != reference &&
                   unchecked(
                       (int)(candidate - reference)) > 0;
        }
    }
}
