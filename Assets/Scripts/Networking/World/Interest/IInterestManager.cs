using System.Collections.Generic;

namespace Networking
{
    public interface IInterestManager
    {
        void GetVisibleObjects(
            Peer peer,
            List<NetObject> results);
    }
}