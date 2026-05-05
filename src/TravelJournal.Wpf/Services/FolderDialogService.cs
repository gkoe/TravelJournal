using Ookii.Dialogs.Wpf;

namespace TravelJournal.Wpf.Services;

public class FolderDialogService : IFolderDialogService
{
    public string? PickFolder(string? initialFolder = null)
    {
        var dlg = new VistaFolderBrowserDialog
        {
            Description = "Foto-Ordner auswählen",
            UseDescriptionForTitle = true,
            SelectedPath = initialFolder ?? string.Empty
        };
        return dlg.ShowDialog() == true ? dlg.SelectedPath : null;
    }
}
