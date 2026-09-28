using System;

namespace Networking
{
    public interface INetworkTransport : IDisposable
    {
        bool IsRunning { get; }

        void StartServer(ushort port);

        ITransportHandle StartClient(
            string address,
            ushort port);

        bool Send(
            ITransportHandle destination,
            byte[] data);

        bool TryPollEvent(out TransportEvent transportEvent);

        void Stop();
    }
}