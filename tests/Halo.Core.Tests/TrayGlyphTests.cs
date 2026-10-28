using Halo.Core.Tray;
using Xunit;

namespace Halo.Core.Tests;

public class TrayGlyphTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(32)]
    public void DrawsAnOpaqueDarkPillWithALightOutlineOnTransparency(int size)
    {
        var pixels = TrayGlyph.Render(size);

        Assert.Equal(size * size, pixels.Length);
        Assert.Equal(0u, pixels[0] >> 24); // corners are transparent
        Assert.Equal(0u, pixels[(size * size) - 1] >> 24);
        var centre = pixels[((size / 2) * size) + (size / 2)];
        Assert.Equal(255u, centre >> 24);
        Assert.True((centre & 0xFF) < 16, "centre should be black");
        var edge = pixels[(size / 2 * size) + 1]; // left end of the pill, on the outline
        Assert.True((edge & 0xFF) > 100, "outline should be light");

        // Premultiplied: no channel exceeds alpha.
        Assert.All(pixels, p => Assert.True((p & 0xFF) <= p >> 24 && ((p >> 8) & 0xFF) <= p >> 24 && ((p >> 16) & 0xFF) <= p >> 24));
    }

    [Fact]
    public void RejectsUnusableSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayGlyph.Render(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayGlyph.Render(1000));
    }
}
