using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace FreeTheLights.Core
{
    /// <summary>
    /// Immutable ARGB color used by the LED engine. Kept free of WPF/GDI types so the engine stays testable.
    /// An alpha of 0 means "transparent": the LED is not driven by this layer.
    /// </summary>
    public readonly struct LedColor : IEquatable<LedColor>
    {
        public static readonly LedColor Transparent = new LedColor(0, 0, 0, 0);
        public static readonly LedColor Off = new LedColor(255, 0, 0, 0);

        public LedColor(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public byte A { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public bool IsTransparent => A == 0;

        public static LedColor FromRgb(byte r, byte g, byte b) => new LedColor(255, r, g, b);

        /// <summary>The fully saturated, full brightness color of <paramref name="hue"/> (0..1 = red → green → blue → red; wraps).</summary>
        public static LedColor FromHue(double hue)
        {
            var h = (hue - Math.Floor(hue)) * 6;
            var sector = (int)h % 6;
            var f = h - Math.Floor(h);
            var up = (byte)Math.Round(255 * f);
            var down = (byte)Math.Round(255 * (1 - f));
            switch (sector)
            {
                case 0: return FromRgb(255, up, 0);
                case 1: return FromRgb(down, 255, 0);
                case 2: return FromRgb(0, 255, up);
                case 3: return FromRgb(0, down, 255);
                case 4: return FromRgb(up, 0, 255);
                default: return FromRgb(255, 0, down);
            }
        }

        /// <summary>
        /// Scales the color channels by <paramref name="factor"/> (0..1).
        /// </summary>
        public LedColor Scale(double factor)
        {
            if (factor >= 1) return this;
            if (factor <= 0) return new LedColor(A, 0, 0, 0);
            return new LedColor(A, (byte)Math.Round(R * factor), (byte)Math.Round(G * factor), (byte)Math.Round(B * factor));
        }

        /// <summary>
        /// Parses "#RGB", "#RRGGBB", "#AARRGGBB" (with or without '#') or a HTML/CSS color name.
        /// Returns <see cref="Transparent"/> for anything unparseable.
        /// </summary>
        public static LedColor Parse(string text)
        {
            return TryParse(text, out var color) ? color : Transparent;
        }

        private static readonly ConcurrentDictionary<string, LedColor> ParseCache = new ConcurrentDictionary<string, LedColor>(StringComparer.Ordinal);

        /// <summary>
        /// <see cref="Parse"/> with a small cache, for the render loop, which parses the same few setting strings every frame.
        /// Thread-safe; the cache is cleared when it grows large (every color slider step is a new string).
        /// </summary>
        public static LedColor ParseCached(string text)
        {
            if (text == null) return Transparent;
            if (ParseCache.TryGetValue(text, out var color)) return color;
            if (ParseCache.Count >= 256) ParseCache.Clear();
            color = Parse(text);
            ParseCache[text] = color;
            return color;
        }

        public static bool TryParse(string text, out LedColor color)
        {
            color = Transparent;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var s = text.Trim();
            var hasHash = s.StartsWith("#", StringComparison.Ordinal);
            var hex = hasHash ? s.Substring(1) : s;

            if (IsHex(hex))
            {
                switch (hex.Length)
                {
                    case 3:
                        color = FromRgb(Dup(hex[0]), Dup(hex[1]), Dup(hex[2]));
                        return true;
                    case 6:
                        color = FromRgb(Byte(hex, 0), Byte(hex, 2), Byte(hex, 4));
                        return true;
                    case 8:
                        color = new LedColor(Byte(hex, 0), Byte(hex, 2), Byte(hex, 4), Byte(hex, 6));
                        return true;
                }
            }

            if (hasHash) return false;

            var named = System.Drawing.Color.FromName(s);
            if (!named.IsKnownColor) return false;
            color = new LedColor(named.A, named.R, named.G, named.B);
            return true;
        }

        public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

        public override string ToString() => ToHex();

        public bool Equals(LedColor other) => A == other.A && R == other.R && G == other.G && B == other.B;

        public override bool Equals(object obj) => obj is LedColor other && Equals(other);

        public override int GetHashCode() => (A << 24) | (R << 16) | (G << 8) | B;

        public static bool operator ==(LedColor left, LedColor right) => left.Equals(right);

        public static bool operator !=(LedColor left, LedColor right) => !left.Equals(right);

        private static bool IsHex(string s)
        {
            if (s.Length == 0) return false;
            foreach (var c in s)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }
            return true;
        }

        private static byte Byte(string hex, int index) => byte.Parse(hex.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        private static byte Dup(char c) => byte.Parse(new string(c, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
