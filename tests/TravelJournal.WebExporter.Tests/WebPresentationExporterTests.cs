using SixLabors.ImageSharp;
using TravelJournal.Core.Models;
using TravelJournal.Core.Services;
using TravelJournal.WebExporter.Services;

namespace TravelJournal.WebExporter.Tests;

public class WebPresentationExporterTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "src_" + Guid.NewGuid());
    private readonly string _out = Path.Combine(Path.GetTempPath(), "out_" + Guid.NewGuid());
    private readonly WebPresentationExporter _sut;

    public WebPresentationExporterTests()
    {
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_out);
        _sut = new WebPresentationExporter(
            new TourCsvReader(),
            new ManifestBuilder(),
            new ImageOptimizer());
    }

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        Directory.Delete(_out, recursive: true);
    }

    private async Task WriteTestJpegAsync(string filename)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(100, 100);
        await img.SaveAsJpegAsync(Path.Combine(_src, filename));
    }

    private void WriteCsv(params Photo[] photos)
    {
        var writer = new TourCsvWriter();
        writer.Write(Path.Combine(_src, "tour.csv"), photos.ToList());
    }

    [Fact]
    public async Task Export_CreatesExpectedFileStructure()
    {
        await WriteTestJpegAsync("selected.jpg");
        WriteCsv(new Photo
        {
            Filename = "selected.jpg",
            State    = PhotoState.Selected,
            DateTime = new DateTime(2026, 4, 26, 10, 0, 0)
        });

        var request = new WebExportRequest
        {
            SourcePhotoFolder = _src,
            OutputFolder      = _out,
            Title             = "Test"
        };

        var count = await _sut.ExportAsync(request);

        count.Should().Be(1);
        File.Exists(Path.Combine(_out, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(_out, "web", "style.css")).Should().BeTrue();
        File.Exists(Path.Combine(_out, "web", "app.js")).Should().BeTrue();
        File.Exists(Path.Combine(_out, "tour.json")).Should().BeTrue();
        File.Exists(Path.Combine(_out, "web", "photos", "selected.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task Export_EmptyInput_ReturnsZero()
    {
        WriteCsv();

        var count = await _sut.ExportAsync(new WebExportRequest
        {
            SourcePhotoFolder = _src,
            OutputFolder      = _out
        });

        count.Should().Be(0);
    }

    [Fact]
    public async Task Export_IndexHtml_ContainsTitleAndTourJson()
    {
        await WriteTestJpegAsync("p.jpg");
        WriteCsv(new Photo
        {
            Filename = "p.jpg",
            State    = PhotoState.Selected,
            DateTime = new DateTime(2026, 4, 26, 10, 0, 0)
        });

        await _sut.ExportAsync(new WebExportRequest
        {
            SourcePhotoFolder = _src,
            OutputFolder      = _out,
            Title             = "Meine Reise"
        });

        var html = await File.ReadAllTextAsync(Path.Combine(_out, "index.html"));
        html.Should().Contain("Meine Reise");
        html.Should().Contain("__TOUR__");
        html.Should().Contain("web/photos/p.jpg");
    }
}
