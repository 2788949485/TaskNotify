using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// Page view model that filters the shared task store by a predicate. Bound to
/// the shared <see cref="TaskHistoryViewModel"/>'s <c>Completed</c> collection.
/// </summary>
public abstract partial class FilteredTasksViewModelBase : PageViewModelBase
{
    private readonly TaskHistoryViewModel _store;

    protected FilteredTasksViewModelBase(TaskHistoryViewModel store)
    {
        _store = store;
        Tasks = CollectionViewSource.GetDefaultView(store.Completed);
        Tasks.Filter = obj => obj is TaskCompletionNoticeViewModel vm && Filter(vm.State);
        store.Completed.CollectionChanged += (_, _) => Tasks.Refresh();
    }

    public ICollectionView Tasks { get; }

    public string RuntimeStatus => _store.RuntimeStatus;

    protected abstract bool Filter(TaskState state);
}
