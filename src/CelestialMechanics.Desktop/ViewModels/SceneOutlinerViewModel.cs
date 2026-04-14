using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;

namespace CelestialMechanics.Desktop.ViewModels;

public sealed partial class SceneOutlinerViewModel : ObservableObject, IDisposable
{
    private readonly SceneService _sceneService;
    private readonly Dispatcher _dispatcher;
    private bool _selectionSyncInProgress;

    public event Action<Guid>? BodySelected;
    public event Action<int>? DeleteRequested;
    public event Action? AddBodyRequested;

    public ObservableCollection<SceneNodeItem> Items { get; } = new();

    [ObservableProperty]
    private SceneNodeItem? _selectedItem;

    public SceneOutlinerViewModel(SceneService sceneService, SimulationService simulationService, Dispatcher dispatcher)
    {
        _sceneService = sceneService;
        _dispatcher = dispatcher;

        _sceneService.SelectionManager.OnSelectionChanged += OnServiceSelectionChanged;
        _sceneService.SceneChanged += OnSceneChanged;

        Refresh();
    }

    public SceneOutlinerViewModel(SceneService sceneService, SimulationService simulationService)
        : this(sceneService, simulationService, Dispatcher.CurrentDispatcher)
    {
    }

    public void Refresh()
    {
        Items.Clear();
        foreach (var item in _sceneService.GetItems())
        {
            Items.Add(item);
        }

        SyncSelectedItemFromService();
    }

    public void SetSelectedNodeId(Guid? nodeId)
    {
        _selectionSyncInProgress = true;
        try
        {
            SelectedItem = nodeId.HasValue
                ? Items.FirstOrDefault(item => item.NodeId == nodeId.Value)
                : null;
        }
        finally
        {
            _selectionSyncInProgress = false;
        }
    }

    partial void OnSelectedItemChanged(SceneNodeItem? value)
    {
        if (_selectionSyncInProgress)
        {
            return;
        }

        if (value == null)
        {
            _sceneService.SelectionManager.Clear();
            return;
        }

        _sceneService.SelectionManager.Select(value.NodeId);
        BodySelected?.Invoke(value.NodeId);
    }

    [RelayCommand]
    private void AddBody()
    {
        AddBodyRequested?.Invoke();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedItem?.BodyId is int bodyId)
        {
            DeleteRequested?.Invoke(bodyId);
        }
    }

    private void OnServiceSelectionChanged(IReadOnlyList<Guid> selected)
    {
        _dispatcher.InvokeAsync(SyncSelectedItemFromService, DispatcherPriority.Background);
    }

    private void OnSceneChanged()
    {
        _dispatcher.InvokeAsync(Refresh, DispatcherPriority.Background);
    }

    private void SyncSelectedItemFromService()
    {
        var selectedNodeId = _sceneService.SelectionManager.SelectedEntity;
        SetSelectedNodeId(selectedNodeId);

        if (selectedNodeId.HasValue)
        {
            BodySelected?.Invoke(selectedNodeId.Value);
        }
    }

    public void Dispose()
    {
        _sceneService.SelectionManager.OnSelectionChanged -= OnServiceSelectionChanged;
        _sceneService.SceneChanged -= OnSceneChanged;
    }
}
