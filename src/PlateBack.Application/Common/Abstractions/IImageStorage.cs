namespace PlateBack.Application.Common.Abstractions;

/// <summary>Stores uploaded listing photos.</summary>
public interface IImageStorage
{
    /// <summary>Saves the file and returns its public path, for example "/uploads/abc.jpg".</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);
}
