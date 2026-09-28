using System;
using System.Collections.Generic;

namespace Networking
{
    public sealed class ServerMessageRouter
    {
        private readonly Dictionary<
            NetworkMessageType,
            Action<Peer, byte[]>> _handlers =
                new Dictionary<
                    NetworkMessageType,
                    Action<Peer, byte[]>>();

        public void Register(
            NetworkMessageType type,
            Action<Peer, byte[]> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(
                    nameof(handler));
            }

            if (_handlers.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"A server handler for {type} " +
                    "is already registered.");
            }

            _handlers.Add(
                type,
                handler);
        }

        public void Unregister(
            NetworkMessageType type,
            Action<Peer, byte[]> handler)
        {
            if (_handlers.TryGetValue(
                    type,
                    out Action<Peer, byte[]> existing) &&
                existing == handler)
            {
                _handlers.Remove(type);
            }
        }

        public bool Dispatch(
            Peer peer,
            NetworkMessageType type,
            byte[] payload)
        {
            if (!_handlers.TryGetValue(
                    type,
                    out Action<Peer, byte[]> handler))
            {
                return false;
            }

            handler(
                peer,
                payload);

            return true;
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}