using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;

namespace PlateBack.Application.Features.Uploads;

/// <summary>
/// POST /uploads/images: a listing photo. The type is decided from the file's
/// first bytes, not from its name or the Content-Type the client sent, so a
/// renamed text file is rejected.
/// </summary>
public sealed class UploadImageHandler(IImageStorage storage)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public async Task<string> HandleAsync(Stream? content, long length, CancellationToken cancellationToken)
    {
        if (content is null || length == 0)
        {
            throw AppException.Validation("file", "Choose a photo to upload.");
        }
        if (length > MaxBytes)
        {
            throw AppException.Validation("file", "The photo must be 10 MB or smaller.");
        }

        var header = new byte[12];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var extension = DetectExtension(header.AsSpan(0, read))
            ?? throw AppException.Validation("file", "Use a JPEG, PNG or WebP photo.");

        using var buffer = new MemoryStream();
        await buffer.WriteAsync(header.AsMemory(0, read), cancellationToken);
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return await storage.SaveAsync(buffer, extension, cancellationToken);
    }

    /// <summary>Recognises JPEG, PNG and WebP by their signatures.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }
        return null;
    }
}
