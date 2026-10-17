using System.Security.Cryptography;
using ParcelFlow.Shipment.Application;

namespace ParcelFlow.Shipment.Infrastructure.Labels;

public sealed class TrackingNumberGenerator : ITrackingNumberGenerator
{
    public string Generate() => "PF" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
}
