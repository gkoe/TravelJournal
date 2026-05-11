using TravelJournal.Core.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;

namespace TravelJournal.Core.Services;

public class ExifReaderService
{
    /// <summary>
    /// Liest EXIF-Metadaten aus einer Bilddatei und gibt ein befülltes Photo-Objekt zurück.
    /// Fehlende Tags werden als null zurückgegeben, es werden keine Exceptions geworfen.
    /// </summary>
    public Photo ReadMetadata(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Datei nicht gefunden: {filePath}", filePath);

        var photo = new Photo
        {
            Filename      = Path.GetFileName(filePath),
            FileSizeBytes = new FileInfo(filePath).Length
        };

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            photo.DateTime = ReadDateTime(directories);
            ReadGps(directories, photo);
            ReadPixelDimensions(directories, photo);
        }
        catch
        {
            // Tolerant: fehlerhafte oder fehlende EXIF-Daten → null-Felder
        }

        return photo;
    }

    private static DateTime? ReadDateTime(IEnumerable<MetadataExtractor.Directory> directories)
    {
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfd?.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var dt) == true)
            return dt;

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (ifd0?.TryGetDateTime(ExifIfd0Directory.TagDateTime, out var dt2) == true)
            return dt2;

        return null;
    }

    private static void ReadGps(IEnumerable<MetadataExtractor.Directory> directories, Photo photo)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps == null) return;

        var location = gps.GetGeoLocation();
        if (location != null)
        {
            photo.Latitude  = location.Value.Latitude;
            photo.Longitude = location.Value.Longitude;
        }

        if (gps.TryGetRational(GpsDirectory.TagAltitude, out var alt))
            photo.Altitude = alt.ToDouble();
    }

    private static void ReadPixelDimensions(IEnumerable<MetadataExtractor.Directory> directories, Photo photo)
    {
        // JPEG-Header liefert physikalische Pixelmaße (unabhängig von EXIF-Orientation)
        var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
        if (jpeg != null)
        {
            if (jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var w))
                photo.PixelWidth = w;
            if (jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var h))
                photo.PixelHeight = h;
            if (photo.PixelWidth.HasValue && photo.PixelHeight.HasValue)
                return;
        }

        // PNG-Header (kein EXIF vorhanden)
        var png = directories.OfType<PngDirectory>()
            .FirstOrDefault(d => d.Name == "PNG-IHDR");
        if (png != null)
        {
            if (png.TryGetInt32(PngDirectory.TagImageWidth, out var pw))
                photo.PixelWidth = pw;
            if (png.TryGetInt32(PngDirectory.TagImageHeight, out var ph))
                photo.PixelHeight = ph;
            if (photo.PixelWidth.HasValue && photo.PixelHeight.HasValue)
                return;
        }

        // Fallback: EXIF SubIFD
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfd == null) return;

        if (!photo.PixelWidth.HasValue
            && subIfd.TryGetInt32(ExifSubIfdDirectory.TagExifImageWidth, out var ew))
            photo.PixelWidth = ew;

        if (!photo.PixelHeight.HasValue
            && subIfd.TryGetInt32(ExifSubIfdDirectory.TagExifImageHeight, out var eh))
            photo.PixelHeight = eh;
    }
}
