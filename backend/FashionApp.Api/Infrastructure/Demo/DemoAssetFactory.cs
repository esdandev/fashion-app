using ImageMagick;
using ImageMagick.Drawing;

namespace FashionApp.Api.Infrastructure.Demo;

/// <summary>
/// Deterministic placeholder art for the public demo: a flat color derived from
/// the garment palette, the garment name and its category. It exists only
/// because no licensed stock photos are available offline; the committed
/// <c>SeedAssets/*.webp</c> files are produced by this same code, and it is also
/// the fallback when an asset file is missing at startup.
/// </summary>
public static class DemoAssetFactory
{
    public static byte[] CreateWebp(DemoSeedItem item)
    {
        var background = new MagickColor(item.ColorHex);

        using var image = new MagickImage(background, 900, 1200);

        var ink = Luminance(background) > 0.55 ? MagickColors.Black : MagickColors.White;

        image.Draw(new Drawables()
            .FillColor(ink)
            .Font("Arial")
            .FontPointSize(58)
            .TextAlignment(TextAlignment.Center)
            .Text(450, 560, item.Name)
            .FontPointSize(38)
            .Text(450, 660, $"Demo · {item.Category}"));

        using var buffer = new MemoryStream();
        image.Format = MagickFormat.WebP;
        image.Quality = 82;
        image.Write(buffer);
        return buffer.ToArray();
    }

    private static double Luminance(IMagickColor<byte> color)
    {
        // Rec. 709 relative luminance over the 0..1 normalized channels.
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }
}
