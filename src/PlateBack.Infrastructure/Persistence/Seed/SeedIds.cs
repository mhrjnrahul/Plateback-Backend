using System.Security.Cryptography;
using System.Text;

namespace PlateBack.Infrastructure.Persistence.Seed;

/// <summary>
/// Stable GUIDs for demo data, derived from readable keys such as
/// "listing-pastry", so tests and docs can refer to the same rows every time.
/// </summary>
public static class SeedIds
{
    public static Guid For(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("plateback-seed:" + key));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3 (name-based)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(hash, bigEndian: true);
    }
}
