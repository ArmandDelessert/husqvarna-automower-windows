using HusqaCockpit.App.Services;
using HusqaCockpit.App.ViewModels;
using HusqaCockpit.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HusqaCockpit.App.Views;

public sealed partial class ScheduleEditorDialog : ContentDialog
{
    public ScheduleEditorDialog(IEnumerable<CalendarTask> tasks)
    {
        ViewModel = new ScheduleEditorViewModel(tasks, Loc.Strings);
        InitializeComponent();
    }

    public ScheduleEditorViewModel ViewModel { get; }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ScheduleRowViewModel row })
        {
            ViewModel.RemoveRow(row);
        }
    }
}
