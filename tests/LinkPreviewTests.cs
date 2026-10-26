using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

public sealed class LinkPreviewTests
{
    [Theory]
    [InlineData("see https://example.com/a?b=1.", "https://example.com/a?b=1")]
    [InlineData("(http://x.org/path)", "http://x.org/path")]
    [InlineData("no link here", null)]
    [InlineData("ftp://example.com", null)]
    public void FindUrl_TakesTheFirstWebAddress(string text, string? expected) =>
        Assert.Equal(expected, LinkPreview.FindUrl(text));

    [Fact]
    public void Parse_PrefersOpenGraph_AndResolvesThePicture()
    {
        const string html = """
            <html><head><title>Plain title</title>
            <meta content="Kopi &amp; Roti" property="og:title">
            <meta name='description' content='  Toko   roti  '>
            <meta property="og:image" content="/img/cover.jpg">
            </head></html>
            """;
        var (card, image) = LinkPreview.Parse(html, new Uri("https://shop.example/menu"));
        Assert.Equal("Kopi & Roti", card.Title);
        Assert.Equal("Toko roti", card.Description);
        Assert.Equal(new Uri("https://shop.example/img/cover.jpg"), image);
    }

    [Fact]
    public void Parse_FallsBackToTitleThenHost()
    {
        Assert.Equal("Hello", LinkPreview.Parse("<title>\n Hello </title>", new Uri("https://a.example/")).Card.Title);
        Assert.Equal("a.example", LinkPreview.Parse("<p>nothing</p>", new Uri("https://a.example/")).Card.Title);
    }
}
