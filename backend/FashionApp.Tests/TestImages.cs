using System.Net.Http.Headers;
using ImageMagick;
using ImageMagick.Drawing;

/// <summary>
/// EXIF orientation values used by the tests. Magick.NET exposes the same
/// values through its <see cref="Orientation"/> enum, but the tests keep a
/// minimal local enum so the fixture contract stays explicit.
/// </summary>
public enum ExifOrientation : ushort
{
    Normal = 1,
    Rotate90 = 6
}

public static class TestImages
{
    /// <summary>
    /// Creates a JPEG of the requested size with a navy rectangle over a pink
    /// background and EXIF orientation metadata. The stream is reset to the
    /// beginning so it can be read immediately.
    /// </summary>
    public static MemoryStream CreateJpeg(
        int width,
        int height,
        ExifOrientation orientation = ExifOrientation.Normal,
        string artist = "TestImages")
    {
        using var image = CreateImage(width, height);

        // ImageMagick keeps the orientation as an image attribute as well as
        // inside the EXIF profile; both must agree for the tag to survive a
        // JPEG round trip.
        image.Orientation = (Orientation)(ushort)orientation;
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)orientation);
        exif.SetValue(ExifTag.Artist, artist);
        image.SetProfile(exif);

        return Encode(image, MagickFormat.Jpeg);
    }

    public static MemoryStream CreatePng(int width, int height)
    {
        using var image = CreateImage(width, height);
        return Encode(image, MagickFormat.Png);
    }

    public static MemoryStream CreateWebp(int width, int height)
    {
        using var image = CreateImage(width, height);
        return Encode(image, MagickFormat.WebP);
    }

    public static MultipartFormDataContent Multipart(string name, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(name));
        content.Add(file, "file", name);
        return content;
    }

    public static MultipartFormDataContent Multipart(string name, Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Multipart(name, buffer.ToArray());
    }

    private static MagickImage CreateImage(int width, int height)
    {
        var image = new MagickImage(MagickColors.HotPink, (uint)width, (uint)height);
        image.Draw(new Drawables()
            .FillColor(MagickColors.Navy)
            .Rectangle(0, 0, width / 4, height / 4));
        return image;
    }

    private static MemoryStream Encode(MagickImage image, MagickFormat format)
    {
        var stream = new MemoryStream();
        image.Format = format;
        image.Write(stream);
        stream.Position = 0;
        return stream;
    }

    private static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
}
