namespace TravelJournal.Wpf.Services;

public enum RotationSaveDecision { Save, Discard, Cancel }

public interface IConfirmDialogService
{
    RotationSaveDecision AskRotationSaveDecision(string filename);
}
