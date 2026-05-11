using TravelJournal.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TravelJournal.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.ScrollSelectedIntoViewRequested += () =>
        {
            // Nach FilteredPhotos.Refresh() setzt der VirtualizingStackPanel den Fokus-
            // Cursor intern zurück. BeginInvoke(Background) läuft nach dem Layout-Pass,
            // sodass ScrollIntoView und Focus auf dem korrekten Container landen.
            Dispatcher.BeginInvoke(() =>
            {
                if (PhotoList.SelectedItem is not { } item) return;
                PhotoList.ScrollIntoView(item);
                if (PhotoList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem lbi)
                    lbi.Focus();
            }, DispatcherPriority.Background);
        };
    }

    private void PhotoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem != null)
            lb.ScrollIntoView(lb.SelectedItem);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (DataContext is not MainViewModel vm) { base.OnClosing(e); return; }

        if (vm.HasPendingAutoSave)
        {
            e.Cancel = true;
            base.OnClosing(e);
            await vm.FlushAutoSaveAsync();
            Close();
            return;
        }

        e.Cancel = !vm.HandleWindowClosing();
        base.OnClosing(e);
    }
}
