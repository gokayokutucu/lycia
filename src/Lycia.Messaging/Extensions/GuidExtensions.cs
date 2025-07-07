namespace Lycia.Messaging.Extensions
{
    public class GuidExtensions
    {
        public static Guid CreateVersion7()
        {
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var bytes = new byte[16];
            var random = new Random();

            // Timestamp: 48 bit = 6 bytes (big endian)
            bytes[0] = (byte)(unixTime >> 40);
            bytes[1] = (byte)(unixTime >> 32);
            bytes[2] = (byte)(unixTime >> 24);
            bytes[3] = (byte)(unixTime >> 16);
            bytes[4] = (byte)(unixTime >> 8);
            bytes[5] = (byte)(unixTime);

            // Random: fill remaining 10 bytes
            byte[] slice = new byte[10];
            random.NextBytes(slice);
            Array.Copy(slice, 0, bytes, 6, 10);

            // Set version (7) bits (high nibble of byte 6)
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

            // Set variant bits (RFC 4122)
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            return new Guid(bytes);
        }

    }
}
