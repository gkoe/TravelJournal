using TravelJournal.Wpf.ViewModels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TravelJournal.Wpf.Views;

public partial class PresentationWindow : Window
{
    public PresentationWindow(PresentationViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.RequestClose              += (_, _) => Close();
        vm.OverlayAnimationRequested += to => AnimateOpacity(OverlayPanel, to);
        vm.HintAnimationRequested    += to => AnimateOpacity(HintText,     to);

        Loaded += async (_, _) => await vm.StartAsync();
    }

    private static void AnimateOpacity(UIElement target, double to, double durationMs = 450)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        target.BeginAnimation(OpacityProperty, anim);
    }
}
