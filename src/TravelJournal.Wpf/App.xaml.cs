using TravelJournal.Core.Services;
using TravelJournal.Wpf.Services;
using TravelJournal.Wpf.ViewModels;
using TravelJournal.Wpf.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

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

        var contactEmail = config["Nominatim:ContactEmail"] ?? "deine@email.at";
        var userAgent    = $"TravelJournal/1.0 ({contactEmail})";

        var services = new ServiceCollection();

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
        services.AddSingleton<IConfirmDialogService,   ConfirmDialogService>();
        services.AddSingleton<IImageRotator,           ImageSharpImageRotator>();
        services.AddSingleton<IMapRendererFactory,     MapRendererFactory>();
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<ImageCropService>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
