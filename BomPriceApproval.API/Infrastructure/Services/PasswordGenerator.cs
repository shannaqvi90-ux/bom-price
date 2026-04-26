using System.Security.Cryptography;

namespace BomPriceApproval.API.Infrastructure.Services;

public static class PasswordGenerator
{
    private const string Lowercase = "abcdefghijkmnpqrstuvwxyz";   // no l, o
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I, O
    private const string Digits = "23456789";                       // no 0, 1
    private const string Specials = "!@#$%^&*";

    public static string Generate(int length = 12)
    {
        if (length < 4) throw new ArgumentException("length >= 4 required for 4 char classes", nameof(length));
        var chars = new char[length];

        // Guarantee one of each class
        chars[0] = PickRandom(Lowercase);
        chars[1] = PickRandom(Uppercase);
        chars[2] = PickRandom(Digits);
        chars[3] = PickRandom(Specials);

        // Fill rest from combined pool
        var pool = Lowercase + Uppercase + Digits + Specials;
        for (int i = 4; i < length; i++) chars[i] = PickRandom(pool);

        // Shuffle (Fisher-Yates with crypto RNG)
        for (int i = length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private static char PickRandom(string source)
        => source[RandomNumberGenerator.GetInt32(source.Length)];
}
