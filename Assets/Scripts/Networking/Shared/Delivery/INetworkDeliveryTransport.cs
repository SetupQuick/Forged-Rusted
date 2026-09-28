using System;

namespace Networking
{
    public interface INetworkDeliveryTransport : IDisposable
    {
        bool IsRunning { get; }

        void StartServer(ushort port);

        ITransportHandle StartClient(
            string address,
            ushort port);

        void RegisterRemote(ITransportHandle remote);

        void RemoveRemote(ITransportHandle remote);

        bool Send(
            ITransportHandle destination,
            NetworkMessageType messageType,
            byte[] payload,
            NetworkDelivery delivery);

        void Update(double now);

        bool TryPollEvent(
            out DeliveryEvent deliveryEvent);

        void Stop();
    }
}