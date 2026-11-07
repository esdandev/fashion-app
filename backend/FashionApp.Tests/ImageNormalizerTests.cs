using System.Security.Cryptography;
using FashionApp.Api.Infrastructure.Storage;
using ImageMagick;

public sealed class ImageNormalizerTests
{
    [Fact]
    public async Task Normalizes_to_max_1600_and_creates_400_thumbnail()
    {
        var input = TestImages.CreateJpeg(2400, 1200, ExifOrientation.Rotate90);
        var result = await new MagickImageNormalizer().NormalizeAsync(input, default);

        using var original = new MagickImage(result.Original);
        using var thumb = new MagickImage(result.Thumbnail);
        // La rotación EXIF convierte 2400x1200 en 1200x2400 y después se
        // reduce al lado mayor de 1600 (sin rotar habría quedado 1600x800).
        Assert.Equal(800, (int)original.Width);
        Assert.Equal(1600, (int)original.Height);
        Assert.True(Math.Max(thumb.Width, thumb.Height) <= 400);
        Assert.Equal(64, result.ContentHash.Length);

        Assert.Equal((int)original.Width, result.Width);
        Assert.Equal((int)original.Height, result.Height);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(result.Original)).ToLowerInvariant(),
            result.ContentHash);
    }

    [Fact]
    public async Task Rejects_corrupt_input()
    {
        using var trash = new MemoryStream([1, 2, 3, 4, 5]);
        await Assert.ThrowsAsync<InvalidImageException>(
            () => new MagickImageNormalizer().NormalizeAsync(trash, default));
    }

    [Fact]
    public async Task Strips_all_metadata()
    {
        var input = TestImages.CreateJpeg(800, 600, ExifOrientation.Rotate90);

        using (var fixture = new MagickImage(input))
        {
            var values = fixture.GetExifProfile()!.Values.ToDictionary(v => v.Tag, v => v.GetValue());
            Assert.Equal(6, Convert.ToInt32(values[ExifTag.Orientation]));
            Assert.Equal("TestImages", values[ExifTag.Artist]);
        }

        input.Position = 0;
        var result = await new MagickImageNormalizer().NormalizeAsync(input, default);

        using var original = new MagickImage(result.Original);
        using var thumb = new MagickImage(result.Thumbnail);
        Assert.Null(original.GetExifProfile());
        Assert.Null(thumb.GetExifProfile());
    }

    [Fact]
    public async Task Does_not_upscale_images_below_the_limits()
    {
        var input = TestImages.CreateJpeg(800, 600);
        var result = await new MagickImageNormalizer().NormalizeAsync(input, default);

        using var original = new MagickImage(result.Original);
        using var thumb = new MagickImage(result.Thumbnail);
        Assert.Equal(800, (int)original.Width);
        Assert.Equal(600, (int)original.Height);
        Assert.Equal(400, (int)thumb.Width);
        Assert.Equal(300, (int)thumb.Height);
    }
}
