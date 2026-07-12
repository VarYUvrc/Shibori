using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace Shibori;

public partial class MainWindow : Window
{
    private readonly DisplayConfigurationService displayService = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        try
        {
            MonitorList.ItemsSource = displayService.GetMonitors();
            UpdateState("準備完了");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void MonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PauseButton.IsEnabled = MonitorList.SelectedItems.Count > 0;
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        var selected = MonitorList.SelectedItems.Cast<MonitorInfo>().ToArray();
        if (selected.Any(m => m.IsPrimary))
        {
            MessageBox.Show(this, "メインモニターは一時停止できません。", "Shibori", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(this, $"{selected.Length}台のモニターを一時停止しますか？\n現在の構成は自動的にバックアップされます。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try { displayService.Pause(selected); UpdateState("一時停止しました"); Reload(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        try { displayService.Restore(); UpdateState("元の構成に戻しました"); Reload(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateState(string message)
    {
        StatusText.Text = message;
        RestoreButton.IsEnabled = displayService.HasBackup;
        BackupText.Text = displayService.GetBackupTime() is { } time
            ? $"バックアップ: {time.ToLocalTime():yyyy/MM/dd HH:mm}"
            : "バックアップなし";
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Shibori", MessageBoxButton.OK, MessageBoxImage.Error);
}
