using Microsoft.AspNetCore.Mvc;

using PlateBack.Application.Features.Uploads;

namespace PlateBack.Api.Endpoints;

internal static class UploadEndpoints
{
    public sealed record ImageUploadResponse(string Url);

    public static void MapUploadEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/uploads/images", async (IFormFile? file, UploadImageHandler handler, HttpRequest request, CancellationToken ct) =>
            {
                await using var stream = file?.OpenReadStream();
                var path = await handler.HandleAsync(stream, file?.Length ?? 0, ct);
                var url = $"{request.Scheme}://{request.Host}{path}";
                return TypedResults.Created(url, new ImageUploadResponse(url));
            })
            .RequireAuthorization()
            .DisableAntiforgery() // Bearer-token API: no cookies are used for this request.
            .WithMetadata(new RequestSizeLimitAttribute(UploadImageHandler.MaxBytes + 64 * 1024))
            .WithTags("Uploads")
            .WithSummary("Upload a listing photo")
            .WithDescription("multipart/form-data with one field named file: JPEG, PNG or WebP, up to 10 MB. The type is checked from the file's content. Use the returned url as the listing's imageUrl.")
            .Accepts<IFormFile>("multipart/form-data")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);
    }
}
