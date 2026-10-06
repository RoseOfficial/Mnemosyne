using System.Text;

namespace Mnemosyne.Assembly.Tests;

/// <summary>The plugin's icon, which the installer downloads from the repository's main branch: a square PNG of at most 512 pixels (Dalamud
/// drops anything larger or not square), holding its image and nothing else, so no program name, path or date travels inside it.</summary>
public class IconFileTests
{
    [Fact]
    public void The_icon_is_a_square_png_of_at_most_512_pixels_holding_its_image_alone()
    {
        var png = File.ReadAllBytes(Path.Combine(AssemblyTests.RepoRoot(), "src", "Mnemosyne", "images", "icon.png"));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int Int(int at) => (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
        var (width, height) = (Int(16), Int(20));
        Assert.True(width == height && width <= 512, $"the icon is {width} x {height}");
        var chunks = new List<string>();
        for (var at = 8; at + 8 <= png.Length; at += 12 + Int(at)) chunks.Add(Encoding.ASCII.GetString(png, at + 4, 4));
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Distinct());
    }
}
