using TravelJournal.Core.Services;
using TravelJournal.Wpf.Services;
using TravelJournal.Wpf.ViewModels;
using TravelJournal.Wpf.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Windows;
using System.Windows.Threading;

namespace TravelJournal.Wpf;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TravelJournal", "logs");
        System.IO.Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Fatal(ex.Exception, "Unbehandelter UI-Fehler");
            ex.Handled = true;
            MessageBox.Show(
                $"Ein unerwarteter Fehler ist aufgetreten:\n{ex.Exception.Message}",
                "TravelJournal – Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception err)
                Log.Fatal(err, "Unbehandelter Hintergrund-Fehler (IsTerminating={IsTerminating})", ex.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Unbeobachtete Task-Ausnahme");
            ex.SetObserved();
        };

        var contactEmail = config["Nominatim:ContactEmail"] ?? "deine@email.at";
        var userAgent    = $"TravelJournal/1.0 ({contactEmail})";

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(dispose: true));

        services.AddHttpClient("tiles", c =>
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            c.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient("geocoder", c =>
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<ExifReaderService>();
        services.AddSingleton<TourCsvReader>();
        services.AddSingleton<TourCsvWriter>();
        services.AddSingleton<PhotoFolderScanner>();
        services.AddSingleton<IFolderDialogService,   FolderDialogService>();
        services.AddSingleton<IThumbnailLoader,        ThumbnailLoader>();
        services.AddSingleton<IReverseGeocoderFactory, ReverseGeocoderFactory>();
        services.AddSingleton<IConfirmDialogService,   FluentConfirmDialogService>();
        services.AddSingleton<IImageRotator,           ImageSharpImageRotator>();
        services.AddSingleton<IMapRendererFactory,     MapRendererFactory>();
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<ImageCropService>();
        services.AddSingleton<TravelJournal.WebExporter.Services.ManifestBuilder>();
        services.AddSingleton<TravelJournal.WebExporter.Services.ImageOptimizer>();
        services.AddSingleton<TravelJournal.WebExporter.IWebPresentationExporter,
                              TravelJournal.WebExporter.WebPresentationExporter>();
        services.AddSingleton<IWebExportService, WebExportService>();
        services.AddSingleton<TravelJournal.Core.Services.IHeicConverter,
                              TravelJournal.Core.Services.MagickHeicConverter>();
        services.AddSingleton<TravelJournal.Core.Services.IPhotoRenamer,
                              TravelJournal.Core.Services.PhotoRenamer>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
