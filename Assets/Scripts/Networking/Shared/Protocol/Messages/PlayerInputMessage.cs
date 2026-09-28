using System;
using UnityEngine;

namespace Networking
{
    [Flags]
    public enum PlayerInputButtons : byte
    {
        None = 0,
        Jump = 1 << 0,
        Sprint = 1 << 1,
        Crouch = 1 << 2
    }

    public readonly struct PlayerInputMessage
    {
        public const int CommandSize =
            sizeof(uint) +
            sizeof(float) +
            sizeof(float) +
            sizeof(float) +
            sizeof(byte);

        public uint NetworkId { get; }

        public uint InputSequence { get; }

        public Vector2 Move { get; }

        public float Yaw { get; }

        public PlayerInputButtons Buttons { get; }

        public bool Jump =>
            (Buttons & PlayerInputButtons.Jump) != 0;

        public bool Sprint =>
            (Buttons & PlayerInputButtons.Sprint) != 0;

        public bool Crouch =>
            (Buttons & PlayerInputButtons.Crouch) != 0;

        public PlayerInputMessage(
            uint networkId,
            uint inputSequence,
            Vector2 move,
            float yaw,
            PlayerInputButtons buttons)
        {
            NetworkId = networkId;
            InputSequence = inputSequence;
            Move = Vector2.ClampMagnitude(move, 1f);
            Yaw = Mathf.Repeat(yaw, 360f);
            Buttons = buttons;
        }

        public PlayerInputMessage WithoutJump()
        {
            return new PlayerInputMessage(
                NetworkId,
                InputSequence,
                Move,
                Yaw,
                Buttons & ~PlayerInputButtons.Jump);
        }

        internal void WriteCommand(PacketWriter writer)
        {
            writer.Write(InputSequence);
            writer.Write(Move.x);
            writer.Write(Move.y);
            writer.Write(Yaw);
            writer.Write((byte)Buttons);
        }

        internal static bool TryReadCommand(
            PacketReader reader,
            uint networkId,
            out PlayerInputMessage message)
        {
            message = default;

            if (reader == null ||
                networkId == 0 ||
                reader.Remaining < CommandSize)
            {
                return false;
            }

            uint inputSequence =
                reader.ReadUInt32();

            float moveX = reader.ReadFloat();
            float moveY = reader.ReadFloat();
            float yaw = reader.ReadFloat();

            var buttons =
                (PlayerInputButtons)
                reader.ReadByte();

            const PlayerInputButtons allowedButtons =
                PlayerInputButtons.Jump |
                PlayerInputButtons.Sprint |
                PlayerInputButtons.Crouch;

            if (inputSequence == 0 ||
                !IsFinite(moveX) ||
                !IsFinite(moveY) ||
                !IsFinite(yaw) ||
                (buttons & ~allowedButtons) != 0)
            {
                return false;
            }

            message = new PlayerInputMessage(
                networkId,
                inputSequence,
                new Vector2(moveX, moveY),
                yaw,
                buttons);

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
