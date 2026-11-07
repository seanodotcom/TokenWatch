using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenWatch.Core;

namespace TokenWatch.WinUI;

public sealed partial class ManagerWindow : Window
{
    private bool _ready;
    private bool _syncing;
    private readonly PropertyChangedEventHandler _sessionChanged;
    private readonly Dictionary<string, DateTimeOffset> _lastChecks = [];
    internal Grid Surface => Root;
    internal string SelectedId => (ProviderList.SelectedItem as ProviderItem)?.Id ?? "";
    internal bool ShowsConnectionInfo => ConnectionInfo.IsOpen;
    public ManagerWindow()
    {
        InitializeComponent();
        Title = App.Session.IsDemo ? "TokenWatch · Sample settings" : "TokenWatch · Settings";
        AppVersionCaption.Text = TokenWatch.AppVersion.Display + (App.Session.IsDemo ? " sample" : "");
        StateNav.Content = App.Session.IsDemo ? "Sample states" : "Activity";
        SessionBadge.Visibility = App.Session.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TokenWatch.ico"));
        AppWindow.TitleBar.BackgroundColor = Ui.Brush("#202329").Color;
        AppWindow.TitleBar.ForegroundColor = Ui.Brush("#EEF0F5").Color;
        AppWindow.TitleBar.ButtonBackgroundColor = Ui.Brush("#202329").Color;
        AppWindow.TitleBar.ButtonForegroundColor = Ui.Brush("#EEF0F5").Color;
        ProviderList.ItemsSource = App.Session.Providers;
        StateProviderPicker.ItemsSource = App.Session.Providers;
        RefreshPicker.ItemsSource = new[] { 1, 2, 5, 10, 15, 30, 60 }.Append(App.Session.RefreshMinutes).Distinct().Order().Select(n => new RefreshChoice(n)).ToArray();
        _sessionChanged = (_, _) => { if (!App.IsExiting) DispatcherQueue.TryEnqueue(() => { if (!App.IsExiting) Update(); }); };
        App.Session.PropertyChanged += _sessionChanged;
        Closed += (_, _) => PrepareShutdown();
        _ready = true;
        Navigation.SelectedItem = ProvidersNav;
        Root.Loaded += (_, _) => { Ui.Place(this, 1060, 770); Update(); SelectProvider("copilot"); };
        Update();
    }
    internal void SelectProvider(string id)
    {
        ProviderList.SelectedItem = App.Session.Providers.FirstOrDefault(p => p.Id == id) ?? App.Session.Providers.FirstOrDefault();
        Navigation.SelectedItem = ProvidersNav; UpdateDetails();
    }
    private void Update()
    {
        if (!_ready) return;
        _syncing = true;
        try
        {
            RemainingSwitch.IsOn = App.Session.ShowRemaining; ResetSwitch.IsOn = App.Session.ShowReset; TopmostSwitch.IsOn = App.Session.Topmost; KeepOpenSwitch.IsOn = App.Session.KeepOpen;
            PaceMarkersSwitch.IsOn = App.Session.ShowPaceMarkers; DailyUsagePicker.Value = App.Session.DailyUsagePercent; DailyUsagePicker.IsEnabled = App.Session.ShowPaceMarkers;
            RefreshPicker.SelectedItem = RefreshPicker.Items.OfType<RefreshChoice>().FirstOrDefault(p => p.Minutes == App.Session.RefreshMinutes);
            if (ProviderList.SelectedItem is not ProviderItem || !App.Session.Providers.Contains((ProviderItem)ProviderList.SelectedItem)) ProviderList.SelectedItem = App.Session.Providers.FirstOrDefault();
            EmptyListText.Visibility = App.Session.Providers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (StateProviderPicker.SelectedItem is null) StateProviderPicker.SelectedIndex = 0;
            SaveStatus.Text = App.Session.PersistenceError ?? (App.Session.IsDemo ? "Sample mode · separate preferences" : "Changes save automatically · existing sign-ins are reused");
            ActivityText.Text = App.Monitor.IsRefreshing ? "Refreshing usage…" : $"Last check: {(App.Monitor.LastAttempt is { } attempt ? attempt.LocalDateTime.ToString("g") : "Not checked")}\nNext check: {App.Monitor.NextRefresh.LocalDateTime:t}\n\n" + string.Join("\n", App.Session.Providers.Select(p => p.Name + " · " + p.Status));
            AddButton.IsEnabled = App.Session.Providers.Count < ProviderCatalog.All.Count;
            UpdateDetails();
        }
        finally { _syncing = false; }
    }
    private void UpdateDetails()
    {
        if (!_ready) return;
        var selected = ProviderList.SelectedItem as ProviderItem;
        Details.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        MoveUpButton.IsEnabled = selected is not null && ProviderList.SelectedIndex > 0;
        MoveDownButton.IsEnabled = selected is not null && ProviderList.SelectedIndex < App.Session.Providers.Count - 1;
        RemoveButton.IsEnabled = selected is not null;
        if (selected is null) return;
        SelectedName.Text = selected.Name;
        LastCheck.Text = App.Session.IsDemo ? _lastChecks.TryGetValue(selected.Id, out var lastCheck) ? lastCheck.ToString("h:mm tt") : "Not checked" : selected.Snapshot.UpdatedAt is { } updated ? updated.LocalDateTime.ToString("g") : "Not checked";
        SelectedSubtitle.Text = selected.Id switch { "copilot" => "AI credits and monthly allowances", "cursor" => "Included usage and on-demand budget", "antigravity" => "Gemini and Argon model allowances", _ => "Five-hour and weekly allowances" };
        SignInSource.Text = selected.Id switch { "claude" => "Claude Code", "codex" => "Codex app / CLI", "antigravity" => "Antigravity", "copilot" => "GitHub CLI", _ => "Cursor Desktop" };
        ConnectionInfo.Severity = selected.Connection switch { ConnectionState.Stale or ConnectionState.RateLimited => InfoBarSeverity.Warning, ConnectionState.SignInRequired => InfoBarSeverity.Error, _ => InfoBarSeverity.Success };
        ConnectionInfo.Title = !selected.Enabled ? "Hidden from your widget" : selected.Connection switch { ConnectionState.Stale => "Showing the last sample reading", ConnectionState.RateLimited => "Waiting for the next allowed check", ConnectionState.SignInRequired => "Sign in to reconnect", _ => "Sample connection ready" };
        ConnectionInfo.Message = !selected.Enabled ? "Switch this provider on to show it in the widget. Its settings are retained." : selected.Connection switch { ConnectionState.Stale => "Last updated 12 minutes ago. Previous values stay visible and dimmed.", ConnectionState.RateLimited => "Retry in 8 minutes. A manual refresh respects the same cooldown.", ConnectionState.SignInRequired => "Usage stays unknown until this provider is connected again.", _ => "Usage values in this preview are simulated." };
        ConnectionButton.Content = selected.Connection == ConnectionState.SignInRequired ? "Reconnect" : "Check connection";
        ConnectionButton.IsEnabled = selected.Connection != ConnectionState.RateLimited;
        SignInButton.Visibility = !App.Session.IsDemo && selected.Id != "antigravity" ? Visibility.Visible : Visibility.Collapsed;
        SignInButton.Content = selected.Id == "cursor" ? "Open Cursor" : "Sign in";
        CodexOptions.Visibility = selected.Id == "codex" ? Visibility.Visible : Visibility.Collapsed;
        if (CodexPath.FocusState == FocusState.Unfocused) CodexPath.Text = App.Session.CodexExecutable ?? "";
        ConnectionGuidance.Text = selected.Id switch
        {
            "claude" => "Sign in once with Claude Code. TokenWatch can reuse that connection and renew it automatically.",
            "codex" => "Uses the ChatGPT subscription connected to your Codex app or CLI.",
            "antigravity" => "Keep Antigravity and a workspace open to read the Gemini and Argon allowances together.",
            "copilot" => "The live app reuses your GitHub CLI sign-in. Your GitHub account needs a Copilot subscription.",
            _ => "The live app reuses your Cursor Desktop sign-in. If it expires, open Cursor and sign in again."
        };
        if (!App.Session.IsDemo)
        {
            ConnectionInfo.Severity = selected.Connection switch { ConnectionState.Connected => InfoBarSeverity.Success, ConnectionState.Loading => InfoBarSeverity.Informational, ConnectionState.Stale or ConnectionState.RateLimited or ConnectionState.SignInRequired or ConnectionState.NotRunning => InfoBarSeverity.Warning, _ => InfoBarSeverity.Error };
            ConnectionInfo.Title = !selected.Enabled ? "Hidden from your widget" : selected.Status;
            ConnectionInfo.Message = selected.Snapshot.Message + (selected.Snapshot.RetryAt is { } retry ? $"\nNext allowed check: {retry.LocalDateTime:g}" : "") + (selected.Connection == ConnectionState.Stale && selected.Snapshot.UpdatedAt is { } observed ? $"\nLast successful reading: {observed.LocalDateTime:g}" : "");
            ConnectionButton.Content = "Check connection";
            ConnectionButton.IsEnabled = !(selected.Snapshot.RetryAt > DateTimeOffset.UtcNow);
            ConnectionGuidance.Text = selected.Definition.Setup.Replace("This usage interface is experimental.", "").Replace("Experimental connection.", "Experimental connection.");
        }
        UsageButton.Visibility = selected.Definition.UsageUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        QuotaSummary.Children.Clear();
        foreach (var quota in selected.Quotas)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Ui.Text(quota.Group is null ? quota.Label : $"{quota.Group} · {quota.Label}", 12, "#B4BAC6"));
            var value = Ui.Text(!selected.Enabled || selected.Connection == ConnectionState.SignInRequired ? "—" : quota.DisplayText(App.Session.ShowRemaining) + (App.Session.ShowRemaining ? " left" : " used"), 12);
            Grid.SetColumn(value, 1); row.Children.Add(value); QuotaSummary.Children.Add(row);
        }
    }
    private void Provider_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetails();
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_ready || args.SelectedItem is not NavigationViewItem item) return;
        var page = item.Tag?.ToString();
        ProvidersPage.Visibility = page == "providers" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = page == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        StatesPage.Visibility = page == "states" && App.Session.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        ActivityPage.Visibility = page == "states" && !App.Session.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page switch { "appearance" => "Appearance", "states" => App.Session.IsDemo ? "Sample states" : "Activity", _ => "Providers" };
        PageSubtitle.Text = page switch { "appearance" => "Make the widget work for your desktop.", "states" => App.Session.IsDemo ? "Explore connection states with sample data." : "Usage checks continue while the widget is hidden.", _ => "Choose what appears in your widget." };
        AddButton.Visibility = page == "providers" ? Visibility.Visible : Visibility.Collapsed;
    }
    private void AddFlyout_Opening(object sender, object e)
    {
        AddFlyout.Items.Clear();
        foreach (var definition in ProviderCatalog.All.Where(p => App.Session.Providers.All(item => item.Id != p.Id)))
        {
            var menu = new MenuFlyoutItem { Text = definition.Name };
            menu.Click += (_, _) => { App.Session.Add(definition.Id); SelectProvider(definition.Id); };
            AddFlyout.Items.Add(menu);
        }
    }
    internal void MoveSelected(int direction) { if (ProviderList.SelectedItem is ProviderItem selected) { App.Session.Move(selected, direction); SelectProvider(selected.Id); } }
    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void Remove_Click(object sender, RoutedEventArgs e) { if (ProviderList.SelectedItem is ProviderItem selected) App.Session.Providers.Remove(selected); }
    private void Preference_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncing) return;
        App.Session.ShowRemaining = RemainingSwitch.IsOn; App.Session.ShowReset = ResetSwitch.IsOn; App.Session.Topmost = TopmostSwitch.IsOn; App.Session.KeepOpen = KeepOpenSwitch.IsOn;
        App.Session.ShowPaceMarkers = PaceMarkersSwitch.IsOn;
    }
    private void DailyUsage_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_ready || _syncing) return;
        App.Session.DailyUsagePercent = double.IsFinite(args.NewValue) ? args.NewValue : 20;
        sender.Value = App.Session.DailyUsagePercent;
    }
    private void Refresh_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (_ready && !_syncing && RefreshPicker.SelectedItem is RefreshChoice choice) App.Session.RefreshMinutes = choice.Minutes; }
    internal async Task CheckSampleConnectionAsync()
    {
        if (!App.Session.IsDemo) { if (ProviderList.SelectedItem is ProviderItem live) await App.Monitor.CheckAsync(live.Id); return; }
        if (ProviderList.SelectedItem is not ProviderItem selected || selected.Connection == ConnectionState.RateLimited) return;
        ConnectionButton.IsEnabled = false; ConnectionButton.Content = "Checking…";
        await Task.Delay(400);
        selected.Connection = ConnectionState.Connected; _lastChecks[selected.Id] = DateTimeOffset.Now;
        UpdateDetails();
    }
    private async void Connection_Click(object sender, RoutedEventArgs e) => await CheckSampleConnectionAsync();
    private async void RefreshAll_Click(object sender, RoutedEventArgs e) => await App.Monitor.RefreshAsync();
    private void SaveCodex_Click(object sender, RoutedEventArgs e)
    {
        var path = CodexPath.Text.Trim().Trim('"');
        if (path.Length > 0 && (!File.Exists(path) || !Path.GetFileName(path).Equals("codex.exe", StringComparison.OrdinalIgnoreCase)))
        { ConnectionInfo.Severity = InfoBarSeverity.Error; ConnectionInfo.Message = "Choose an existing codex.exe or leave the path empty for automatic detection."; return; }
        App.Session.CodexExecutable = path.Length == 0 ? null : path;
    }
    private async void BrowseCodex_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) { CodexPath.Text = file.Path; SaveCodex_Click(sender, e); }
    }
    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (App.Session.IsDemo || ProviderList.SelectedItem is not ProviderItem provider) return;
        SignInButton.IsEnabled = false;
        try
        {
            System.Diagnostics.Process? process = null;
            if (provider.Id == "claude")
            {
                process = await StartClaudeSignInAsync();
                if (process is null) return;
            }
            else if (provider.Id is "codex" or "copilot")
            {
                var executable = provider.Id == "codex" ? TokenWatch.CodexProvider.FindExecutable(App.Session.CodexExecutable) : TokenWatch.LocalProviderCredentials.FindGitHubCli();
                if (executable is not null) process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, provider.Id == "codex" ? "login" : "auth login --web --hostname github.com") { UseShellExecute = true });
            }
            else if (provider.Id == "cursor")
            {
                var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "cursor", "Cursor.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cursor", "Cursor.exe") };
                var executable = candidates.FirstOrDefault(File.Exists);
                if (executable is not null) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true }); ConnectionInfo.Message = "Finish signing in in Cursor, then click Check connection."; return; }
            }
            if (process is null) { ConnectionInfo.Severity = InfoBarSeverity.Warning; ConnectionInfo.Message = "The provider app or CLI was not found. Install or sign in to it, then check the connection again."; return; }
            using (process) await process.WaitForExitAsync();
            if (!App.IsExiting) await App.Monitor.CheckAsync(provider.Id);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { if (_ready) { ConnectionInfo.Severity = InfoBarSeverity.Error; ConnectionInfo.Message = "Sign-in could not be opened. Sign in in the provider's app or CLI, then check the connection."; } }
        finally { if (_ready) SignInButton.IsEnabled = true; }
    }
    internal static ContentDialog ClaudeSetupDialog(XamlRoot root) => new()
    {
        XamlRoot = root, RequestedTheme = ElementTheme.Dark,
        Title = "Connect your Claude account",
        Content = new TextBlock { Text = "TokenWatch couldn't find a compatible Claude Code sign-in helper. Set up Anthropic's official Windows helper, then sign in with your existing Claude account in the browser. A setup window will open first.", TextWrapping = TextWrapping.Wrap },
        PrimaryButtonText = "Set up and sign in", SecondaryButtonText = "Setup guide", CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary
    };
    private async Task<System.Diagnostics.Process?> StartClaudeSignInAsync()
    {
        var executable = await Task.Run(TokenWatch.ClaudeSignIn.FindExecutable);
        if (executable is null)
        {
            var choice = await ClaudeSetupDialog(Root.XamlRoot).ShowAsync();
            if (choice == ContentDialogResult.Secondary)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://code.claude.com/docs/en/setup"));
                return null;
            }
            if (choice != ContentDialogResult.Primary) return null;
            ConnectionInfo.Severity = InfoBarSeverity.Informational;
            ConnectionInfo.Message = "Setting up Claude sign-in. Finish the setup window; account sign-in will open next.";
            using var setup = System.Diagnostics.Process.Start(TokenWatch.ClaudeSignIn.CreateSetupStartInfo());
            if (setup is not null) await setup.WaitForExitAsync();
            if (App.IsExiting) return null;
            executable = await Task.Run(TokenWatch.ClaudeSignIn.FindExecutable);
            if (setup is null || setup.ExitCode != 0 || executable is null)
            {
                ConnectionInfo.Severity = InfoBarSeverity.Warning;
                ConnectionInfo.Message = "Claude sign-in setup didn't finish. Click Sign in to try again or open the setup guide.";
                return null;
            }
        }
        ConnectionInfo.Severity = InfoBarSeverity.Informational;
        ConnectionInfo.Message = "Finish signing in with your existing Claude account in the Claude window or browser. TokenWatch will check the connection afterward.";
        return System.Diagnostics.Process.Start(TokenWatch.ClaudeSignIn.CreateStartInfo(executable));
    }
    private async void Usage_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is ProviderItem selected && selected.Definition.UsageUrl.Length > 0)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(selected.Definition.UsageUrl));
    }
    private void ApplyState_Click(object sender, RoutedEventArgs e)
    { if (App.Session.IsDemo && StateProviderPicker.SelectedItem is ProviderItem provider) { provider.Connection = StatePicker.SelectedIndex switch { 1 => ConnectionState.Stale, 2 => ConnectionState.SignInRequired, 3 => ConnectionState.RateLimited, _ => ConnectionState.Connected }; SelectProvider(provider.Id); } }
    internal void ShowAppearance() => Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().First(p => p.Tag?.ToString() == "appearance");
    internal void PrepareShutdown() { _ready = false; App.Session.PropertyChanged -= _sessionChanged; }
    private sealed record RefreshChoice(int Minutes) { public override string ToString() => Minutes == 1 ? "Every minute" : $"Every {Minutes} minutes"; }
}
