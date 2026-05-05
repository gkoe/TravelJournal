using TravelJournal.Core.MapRendering;

namespace TravelJournal.Wpf.Services;

public interface IMapRendererFactory
{
    IMapRenderer Create(string photoFolder, Action<string>? statusCallback = null);
    IMapRenderer Create(string photoFolder, MapRenderingOptions options, Action<string>? statusCallback = null);
    MapRenderingOptions LoadBaseOptions();
}
