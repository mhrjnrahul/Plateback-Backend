using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using PlateBack.Application.Common.Abstractions;

namespace PlateBack.Infrastructure.Storage;

/// <summary>Where uploaded photos go ("Uploads" section).</summary>
public sealed class UploadOptions
{
    public const string Section = "Uploads";

    /// <summary>Folder on disk, relative to the API's content root.</summary>
    public string Path { get; set; } = "uploads";

    /// <summary>URL path the folder is served under.</summary>
    public string RequestPath { get; set; } = "/uploads";
}

/// <summary>
/// Saves photos to a local folder that the API serves as static files. Fine
/// for one server; Week 6 can swap in cloud storage behind the same interface.
/// </summary>
public sealed class LocalImageStorage(IOptions<UploadOptions> options, IHostEnvironment environment) : IImageStorage
{
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        var folder = FolderPath(options.Value, environment);
        Directory.CreateDirectory(folder);
        // A random name: the client's file name is never used on disk.
        var name = $"{Guid.CreateVersion7():N}{extension}";
        await using (var file = File.Create(System.IO.Path.Combine(folder, name)))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        return $"{options.Value.RequestPath.TrimEnd('/')}/{name}";
    }

    public static string FolderPath(UploadOptions options, IHostEnvironment environment) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(environment.ContentRootPath, options.Path));
}
