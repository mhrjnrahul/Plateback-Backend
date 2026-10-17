using System.Security.Cryptography;

namespace PlateBack.Domain.Entities;

/// <summary>
/// Pickup codes: 8 characters from a 31-character alphabet without 0, 1, I, L
/// and O, shown as "K7M2-9QXA" (design doc 5.6). About 8.5 x 10^11 values.
/// </summary>
public static class PickupCode
{
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int Length = 8;

    /// <summary>A new random code, for example "K7M2-9QXA".</summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return Format(chars);
    }

    /// <summary>
    /// Turns what a person typed ("k7m2 9qxa", "K7M29QXA") into the stored form,
    /// or null when it cannot be a valid code.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }
        Span<char> chars = stackalloc char[Length];
        var count = 0;
        foreach (var c in input.ToUpperInvariant())
        {
            if (c is ' ' or '-')
            {
                continue;
            }
            if (count == Length || !Alphabet.Contains(c))
            {
                return null;
            }
            chars[count++] = c;
        }
        return count == Length ? Format(chars) : null;
    }

    private static string Format(ReadOnlySpan<char> chars) => $"{chars[..4]}-{chars[4..]}";
}
