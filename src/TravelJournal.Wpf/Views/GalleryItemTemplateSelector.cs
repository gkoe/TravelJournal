using TravelJournal.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace TravelJournal.Wpf.Views;

public class GalleryItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PhotoTemplate { get; set; }
    public DataTemplate? HeicTemplate  { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item switch
        {
            HeicItemViewModel => HeicTemplate,
            _                 => PhotoTemplate
        };
}
