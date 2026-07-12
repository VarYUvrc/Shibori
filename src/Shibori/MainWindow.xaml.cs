using System.Diagnostics;
using System.IO;
using System.Windows;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;

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
        Loaded += async (_, _) => { Reload(); if (!App.StartedWithWindows) await CheckForUpdateAsync(); };
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
        RunDisplayOperation(() => displayService.Pause([monitor with { IsPrimary = false }]));
    }

    private void Role_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (busy || sender is not WpfComboBox { Tag: MonitorInfo monitor, SelectedValue: string role } || role != "メイン" || monitor.IsPrimary)
            return;
        RunDisplayOperation(() => displayService.SetPrimary(monitor));
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
        var message = $"Shibori\nバージョン {UpdateChecker.CurrentVersionLabel}\n\nライセンス\nShibori: Apache License 2.0\n.NET 8 / WPF / Windows SDK: Microsoft ライセンス\n追加のNuGetパッケージ: なし\n\n不具合の連絡先\n{UpdateChecker.IssuesUrl}\nログ: {AppLogger.DirectoryPath}";
        var recovery = new System.Windows.Controls.Expander
        {
            Header = "モニターが見つからない場合の復旧",
            Margin = new Thickness(4, 10, 4, 0),
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "Shiboriを起動したままWindowsを再起動すると、次回起動時に一時停止中のモニターを復旧できます。表示されない場合は、Shiboriを先に起動してからWindowsの表示設定を開き、表示を検出してください。バックアップは %LOCALAPPDATA%\\Shibori\\display-backup.json に保存されます。スタートアップ登録時はショートカットのリンク先末尾に --startup を付けると、更新ダイアログを表示せず起動できます。現在はタスクトレイ常駐ではなく、通常のウィンドウとして起動します。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            }
        };
        if (CopyableDialog.Show(this, "Shiboriについて", message, secondaryButton: "ログフォルダーを開く", extraContent: recovery))
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
    public static bool Show(Window owner, string title, string message, string? primaryButton = null, string? secondaryButton = null, System.Windows.UIElement? extraContent = null)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 560, Height = extraContent is null ? 320 : 460, MinWidth = 420, MinHeight = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false };
        var text = new System.Windows.Controls.TextBox { Text = message, IsReadOnly = true, IsReadOnlyCaretVisible = true, Focusable = true, IsTabStop = true, Cursor = System.Windows.Input.Cursors.IBeam, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(4) };
        var primary = new System.Windows.Controls.Button { Content = primaryButton, MinWidth = 100, IsDefault = true };
        primary.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        var secondary = new System.Windows.Controls.Button { Content = secondaryButton, MinWidth = 120 };
        secondary.Click += (_, _) => { Directory.CreateDirectory(AppLogger.DirectoryPath); Process.Start(new ProcessStartInfo("explorer.exe", AppLogger.DirectoryPath) { UseShellExecute = true }); };
        var close = new System.Windows.Controls.Button { Content = "閉じる", MinWidth = 80, IsCancel = true };
        close.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        if (primaryButton is not null) buttons.Children.Add(primary); if (secondaryButton is not null) buttons.Children.Add(secondary); buttons.Children.Add(close);
        var body = new System.Windows.Controls.StackPanel(); body.Children.Add(text); if (extraContent is not null) body.Children.Add(extraContent);
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(18) };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(body);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}
