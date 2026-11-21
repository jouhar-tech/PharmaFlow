namespace PharmaFlow.Services;

public sealed record SupabaseStoredObject(byte[] Content, string ContentType);

public interface ISupabaseStorageService
{
    Task UploadAsync(
        string objectPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<SupabaseStoredObject?> DownloadAsync(
        string objectPath,
        string? fallbackContentType = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string objectPath,
        CancellationToken cancellationToken = default);
}
