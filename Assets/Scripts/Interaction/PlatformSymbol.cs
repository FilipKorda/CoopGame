using UnityEngine;

namespace CoopGame
{
    /// <summary>The symbols on the symbol panel's buttons and on the platforms they move.</summary>
    public enum PlatformSymbol : byte { Kwadrat, Trojkat, Gwiazda, Ksiezyc, Kolo, Slonce, Prostokat, Romb, Okrag }

    public static class PlatformSymbols
    {
        public const int Count = 9;

        static readonly string[] Names = { "Kwadrat", "Trójkąt", "Gwiazda", "Księżyc", "Koło", "Słońce", "Prostokąt", "Romb", "Okrąg" };

        static readonly Color[] Colors =
        {
            new Color(0.25f, 0.5f, 1f),     // Kwadrat: blue
            new Color(0.3f, 0.85f, 0.3f),   // Trójkąt: green
            new Color(1f, 0.85f, 0.15f),    // Gwiazda: yellow
            new Color(0.7f, 0.5f, 1f),      // Księżyc: violet
            new Color(0.95f, 0.25f, 0.2f),  // Koło: red
            new Color(1f, 0.55f, 0.1f),     // Słońce: orange
            new Color(0.2f, 0.85f, 0.9f),   // Prostokąt: cyan
            new Color(1f, 0.4f, 0.75f),     // Romb: pink
            new Color(0.95f, 0.95f, 0.95f), // Okrąg: white
        };

        public static string DisplayName(PlatformSymbol symbol) => Names[(int)symbol];

        public static Color Tint(PlatformSymbol symbol) => Colors[(int)symbol];

        public static int Bit(PlatformSymbol symbol) => 1 << (int)symbol;

        /// <summary>Finds a symbol's name (Polish, any letter case) in a text, e.g. an object name like "Quad (2) księżyc".</summary>
        public static bool TryFind(string text, out PlatformSymbol symbol)
        {
            string lower = text.ToLowerInvariant();
            for (int i = 0; i < Count; i++)
                if (lower.Contains(Names[i].ToLowerInvariant()))
                {
                    symbol = (PlatformSymbol)i;
                    return true;
                }
            symbol = default;
            return false;
        }
    }
}
