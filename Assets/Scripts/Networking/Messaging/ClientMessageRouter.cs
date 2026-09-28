using System;
using System.Collections.Generic;

namespace Networking
{
    public sealed class ClientMessageRouter
    {
        private readonly Dictionary<
            NetworkMessageType,
            Action<byte[]>> _handlers =
                new Dictionary<
                    NetworkMessageType,
                    Action<byte[]>>();

        public void Register(
            NetworkMessageType type,
            Action<byte[]> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(
                    nameof(handler));
            }

            if (_handlers.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"A client handler for {type} " +
                    "is already registered.");
            }

            _handlers.Add(
                type,
                handler);
        }

        public void Unregister(
            NetworkMessageType type,
            Action<byte[]> handler)
        {
            if (_handlers.TryGetValue(
                    type,
                    out Action<byte[]> existing) &&
                existing == handler)
            {
                _handlers.Remove(type);
            }
        }

        public bool Dispatch(
            NetworkMessageType type,
            byte[] payload)
        {
            if (!_handlers.TryGetValue(
                    type,
                    out Action<byte[]> handler))
            {
                return false;
            }

            handler(payload);

            return true;
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}