using System;
using UnityEngine;

namespace Networking
{
    public readonly struct NetTransformState
    {
        public const int PayloadSize =
            sizeof(float) * 7;

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public NetTransformState(
            Vector3 position,
            Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public void Write(PacketWriter writer)
        {
            writer.Write(Position.x);
            writer.Write(Position.y);
            writer.Write(Position.z);

            writer.Write(Rotation.x);
            writer.Write(Rotation.y);
            writer.Write(Rotation.z);
            writer.Write(Rotation.w);
        }

        public static bool TryRead(
            PacketReader reader,
            out NetTransformState message)
        {
            message = default;

            if (reader == null ||
                reader.Remaining != PayloadSize)
            {
                return false;
            }

            try
            {
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

                if (!IsFinite(position) ||
                    !IsFinite(rotation))
                {
                    return false;
                }

                float magnitudeSquared =
                    rotation.x * rotation.x +
                    rotation.y * rotation.y +
                    rotation.z * rotation.z +
                    rotation.w * rotation.w;

                if (magnitudeSquared < 0.0001f ||
                    float.IsInfinity(magnitudeSquared))
                {
                    return false;
                }

                message =
                    new NetTransformState(
                        position,
                        rotation.normalized);

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
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(
            Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}