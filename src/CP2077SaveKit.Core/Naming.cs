using System.Globalization;
using System.Text;

namespace CP2077SaveKit.Core;

/// <summary>Cosmetic name formatting — turns the AIO catalog's ALL-CAPS names into game-style
/// title case ("MALORIAN ARMS 3156" -> "Malorian Arms 3156").</summary>
public static class Naming
{
    public static string Pretty(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        // Only re-case strings that are essentially all-caps; leave already-cased text alone.
        var letters = s.Where(char.IsLetter).ToArray();
        if (letters.Length > 0 && letters.Any(char.IsLower)) return s;

        var sb = new StringBuilder(s.Length);
        bool startWord = true;
        foreach (var ch in s)
        {
            if (startWord && char.IsLetter(ch)) sb.Append(char.ToUpper(ch, CultureInfo.InvariantCulture));
            else if (char.IsLetter(ch)) sb.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            else sb.Append(ch);
            // a new word starts after a space, '/', '-', '(' etc. (but not after '.' so "Mk.5" stays)
            startWord = ch is ' ' or '/' or '-' or '(' or '_';
        }
        return sb.ToString();
    }
}
