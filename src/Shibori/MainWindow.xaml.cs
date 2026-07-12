using System.Diagnostics;
using System.IO;
using System.Windows;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace Shibori;

public partial class MainWindow : Window
{
    private readonly DisplayConfigurationService displayService = new();
    private bool suppressEvents;
    private bool busy;
    private UpdateChecker.ReleaseInfo? pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        AppLogger.Info("Application started.");
        InfoButton.ToolTip = $"Shibori v{UpdateChecker.CurrentVersionLabel}";
        Loaded += async (_, _) => { Reload(); if (!App.StartedWithWindows) await CheckForUpdateAsync(); };
    }

    private void Reload()
    {
        try { suppressEvents = true; MonitorList.ItemsSource = displayService.GetMonitors(); suppressEvents = false; }
        catch (Exception ex) { suppressEvents = false; ShowError(ex); }
    }

    private void ConnectionToggle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (suppressEvents || busy || sender is not WpfToggleButton { Tag: MonitorInfo monitor } toggle) return;
        if (toggle.IsChecked == true) RunDisplayOperation(() => displayService.Restore(monitor));
        else RunDisplayOperation(() => displayService.Pause([monitor with { IsPrimary = false }]));
    }

    private void MainCheckBox_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (suppressEvents || busy || sender is not WpfCheckBox { Tag: MonitorInfo monitor } checkBox) return;
        if (!checkBox.IsChecked.GetValueOrDefault()) { checkBox.IsChecked = true; return; }
        if (!monitor.IsPrimary) RunDisplayOperation(() => displayService.SetPrimary(monitor));
    }

    private void RunDisplayOperation(Action operation)
    {
        busy = true;
        MonitorList.IsEnabled = false;
        try { operation(); AppLogger.Info("Display configuration changed."); Reload(); }
        catch (Exception ex) { AppLogger.Error(ex, "Display configuration change failed"); Reload(); ShowError(ex); }
        finally { busy = false; MonitorList.IsEnabled = true; }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var message = $"Shibori\nバージョン {UpdateChecker.CurrentVersionLabel}\n\nライセンス\nShibori: Apache License 2.0（LICENSE）\n同梱.NET 8ランタイム: Microsoftライセンス・通知（配布zip内のTHIRD-PARTY-NOTICES.md）\n\n不具合の連絡先\n{UpdateChecker.IssuesUrl}\n現在のログ: {AppLogger.CurrentLogPath}";
        var help = new System.Windows.Controls.StackPanel();
        help.Children.Add(CreateTroubleshooting("モニターが一覧から消えた場合", "Shiboriを起動したままWindowsを再起動すると、次回起動時に一時停止中のモニターを復旧できます。表示されない場合はWindowsの表示設定で検出を行ってください。"));
        help.Children.Add(CreateTroubleshooting("表示構成の復旧に失敗した場合", "ケーブルの抜き差しやWindowsの表示設定変更を止め、Shiboriを再起動してから再試行してください。バックアップは %LOCALAPPDATA%\\Shibori\\display-backup.json に保存されています。"));
        help.Children.Add(CreateTroubleshooting("ログを提出する場合", $"GitHub Issues（{UpdateChecker.IssuesUrl}）へ状況を記載し、ログファイルを添付してください。ログフォルダーとログファイルは下のボタンから開けます。"));
        help.Children.Add(CreateTroubleshooting("Windows起動時に使う場合", "スタートアップ登録時はショートカットのリンク先末尾に --startup を追加してください。更新ダイアログを表示せず起動できます。現在はタスクトレイ常駐ではなく通常のウィンドウです。"));
        CopyableDialog.Show(this, "Shiboriについて", message, secondaryButton: "ログフォルダーを開く", tertiaryButton: "ログファイルを開く", extraContent: help);
    }

    private static System.Windows.Controls.Expander CreateTroubleshooting(string title, string text) => new()
    {
        Header = title,
        Margin = new Thickness(4, 6, 4, 0),
        Content = new System.Windows.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }
    };

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var update = await UpdateChecker.FindAsync();
            if (update is null || !IsLoaded) return;
            pendingUpdate = update;
            UpdateButton.Content = $"update to v{update.VersionLabel}";
            UpdateButton.Visibility = Visibility.Visible;
            AppLogger.Info($"Update available: {update.VersionLabel}");
            if (CopyableDialog.Show(this, "アップデートがあります", $"現在: {UpdateChecker.CurrentVersionLabel}\n最新: {update.VersionLabel}", "今すぐ更新")) await InstallUpdateAsync();
        }
        catch (Exception ex) { AppLogger.Error(ex, "Update check failed"); }
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await InstallUpdateAsync();

    private async Task InstallUpdateAsync()
    {
        if (pendingUpdate is null || busy) return;
        busy = true; UpdateButton.IsEnabled = false;
        try { await UpdateInstaller.InstallAsync(pendingUpdate); }
        catch (Exception ex) { busy = false; UpdateButton.IsEnabled = true; AppLogger.Error(ex, "Update installation failed"); ShowError(ex); }
    }

    private void ShowError(Exception ex) => CopyableDialog.Show(this, "Shibori", ex.Message, secondaryButton: "ログフォルダーを開く", tertiaryButton: "ログファイルを開く");
}

internal static class CopyableDialog
{
    public static bool Show(Window owner, string title, string message, string? primaryButton = null, string? secondaryButton = null, string? tertiaryButton = null, System.Windows.UIElement? extraContent = null)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 560, Height = extraContent is null ? 320 : 520, MinWidth = 420, MinHeight = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false };
        var text = new System.Windows.Controls.TextBox { Text = message, IsReadOnly = true, IsReadOnlyCaretVisible = true, Focusable = true, IsTabStop = true, Cursor = System.Windows.Input.Cursors.IBeam, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(4) };
        var primary = new System.Windows.Controls.Button { Content = primaryButton, MinWidth = 100, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        primary.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        var secondary = new System.Windows.Controls.Button { Content = secondaryButton, MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        secondary.Click += (_, _) => OpenLogDirectory();
        var tertiary = new System.Windows.Controls.Button { Content = tertiaryButton, MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        tertiary.Click += (_, _) => OpenLogFile();
        var close = new System.Windows.Controls.Button { Content = "閉じる", MinWidth = 80, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        if (primaryButton is not null) buttons.Children.Add(primary); if (secondaryButton is not null) buttons.Children.Add(secondary); if (tertiaryButton is not null) buttons.Children.Add(tertiary); buttons.Children.Add(close);
        var body = new System.Windows.Controls.StackPanel(); body.Children.Add(text); if (extraContent is not null) body.Children.Add(extraContent);
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(18) };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(body);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }

    private static void OpenLogDirectory()
    {
        Directory.CreateDirectory(AppLogger.DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", AppLogger.DirectoryPath) { UseShellExecute = true });
    }

    private static void OpenLogFile()
    {
        if (!File.Exists(AppLogger.CurrentLogPath)) AppLogger.Info("Log file opened.");
        Process.Start(new ProcessStartInfo(AppLogger.CurrentLogPath) { UseShellExecute = true });
    }
}
