using System;
using System.Net;

namespace Networking
{
    public sealed class UdpTransportHandle : ITransportHandle, IEquatable<UdpTransportHandle>
    {
        public IPEndPoint EndPoint { get; }

        public UdpTransportHandle(IPEndPoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            EndPoint = new IPEndPoint(endpoint.Address, endpoint.Port);
        }

        public bool Equals(UdpTransportHandle other)
        {
            return other != null && EndPoint.Equals(other.EndPoint);
        }

        public override bool Equals(object obj)
        {
            return obj is UdpTransportHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return EndPoint.GetHashCode();
        }
    }
}
