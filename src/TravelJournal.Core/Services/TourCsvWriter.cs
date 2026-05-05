using CsvHelper;
using CsvHelper.Configuration;
using TravelJournal.Core.Models;
using System.Globalization;
using System.Text;

namespace TravelJournal.Core.Services;

public class TourCsvWriter
{
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        Delimiter = ";",
        HasHeaderRecord = true,
        NewLine = "\r\n",
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
    };

    /// <summary>
    /// Schreibt alle Photos in eine CSV-Datei gemäß dem tour.csv-Schema.
    /// Sortiert nach DateTime aufsteigend, Einträge ohne DateTime ans Ende.
    /// </summary>
    public void Write(string csvPath, IEnumerable<Photo> photos)
    {
        var sorted = photos
            .OrderBy(p => p.DateTime.HasValue ? 0 : 1)
            .ThenBy(p => p.DateTime);

        using var writer = new StreamWriter(csvPath, append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, Config);

        csv.Context.RegisterClassMap<PhotoMap>();
        csv.WriteRecords(sorted);
    }
}

internal sealed class PhotoMap : ClassMap<Photo>
{
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

    public PhotoMap()
    {
        Map(p => p.Filename).Index(0).Name("Filename");
        Map(p => p.DateTime).Index(1).Name("DateTime")
            .TypeConverterOption.Format(DateTimeFormat)
            .TypeConverterOption.NullValues(string.Empty);
        Map(p => p.Latitude).Index(2).Name("Latitude")
            .TypeConverterOption.NullValues(string.Empty);
        Map(p => p.Longitude).Index(3).Name("Longitude")
            .TypeConverterOption.NullValues(string.Empty);
        Map(p => p.Altitude).Index(4).Name("Altitude")
            .TypeConverterOption.NullValues(string.Empty);
        Map(p => p.State).Index(5).Name("State")
            .TypeConverter<PhotoStateIntConverter>();
        Map(p => p.Title).Index(6).Name("Title")
            .TypeConverterOption.NullValues(string.Empty);
        Map(p => p.Description).Index(7).Name("Description")
            .TypeConverterOption.NullValues(string.Empty);
        Map(p => p.Location).Index(8).Name("Location")
            .TypeConverterOption.NullValues(string.Empty);
    }
}

internal sealed class PhotoStateIntConverter : CsvHelper.TypeConversion.DefaultTypeConverter
{
    public override string? ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        => ((int)(PhotoState)(value ?? PhotoState.None)).ToString();

    public override object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
    {
        if (string.IsNullOrWhiteSpace(text)) return PhotoState.None;
        if (int.TryParse(text, out var val) && Enum.IsDefined(typeof(PhotoState), val))
            return (PhotoState)val;
        return PhotoState.None;
    }
}
