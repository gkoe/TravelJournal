using TravelJournal.Core.Models;
using System.Text.RegularExpressions;

namespace TravelJournal.Core.Services;

public sealed record ScanResult(
    IReadOnlyList<Photo>    Photos,
    IReadOnlyList<HeicItem> HeicCandidates,
    IReadOnlyList<string>   NewFilenames,
    IReadOnlyList<string>   MissingFilenames
);

public class PhotoFolderScanner
{
    private static readonly Regex MapFilePattern =
        new(@"^map_(\d{4})-(\d{2})-(\d{2})T(\d{2})-(\d{2})-(\d{2})(_[A-Za-z0-9]+)?\.png$",
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
        // Collect all relevant disk files: JPEG + map_*.png
        var diskFiles = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext  = Path.GetExtension(f);
                var name = Path.GetFileName(f);
                return ext.Equals(".jpg",  StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    || (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                        && MapFilePattern.IsMatch(name));
            })
            .ToList();

        var diskNames = diskFiles
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);

        var existingByName = existing != null
            ? existing.ToDictionary(p => p.Filename, p => p, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Photo>(StringComparer.OrdinalIgnoreCase);

        var csvPath = Path.Combine(folderPath, "tour.csv");
        Dictionary<string, Photo> csvPhotos = new(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(csvPath))
        {
            foreach (var p in _csvReader.Read(csvPath))
            {
                if (!existingByName.ContainsKey(p.Filename))
                    csvPhotos[p.Filename] = p;
            }
        }

        // ── Fotos (inkl. Karten) ───────────────────────────────
        var result           = new List<Photo>();
        var newFilenames     = new List<string>();
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
                var photo = IsMapFile(name)
                    ? BuildMapPhotoFromDisk(name, path)
                    : ReadExifSafe(path);
                result.Add(photo);
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

        // ── HEIC/HEIF ──────────────────────────────────────────
        var heicCandidates = ScanHeicFiles(folderPath);

        return new ScanResult(sorted, heicCandidates, newFilenames, missingFilenames);
    }

    private static IReadOnlyList<HeicItem> ScanHeicFiles(string folderPath)
    {
        var result = new List<HeicItem>();
        foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(path);
            if (!ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".heif", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new HeicItem
            {
                Filename = Path.GetFileName(path),
                FullPath = path,
                DateTime = null
            });
        }
        return result.OrderBy(h => h.Filename).ToList();
    }

    private static bool IsMapFile(string filename) =>
        MapFilePattern.IsMatch(filename);

    private Photo BuildMapPhotoFromDisk(string filename, string fullPath)
    {
        var dt   = ParseMapDateTime(filename)
                   ?? File.GetLastWriteTime(fullPath);
        var exif = ReadExifSafe(fullPath);
        return new Photo
        {
            EntryType     = EntryType.Map,
            Filename      = filename,
            DateTime      = dt,
            State         = PhotoState.None,   // user must confirm; CSV-backed maps use CSV state
            FileSizeBytes = exif.FileSizeBytes,
            PixelWidth    = exif.PixelWidth,
            PixelHeight   = exif.PixelHeight,
        };
    }

    private static DateTime? ParseMapDateTime(string filename)
    {
        var match = MapFilePattern.Match(filename);
        if (!match.Success) return null;
        return new DateTime(
            int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value),
            int.Parse(match.Groups[5].Value), int.Parse(match.Groups[6].Value));
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
