using System;
using System.Collections.Generic;

namespace Networking
{
    public sealed class FullInterestManager :
        IInterestManager
    {
        private readonly NetworkWorld _world;

        public FullInterestManager(
            NetworkWorld world)
        {
            _world = world ??
                throw new ArgumentNullException(
                    nameof(world));
        }

        public void GetVisibleObjects(
            Peer peer,
            List<NetObject> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(
                    nameof(results));
            }

            results.Clear();

            foreach (
                NetObject netObject
                in _world.Objects)
            {
                if (netObject == null ||
                    !netObject.IsSpawned)
                {
                    continue;
                }

                results.Add(
                    netObject);
            }
        }
    }
}