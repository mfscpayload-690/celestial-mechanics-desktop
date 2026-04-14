using System.Diagnostics;
using System.IO;
using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.Services;

public sealed class SimulationPanelLauncher
{
    public async Task<(bool Success, Process? Process, string? Error)> TryLaunchAsync(ProjectInfo project)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            return (false, null, "Unable to locate solution root to launch the simulation panel.");
        }

        var appBinDir = Path.Combine(repoRoot, "src", "CelestialMechanics.App", "bin", "Debug", "net8.0");
        var appExePath = Path.Combine(appBinDir, "CelestialMechanics.App.exe");
        var appDllPath = Path.Combine(appBinDir, "CelestialMechanics.App.dll");
        var appProjectPath = Path.Combine(repoRoot, "src", "CelestialMechanics.App", "CelestialMechanics.App.csproj");

        var args = $"--projectPath \"{project.Path}\"";

        if (!File.Exists(appExePath) && !File.Exists(appDllPath))
        {
            if (!File.Exists(appProjectPath))
            {
                return (false, null, "Simulation panel project was not found.");
            }

            var buildResult = await BuildSimulationAppAsync(repoRoot, appProjectPath);
            if (!buildResult.Success)
            {
                return (false, null, buildResult.Error);
            }
        }

        try
        {
            if (File.Exists(appExePath))
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = appExePath,
                    Arguments = args,
                    WorkingDirectory = appBinDir,
                    UseShellExecute = true,
                });

                if (process == null)
                {
                    return (false, null, "Failed to start simulation executable.");
                }

                return (true, process, null);
            }

            if (File.Exists(appDllPath))
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{appDllPath}\" {args}",
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process == null)
                {
                    return (false, null, "Failed to start simulation runtime.");
                }

                return (true, process, null);
            }

            return (false, null, "Simulation panel build output was not found after build.");
        }
        catch (Exception ex)
        {
            return (false, null, $"Failed to launch simulation panel: {ex.Message}");
        }
    }

    private static async Task<(bool Success, string? Error)> BuildSimulationAppAsync(string repoRoot, string appProjectPath)
    {
        try
        {
            var buildProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{appProjectPath}\" -c Debug -nologo",
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (buildProcess == null)
            {
                return (false, "Failed to start background build for simulation panel.");
            }

            await buildProcess.WaitForExitAsync();

            if (buildProcess.ExitCode == 0)
            {
                return (true, null);
            }

            string stderr = await buildProcess.StandardError.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(stderr))
            {
                stderr = "Unknown build error.";
            }

            return (false, $"Simulation panel build failed: {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to build simulation panel: {ex.Message}");
        }
    }

    private static string? FindRepoRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            var slnPath = Path.Combine(dir.FullName, "CelestialMechanics.sln");
            if (File.Exists(slnPath))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
