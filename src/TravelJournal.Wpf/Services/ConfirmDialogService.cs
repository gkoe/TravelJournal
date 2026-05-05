using System.Windows;

namespace TravelJournal.Wpf.Services;

public class ConfirmDialogService : IConfirmDialogService
{
    public RotationSaveDecision AskRotationSaveDecision(string filename)
    {
        var result = MessageBox.Show(
            $"Das Foto \"{filename}\" wurde gedreht, aber noch nicht gespeichert.",
            "Rotation nicht gespeichert",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes    => RotationSaveDecision.Save,
            MessageBoxResult.No     => RotationSaveDecision.Discard,
            _                       => RotationSaveDecision.Cancel
        };
    }
}
