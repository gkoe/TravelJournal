using TravelJournal.Core.Models;
using System.Text.RegularExpressions;

namespace TravelJournal.Core.Services;

public sealed record ScanResult(
    IReadOnlyList<Photo>   Photos,
    IReadOnlyList<MapItem> Maps,
    IReadOnlyList<string>  NewFilenames,
    IReadOnlyList<string>  MissingFilenames
);

public class PhotoFolderScanner
{
    private static readonly Regex MapFilePattern =
        new(@"^map_(\d{4})-(\d{2})-(\d{2})T(\d{2})-(\d{2})-(\d{2})\.png$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ExifReaderService _exifReader;
    private readonly TourCsvReader     _csvReader;

    public PhotoFolderScanner(ExifReaderService exifReader, TourCsvReader csvReader)
    {
        _exifReader = exifReader;
        _csvReader  = csvReader;
    }

    public ScanResult Scan(string folderPath, IEnumerable<Photo>? existing = null)
    {
        var jpgFiles = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f);
                return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        var diskNames = jpgFiles
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);

        var existingByName = existing != null
            ? existing.ToDictionary(p => p.Filename, p => p, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Photo>(StringComparer.OrdinalIgnoreCase);

        var csvPath = Path.Combine(folderPath, "tour.csv");
        Dictionary<string, Photo> csvPhotos = new(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(csvPath))
        {
            foreach (var p in _csvReader.Read(csvPath))
                if (!existingByName.ContainsKey(p.Filename))
                    csvPhotos[p.Filename] = p;
        }

        var result         = new List<Photo>();
        var newFilenames   = new List<string>();
        var missingFilenames = new List<string>();

        foreach (var (name, path) in diskNames)
        {
            if (existingByName.TryGetValue(name, out var existingPhoto))
            {
                result.Add(FillMissingExifFields(existingPhoto, path));
            }
            else if (csvPhotos.TryGetValue(name, out var csvPhoto))
            {
                result.Add(FillMissingExifFields(csvPhoto, path));
            }
            else
            {
                result.Add(ReadExifSafe(path));
                newFilenames.Add(name);
            }
        }

        foreach (var (name, csvPhoto) in csvPhotos)
        {
            if (!diskNames.ContainsKey(name))
            {
                result.Add(csvPhoto);
                missingFilenames.Add(name);
            }
        }

        foreach (var (name, existingPhoto) in existingByName)
        {
            if (!diskNames.ContainsKey(name) && !csvPhotos.ContainsKey(name))
            {
                result.Add(existingPhoto);
                missingFilenames.Add(name);
            }
        }

        var sorted = result
            .OrderBy(p => p.DateTime.HasValue ? 0 : 1)
            .ThenBy(p => p.DateTime)
            .ToList();

        var maps = ScanMaps(folderPath);

        return new ScanResult(sorted, maps, newFilenames, missingFilenames);
    }

    private static IReadOnlyList<MapItem> ScanMaps(string folderPath)
    {
        var maps = new List<MapItem>();

        foreach (var path in Directory.EnumerateFiles(folderPath, "map_*.png", SearchOption.TopDirectoryOnly))
        {
            var name  = Path.GetFileName(path);
            var match = MapFilePattern.Match(name);
            DateTime dt;
            if (match.Success)
            {
                dt = new DateTime(
                    int.Parse(match.Groups[1].Value),
                    int.Parse(match.Groups[2].Value),
                    int.Parse(match.Groups[3].Value),
                    int.Parse(match.Groups[4].Value),
                    int.Parse(match.Groups[5].Value),
                    int.Parse(match.Groups[6].Value));
            }
            else
            {
                dt = File.GetLastWriteTime(path);
            }

            maps.Add(new MapItem { Filename = name, DateTime = dt, FullPath = path });
        }

        return maps.OrderBy(m => m.DateTime).ToList();
    }

    private Photo FillMissingExifFields(Photo photo, string filePath)
    {
        var exif = ReadExifSafe(filePath);

        photo.FileSizeBytes = exif.FileSizeBytes;
        photo.PixelWidth    = exif.PixelWidth;
        photo.PixelHeight   = exif.PixelHeight;

        photo.DateTime  ??= exif.DateTime;
        photo.Latitude  ??= exif.Latitude;
        photo.Longitude ??= exif.Longitude;
        photo.Altitude  ??= exif.Altitude;
        return photo;
    }

    private Photo ReadExifSafe(string filePath)
    {
        try   { return _exifReader.ReadMetadata(filePath); }
        catch { return new Photo { Filename = Path.GetFileName(filePath) }; }
    }
}
