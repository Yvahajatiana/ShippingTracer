using System.Security.Cryptography;
using System.Text;

namespace ParcelFlow.BuildingBlocks.Application.Identity;

/// <summary>
/// Name-based identifiers: the same scope and source identifiers always produce the same GUID, so an operation
/// replayed after a failure publishes the same EventId.
/// </summary>
/// <remarks>
/// The value is the first 16 bytes of SHA-256(UTF-8 scope || source GUID bytes), marked as an RFC 9562 version 8
/// (custom) UUID with the RFC variant. It is the algorithm of the Warehouse delivery exit scan identifier, so both
/// produce the same value for the same input. Scopes follow <c>service:fact:</c> so that the identifiers derived
/// for different facts never collide, even when they share a source.
/// </remarks>
public static class DeterministicGuid
{
    public static Guid Create(string scope, Guid source) => Create(scope, [source]);

    public static Guid Create(string scope, Guid first, Guid second) => Create(scope, [first, second]);

    private static Guid Create(string scope, ReadOnlySpan<Guid> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var scopeLength = Encoding.UTF8.GetByteCount(scope);
        var input = new byte[scopeLength + sources.Length * 16];
        Encoding.UTF8.GetBytes(scope, input);
        for (var index = 0; index < sources.Length; index++)
        {
            if (sources[index] == Guid.Empty) throw new ArgumentException("Source identifiers are required.", nameof(sources));
            sources[index].TryWriteBytes(input.AsSpan(scopeLength + index * 16, 16));
        }
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
