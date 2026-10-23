using FreeTheLights.Core;
using Xunit;

namespace FreeTheLights.Tests
{
    public class LedColorTests
    {
        [Theory]
        [InlineData("#FFFF0000", 255, 255, 0, 0)]
        [InlineData("#00000000", 0, 0, 0, 0)]
        [InlineData("#00FF00", 255, 0, 255, 0)]
        [InlineData("#F00", 255, 255, 0, 0)]
        [InlineData("80112233", 0x80, 0x11, 0x22, 0x33)]
        [InlineData("Red", 255, 255, 0, 0)]
        [InlineData("blue", 255, 0, 0, 255)]
        public void Parses_hex_and_names(string text, int a, int r, int g, int b)
        {
            Assert.Equal(new LedColor((byte)a, (byte)r, (byte)g, (byte)b), LedColor.Parse(text));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("#12")]
        [InlineData("notacolor")]
        public void Unparseable_is_transparent(string text)
        {
            Assert.True(LedColor.Parse(text).IsTransparent);
        }

        [Theory]
        [InlineData("#FFFF0000")]
        [InlineData("blue")]
        [InlineData("not a color")]
        [InlineData("")]
        [InlineData(null)]
        public void Cached_parsing_matches_parsing(string text)
        {
            Assert.Equal(LedColor.Parse(text), LedColor.ParseCached(text));
            Assert.Equal(LedColor.Parse(text), LedColor.ParseCached(text));
        }

        [Theory]
        [InlineData(0.0, 255, 0, 0)]
        [InlineData(1 / 6.0, 255, 255, 0)]
        [InlineData(1 / 3.0, 0, 255, 0)]
        [InlineData(2 / 3.0, 0, 0, 255)]
        [InlineData(1.0, 255, 0, 0)]
        [InlineData(-1 / 3.0, 0, 0, 255)]
        public void Hues_go_round_the_rainbow(double hue, int r, int g, int b)
        {
            Assert.Equal(LedColor.FromRgb((byte)r, (byte)g, (byte)b), LedColor.FromHue(hue));
        }

        [Fact]
        public void Hex_roundtrip()
        {
            var c = new LedColor(0xFF, 0x12, 0xAB, 0x00);
            Assert.Equal(c, LedColor.Parse(c.ToHex()));
        }

        [Fact]
        public void Scale_keeps_alpha()
        {
            var c = LedColor.FromRgb(200, 100, 50).Scale(0.5);
            Assert.Equal(new LedColor(255, 100, 50, 25), c);
        }
    }
}
