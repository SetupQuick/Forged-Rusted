using System;
using UnityEngine;

namespace Networking
{
    public readonly struct PlayerMovementStateMessage
    {
        /*
         * uint:
         *   NetworkId
         *   ServerTick
         *   AcknowledgedInputSequence
         *
         * floats:
         *   Position: 3
         *   Rotation: 4
         *   VerticalVelocity: 1
         *   ControllerHeight: 1
         *
         * Optional ability block (only once the player has used a
         * movement ability, so most states stay small):
         *   7 uints (flag / timer sequences), dash direction x z,
         *   grapple point xyz, 1 byte grappling.
         */
        public const int PayloadSize =
            (sizeof(uint) * 3) +
            (sizeof(float) * 9);

        public const int AbilityBlockSize =
            (sizeof(uint) * 7) +
            (sizeof(float) * 5) +
            1;

        public uint NetworkId { get; }

        public uint ServerTick { get; }

        public uint AcknowledgedInputSequence
        {
            get;
        }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public float VerticalVelocity { get; }

        public float ControllerHeight { get; }

        public bool HasAbilityState { get; }

        public MovementAbilityState AbilityState { get; }

        public PlayerMovementStateMessage(
            uint networkId,
            uint serverTick,
            uint acknowledgedInputSequence,
            Vector3 position,
            Quaternion rotation,
            float verticalVelocity,
            float controllerHeight,
            MovementAbilityState abilityState = default,
            bool hasAbilityState = false)
        {
            AbilityState =
                abilityState;

            HasAbilityState =
                hasAbilityState;

            NetworkId =
                networkId;

            ServerTick =
                serverTick;

            AcknowledgedInputSequence =
                acknowledgedInputSequence;

            Position =
                position;

            Rotation =
                rotation;

            VerticalVelocity =
                verticalVelocity;

            ControllerHeight =
                controllerHeight;
        }

        public void Write(
            PacketWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(
                    nameof(writer));
            }

            writer.Write(
                NetworkId);

            writer.Write(
                ServerTick);

            writer.Write(
                AcknowledgedInputSequence);

            writer.Write(Position.x);
            writer.Write(Position.y);
            writer.Write(Position.z);

            writer.Write(Rotation.x);
            writer.Write(Rotation.y);
            writer.Write(Rotation.z);
            writer.Write(Rotation.w);

            writer.Write(
                VerticalVelocity);

            writer.Write(
                ControllerHeight);

            if (!HasAbilityState)
                return;

            MovementAbilityState a = AbilityState;
            writer.Write(a.LastDashFlag);
            writer.Write(a.LastGrappleFlag);
            writer.Write(a.LastJumpFlag);
            writer.Write(a.DashSequence);
            writer.Write(a.DoubleJumpSequence);
            writer.Write(a.GroundSequence);
            writer.Write(a.GrappleSequence);
            writer.Write(a.DashDirection.x);
            writer.Write(a.DashDirection.z);
            writer.Write(a.GrapplePoint.x);
            writer.Write(a.GrapplePoint.y);
            writer.Write(a.GrapplePoint.z);
            writer.Write((byte)(a.Grappling ? 1 : 0));
        }

        /// <summary>The motor state this message describes.</summary>
        public CharacterMotorState ToMotorState() =>
            new CharacterMotorState(
                Position,
                Rotation,
                VerticalVelocity,
                ControllerHeight,
                AbilityState,
                hasAbilityState: true); // absent block = nothing used yet (all zero)

        public static bool TryRead(
            byte[] data,
            out PlayerMovementStateMessage message)
        {
            message = default;

            if (data == null ||
                (data.Length != PayloadSize &&
                 data.Length != PayloadSize + AbilityBlockSize))
            {
                return false;
            }

            try
            {
                var reader =
                    new PacketReader(data);

                uint networkId =
                    reader.ReadUInt32();

                uint serverTick =
                    reader.ReadUInt32();

                uint acknowledgedInputSequence =
                    reader.ReadUInt32();

                var position =
                    new Vector3(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat());

                var rotation =
                    new Quaternion(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat());

                float verticalVelocity =
                    reader.ReadFloat();

                float controllerHeight =
                    reader.ReadFloat();

                bool hasAbilities =
                    reader.Remaining == AbilityBlockSize;

                MovementAbilityState abilities = default;

                if (hasAbilities)
                {
                    abilities.LastDashFlag = reader.ReadUInt32();
                    abilities.LastGrappleFlag = reader.ReadUInt32();
                    abilities.LastJumpFlag = reader.ReadUInt32();
                    abilities.DashSequence = reader.ReadUInt32();
                    abilities.DoubleJumpSequence = reader.ReadUInt32();
                    abilities.GroundSequence = reader.ReadUInt32();
                    abilities.GrappleSequence = reader.ReadUInt32();
                    float dashX = reader.ReadFloat();
                    float dashZ = reader.ReadFloat();
                    abilities.DashDirection = new Vector3(dashX, 0f, dashZ);
                    abilities.GrapplePoint = new Vector3(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat());
                    abilities.Grappling = reader.ReadByte() != 0;

                    if (!IsFinite(abilities.DashDirection) ||
                        !IsFinite(abilities.GrapplePoint))
                    {
                        return false;
                    }
                }

                if (reader.Remaining != 0)
                    return false;

                if (networkId == 0 ||
                    !IsFinite(position) ||
                    !IsFinite(rotation) ||
                    !IsFinite(verticalVelocity) ||
                    !IsFinite(controllerHeight) ||
                    controllerHeight <= 0f)
                {
                    return false;
                }

                float magnitudeSquared =
                    rotation.x * rotation.x +
                    rotation.y * rotation.y +
                    rotation.z * rotation.z +
                    rotation.w * rotation.w;

                if (magnitudeSquared < 0.0001f ||
                    float.IsInfinity(
                        magnitudeSquared))
                {
                    return false;
                }

                message =
                    new PlayerMovementStateMessage(
                        networkId,
                        serverTick,
                        acknowledgedInputSequence,
                        position,
                        rotation.normalized,
                        verticalVelocity,
                        controllerHeight,
                        abilities,
                        hasAbilities);

                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool IsFinite(
            Vector3 value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(
            Quaternion value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z) &&
                IsFinite(value.w);
        }

        private static bool IsFinite(
            float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}