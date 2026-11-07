using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TokenWatch.Core;

namespace TokenWatch;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly bool _demo;
    private readonly Func<string, CancellationToken, Task<UsageSnapshot>>? _check;
    private readonly CancellationTokenSource _closed = new();
    private readonly Dictionary<string, UsageSnapshot> _snapshots;
    internal ObservableCollection<ManagedProvider> Providers { get; } = [];
    internal bool ClaudeSignInCompleted { get; private set; }
    private sealed record Interval(int Minutes) { public override string ToString() => Minutes == 1 ? "1 minute" : $"{Minutes} minutes"; }
    internal SettingsWindow(Settings settings, bool demo = false, IEnumerable<UsageSnapshot>? snapshots = null,
        Func<string, CancellationToken, Task<UsageSnapshot>>? check = null)
    {
        _settings = settings; _demo = demo; _check = check;
        _snapshots = (snapshots ?? []).ToDictionary(s => s.Id);
        InitializeComponent();
        PinnedOption.IsChecked = settings.Pinned; TopmostOption.IsChecked = settings.AlwaysOnTop;
        ShowRemainingOption.IsChecked = settings.ShowRemaining; ShowUsedOption.IsChecked = !settings.ShowRemaining;
        ResetDetailsOption.IsChecked = settings.ShowResetDetails;
        PaceMarkersOption.IsChecked = settings.ShowPaceMarkers;
        DailyUsageBudget.Text = settings.DailyUsagePercent.ToString(System.Globalization.CultureInfo.CurrentCulture);
        ActivityText.Text = string.Join("\n\n", _snapshots.Values.Select(s => ProviderCatalog.All.First(p => p.Id == s.Id).Name + " · " + s.State + "\n" + s.Message + (s.UpdatedAt is { } time ? $"\nLast success: {time.LocalDateTime:g}" : "") + (s.RetryAt is { } retry ? $"\nNext allowed check: {retry.LocalDateTime:g}" : "")));
        var intervals = new[] { 1, 2, 5, 10, 15, 30, 60 }.Select(n => new Interval(n)).ToList();
        if (!intervals.Any(i => i.Minutes == settings.RefreshMinutes)) intervals.Add(new(settings.RefreshMinutes));
        RefreshInterval.ItemsSource = intervals;
        RefreshInterval.SelectedItem = intervals.First(i => i.Minutes == settings.RefreshMinutes);
        CodexPath.Text = settings.CodexExecutable ?? "";
        foreach (var id in ProviderCatalog.Normalize(settings.ProviderOrder))
            Providers.Add(new(ProviderCatalog.All.First(p => p.Id == id), settings.IsProviderEnabled(id), StatusFor(id)));
        ManagedProviderList.ItemsSource = Providers;
        UpdateCatalog(); ManagedProviderList.SelectedIndex = Providers.Count > 0 ? 0 : -1;
        Closed += (_, _) => _closed.Cancel();
    }
    private string StatusFor(string id) => _snapshots.TryGetValue(id, out var snapshot) ? snapshot.State switch
    { ConnectionState.Connected => "Connected", ConnectionState.Stale => "Stale", ConnectionState.SignInRequired => "Sign in", ConnectionState.RateLimited => "Cooldown", ConnectionState.Disabled => "Paused", _ => "Not connected" } : "Not checked";
    internal void FocusProvider(string id)
    {
        ManagedProviderList.SelectedItem = Providers.FirstOrDefault(p => p.Id == id);
        ProviderDetailsPanel.BringIntoView();
    }
    internal void AddProvider(string id)
    {
        if (Providers.Any(p => p.Id == id) || ProviderCatalog.All.FirstOrDefault(p => p.Id == id) is not { } definition) return;
        var provider = new ManagedProvider(definition, true, StatusFor(id));
        Providers.Add(provider); ManagedProviderList.SelectedItem = provider; UpdateCatalog();
    }
    internal void MoveProvider(int direction)
    {
        var index = ManagedProviderList.SelectedIndex;
        var next = index + direction;
        if (index < 0 || next < 0 || next >= Providers.Count) return;
        Providers.Move(index, next); ManagedProviderList.SelectedIndex = next; UpdateDetails();
    }
    internal void RemoveSelectedProvider()
    {
        var index = ManagedProviderList.SelectedIndex;
        if (index < 0) return;
        Providers.RemoveAt(index); ManagedProviderList.SelectedIndex = Math.Min(index, Providers.Count - 1); UpdateCatalog();
    }
    private void UpdateCatalog()
    {
        var available = ProviderCatalog.All.Where(p => Providers.All(row => row.Id != p.Id)).ToArray();
        ProviderCatalogPicker.ItemsSource = available; ProviderCatalogPicker.SelectedIndex = available.Length > 0 ? 0 : -1;
        AddProviderButton.IsEnabled = available.Length > 0;
        ProviderCatalogPicker.Visibility = available.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CatalogCompleteText.Visibility = available.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        AddProviderButton.Content = available.Length > 0 ? "Add provider" : "All added";
        EmptyProvidersText.Visibility = Providers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetails();
    }
    private void UpdateDetails()
    {
        if (ProviderDetailsPanel is null) return;
        var selected = ManagedProviderList.SelectedItem as ManagedProvider;
        ProviderDetailsPanel.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        RemoveProviderButton.IsEnabled = selected is not null;
        MoveUpButton.IsEnabled = ManagedProviderList.SelectedIndex > 0;
        MoveDownButton.IsEnabled = selected is not null && ManagedProviderList.SelectedIndex < Providers.Count - 1;
        if (selected is null) return;
        SelectedProviderName.Text = selected.Name;
        SelectedProviderSetup.Text = selected.Definition.Setup;
        SelectedProviderStatus.Text = _snapshots.TryGetValue(selected.Id, out var snapshot)
            ? snapshot.Message + (snapshot.UpdatedAt is { } observed ? $"\nLast successful refresh: {observed.LocalDateTime:g}" : "") + (snapshot.RetryAt is { } retry ? $"\nNext allowed check: {retry.LocalDateTime:g}" : "") : "Not checked yet. Add and save, or check the connection now.";
        CheckConnectionButton.IsEnabled = !_snapshots.TryGetValue(selected.Id, out var current) || !(current.RetryAt > DateTimeOffset.UtcNow);
        ClaudeSignInButton.Visibility = selected.Id == "claude" ? Visibility.Visible : Visibility.Collapsed;
        GitHubSignInButton.Visibility = selected.Id == "copilot" ? Visibility.Visible : Visibility.Collapsed;
        OtherSignInButton.Visibility = selected.Id is "codex" or "cursor" ? Visibility.Visible : Visibility.Collapsed;
        OtherSignInButton.Content = selected.Id == "cursor" ? "Open Cursor" : "Sign in to Codex";
        CodexOptions.Visibility = selected.Id == "codex" ? Visibility.Visible : Visibility.Collapsed;
        UsagePageButton.Visibility = selected.Definition.UsageUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Provider_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDetails();
    private void AddProvider_Click(object sender, RoutedEventArgs e) { if (ProviderCatalogPicker.SelectedItem is ProviderDefinition provider) AddProvider(provider.Id); }
    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveProvider(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveProvider(1);
    private void RemoveProvider_Click(object sender, RoutedEventArgs e) => RemoveSelectedProvider();
    private void UsagePage_Click(object sender, RoutedEventArgs e)
    { if (ManagedProviderList.SelectedItem is ManagedProvider provider && provider.Definition.UsageUrl.Length > 0) MainWindow.OpenUrl(provider.Definition.UsageUrl); }
    private async void CheckConnection_Click(object sender, RoutedEventArgs e)
    {
        if (ManagedProviderList.SelectedItem is not ManagedProvider provider) return;
        if (_demo) { SelectedProviderStatus.Text = "Sample mode: account connections are not queried."; return; }
        CheckConnectionButton.IsEnabled = false; SelectedProviderStatus.Text = "Checking connection…";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var snapshot = _check is null ? UsageSnapshot.Empty(provider.Id, ConnectionState.Unavailable, "Save and refresh the widget to check this connection.") : await _check(provider.Id, timeout.Token);
            _snapshots[provider.Id] = snapshot; provider.SetStatus(StatusFor(provider.Id));
        }
        catch (OperationCanceledException) { if (!_closed.IsCancellationRequested) _snapshots[provider.Id] = UsageSnapshot.Empty(provider.Id, ConnectionState.Unavailable, "Connection check timed out. Retry when ready."); }
        catch (Exception) { _snapshots[provider.Id] = UsageSnapshot.Empty(provider.Id, ConnectionState.Unavailable, "Connection check failed. Check sign-in and retry."); }
        finally { if (!_closed.IsCancellationRequested) { CheckConnectionButton.IsEnabled = true; UpdateDetails(); } }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Codex executable (codex.exe)|codex.exe", Title = "Select Codex executable" };
        if (picker.ShowDialog(this) == true) CodexPath.Text = picker.FileName;
    }
    private async void GitHubSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { SelectedProviderStatus.Text = "Sample mode: sign-in is disabled."; return; }
        var executable = LocalProviderCredentials.FindGitHubCli();
        if (executable is null)
        {
            ErrorText.Text = "Install GitHub CLI from cli.github.com, then use this button to sign in. Your GitHub account needs a Copilot subscription.";
            ErrorText.BringIntoView(); return;
        }
        GitHubSignInButton.IsEnabled = false;
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, "auth login --web --hostname github.com") { UseShellExecute = true });
            if (process is null) return;
            SelectedProviderStatus.Text = "Complete sign-in in the GitHub CLI window and browser, then check the connection.";
            await process.WaitForExitAsync();
            ClaudeSignInCompleted |= process.ExitCode == 0;
            if (!_closed.IsCancellationRequested) SelectedProviderStatus.Text = process.ExitCode == 0 ? "GitHub sign-in completed. Check connection, then save." : "Sign-in did not complete. Try again when ready.";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { ErrorText.Text = "GitHub sign-in could not be opened. Run gh auth login yourself, then refresh."; ErrorText.BringIntoView(); }
        finally { GitHubSignInButton.IsEnabled = true; }
    }
    private async void ClaudeSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { SelectedProviderStatus.Text = "Sample mode: sign-in is disabled."; return; }
        try
        {
            ClaudeSignInButton.IsEnabled = false;
            var executable = await Task.Run(ClaudeSignIn.FindExecutable);
            if (executable is null)
            {
                var guide = new ClaudeSetupWindow { Owner = this }; guide.ShowDialog();
                if (guide.Choice == ClaudeSetupChoice.Guide) { MainWindow.OpenUrl("https://code.claude.com/docs/en/setup"); return; }
                if (guide.Choice != ClaudeSetupChoice.Install) return;
                ErrorText.Text = "Finish the official Claude Code setup window. Account sign-in will open next.";
                using var setup = System.Diagnostics.Process.Start(ClaudeSignIn.CreateSetupStartInfo());
                if (setup is null) return;
                await setup.WaitForExitAsync(_closed.Token);
                executable = await Task.Run(ClaudeSignIn.FindExecutable);
                if (setup.ExitCode != 0 || executable is null) { ErrorText.Text = "Claude setup did not complete. Try again or open the setup guide."; return; }
            }
            if (_closed.IsCancellationRequested) return;
            using var process = System.Diagnostics.Process.Start(ClaudeSignIn.CreateStartInfo(executable));
            if (process is null) return;
            ErrorText.Text = "Finish sign-in in the Claude window or browser, then save and refresh.";
            await process.WaitForExitAsync(_closed.Token);
            ClaudeSignInCompleted = process.ExitCode == 0;
            if (process.ExitCode == 0 && _check is not null && !_closed.IsCancellationRequested) _snapshots["claude"] = await _check("claude", _closed.Token);
            ErrorText.Text = process.ExitCode == 0 ? "Sign-in completed. Save and refresh to check usage. Any server cooldown will still be respected." : "Sign-in did not complete. You can try again when ready.";
            ErrorText.BringIntoView();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { ErrorText.Text = "Claude sign-in could not be opened. Run claude auth login, then refresh TokenWatch."; ErrorText.BringIntoView(); }
        finally { if (!_closed.IsCancellationRequested) { ClaudeSignInButton.IsEnabled = true; UpdateDetails(); } }
    }
    private async void OtherSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_demo || ManagedProviderList.SelectedItem is not ManagedProvider provider) return;
        try
        {
            var executable = provider.Id == "codex" ? CodexProvider.FindExecutable(_settings.CodexExecutable) : new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "cursor", "Cursor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cursor", "Cursor.exe") }.FirstOrDefault(File.Exists);
            if (executable is null) { ErrorText.Text = "The provider app or CLI was not found. Install it, then try again."; return; }
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, provider.Id == "codex" ? "login" : "") { UseShellExecute = true });
            if (provider.Id == "cursor") { SelectedProviderStatus.Text = "Finish signing in in Cursor, then check the connection."; return; }
            if (process is null) return;
            OtherSignInButton.IsEnabled = false;
            await process.WaitForExitAsync(_closed.Token);
            ClaudeSignInCompleted |= process.ExitCode == 0;
            if (_check is not null && !_closed.IsCancellationRequested) { _snapshots[provider.Id] = await _check(provider.Id, _closed.Token); UpdateDetails(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException) { ErrorText.Text = "Sign-in could not be opened. Sign in in the provider app, then check the connection."; }
        finally { if (!_closed.IsCancellationRequested) OtherSignInButton.IsEnabled = true; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = CodexPath.Text.Trim().Trim('"');
        if (path.Length > 0 && (!File.Exists(path) || !Path.GetFileName(path).Equals("codex.exe", StringComparison.OrdinalIgnoreCase)))
        { ErrorText.Text = "Select an existing codex.exe, or leave the path blank for automatic detection."; ErrorText.BringIntoView(); return; }
        if (!double.TryParse(DailyUsageBudget.Text, out var dailyBudget) || !double.IsFinite(dailyBudget) || dailyBudget < 1 || dailyBudget > 100)
        { ErrorText.Text = "Enter a daily usage budget from 1 to 100 percent."; ErrorText.BringIntoView(); return; }
        _settings.ShowPaceMarkers = PaceMarkersOption.IsChecked == true;
        _settings.DailyUsagePercent = dailyBudget;
        _settings.Pinned = PinnedOption.IsChecked == true; _settings.AlwaysOnTop = TopmostOption.IsChecked == true;
        _settings.ShowRemaining = ShowRemainingOption.IsChecked == true;
        _settings.ShowResetDetails = ResetDetailsOption.IsChecked == true;
        _settings.ProviderOrder = Providers.Select(p => p.Id).ToArray();
        _settings.ClaudeEnabled = Providers.Any(p => p.Id == "claude" && p.Enabled); _settings.CodexEnabled = Providers.Any(p => p.Id == "codex" && p.Enabled);
        _settings.AntigravityEnabled = Providers.Any(p => p.Id == "antigravity" && p.Enabled);
        _settings.CopilotEnabled = Providers.Any(p => p.Id == "copilot" && p.Enabled); _settings.CursorEnabled = Providers.Any(p => p.Id == "cursor" && p.Enabled);
        _settings.RefreshMinutes = ((Interval)RefreshInterval.SelectedItem).Minutes;
        _settings.CodexExecutable = path.Length == 0 ? null : path;
        DialogResult = true;
    }
}

internal sealed class ManagedProvider(ProviderDefinition definition, bool enabled, string status) : Observable
{
    public ProviderDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    private bool _enabled = enabled;
    private string _status = status;
    public bool Enabled { get => _enabled; set { _enabled = value; Changed(); Changed(nameof(Status)); } }
    public string Status => Enabled ? _status : "Paused";
    public void SetStatus(string value) { _status = value; Changed(nameof(Status)); }
}
