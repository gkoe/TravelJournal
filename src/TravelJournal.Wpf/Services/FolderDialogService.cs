using Microsoft.Win32;

namespace TravelJournal.Wpf.Services;

public class FolderDialogService : IFolderDialogService
{
    public string? PickFolder(string? initialFolder = null)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Foto-Ordner auswählen",
            InitialDirectory = initialFolder ?? string.Empty
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
