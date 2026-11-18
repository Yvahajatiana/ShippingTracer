using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public enum DeliveryProofField { RecipientName, Signature, Photo }

/// <summary>Retention and encryption settings of the delivery proofs (section <c>DeliveryProofs</c>).</summary>
public sealed record DeliveryProofSettings(string EncryptionKey, IReadOnlyList<string> PreviousEncryptionKeys, int RetentionDays, TimeSpan PurgeInterval)
{
    public const string Section = "DeliveryProofs";
    public const int DefaultRetentionDays = 90;

    public static DeliveryProofSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var key = section["EncryptionKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("DeliveryProofs:EncryptionKey is required (base64 of 32 random bytes, e.g. `openssl rand -base64 32`).");
        var previous = (section["PreviousEncryptionKeys"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var retentionDays = DefaultRetentionDays;
        if (!string.IsNullOrWhiteSpace(section["RetentionDays"]) &&
            (!int.TryParse(section["RetentionDays"], NumberStyles.None, CultureInfo.InvariantCulture, out retentionDays) || retentionDays is < 1 or > 3650))
            throw new InvalidOperationException("DeliveryProofs:RetentionDays must be a whole number of days between 1 and 3650.");
        var purgeInterval = TimeSpan.FromHours(1);
        if (!string.IsNullOrWhiteSpace(section["PurgeInterval"]) &&
            (!TimeSpan.TryParse(section["PurgeInterval"], CultureInfo.InvariantCulture, out purgeInterval) || purgeInterval <= TimeSpan.Zero))
            throw new InvalidOperationException("DeliveryProofs:PurgeInterval must be a positive duration (e.g. 01:00:00).");
        return new(key.Trim(), previous, retentionDays, purgeInterval);
    }
}

/// <summary>
/// AES-256-GCM encryption of the personal data kept as delivery proof. Stored layout:
/// format marker (5 bytes) | key id (4) | nonce (12) | tag (16) | ciphertext. The key id is derived
/// from the key, so previous keys keep decrypting during a rotation. The associated data binds each
/// value to its field: a signature cannot be swapped into the photo column. Values written before
/// encryption existed (no marker) are returned unchanged until the maintenance job encrypts them.
/// </summary>
public sealed class DeliveryProofProtector
{
    // Leading zero byte: no recipient name (control characters are rejected), PNG or JPEG starts so.
    private static readonly byte[] Marker = [0x00, (byte)'P', (byte)'F', (byte)'E', 0x01];
    private const int KeyIdSize = 4, NonceSize = 12, TagSize = 16, KeySize = 32;
    private static readonly int HeaderSize = Marker.Length + KeyIdSize + NonceSize + TagSize;

    private readonly byte[] _activeKey;
    private readonly uint _activeKeyId;
    private readonly Dictionary<uint, byte[]> _keys = [];

    public DeliveryProofProtector(DeliveryProofSettings settings)
    {
        _activeKey = DecodeKey(settings.EncryptionKey, "DeliveryProofs:EncryptionKey");
        _activeKeyId = KeyId(_activeKey);
        _keys[_activeKeyId] = _activeKey;
        foreach (var previous in settings.PreviousEncryptionKeys)
        {
            var key = DecodeKey(previous, "DeliveryProofs:PreviousEncryptionKeys");
            _keys.TryAdd(KeyId(key), key);
        }
    }

    /// <summary>Marker prefix of an encrypted value, for SQL filters on legacy plaintext rows.</summary>
    public static ReadOnlySpan<byte> FormatMarker => Marker;

    public static bool IsProtected(ReadOnlySpan<byte> stored) => stored.Length >= HeaderSize && stored.StartsWith(Marker);

    public byte[] Protect(ReadOnlySpan<byte> plaintext, DeliveryProofField field)
    {
        var stored = new byte[HeaderSize + plaintext.Length];
        Marker.CopyTo(stored, 0);
        BinaryPrimitives.WriteUInt32BigEndian(stored.AsSpan(Marker.Length, KeyIdSize), _activeKeyId);
        var nonce = stored.AsSpan(Marker.Length + KeyIdSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_activeKey, TagSize);
        aes.Encrypt(nonce, plaintext, stored.AsSpan(HeaderSize), stored.AsSpan(Marker.Length + KeyIdSize + NonceSize, TagSize), AssociatedData(field));
        return stored;
    }

    public byte[] Unprotect(byte[] stored, DeliveryProofField field)
    {
        if (!IsProtected(stored)) return stored;
        var keyId = BinaryPrimitives.ReadUInt32BigEndian(stored.AsSpan(Marker.Length, KeyIdSize));
        if (!_keys.TryGetValue(keyId, out var key))
            throw new CryptographicException($"Delivery proof encrypted with unknown key {keyId:x8}: add it to DeliveryProofs:PreviousEncryptionKeys.");
        var plaintext = new byte[stored.Length - HeaderSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(stored.AsSpan(Marker.Length + KeyIdSize, NonceSize), stored.AsSpan(HeaderSize),
            stored.AsSpan(Marker.Length + KeyIdSize + NonceSize, TagSize), plaintext, AssociatedData(field));
        return plaintext;
    }

    public byte[] ProtectText(string value, DeliveryProofField field) => Protect(Encoding.UTF8.GetBytes(value), field);
    public string UnprotectText(byte[] stored, DeliveryProofField field) => Encoding.UTF8.GetString(Unprotect(stored, field));

    private static byte[] AssociatedData(DeliveryProofField field) => Encoding.ASCII.GetBytes("parcelflow:delivery-proof:" + field);

    private static uint KeyId(byte[] key) => BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(key));

    private static byte[] DecodeKey(string value, string setting)
    {
        var buffer = new byte[KeySize + 1];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var written) || written != KeySize)
            throw new InvalidOperationException($"{setting} must be the base64 encoding of exactly {KeySize} bytes.");
        return buffer[..KeySize];
    }
}
