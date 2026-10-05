using HusqaCockpit.Presentation;
using Microsoft.UI.Dispatching;

namespace HusqaCockpit.App.Services;

/// <summary>The WinUI thread of the app, for the view models.</summary>
public sealed class DispatcherQueueUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool CheckAccess() => queue.HasThreadAccess;

    public void Post(Action action) => queue.TryEnqueue(() => action());
}
