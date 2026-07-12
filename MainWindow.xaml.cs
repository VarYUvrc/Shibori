using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace Shibori;

public partial class MainWindow : Window
{
    private readonly DisplayConfigurationService displayService = new();
    private bool suppressCheckEvents;
    private bool busy;
    private UpdateChecker.ReleaseInfo? pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        AppLogger.Info("Application started.");
        InfoButton.ToolTip = $"Shibori v{UpdateChecker.CurrentVersionLabel}";
        Loaded += async (_, _) => { Reload(); await CheckForUpdateAsync(); };
    }

    private void Reload()
    {
        try
        {
            suppressCheckEvents = true;
            MonitorList.ItemsSource = displayService.GetMonitors();
            suppressCheckEvents = false;
        }
        catch (Exception ex) { suppressCheckEvents = false; ShowError(ex); }
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        if (suppressCheckEvents || busy || sender is not WpfCheckBox { Tag: MonitorInfo monitor } checkBox) return;
        if (checkBox.IsChecked == true)
        {
            RunDisplayOperation(() => displayService.Restore(monitor));
            return;
        }
        if (monitor.IsPrimary)
        {
            suppressCheckEvents = true;
            checkBox.IsChecked = true;
            suppressCheckEvents = false;
            ShowError(new InvalidOperationException("メインモニターは切断できません。"));
            return;
        }
        RunDisplayOperation(() => displayService.Pause([monitor]));
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
        var message = $"Shibori\nバージョン {UpdateChecker.CurrentVersionLabel}\n\nライセンス\nShibori: Apache License 2.0\n.NET 8 / WPF / Windows SDK: Microsoft提供\n外部NuGetパッケージ: なし\n\n不具合の報告\n{UpdateChecker.IssuesUrl}\nログ: {AppLogger.DirectoryPath}";
        if (CopyableDialog.Show(this, "Shiboriについて", message, secondaryButton: "ログフォルダーを開く"))
            OpenLogDirectory();
    }

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
            if (CopyableDialog.Show(this, "アップデートがあります", $"現在: {UpdateChecker.CurrentVersionLabel}\n最新: {update.VersionLabel}\n\n更新ファイルをダウンロードして、このアプリを再起動します。", "今すぐ更新"))
                await InstallUpdateAsync();
        }
        catch (Exception ex) { AppLogger.Error(ex, "Update check failed"); }
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await InstallUpdateAsync();

    private async Task InstallUpdateAsync()
    {
        if (pendingUpdate is null || busy) return;
        busy = true;
        UpdateButton.IsEnabled = false;
        try { await UpdateInstaller.InstallAsync(pendingUpdate); }
        catch (Exception ex) { busy = false; UpdateButton.IsEnabled = true; AppLogger.Error(ex, "Update installation failed"); ShowError(ex); }
    }

    private void OpenLogDirectory()
    {
        Directory.CreateDirectory(AppLogger.DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", AppLogger.DirectoryPath) { UseShellExecute = true });
    }

    private void ShowError(Exception ex) => CopyableDialog.Show(this, "Shibori", ex.Message, secondaryButton: "ログフォルダーを開く");
}

internal static class CopyableDialog
{
    public static bool Show(Window owner, string title, string message, string? primaryButton = null, string? secondaryButton = null)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 560, Height = 320, MinWidth = 420, MinHeight = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false };
        var text = new System.Windows.Controls.TextBox { Text = message, IsReadOnly = true, IsReadOnlyCaretVisible = true, Focusable = true, IsTabStop = true, Cursor = System.Windows.Input.Cursors.IBeam, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(4) };
        var primary = new System.Windows.Controls.Button { Content = primaryButton, MinWidth = 100, IsDefault = true };
        primary.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        var secondary = new System.Windows.Controls.Button { Content = secondaryButton, MinWidth = 120 };
        secondary.Click += (_, _) => { Directory.CreateDirectory(AppLogger.DirectoryPath); Process.Start(new ProcessStartInfo("explorer.exe", AppLogger.DirectoryPath) { UseShellExecute = true }); };
        var close = new System.Windows.Controls.Button { Content = "閉じる", MinWidth = 80, IsCancel = true };
        close.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        if (primaryButton is not null) buttons.Children.Add(primary); if (secondaryButton is not null) buttons.Children.Add(secondary); buttons.Children.Add(close);
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(18) };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(text);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}
