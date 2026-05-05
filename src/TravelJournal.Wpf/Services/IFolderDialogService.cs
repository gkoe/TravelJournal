namespace TravelJournal.Wpf.Services;

public interface IFolderDialogService
{
    string? PickFolder(string? initialFolder = null);
}
