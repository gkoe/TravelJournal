using TravelJournal.Core.MapRendering;
using TravelJournal.Core.MapRendering.Models;
using TravelJournal.Core.MapRendering.TileSources;
using FluentAssertions;
using System.Net;
using System.Net.Http;

namespace TravelJournal.Core.Tests.MapRendering;

public class MapTilerTileSourceTests
{
    private static readonly MapTile SampleTile = new(11, 1100, 720);
    private const string ApiKey = "test-key";

    private static MapTilerTileSource BuildSource(
        string styleId = "outdoor-v2",
        string language = "de",
        string? customTemplate = null,
        string? apiKey = ApiKey)
    {
        var options = new MapRenderingOptions
        {
            MapTilerApiKey        = apiKey,
            StyleId               = styleId,
            Language              = language,
            CustomTileUrlTemplate = customTemplate,
        };
        var handler = new RecordingHandler();
        var http    = new HttpClient(handler);
        return new MapTilerTileSource(http, options);
    }

    // ── ProviderId ────────────────────────────────────────────

    [Fact]
    public void ProviderId_IncludesStyleAndLanguage()
    {
        var src = BuildSource("streets-v2", "de");
        src.ProviderId.Should().Be("maptiler-streets-v2-de");
    }

    [Fact]
    public void ProviderId_ChangesWhenStyleChanges()
    {
        var a = BuildSource("streets-v2", "de");
        var b = BuildSource("outdoor-v2", "de");
        a.ProviderId.Should().NotBe(b.ProviderId);
    }

    [Fact]
    public void ProviderId_IsCustomWhenTemplateSet()
    {
        var src = BuildSource(customTemplate: "https://example.com/{z}/{x}/{y}.png");
        src.ProviderId.Should().Be("maptiler-custom");
    }

    // ── URL building ──────────────────────────────────────────

    [Fact]
    public async Task GetTileAsync_BuildsCorrectUrl_ForStreetsV2()
    {
        var handler = new RecordingHandler();
        var options = new MapRenderingOptions
        {
            MapTilerApiKey = ApiKey,
            StyleId        = "streets-v2",
            Language       = "de",
        };
        var src = new MapTilerTileSource(new HttpClient(handler), options);

        await src.GetTileAsync(SampleTile, default);

        handler.LastUrl.Should().Be(
            $"https://api.maptiler.com/maps/streets-v2/256/11/1100/720.png?key={ApiKey}&lang=de");
    }

    [Fact]
    public async Task GetTileAsync_OmitsLangParam_WhenLanguageEmpty()
    {
        var handler = new RecordingHandler();
        var options = new MapRenderingOptions
        {
            MapTilerApiKey = ApiKey,
            StyleId        = "outdoor-v2",
            Language       = "",
        };
        var src = new MapTilerTileSource(new HttpClient(handler), options);

        await src.GetTileAsync(SampleTile, default);

        handler.LastUrl.Should().NotContain("lang=");
    }

    [Fact]
    public async Task GetTileAsync_ReplacesAllPlaceholders_ForCustomTemplate()
    {
        var handler  = new RecordingHandler();
        var template = "https://tiles.example.com/{z}/{x}/{y}?key={key}&language={lang}";
        var options  = new MapRenderingOptions
        {
            MapTilerApiKey        = ApiKey,
            CustomTileUrlTemplate = template,
            Language              = "en",
        };
        var src = new MapTilerTileSource(new HttpClient(handler), options);

        await src.GetTileAsync(SampleTile, default);

        handler.LastUrl.Should().Be(
            $"https://tiles.example.com/11/1100/720?key={ApiKey}&language=en");
    }

    [Fact]
    public async Task GetTileAsync_IgnoresStyleAndLanguage_WhenCustomTemplateSet()
    {
        var handler  = new RecordingHandler();
        var template = "https://tiles.example.com/{z}/{x}/{y}.png";
        var options  = new MapRenderingOptions
        {
            MapTilerApiKey        = ApiKey,
            StyleId               = "streets-v2",
            Language              = "de",
            CustomTileUrlTemplate = template,
        };
        var src = new MapTilerTileSource(new HttpClient(handler), options);

        await src.GetTileAsync(SampleTile, default);

        handler.LastUrl.Should().NotContain("streets-v2");
        handler.LastUrl.Should().NotContain("lang=");
    }

    // ── Validation ────────────────────────────────────────────

    [Fact]
    public void Constructor_Throws_WhenNoApiKeyAndNoTemplate()
    {
        var options = new MapRenderingOptions { MapTilerApiKey = null };
        var act     = () => new MapTilerTileSource(new HttpClient(), options);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_DoesNotThrow_WhenOnlyTemplateProvided()
    {
        var options = new MapRenderingOptions
        {
            MapTilerApiKey        = null,
            CustomTileUrlTemplate = "https://example.com/{z}/{x}/{y}.png",
        };
        var act = () => new MapTilerTileSource(new HttpClient(), options);
        act.Should().NotThrow();
    }

    // ── Fake handler ──────────────────────────────────────────

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string LastUrl { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri?.ToString() ?? "";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };
            return Task.FromResult(response);
        }
    }
}
