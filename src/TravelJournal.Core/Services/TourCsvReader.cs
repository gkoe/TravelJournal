using CsvHelper;
using CsvHelper.Configuration;
using TravelJournal.Core.Models;
using System.Globalization;
using System.Text;

namespace TravelJournal.Core.Services;

public class TourCsvReader
{
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        Delimiter = ";",
        HasHeaderRecord = true,
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null
    };

    /// <summary>
    /// Liest eine tour.csv ein und gibt die enthaltenen Photos zurück.
    /// Tolerant gegenüber fehlenden/unbekannten Werten.
    /// </summary>
    public List<Photo> Read(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"CSV nicht gefunden: {csvPath}", csvPath);

        using var reader = new StreamReader(csvPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvReader(reader, Config);

        csv.Context.RegisterClassMap<PhotoMap>();
        return csv.GetRecords<Photo>().ToList();
    }
}
