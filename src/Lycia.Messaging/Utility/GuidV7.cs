using System.Security.Cryptography;
using System;

namespace Lycia.Messaging.Utility;

/// <summary>
/// Cross-version UUIDv7 (Guid Version 7) generator. Uses native .NET 9+ if available,
/// falls back to compatible custom implementation for .NET 5/6/7/8/Standard2.0.
/// Always use GuidV7Helper.NewGuidV7()!
/// </summary>
public static class GuidV7
{
#if NET9_0_OR_GREATER
    public static Guid NewGuidV7()
    {
        // .NET 9 and later: native Version 7 support
        return Guid.CreateVersion7();
    }
#else
    public static Guid NewGuidV7()
    {
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] rand = new byte[10];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(rand); //  backward-compatible
        }

        byte[] bytes = new byte[16];

        // Timestamp: first 6 bytes (48 bits)
        bytes[0] = (byte)(unixTime >> 40);
        bytes[1] = (byte)(unixTime >> 32);
        bytes[2] = (byte)(unixTime >> 24);
        bytes[3] = (byte)(unixTime >> 16);
        bytes[4] = (byte)(unixTime >> 8);
        bytes[5] = (byte)(unixTime);

        // Random part (last 10 bytes)
        Buffer.BlockCopy(rand, 0, bytes, 6, 10);

        // Set version 7 (bits 48-51)
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        // Set variant (bits 64-65, RFC 4122)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
#endif
}