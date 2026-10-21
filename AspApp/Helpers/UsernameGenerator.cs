using System;
using System.Text;

public class UsernameGenerator
{
    private char[] Base36Chars { get; }
    private DateTime CustomEpoch { get; }
    private readonly Random _random = new Random();

    public UsernameGenerator()
    {
        Base36Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        CustomEpoch = new DateTime(2026, 10, 21, 0, 0, 0);
    }

    public string GetUniqueInMilliSeconds(string? hint = null)
    {
        hint = hint?.Substring(0, 30) ?? "User_";
        // 1. Get the 13-digit millisecond timestamp
        long milliseconds = (long)(DateTime.UtcNow - CustomEpoch).TotalMilliseconds;

        // 2. Convert it to Base-36
        StringBuilder result = new StringBuilder(hint);
        while (milliseconds > 0)
        {
            result.Insert(hint.Length, Base36Chars[milliseconds % 36]);
            milliseconds /= 36;
        }

        // 3. Append 2 random alphanumeric characters
        char rand1 = Base36Chars[_random.Next(36)];
        char rand2 = Base36Chars[_random.Next(36)];

        // Example output: "User_1ZK4P95Q"
        return $"{result}{rand1}{rand2}";
    }
}
