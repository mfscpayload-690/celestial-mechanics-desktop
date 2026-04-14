using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CelestialMechanics.Desktop.Core;
using CelestialMechanics.Desktop.Infrastructure.Security;
using CelestialMechanics.Desktop.Services;
using CelestialMechanics.Desktop.ViewModels;
using CelestialMechanics.Desktop.Views;
using CelestialMechanics.Renderer;
using CelestialMechanics.Simulation;

namespace CelestialMechanics.Desktop;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// Global service provider — accessible from anywhere that needs to resolve services.
    /// Prefer constructor injection where possible; use this only in code-behind.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // SEC-05: Harden DLL search order — remove CWD from search path
        HardenDllSearch();

        // SEC-08: Global unhandled exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // ── Build DI container ────────────────────────────────────
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // ── Global application state and navigation ─────
                services.AddSingleton<AppState>();
                services.AddSingleton<NavigationService>();

                // ── Backend engine (already exists) ──────────────
                services.AddSingleton<SimulationEngine>();
                services.AddSingleton<RenderSettings>();

                // ── Application services ─────────────────────────
                services.AddSingleton<SimulationService>();
                services.AddSingleton<ISimulationService>(sp =>
                    new SimulationServiceAdapter(sp.GetRequiredService<SimulationService>()));
                services.AddSingleton<SceneService>();
                services.AddSingleton<ISceneService>(sp =>
                    new SceneServiceAdapter(sp.GetRequiredService<SceneService>()));
                services.AddSingleton<ProjectService>();
                services.AddSingleton<IProjectService>(sp =>
                    new ProjectServiceAdapter(sp.GetRequiredService<ProjectService>()));
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<SimulationPanelLauncher>();

                // ── ViewModels ───────────────────────────────────
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton(sp => new SimulationViewModel(
                    Dispatcher.CurrentDispatcher,
                    sp.GetRequiredService<SimulationService>(),
                    sp.GetRequiredService<SceneService>(),
                    sp.GetRequiredService<ProjectService>(),
                    sp.GetRequiredService<NavigationService>(),
                    sp.GetRequiredService<RenderSettings>()));

                // ── View host targets ────────────────────────────
                services.AddSingleton<HomeView>();
                services.AddSingleton<SimulationView>();
                services.AddSingleton<AnalysisView>();

                // ── Root shell window ────────────────────────────
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Services = _host.Services;

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        var appState = Services.GetRequiredService<AppState>();
        appState.SetMode(AppMode.Home);

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.GetService<SimulationViewModel>()?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }

    // ── SEC-05: DLL hijacking mitigation ──────────────────────────
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static void HardenDllSearch()
    {
        try
        {
            SetDllDirectory(string.Empty);
        }
        catch
        {
            // Non-critical on non-Windows platforms
        }
    }

    // ── SEC-08: Exception handling ────────────────────────────────

    private void OnDispatcherUnhandledException(
        object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleException(e.Exception);
    }

    private static void OnUnhandledException(
        object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            HandleException(ex);
    }

    private static void OnUnobservedTaskException(
        object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        HandleException(e.Exception);
    }

    private static void HandleException(Exception ex)
    {
        CrashLogger.WriteLog(ex);

        try
        {
            MessageBox.Show(
                "An unexpected error occurred. The application will continue.\n\n" +
                "Error reference: " + CrashLogger.LastErrorId + "\n" +
                "Details have been saved to: " + CrashLogger.LogPath,
                "Celestial Mechanics — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // If even the MessageBox fails, there's nothing we can do
        }
    }
}

// ═══════════════════════════════════════════════════════════
// DI Adapters — bridge concrete services to their interfaces.
// Full implementations already exist; these thin adapters
// let the DI container resolve the interface contracts.
// ═══════════════════════════════════════════════════════════

internal sealed class SimulationServiceAdapter : ISimulationService
{
    private readonly SimulationService _svc;
    public SimulationServiceAdapter(SimulationService svc) => _svc = svc;

    public string State => _svc.LastState.ToString();
    public double CurrentTime => _svc.LastSimTime;
    public long StepCount => _svc.StepCount;
    public double TimeScale
    {
        get => _svc.TimeScale;
        set => _svc.TimeScale = value;
    }

    public event Action? StateChanged;
    public event EventHandler<SimulationSnapshotEventArgs>? SnapshotUpdated
    {
        add => _svc.SnapshotUpdated += value;
        remove => _svc.SnapshotUpdated -= value;
    }
    public event EventHandler<Models.SimEventLogEntry>? EventLogged
    {
        add => _svc.EventLogged += value;
        remove => _svc.EventLogged -= value;
    }

    public void Play() => _svc.Play();
    public void Pause() => _svc.Pause();
    public void Step() => _svc.Step();
    public void Reset() => _svc.ResetScene();
    public void StartSimThread() => _svc.StartSimThread();
    public void StopSimThread() => _svc.StopSimThread();
}

internal sealed class SceneServiceAdapter : ISceneService
{
    private readonly SceneService _svc;
    public SceneServiceAdapter(SceneService svc) => _svc = svc;

    public event Action? SceneChanged;
    public event Action<Guid?>? SelectionChanged;

    public Guid? SelectedNodeId => _svc.SelectionManager.SelectedEntity;

    public void Select(Guid nodeId) => _svc.SelectionManager.Select(nodeId);
    public void ClearSelection() => _svc.SelectionManager.Clear();
    public void RefreshFromSimulation() { /* wired in later phases */ }
}

internal sealed class ProjectServiceAdapter : IProjectService
{
    private readonly ProjectService _svc;
    public ProjectServiceAdapter(ProjectService svc) => _svc = svc;

    public Models.ProjectInfo CreateProject(string name, string location) =>
        _svc.CreateProject(name, location);

    public List<Models.ProjectInfo> GetRecentProjects() =>
        _svc.GetRecentProjects();

    public Models.ProjectInfo? OpenProject(string path) =>
        _svc.OpenProject(path);

    public string GetDefaultProjectsRoot() =>
        ProjectService.GetDefaultProjectsRoot();
}
