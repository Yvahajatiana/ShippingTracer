using FluentAssertions;
using ParcelFlow.Delivery.Application;
using Xunit;

namespace ParcelFlow.Delivery.UnitTests;

public sealed class ProofImagesTests
{
    private static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=");
    private static byte[] Jpeg() => Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==");

    [Fact]
    public void Real_png_signature_and_png_or_jpeg_photos_are_recognized()
    {
        ProofImages.IsValidSignature(Png()).Should().BeTrue();
        ProofImages.IsValidPhoto(Png()).Should().BeTrue();
        ProofImages.IsValidPhoto(Jpeg()).Should().BeTrue();
        ProofImages.PhotoContentType(Png()).Should().Be("image/png");
        ProofImages.PhotoContentType(Jpeg()).Should().Be("image/jpeg");
        ProofImages.IsValidSignature(Jpeg()).Should().BeFalse();
        ProofImages.IsValidSignature(null).Should().BeTrue();
        ProofImages.IsValidPhoto(null).Should().BeTrue();
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    [InlineData("chunk-overflow")]
    public void Invalid_png_containers_are_rejected(string corruption)
    {
        var image = Png();
        switch (corruption)
        {
            case "checksum": image[31] ^= 255; break;
            case "truncated": image = image[..^1]; break;
            case "trailing": image = [.. image, 0]; break;
            case "chunk-overflow": image[8] = 255; break;
        }
        ProofImages.IsValidSignature(image).Should().BeFalse();
        ProofImages.IsValidPhoto(image).Should().BeFalse();
    }

    [Fact]
    public void Truncated_jpeg_or_oversized_declared_dimensions_are_rejected()
    {
        ProofImages.IsValidPhoto(Jpeg()[..^1]).Should().BeFalse();
        var image = Jpeg();
        var frame = Enumerable.Range(0, image.Length - 1).First(i => image[i] == 255 && image[i + 1] == 192);
        image[frame + 7] = 32;
        image[frame + 8] = 1; // Width 8193: header remains a bounded valid segment.
        ProofImages.IsValidPhoto(image).Should().BeFalse();
    }

    [Fact]
    public void Payload_budgets_are_checked_before_parsing_large_images()
    {
        ProofImages.IsValidSignature(new byte[ProofImages.MaxSignatureBytes + 1]).Should().BeFalse();
        ProofImages.IsValidPhoto(new byte[ProofImages.MaxPhotoBytes + 1]).Should().BeFalse();
    }

    [Fact]
    public void Malformed_headers_cannot_escape_validation_as_parser_exceptions()
    {
        var random = new Random(42);
        for (var size = 0; size < 512; size++)
        {
            var content = new byte[size]; random.NextBytes(content);
            if (size > 8) new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(content, 0);
            ProofImages.IsValidPhoto(content).Should().BeFalse();
            ProofImages.IsValidSignature(content).Should().BeFalse();
        }
        ProofImages.IsValidPhoto([255, 216, 255, 217]).Should().BeFalse();
        ProofImages.IsValidSignature([]).Should().BeFalse();
        var invalidType = () => ProofImages.PhotoContentType([1, 2, 3]);
        invalidType.Should().Throw<ArgumentException>();
    }
}
