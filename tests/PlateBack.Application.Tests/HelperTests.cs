using PlateBack.Application.Common.Paging;
using PlateBack.Application.Features.Listings;
using PlateBack.Application.Features.Uploads;

namespace PlateBack.Application.Tests;

public class GeoTests
{
    [Fact]
    public void Thamel_to_Jhamsikhel_is_about_four_kilometres()
    {
        // Sunrise Bakery to Green Leaf Cafe in the demo data
        Geo.DistanceKm(27.7154, 85.3123, 27.6795, 85.3088).ShouldBe(4.0, tolerance: 0.1);
    }

    [Fact]
    public void The_bounding_box_contains_the_whole_circle()
    {
        var box = Geo.BoundingBox(27.7, 85.3, 5);
        Geo.DistanceKm(27.7, 85.3, box.MaxLat, 85.3).ShouldBe(5, tolerance: 0.05);
        Geo.DistanceKm(27.7, 85.3, 27.7, box.MaxLng).ShouldBe(5, tolerance: 0.05);
    }
}

public class ImageDetectionTests
{
    [Fact]
    public void Recognises_real_image_signatures()
    {
        UploadImageHandler.DetectExtension([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe(".jpg");
        UploadImageHandler.DetectExtension([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).ShouldBe(".png");
        UploadImageHandler.DetectExtension("RIFF\0\0\0\0WEBP"u8).ShouldBe(".webp");
    }

    [Fact]
    public void A_text_file_is_not_an_image() =>
        UploadImageHandler.DetectExtension("hello world!"u8).ShouldBeNull();
}

public class PagingTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 500, 1, 50)]
    [InlineData(3, 10, 3, 10)]
    public void Uses_the_API_defaults_and_limits(int? page, int? size, int expectedPage, int expectedSize)
    {
        var request = PageRequest.From(page, size);
        request.Page.ShouldBe(expectedPage);
        request.PageSize.ShouldBe(expectedSize);
    }
}
