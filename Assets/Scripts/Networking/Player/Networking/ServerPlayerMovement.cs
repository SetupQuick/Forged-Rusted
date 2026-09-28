using System;
using System.Collections.Generic;

namespace Networking
{
    public sealed class ServerPlayerMovement :
        IDisposable
    {
        private readonly GameServer _server;

        private readonly ServerMessageRouter
            _router;

        private readonly NetworkWorld _world;

        private readonly Dictionary<
            uint,
            PlayerState> _playersByPeer =
                new Dictionary<
                    uint,
                    PlayerState>();

        private bool _disposed;

        public ServerPlayerMovement(
            GameServer server,
            ServerMessageRouter router,
            NetworkWorld world)
        {
            _server = server ??
                throw new ArgumentNullException(
                    nameof(server));

            _router = router ??
                throw new ArgumentNullException(
                    nameof(router));

            _world = world ??
                throw new ArgumentNullException(
                    nameof(world));

            _router.Register(
                NetworkMessageType.PlayerInput,
                OnPlayerInput);

            _server.PeerDisconnected +=
                OnPeerDisconnected;

            _world.ObjectSpawned +=
                OnObjectSpawned;

            _world.ObjectDespawned +=
                OnObjectDespawned;

            /*
             * Normally no players exist this early, but this
             * keeps construction safe if authoritative objects
             * were registered beforehand.
             */
            foreach (
                NetObject netObject
                in _world.Objects)
            {
                RegisterPlayer(
                    netObject);
            }
        }

        public void Tick(
            uint serverTick)
        {
            if (_disposed)
                return;

            SimulatePlayers(
                serverTick,
                NetworkTime.TickDelta);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _router.Unregister(
                NetworkMessageType.PlayerInput,
                OnPlayerInput);

            _server.PeerDisconnected -=
                OnPeerDisconnected;

            _world.ObjectSpawned -=
                OnObjectSpawned;

            _world.ObjectDespawned -=
                OnObjectDespawned;

            _playersByPeer.Clear();
        }

        private void OnPlayerInput(
            Peer peer,
            byte[] data)
        {
            if (peer == null)
                return;

            if (!PlayerInputBatchMessage.TryRead(
                    data,
                    out PlayerInputBatchMessage batch))
            {
                return;
            }

            if (!_playersByPeer.TryGetValue(
                    peer.Id,
                    out PlayerState state))
            {
                return;
            }

            /*
             * A client may only send movement commands for
             * the player the server assigned to that peer.
             */
            if (batch.NetworkId !=
                state.NetworkId)
            {
                return;
            }

            for (int index = 0;
                 index < batch.Commands.Length;
                 index++)
            {
                state.InputBuffer.TryInsert(
                    batch.Commands[index]);
            }
        }

        private void OnPeerDisconnected(
            uint peerId)
        {
            _playersByPeer.Remove(
                peerId);
        }

        private void OnObjectSpawned(
            NetObject netObject)
        {
            RegisterPlayer(
                netObject);
        }

        private void OnObjectDespawned(
            uint networkId)
        {
            uint ownerToRemove = 0;

            foreach (
                KeyValuePair<
                    uint,
                    PlayerState> entry
                in _playersByPeer)
            {
                if (entry.Value.NetworkId !=
                    networkId)
                {
                    continue;
                }

                ownerToRemove =
                    entry.Key;

                break;
            }

            if (ownerToRemove != 0)
            {
                _playersByPeer.Remove(
                    ownerToRemove);
            }
        }

        private void RegisterPlayer(
            NetObject netObject)
        {
            if (netObject == null ||
                !netObject.IsSpawned ||
                netObject.OwnerPeerId == 0)
            {
                return;
            }

            if (_playersByPeer.ContainsKey(
                    netObject.OwnerPeerId))
            {
                return;
            }

            if (!TryFindPeer(
                    netObject.OwnerPeerId,
                    out Peer peer))
            {
                return;
            }

            if (!netObject.TryGetComponent(
                    out CharacterMotor motor))
            {
                return;
            }

            motor.SetSimulationEnabled(
                true);

            _playersByPeer[
                netObject.OwnerPeerId] =
                new PlayerState(
                    netObject.NetworkId,
                    peer,
                    motor,
                    new ServerInputCommandBuffer(
                        netObject.NetworkId,
                        netObject.transform
                            .eulerAngles.y));
        }

        private void SimulatePlayers(
            uint serverTick,
            float deltaTime)
        {
            foreach (
                PlayerState state
                in _playersByPeer.Values)
            {
                if (state.Peer == null ||
                    state.Motor == null)
                {
                    continue;
                }

                PlayerInputMessage command =
                    state.InputBuffer
                        .GetCommandForTick(
                            out bool consumesSequence);

                /*
                 * Authoritative simulation happens exactly
                 * once per server network tick.
                 */
                state.Motor.Simulate(
                    command,
                    deltaTime);

                if (consumesSequence)
                {
                    /*
                     * Only real consumed commands advance
                     * acknowledgement.
                     *
                     * Fallback/held commands do not.
                     */
                    state.InputBuffer.MarkSimulated(
                        command);
                }

                /*
                 * Send every tick.
                 *
                 * This is UnreliableSequenced, so repeated
                 * authoritative states and acknowledgements
                 * provide natural loss recovery.
                 */
                SendMovementState(
                    state,
                    serverTick);
            }
        }

        private void SendMovementState(
            PlayerState state,
            uint serverTick)
        {
            CharacterMotorState motorState =
                state.Motor.CaptureState();

            var message =
                new PlayerMovementStateMessage(
                    state.NetworkId,
                    serverTick,
                    state.InputBuffer
                        .LastSimulatedSequence,
                    motorState.Position,
                    motorState.Rotation,
                    motorState.VerticalVelocity,
                    motorState.ControllerHeight);

            _server.Send(
                state.Peer,
                NetworkMessageType.PlayerMovementState,
                writer =>
                    message.Write(writer),
                NetworkDelivery.UnreliableSequenced);
        }

        private bool TryFindPeer(
            uint peerId,
            out Peer peer)
        {
            foreach (
                Peer candidate
                in _server.Peers)
            {
                if (candidate == null ||
                    candidate.Id != peerId)
                {
                    continue;
                }

                peer = candidate;
                return true;
            }

            peer = null;
            return false;
        }

        private sealed class PlayerState
        {
            public uint NetworkId { get; }

            public Peer Peer { get; }

            public CharacterMotor Motor { get; }

            public ServerInputCommandBuffer
                InputBuffer { get; }

            public PlayerState(
                uint networkId,
                Peer peer,
                CharacterMotor motor,
                ServerInputCommandBuffer inputBuffer)
            {
                if (networkId == 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(networkId));
                }

                NetworkId =
                    networkId;

                Peer = peer ??
                    throw new ArgumentNullException(
                        nameof(peer));

                Motor = motor ??
                    throw new ArgumentNullException(
                        nameof(motor));

                InputBuffer = inputBuffer ??
                    throw new ArgumentNullException(
                        nameof(inputBuffer));
            }
        }
    }
}