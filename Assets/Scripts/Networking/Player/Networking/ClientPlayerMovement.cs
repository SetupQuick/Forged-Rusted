using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    public sealed class ClientPlayerMovement :
        IDisposable
    {
        private const int MaximumInputHistory = 32;
        private const int MaximumPendingInputs = 256;

        private readonly GameClient _client;

        private readonly ClientMessageRouter
            _router;

        private readonly NetworkWorld _world;

        /*
         * Recent commands used for redundant
         * network transmission.
         */
        private readonly List<PlayerInputMessage>
            _inputHistory =
                new List<PlayerInputMessage>(
                    MaximumInputHistory);

        /*
         * Commands predicted locally but not yet
         * acknowledged by the server.
         */
        private readonly List<PlayerInputMessage>
            _pendingInputs =
                new List<PlayerInputMessage>(
                    MaximumPendingInputs);

        private NetObject _localPlayer;

        private PlayerInputReader _inputReader;

        private CharacterMotor _predictedMotor;

        private uint _nextInputSequence = 1;

        private uint _lastReconciledInputSequence;

        private bool _hasReconciledInput;

        private bool _predictionEnabled;

        private bool _disposed;

        public ClientPlayerMovement(
            GameClient client,
            ClientMessageRouter router,
            NetworkWorld world)
        {
            _client = client ??
                throw new ArgumentNullException(
                    nameof(client));

            _router = router ??
                throw new ArgumentNullException(
                    nameof(router));

            _world = world ??
                throw new ArgumentNullException(
                    nameof(world));

            _world.ObjectDespawned +=
                OnObjectDespawned;

            _router.Register(
                NetworkMessageType.PlayerMovementState,
                OnMovementState);
        }

        public void Tick()
        {
            if (_disposed ||
                !_client.IsConnected)
            {
                return;
            }

            EnsureLocalPlayer();

            if (_localPlayer == null ||
                _inputReader == null ||
                _predictedMotor == null)
            {
                return;
            }

            SendInput();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _router.Unregister(
                NetworkMessageType.PlayerMovementState,
                OnMovementState);

            _world.ObjectDespawned -=
                OnObjectDespawned;

            ClearLocalPlayer();
        }

        private void SendInput()
        {
            PlayerInputMessage message =
                _inputReader.BuildMessage(
                    _localPlayer.NetworkId,
                    AllocateNextInputSequence());

            AddToInputHistory(
                message);

            SendInputBatch();

            /*
             * Prediction happens even if the packet couldn't
             * be sent this tick.
             *
             * The command remains in input history and may be
             * included redundantly in later packets.
             */
            Predict(
                message);
        }

        private void AddToInputHistory(
            PlayerInputMessage message)
        {
            _inputHistory.Add(
                message);

            if (_inputHistory.Count >
                MaximumInputHistory)
            {
                _inputHistory.RemoveAt(0);
            }
        }

        private void SendInputBatch()
        {
            int commandCount =
                Math.Min(
                    PlayerInputBatchMessage.MaximumCommands,
                    _inputHistory.Count);

            if (commandCount <= 0)
                return;

            var commands =
                new PlayerInputMessage[
                    commandCount];

            int firstCommand =
                _inputHistory.Count -
                commandCount;

            for (int index = 0;
                 index < commandCount;
                 index++)
            {
                commands[index] =
                    _inputHistory[
                        firstCommand + index];
            }

            var batch =
                new PlayerInputBatchMessage(
                    _localPlayer.NetworkId,
                    commands);

            bool sent =
                _client.Send(
                    NetworkMessageType.PlayerInput,
                    writer =>
                        batch.Write(writer),
                    NetworkDelivery.UnreliableSequenced);

            if (!sent)
                return;

            NetworkRuntime runtime =
                NetworkRuntime.Current;

            if (runtime?.Diagnostics == null)
                return;

            PlayerInputMessage newestCommand =
                commands[
                    commands.Length - 1];

            runtime.Diagnostics
                .LatestSentInputSequence =
                    newestCommand.InputSequence;
        }

        private void Predict(
            PlayerInputMessage message)
        {
            if (!_predictionEnabled ||
                _predictedMotor == null)
            {
                return;
            }

            /*
             * One input command represents exactly
             * one network simulation tick.
             */
            _predictedMotor.Simulate(
                message,
                NetworkTime.TickDelta);

            _pendingInputs.Add(
                message);

            if (_pendingInputs.Count >
                MaximumPendingInputs)
            {
                /*
                 * This should normally never happen.
                 * Keeping the queue bounded prevents a broken
                 * connection from growing memory indefinitely.
                 */
                _pendingInputs.RemoveAt(0);
            }

            ReportPendingInputCount();
        }

        private void OnMovementState(
            byte[] data)
        {
            if (_disposed)
                return;

            if (!PlayerMovementStateMessage.TryRead(
                    data,
                    out PlayerMovementStateMessage state))
            {
                return;
            }

            NetworkRuntime runtime =
                NetworkRuntime.Current;

            if (runtime?.Diagnostics != null)
            {
                runtime.Diagnostics.ServerTick =
                    state.ServerTick;
            }

            /*
             * Listen server shares the authoritative object.
             * It sends input through its local client, but it
             * must not reconcile/predict that shared object.
             */
            if (!_predictionEnabled ||
                _localPlayer == null ||
                _predictedMotor == null)
            {
                return;
            }

            if (state.NetworkId !=
                _localPlayer.NetworkId)
            {
                return;
            }

            Reconcile(
                state);
        }

        private void Reconcile(
            PlayerMovementStateMessage state)
        {
            uint acknowledgedInput =
                state.AcknowledgedInputSequence;

            /*
             * Zero means the server has not simulated a real
             * client input command yet.
             */
            if (acknowledgedInput == 0)
                return;

            /*
             * Ignore duplicate or older movement states.
             *
             * PlayerMovementState uses UnreliableSequenced,
             * but keeping this guard makes reconciliation
             * independently safe.
             */
            if (_hasReconciledInput &&
                !IsNewer(
                    acknowledgedInput,
                    _lastReconciledInputSequence))
            {
                return;
            }

            _hasReconciledInput = true;

            _lastReconciledInputSequence =
                acknowledgedInput;

            CharacterMotorState predictedState =
                _predictedMotor.CaptureState();

            float correctionDistance =
                Vector3.Distance(
                    predictedState.Position,
                    state.Position);

            NetworkDiagnostics diagnostics =
                NetworkRuntime.Current?.
                    Diagnostics;

            diagnostics?.ReportReconciliation(
                acknowledgedInput,
                correctionDistance);

            /*
             * The authoritative server state already includes
             * every input up to and including acknowledgedInput.
             */
            RemoveAcknowledgedInputs(
                acknowledgedInput);

            /*
             * Go back to the authoritative state.
             */
            _predictedMotor.RestoreState(
                new CharacterMotorState(
                    state.Position,
                    state.Rotation,
                    state.VerticalVelocity,
                    state.ControllerHeight));

            /*
             * Then replay everything the server has not
             * acknowledged yet.
             */
            for (int index = 0;
                 index < _pendingInputs.Count;
                 index++)
            {
                _predictedMotor.Simulate(
                    _pendingInputs[index],
                    NetworkTime.TickDelta);
            }

            ReportPendingInputCount();
        }

        private void RemoveAcknowledgedInputs(
            uint acknowledgedInput)
        {
            _pendingInputs.RemoveAll(
                command =>
                    !IsNewer(
                        command.InputSequence,
                        acknowledgedInput));
        }

        private void EnsureLocalPlayer()
        {
            if (_localPlayer != null &&
                _localPlayer.IsSpawned &&
                _localPlayer.IsLocallyOwned &&
                _inputReader != null &&
                _predictedMotor != null)
            {
                return;
            }

            ClearLocalPlayer();

            foreach (
                NetObject netObject
                in _world.Objects)
            {
                if (netObject == null ||
                    !netObject.IsSpawned ||
                    !netObject.IsLocallyOwned)
                {
                    continue;
                }

                if (!netObject.TryGetComponent(
                        out PlayerInputReader reader))
                {
                    continue;
                }

                if (!netObject.TryGetComponent(
                        out CharacterMotor predictedMotor))
                {
                    continue;
                }

                _localPlayer =
                    netObject;

                _inputReader =
                    reader;

                _predictedMotor =
                    predictedMotor;

                _inputReader.Activate();

                StartPrediction();

                Debug.Log(
                    $"Local player " +
                    $"{_localPlayer.NetworkId}: " +
                    $"prediction enabled = " +
                    $"{_predictionEnabled}");

                return;
            }
        }

        private void StartPrediction()
        {
            NetworkRuntime runtime =
                NetworkRuntime.Current;

            _pendingInputs.Clear();

            _lastReconciledInputSequence = 0;
            _hasReconciledInput = false;

            /*
             * A listen server shares one authoritative object
             * between its server and local client.
             *
             * Sending input is still required, but local
             * prediction would simulate the same CharacterMotor
             * twice.
             */
            if (runtime == null ||
                !runtime.RunsClient ||
                runtime.RunsServer)
            {
                _predictionEnabled = false;

                ReportPendingInputCount();

                return;
            }

            _predictionEnabled = true;

            _predictedMotor?.
                SetSimulationEnabled(
                    true);

            ReportPendingInputCount();
        }

        private void StopPrediction()
        {
            _pendingInputs.Clear();

            _predictionEnabled = false;

            ReportPendingInputCount();

            NetworkRuntime runtime =
                NetworkRuntime.Current;

            /*
             * Dedicated client:
             * stop local CharacterMotor simulation.
             *
             * Listen server:
             * server owns the CharacterMotor, so don't disable it.
             */
            if (runtime == null ||
                !runtime.RunsServer)
            {
                _predictedMotor?.
                    SetSimulationEnabled(
                        false);
            }
        }

        private void OnObjectDespawned(
            uint networkId)
        {
            if (_localPlayer == null ||
                _localPlayer.NetworkId !=
                    networkId)
            {
                return;
            }

            ClearLocalPlayer();
        }

        private void ClearLocalPlayer()
        {
            _inputReader?.
                Deactivate();

            StopPrediction();

            _inputReader = null;
            _predictedMotor = null;
            _localPlayer = null;

            ResetInputState();
        }

        private void ResetInputState()
        {
            _inputHistory.Clear();
            _pendingInputs.Clear();

            _nextInputSequence = 1;

            _lastReconciledInputSequence = 0;
            _hasReconciledInput = false;

            ReportPendingInputCount();
        }

        private uint AllocateNextInputSequence()
        {
            uint sequence =
                _nextInputSequence++;

            /*
             * Sequence zero is reserved for
             * "no command has been simulated".
             */
            if (sequence == 0)
            {
                sequence =
                    _nextInputSequence++;
            }

            return sequence;
        }

        private void ReportPendingInputCount()
        {
            NetworkRuntime runtime =
                NetworkRuntime.Current;

            if (runtime?.Diagnostics == null)
                return;

            uint networkId =
                _localPlayer != null
                    ? _localPlayer.NetworkId
                    : 0;

            runtime.Diagnostics.ReportPendingInputs(
                _pendingInputs.Count,
                networkId);
        }

        private static bool IsNewer(
            uint candidate,
            uint reference)
        {
            return
                candidate != reference &&
                unchecked(
                    (int)(candidate - reference)) > 0;
        }
    }
}