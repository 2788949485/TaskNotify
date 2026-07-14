using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TaskNotify.Desktop.Models;
using TaskNotify.Desktop.ViewModels;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// Shell view model — drives the left menu and forwards clicks to
/// <see cref="Services.NavigationService"/>. Singleton in DI.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;

    public MainViewModel(IServiceProvider services, Services.NavigationService navigation)
    {
        _services = services;
        Navigation = navigation;
        foreach (var item in Services.MenuCatalog.All)
        {
            MenuItems.Add(item);
        }

        // Land on the first page at startup.
        SelectFirst();
    }

    public Services.NavigationService Navigation { get; }

    public ObservableCollection<AppMenuItem> MenuItems { get; } = new();

    /// <summary>The currently selected menu entry (drives highlighted state in the ListBox).</summary>
    [ObservableProperty]
    private AppMenuItem? _selectedMenu;

    partial void OnSelectedMenuChanged(AppMenuItem? value)
    {
        if (value is null) return;
        NavigateTo(value);
    }

    /// <summary>
    /// Navigates to the task center and selects the given task. Used by toast
    /// click-through so the user lands on the row that produced the notification.
    /// </summary>
    public void SelectTaskInCenter(Guid taskId)
    {
        var center = MenuItems.FirstOrDefault(m => m.ViewModelType == typeof(TaskCenterViewModel));
        if (center is not null) SelectedMenu = center;

        var vm = _services.GetService<TaskCenterViewModel>();
        if (vm is null) return;
        Navigation.Navigate(vm);
        vm.SelectTaskById(taskId);
    }

    private void SelectFirst()
    {
        if (MenuItems.Count > 0) SelectedMenu = MenuItems[0];
    }

    private void NavigateTo(AppMenuItem item)
    {
        if (item.IsPlaceholder || item.ViewModelType is null)
        {
            Navigation.Navigate(new PlaceholderViewModel(item.Title, "Phase 5 中实现。"));
            return;
        }

        var vm = _services.GetService(item.ViewModelType);
        if (vm is null)
        {
            Navigation.Navigate(new PlaceholderViewModel(item.Title, "该页面未在 DI 中注册。"));
            return;
        }

        Navigation.Navigate(vm);
    }
}

