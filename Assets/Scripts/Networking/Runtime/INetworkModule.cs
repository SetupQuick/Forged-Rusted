using System;

namespace Networking
{
    public interface INetworkModule : IDisposable
    {
        int TickOrder { get; }
        int DisposeOrder { get; }
        void Tick(double now, double deltaTime);
    }
}