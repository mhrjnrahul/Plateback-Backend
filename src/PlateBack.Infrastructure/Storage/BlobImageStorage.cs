using System.Text.RegularExpressions;

using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Microsoft.Extensions.Options;

using PlateBack.Application.Common.Abstractions;

namespace PlateBack.Infrastructure.Storage;

/// <summary>Which photo store to use ("Storage" section).</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>"Local" (a folder on disk, the default) or "Blob" (Azure Blob Storage).</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>For Blob on Azure, for example https://platebackphotos.blob.core.windows.net. The App Service signs in with its managed identity.</summary>
    public string? BlobServiceUri { get; set; }

    /// <summary>For Blob locally: a connection string (for example the Azurite emulator's) instead of an identity.</summary>
    public string? BlobConnectionString { get; set; }

    public string BlobContainer { get; set; } = "listing-photos";

    public bool UsesBlob => string.Equals(Provider, "Blob", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A stored photo, ready to stream to the browser.</summary>
public sealed record StoredImage(Stream Content, string ContentType);

/// <summary>
/// Saves photos to a private Azure Blob Storage container (Week 6). The URL
/// stays /uploads/{name} on the API's own address and the API streams the
/// photo, so the container needs no public access and the browser sees one site.
/// </summary>
public sealed partial class BlobImageStorage(IOptions<StorageOptions> options, IOptions<UploadOptions> uploads) : IImageStorage
{
    private readonly Lazy<BlobContainerClient> container = new(() => CreateContainer(options.Value));

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        // A random name: the client's file name is never used.
        var name = $"{Guid.CreateVersion7():N}{extension}";
        await container.Value.GetBlobClient(name).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = ContentTypes.GetValueOrDefault(extension, "application/octet-stream"),
            },
        }, cancellationToken);
        return $"{uploads.Value.RequestPath.TrimEnd('/')}/{name}";
    }

    /// <summary>The photo, or null if the name is not one this class could have made or it does not exist.</summary>
    public async Task<StoredImage?> OpenAsync(string name, CancellationToken cancellationToken)
    {
        if (!StoredName().IsMatch(name))
        {
            return null; // never pass arbitrary input on as a blob name
        }
        try
        {
            var download = await container.Value.GetBlobClient(name).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new StoredImage(download.Value.Content, download.Value.Details.ContentType ?? "application/octet-stream");
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    private static BlobContainerClient CreateContainer(StorageOptions options) =>
        !string.IsNullOrEmpty(options.BlobConnectionString)
            ? new BlobContainerClient(options.BlobConnectionString, options.BlobContainer)
            : new BlobContainerClient(
                new Uri($"{options.BlobServiceUri?.TrimEnd('/') ?? throw new InvalidOperationException("Storage:BlobServiceUri is not set.")}/{options.BlobContainer}"),
                new DefaultAzureCredential());

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$")]
    private static partial Regex StoredName();
}
