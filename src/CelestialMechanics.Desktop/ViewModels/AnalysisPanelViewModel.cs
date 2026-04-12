using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Analysis panel — event log + energy chart data.
/// </summary>
public sealed partial class AnalysisPanelViewModel : ObservableObject
{
    /// <summary>Event log entries — newest first.</summary>
    public ObservableCollection<SimEventLogEntry> EventLog { get; } = new();

    /// <summary>Total energy time series for charting.</summary>
    public ObservableCollection<double> TotalEnergySeries { get; } = new();
    
    /// <summary>Kinetic energy time series.</summary>
    public ObservableCollection<double> KineticEnergySeries { get; } = new();
    
    /// <summary>Potential energy time series.</summary>
    public ObservableCollection<double> PotentialEnergySeries { get; } = new();

    /// <summary>Time labels for chart X axis.</summary>
    public ObservableCollection<double> TimeLabels { get; } = new();

    private const int MaxLogEntries = 200;
    private const int MaxChartPoints = 300;

    /// <summary>
    /// Adds an event to the log. Thread-safe — must be called from UI thread.
    /// </summary>
    public void AddEvent(SimEventLogEntry entry)
    {
        EventLog.Insert(0, entry); // Newest first
        while (EventLog.Count > MaxLogEntries)
            EventLog.RemoveAt(EventLog.Count - 1);
    }

    /// <summary>
    /// Updates energy chart data from a simulation snapshot.
    /// </summary>
    public void UpdateEnergy(double time, double totalEnergy, double kineticEnergy, double potentialEnergy)
    {
        TimeLabels.Add(time);
        TotalEnergySeries.Add(totalEnergy);
        KineticEnergySeries.Add(kineticEnergy);
        PotentialEnergySeries.Add(potentialEnergy);

        // Trim to max chart points
        while (TimeLabels.Count > MaxChartPoints)
        {
            TimeLabels.RemoveAt(0);
            TotalEnergySeries.RemoveAt(0);
            KineticEnergySeries.RemoveAt(0);
            PotentialEnergySeries.RemoveAt(0);
        }
    }

    /// <summary>
    /// Clears all chart data and event log.
    /// </summary>
    public void Clear()
    {
        EventLog.Clear();
        TimeLabels.Clear();
        TotalEnergySeries.Clear();
        KineticEnergySeries.Clear();
        PotentialEnergySeries.Clear();
    }
}
