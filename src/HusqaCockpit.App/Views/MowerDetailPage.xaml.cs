using System.ComponentModel;
using HusqaCockpit.App.Services;
using HusqaCockpit.App.ViewModels;
using HusqaCockpit.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HusqaCockpit.App.Views;

public sealed partial class MowerDetailPage : Page, INotifyPropertyChanged
{
    public MowerDetailPage()
    {
        InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Null only when navigation targeted a mower that no longer exists.</summary>
    public MowerViewModel? ViewModel { get; private set; }

    public string? MowerId => ViewModel?.Id;

    public Visibility NoMessagesVisibility =>
        ViewModel is { MessagesLoaded: true, Messages.Count: 0 } ? Visibility.Visible : Visibility.Collapsed;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var viewModel = e.Parameter is string id ? App.Current.Dashboard.Find(id) : null;
        if (viewModel is null)
        {
            // The mower disappeared (e.g. account changed): fall back to the dashboard.
            DispatcherQueue.TryEnqueue(() => Frame.Navigate(typeof(DashboardPage)));
            return;
        }

        ViewModel = viewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Bindings.Update();
        UpdateMap(recenter: true);
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        // A new page (and map) is created on every visit: release the browser process.
        Map.CloseWebView();
        base.OnNavigatedFrom(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MowerViewModel.MessagesLoaded))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoMessagesVisibility)));
        }
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            // The mower was updated: its track may have grown.
            UpdateMap(recenter: false);
        }
    }

    private void UpdateMap(bool recenter)
    {
        if (ViewModel is null)
        {
            return;
        }

        var index = Math.Max(0, App.Current.Dashboard.Mowers.IndexOf(ViewModel));
        Map.SetTracks([MapTrack.From(ViewModel.Mower, MapTrack.ColorFor(index))], labels: false, recenter);
    }

    private void Recenter_Click(object sender, RoutedEventArgs e) => Map.Recenter();

    private void CommandInfo_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (ViewModel is not null)
        {
            ViewModel.CommandMessage = null;
        }
    }

    private async void EditSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new ScheduleEditorDialog(ViewModel.CurrentTasks) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SaveScheduleAsync(dialog.ViewModel.ToTasks());
        }
    }

    private async void ResetBlades_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Dialog_ResetBladesTitle"),
            Content = Loc.Format("Dialog_ResetBladesText", ViewModel.Name),
            PrimaryButtonText = Loc.Get("Dialog_Reset"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ResetBladeUsageCommand.ExecuteAsync(null);
        }
    }
}
