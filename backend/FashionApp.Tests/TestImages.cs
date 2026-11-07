using System.Net.Http.Headers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

/// <summary>
/// EXIF orientation values used by the tests. ImageSharp no longer ships this
/// enum, so the two values the suite needs live here.
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
        ExifOrientation orientation = ExifOrientation.Normal)
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(x => x.BackgroundColor(Color.HotPink));

        var rectWidth = Math.Max(1, width / 4);
        var rectHeight = Math.Max(1, height / 4);
        for (var y = 0; y < rectHeight; y++)
        {
            for (var x = 0; x < rectWidth; x++)
            {
                image[x, y] = Color.Navy.ToPixel<Rgba32>();
            }
        }

        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)orientation);
        image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "TestImages");

        var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        stream.Position = 0;
        return stream;
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

    private static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
}
