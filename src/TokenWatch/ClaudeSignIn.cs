using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TokenWatch;

internal static class ClaudeSignIn
{
    internal static string? FindExecutable()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directories = new List<string>();
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            directories.AddRange((Environment.GetEnvironmentVariable("PATH", target) ?? "").Split(Path.PathSeparator));
        var prefix = Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX");
        if (!string.IsNullOrWhiteSpace(prefix)) directories.Add(prefix);
        var running = new List<string>();
        var desktopDirectories = new List<string>();
        foreach (var process in Process.GetProcessesByName("claude"))
            using (process)
                try
                {
                    if (process.MainModule?.FileName is { } executable && Path.GetFileName(executable).Equals("claude.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsDesktopExecutable(executable)) desktopDirectories.Add(Path.GetDirectoryName(executable)!);
                        else running.Add(executable);
                    }
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        return FindExecutable(home, local, roaming, directories, running, desktopDirectories);
    }

    // Explicit inputs allow offline coverage of different machines' install layouts.
    internal static string? FindExecutable(string home, string local, string roaming, IEnumerable<string> searchDirectories, IEnumerable<string>? runningExecutables = null, IEnumerable<string>? desktopDirectories = null)
    {
        var candidates = new List<string>(runningExecutables ?? Array.Empty<string>()) { Path.Combine(home, ".local", "bin", "claude.exe") };
        var directories = searchDirectories.Concat(new[]
        {
            Path.Combine(roaming, "npm"), Path.Combine(home, ".claude", "local", "node_modules", ".bin"),
            Path.Combine(local, "Microsoft", "WinGet", "Links")
        });
        foreach (var entry in directories.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var directory = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
            // Avoid searching the app's working directory for a launcher.
            if (!Path.IsPathFullyQualified(directory)) continue;
            candidates.Add(Path.Combine(directory, "claude.exe"));
            candidates.Add(Path.Combine(directory, "claude.cmd"));
        }
        var direct = candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(path => File.Exists(path) && !IsDesktopExecutable(path));
        if (direct is not null) return direct;
        foreach (var directory in (desktopDirectories ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var bundled = BundledExecutable(Path.Combine(directory, "resources"));
            if (bundled is not null) return bundled;
        }
        var desktopData = Path.Combine(local, "Claude-Data");
        // Claude Desktop bundles the CLI separately from its desktop executable.
        var cliRoots = Directories(desktopData, "claude_*").Select(profile => Path.Combine(profile, "claude-code")).Concat(new[]
        {
            Path.Combine(roaming, "Claude", "claude-code"),
            Path.Combine(local, "Claude-3p", "claude-code"), Path.Combine(roaming, "Claude-3p", "claude-code")
        }).ToList();
        var packages = Path.Combine(local, "Packages");
        foreach (var package in Directories(packages, "Claude_*").Concat(Directories(packages, "AnthropicPBC.Claude_*")))
            cliRoots.Add(Path.Combine(package, "LocalCache", "Roaming", "Claude", "claude-code"));
        foreach (var cliRoot in cliRoots)
        {
            foreach (var version in Directories(cliRoot, "*").OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                var bundled = BundledExecutable(version);
                if (bundled is not null) return bundled;
            }
        }
        return null;
    }

    private static bool IsDesktopExecutable(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return directory is not null && (File.Exists(Path.Combine(directory, "chrome_elf.dll"))
            || File.Exists(Path.Combine(directory, "resources", "app.asar"))
            || Directory.Exists(Path.Combine(directory, "resources", "app.asar.unpacked")));
    }

    private static string? BundledExecutable(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;
            return Directory.GetFiles(directory, "claude.exe", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .FirstOrDefault(path => !path.Contains("win32-arm64", StringComparison.OrdinalIgnoreCase) && !IsDesktopExecutable(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string[] Directories(string path, string pattern)
    {
        try { return Directory.Exists(path) ? Directory.GetDirectories(path, pattern) : Array.Empty<string>(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    internal static ProcessStartInfo CreateStartInfo(string cli)
    {
        if (!Path.GetExtension(cli).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
            return new ProcessStartInfo(cli, "auth login") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
        // npm installs a batch launcher. Pass its path through the child environment
        // so spaces and shell metacharacters in the install path remain literal.
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /v:off /c \"\"%TOKENWATCH_CLAUDE_LOGIN_CLI%\" auth login\"")
        { UseShellExecute = false, CreateNoWindow = false, WindowStyle = ProcessWindowStyle.Normal };
        info.Environment["TOKENWATCH_CLAUDE_LOGIN_CLI"] = cli;
        // Explorer may still have the PATH from before Node/Claude was installed.
        // Let the npm shim find Node using the saved user/system PATH as well.
        info.Environment["PATH"] = string.Join(Path.PathSeparator.ToString(),
            new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
                .Select(target => Environment.GetEnvironmentVariable("PATH", target))
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => Environment.ExpandEnvironmentVariables(value!)));
        return info;
    }

    internal static Process? Start()
    {
        var cli = FindExecutable();
        if (cli is null) return null;
        // This is invoked only by the user's Sign in button. The user completes browser authentication.
        return Process.Start(CreateStartInfo(cli));
    }

    internal static ProcessStartInfo CreateSetupStartInfo() => new(
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
        "-NoProfile -ExecutionPolicy Bypass -Command \"& { $ErrorActionPreference = 'Stop'; try { Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression; if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; exit 0 } catch { Write-Host $_; Read-Host 'Setup failed. Press Enter to close'; exit 1 } }\"")
    { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
}
