using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace AULGK
{
    // 转换器：当 DisplayText 为空时隐藏控件
    public class EmptyTextToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // 转换器：当 DisplayText 为空时返回固定高度
    public class EmptyTextToHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return string.IsNullOrEmpty(value?.ToString()) ? 30.0 : double.NaN;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MainWindow : Window
    {
        private readonly HttpClient httpClient = new HttpClient();
        private readonly string presetServersUrl = "https://mxzc.cloud:35249/preset_servers.json"; // 替换为你的 HTTPS 地址
        private readonly string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private readonly string filePath;
        private readonly string logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"..\LocalLow\Innersloth\Among Us\AULGK.log");
        private List<RegionInfo> regions;
        private int currentRegionIdx = 3;
        private RegionInfo? currentRegion;
        private TextBox? nameEntry, pingEntry, portEntry, translateEntry;
        private TextBlock? nameStatus, pingStatus, portStatus, translateStatus;
        private StackPanel? advancedPanel;
        private bool advancedVisible;
        private List<PresetServer> presetServers;
        private bool isUpdatingSelection = false;
        private Point? dragStartPoint;
        private int lastInsertIndex = -1;
        private bool isDragging = false;
        private bool isUpdatingPingDelays = false;

        private void WriteLog(string message)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // 忽略日志写入错误
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            filePath = System.IO.Path.Combine(appDataPath, @"..\LocalLow\Innersloth\Among Us\regionInfo.json");
            regions = new List<RegionInfo>();
            presetServers = new List<PresetServer>(); LoadData();
            InitializePresetServers();
            Dispatcher.InvokeAsync(async () =>
            {
                await LoadPresetServersAsync();
                ShowMainPage();
            }, DispatcherPriority.Background);
        }

        private void LoadData()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var data = JsonSerializer.Deserialize<RegionData>(json);
                    if (data != null)
                    {
                        regions = data.Regions ?? new List<RegionInfo>();
                        currentRegionIdx = data.CurrentRegionIdx;
                    }
                }
                else
                {
                    SaveData();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                regions = new List<RegionInfo>();
                SaveData();
            }
        }

        private void SaveData()
        {
            try
            {
                var data = new RegionData
                {
                    CurrentRegionIdx = currentRegionIdx,
                    Regions = regions
                };
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存文件时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private readonly ObservableCollection<PresetServerDisplayItem> presetServerDisplayItems = new ObservableCollection<PresetServerDisplayItem>();

        private void InitializePresetServers()
        {
            presetServerDisplayItems.Clear();
            for (int i = 0; i < presetServers.Count; i++)
            {
                var displayItem = new PresetServerDisplayItem
                {
                    DisplayText = StripColorTags(presetServers[i].Name ?? ""),
                    PresetIndex = i,
                    IsSelected = false
                };
                presetServerDisplayItems.Add(displayItem);
                WriteLog($"InitializePresetServers: Added item {i}, DisplayText={displayItem.DisplayText}");
            }
            if (PresetServerListBox.ItemsSource == null)
            {
                PresetServerListBox.ItemsSource = presetServerDisplayItems;
            }
            PresetServerListBox.Items.Refresh();
            WriteLog($"InitializePresetServers: Loaded {presetServers.Count} preset servers");
        }

        private async Task LoadPresetServersAsync()
        {
            WriteLog($"LoadPresetServersAsync: Fetching preset servers from {presetServersUrl}");
            try
            {
                var response = await httpClient.GetAsync(presetServersUrl);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var servers = JsonSerializer.Deserialize<List<PresetServer>>(json);
                if (servers != null && servers.Any())
                {
                    presetServers.Clear();
                    presetServers.AddRange(servers);
                    WriteLog($"LoadPresetServersAsync: Loaded {servers.Count} preset servers");
                }
                else
                {
                    WriteLog("LoadPresetServersAsync: No preset servers found in response");
                    InitializeDefaultPresetServers();
                }
            }
            catch (Exception ex)
            {
                WriteLog($"LoadPresetServersAsync: Error fetching preset servers: {ex.Message}");
                InitializeDefaultPresetServers();
            }
            InitializePresetServers();
        }

        private void InitializeDefaultPresetServers()
        {
            WriteLog("InitializeDefaultPresetServers: Loading fallback preset servers");
            presetServers.Clear();
            presetServers.AddRange(new List<PresetServer>
        {
            new PresetServer { Name = "Niko233(CN)", PingServer = "au-cn.niko233.me", Port = "443" }
        });
            InitializePresetServers();
        }

        private void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("steam://rungameid/945360") { UseShellExecute = true });
                MessageBox.Show("正在启动 Among Us...", "启动游戏", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动游戏失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowMainPage()
        {
            var slideOutAnimation = (Storyboard)FindResource("SlideOutServerPanel");
            slideOutAnimation.Completed += (s, e) =>
            {
                ServerPanel.Visibility = Visibility.Collapsed;
                MainPanel.Visibility = Visibility.Visible;
            };
            slideOutAnimation.Begin();
            UpdateServerList();
        }

        private void OpenServerEditor_Click(object sender, RoutedEventArgs e)
        {
            MainPanel.Visibility = Visibility.Collapsed;
            ServerPanel.Visibility = Visibility.Visible;
            var slideInAnimation = (Storyboard)FindResource("SlideInServerPanel");
            slideInAnimation.Completed += async (s, e) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    await LoadPresetServersAsync();
                    UpdateServerList();
                    ServerListBox.SelectedIndex = -1;
                    foreach (ServerDisplayItem item in ServerListBox.Items)
                    {
                        item.IsSelected = false;
                    }
                    currentRegion = null;
                    DeleteButton.Visibility = Visibility.Hidden;
                    DetailPanel.Children.Clear();
                    var emptyLabel = new TextBlock
                    {
                        Text = "请从左侧列表中选择一个服务器进行配置",
                        Foreground = new SolidColorBrush(Colors.Gray),
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    DetailPanel.Children.Add(emptyLabel);
                });
            };
            slideInAnimation.Begin();
        }

        private readonly ObservableCollection<ServerDisplayItem> serverDisplayItems = new ObservableCollection<ServerDisplayItem>();

        private void UpdateServerList()
        {
            WriteLog($"UpdateServerList: Starting with {regions.Count} regions");
            var selectedIndices = serverDisplayItems
                .Where(item => item.IsSelected && !string.IsNullOrEmpty(item.DisplayText))
                .Select(item => item.RegionIndex)
                .ToList();
            var oldSelectedIndex = ServerListBox.SelectedIndex;
            var newSelectedIndex = -1;

            List<RegionInfo> oldSelectedRegions = selectedIndices
                .Where(i => i >= 0 && i < regions.Count)
                .Select(i => regions[i])
                .ToList();
            if (oldSelectedIndex >= 0 && oldSelectedIndex < regions.Count)
            {
                oldSelectedRegions.Add(regions[oldSelectedIndex]);
            }

            serverDisplayItems.Clear();
            for (int i = 0; i < regions.Count; i++)
            {
                var displayItem = new ServerDisplayItem
                {
                    DisplayText = StripColorTags(regions[i].Name ?? ""),
                    RegionIndex = i,
                    IsSelected = selectedIndices.Contains(i),
                    PingText = "N/A",
                    PingTextColor = new SolidColorBrush(Colors.Gray)
                };
                serverDisplayItems.Add(displayItem);
            }

            if (isDragging)
            {
                serverDisplayItems.Add(new ServerDisplayItem
                {
                    DisplayText = "",
                    RegionIndex = -1,
                    IsSelected = false,
                    PingText = "",
                    PingTextColor = new SolidColorBrush(Colors.Transparent)
                });
            }

            foreach (var item in serverDisplayItems)
            {
                if (!string.IsNullOrEmpty(item.DisplayText) && item.RegionIndex < regions.Count)
                {
                    item.IsSelected = oldSelectedRegions.Contains(regions[item.RegionIndex]);
                    if (item.IsSelected && newSelectedIndex == -1)
                    {
                        newSelectedIndex = item.RegionIndex;
                    }
                }
            }

            if (newSelectedIndex == -1 && oldSelectedRegions.Any())
            {
                newSelectedIndex = regions.FindIndex(r => oldSelectedRegions.Contains(r));
            }

            ServerCountText.Text = $"当前共有 {regions.Count} 个服务器";
            ServerWarningText.Visibility = regions.Count > 14 ? Visibility.Visible : Visibility.Collapsed;

            if (ServerListBox.ItemsSource == null)
            {
                ServerListBox.ItemsSource = serverDisplayItems;
            }

            if (newSelectedIndex >= 0 && newSelectedIndex < regions.Count)
            {
                isUpdatingSelection = true;
                ServerListBox.SelectedIndex = newSelectedIndex;
                isUpdatingSelection = false;
            }

            UpdateDeleteButtonVisibility();
            Dispatcher.InvokeAsync(UpdatePingDelays, DispatcherPriority.Background);
        }

        private string StripColorTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";
            return Regex.Replace(input, @"<color=#[0-9A-Fa-f]{6,8}>(.*?)</color>", "$1").Trim();
        }

        private async void UpdatePingDelays()
        {
            if (isUpdatingPingDelays)
            {
                WriteLog("UpdatePingDelays: Already running, skipping");
                return;
            }

            isUpdatingPingDelays = true;
            WriteLog($"UpdatePingDelays: Starting with {regions.Count} regions, {serverDisplayItems.Count} items");
            if (regions.Count == 0)
            {
                WriteLog("UpdatePingDelays: No regions available, exiting");
                isUpdatingPingDelays = false;
                return;
            }

            Dispatcher.Invoke(() => ServerListBox.UpdateLayout());
            WriteLog($"UpdatePingDelays: ServerListBox layout updated, ItemContainerGenerator Status: {ServerListBox.ItemContainerGenerator.Status}");

            for (int i = 0; i < serverDisplayItems.Count; i++)
            {
                var item = serverDisplayItems[i];
                if (item == null || string.IsNullOrEmpty(item.DisplayText) || item.RegionIndex >= regions.Count || item.RegionIndex < 0)
                {
                    WriteLog($"UpdatePingDelays: Skipping item {i} (null, empty text, or invalid regionIndex)");
                    continue;
                }

                var region = regions[item.RegionIndex];
                WriteLog($"UpdatePingDelays: Processing item {i}, DisplayText={item.DisplayText}, regionIndex={item.RegionIndex}");

                item.PingText = "测延迟...";
                item.PingTextColor = new SolidColorBrush(Colors.Gray);
                WriteLog($"PingText for item {i} set to: 测延迟...");

                try
                {
                    WriteLog($"Pinging server: {region.PingServer}");
                    var ping = await PingServerAsync(region.PingServer);
                    item.PingText = ping >= 0 ? $"{ping} ms" : "超时";
                    item.PingTextColor = ping >= 0 ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);
                    WriteLog($"PingText for item {i} set to: {item.PingText}");
                }
                catch (Exception ex)
                {
                    item.PingText = "错误";
                    item.PingTextColor = new SolidColorBrush(Colors.Red);
                    WriteLog($"PingText for item {i} set to: 错误");
                    WriteLog($"Ping error for {region.PingServer}: {ex.Message}");
                }
            }

            WriteLog("UpdatePingDelays: Completed");
            Dispatcher.Invoke(() => ServerListBox.InvalidateVisual());
            isUpdatingPingDelays = false;
        }

        private void ServerListBox_Loaded(object sender, RoutedEventArgs e)
        {
            WriteLog("ServerListBox_Loaded: ServerListBox loaded, updating layout");
            ServerListBox.UpdateLayout();
            Dispatcher.InvokeAsync(() =>
            {
                WriteLog("ServerListBox_Loaded: Delayed UpdatePingDelays call");
                UpdatePingDelays();
            }, DispatcherPriority.Background);
        }

        private async Task<long> PingServerAsync(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                WriteLog($"PingServerAsync: Empty host provided");
                return -1;
            }
            using (var ping = new Ping())
            {
                try
                {
                    WriteLog($"Sending ping to {host}");
                    var reply = await ping.SendPingAsync(host, 1000);
                    WriteLog($"Ping reply from {host}: Status={reply.Status}, Time={reply.RoundtripTime}");
                    return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
                }
                catch (Exception ex)
                {
                    WriteLog($"PingServerAsync error for {host}: {ex.Message}");
                    return -1;
                }
            }
        }

        private T? FindVisualChild<T>(DependencyObject obj, string name) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T target && (string)child.GetValue(FrameworkElement.NameProperty) == name)
                {
                    WriteLog($"FindVisualChild: Found {name} of type {typeof(T).Name}");
                    return target;
                }
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ServerDisplayItem item)
            {
                item.IsSelected = checkBox.IsChecked ?? false;
                WriteLog($"CheckBox_Click: Server {regions[item.RegionIndex].Name} IsSelected={item.IsSelected}");
                UpdateDeleteButtonVisibility();
            }
        }

        private void PresetCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is PresetServerDisplayItem item)
            {
                item.IsSelected = checkBox.IsChecked ?? false;
                WriteLog($"PresetCheckBox_Click: Preset server {presetServers[item.PresetIndex].Name} IsSelected={item.IsSelected}");
                UpdateSelectAllCheckBoxState();
            }
        }

        private void SelectAllPresetCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                bool isChecked = checkBox.IsChecked ?? false;
                foreach (PresetServerDisplayItem item in PresetServerListBox.Items)
                {
                    item.IsSelected = isChecked;
                }
                WriteLog($"SelectAllPresetCheckBox_Click: Set all preset servers IsSelected={isChecked}");
                PresetServerListBox.Items.Refresh();
            }
        }

        private void UpdateSelectAllCheckBoxState()
        {
            var allItems = PresetServerListBox.Items.Cast<PresetServerDisplayItem>().ToList();
            if (allItems.Any())
            {
                bool allSelected = allItems.All(item => item.IsSelected);
                bool anySelected = allItems.Any(item => item.IsSelected);
                SelectAllPresetCheckBox.IsChecked = allSelected ? true : anySelected ? null : false;
            }
            else
            {
                SelectAllPresetCheckBox.IsChecked = false;
            }
        }

        private void UpdateDeleteButtonVisibility()
        {
            bool hasSelected = ServerListBox.Items.Cast<ServerDisplayItem>()
                .Any(item => item.IsSelected && !string.IsNullOrEmpty(item.DisplayText));
            DeleteButton.Visibility = hasSelected ? Visibility.Visible : Visibility.Hidden;
        }

        private void ServerListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStartPoint = e.GetPosition(null);
        }

        private void ServerListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragStartPoint == null)
                return;

            Point currentPosition = e.GetPosition(null);
            Vector diff = dragStartPoint.Value - currentPosition;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                ListBox listBox = sender as ListBox;
                ListBoxItem listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (listBoxItem != null && listBoxItem.DataContext is ServerDisplayItem draggedItem && !string.IsNullOrEmpty(draggedItem.DisplayText))
                {
                    // 清除所有复选框
                    foreach (ServerDisplayItem item in ServerListBox.Items)
                    {
                        item.IsSelected = false;
                    }
                    UpdateDeleteButtonVisibility();
                    ServerListBox.Items.Refresh();
                    WriteLog("ClearCheckBoxesOnDragStart: Cleared all server checkboxes");

                    // 添加空白占位项
                    ServerListBox.Items.Add(new ServerDisplayItem
                    {
                        DisplayText = "",
                        RegionIndex = -1,
                        IsSelected = false
                    });
                    isDragging = true;
                    WriteLog("DragStart: Added placeholder item at bottom");

                    DragDrop.DoDragDrop(listBoxItem, draggedItem, DragDropEffects.Move);
                    dragStartPoint = null;
                }
            }
        }

        private void ServerListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ServerDisplayItem)) is ServerDisplayItem draggedItem)
            {
                e.Effects = DragDropEffects.Move;

                ListBox listBox = sender as ListBox;
                if (listBox == null)
                {
                    e.Effects = DragDropEffects.None;
                    return;
                }

                Point mousePos = e.GetPosition(listBox);
                int insertIndex = -1;

                // 计算插入位置
                for (int i = 0; i < listBox.Items.Count; i++)
                {
                    if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                    {
                        Point itemPos = item.PointToScreen(new Point(0, 0));
                        Point relativePos = listBox.PointToScreen(new Point(0, 0));
                        double itemTop = itemPos.Y - relativePos.Y;
                        double itemHeight = item.ActualHeight;

                        if (mousePos.Y >= itemTop && mousePos.Y < itemTop + itemHeight / 2)
                        {
                            insertIndex = i;
                            break;
                        }
                        else if (mousePos.Y >= itemTop + itemHeight / 2 && mousePos.Y < itemTop + itemHeight)
                        {
                            insertIndex = i + 1;
                            break;
                        }
                    }
                }

                if (insertIndex == -1 && mousePos.Y > 0)
                {
                    insertIndex = listBox.Items.Count;
                }

                // 更新插入线显示
                if (insertIndex != lastInsertIndex)
                {
                    // 隐藏所有插入线
                    for (int i = 0; i < listBox.Items.Count; i++)
                    {
                        if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                        {
                            var insertLine = FindVisualChild<Rectangle>(item, "InsertLine");
                            if (insertLine != null)
                            {
                                insertLine.Visibility = Visibility.Collapsed;
                            }
                        }
                    }

                    // 显示当前插入位置的线
                    if (insertIndex >= 0 && insertIndex <= listBox.Items.Count)
                    {
                        int displayIndex = insertIndex < listBox.Items.Count ? insertIndex : listBox.Items.Count - 1;
                        if (listBox.ItemContainerGenerator.ContainerFromIndex(displayIndex) is ListBoxItem targetItem)
                        {
                            var insertLine = FindVisualChild<Rectangle>(targetItem, "InsertLine");
                            if (insertLine != null)
                            {
                                insertLine.Visibility = Visibility.Visible;
                            }
                        }
                    }

                    lastInsertIndex = insertIndex;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ServerListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ServerDisplayItem)) is ServerDisplayItem draggedItem)
            {
                ListBox listBox = sender as ListBox;
                ListBoxItem targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

                int newIndex = -1;
                if (targetItem != null)
                {
                    ServerDisplayItem targetData = (ServerDisplayItem)targetItem.DataContext;
                    newIndex = string.IsNullOrEmpty(targetData.DisplayText) ? regions.Count : targetData.RegionIndex;
                    Point mousePos = e.GetPosition(targetItem);
                    if (mousePos.Y > targetItem.ActualHeight / 2 && !string.IsNullOrEmpty(targetData.DisplayText))
                    {
                        newIndex++;
                    }
                }
                else
                {
                    newIndex = regions.Count;
                }

                int oldIndex = draggedItem.RegionIndex;
                if (oldIndex == newIndex || oldIndex + 1 == newIndex)
                {
                    ResetDragVisualEffects(listBox);
                    return;
                }

                WriteLog($"DragDrop: Moving server {regions[oldIndex].Name} from index {oldIndex} to {newIndex}");

                RegionInfo draggedRegion = regions[oldIndex];
                regions.RemoveAt(oldIndex);
                if (newIndex > oldIndex)
                {
                    newIndex--;
                }
                regions.Insert(newIndex, draggedRegion);

                SaveData();
                UpdateServerList();
            }

            ResetDragVisualEffects(sender as ListBox);
        }

        private void ResetDragVisualEffects(ListBox listBox)
        {
            if (listBox == null)
                return;

            // 隐藏所有插入线
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                {
                    var insertLine = FindVisualChild<Rectangle>(item, "InsertLine");
                    if (insertLine != null)
                    {
                        insertLine.Visibility = Visibility.Collapsed;
                    }
                }
            }
            lastInsertIndex = -1;

            // 移除空白占位项
            if (isDragging)
            {
                var placeholder = ServerListBox.Items.Cast<ServerDisplayItem>().LastOrDefault(item => string.IsNullOrEmpty(item.DisplayText));
                if (placeholder != null)
                {
                    ServerListBox.Items.Remove(placeholder);
                    WriteLog("DragEnd: Removed placeholder item");
                }
                isDragging = false;
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ShowMainPage();
        }

        private void NewServerButton_Click(object sender, RoutedEventArgs e)
        {
            var newServer = new RegionInfo
            {
                Name = "新服务器",
                PingServer = "example.com",
                Servers = new List<ServerInfo>
                {
                    new ServerInfo
                    {
                        Name = "Http-1",
                        Ip = "https://example.com",
                        Port = 22023,
                        UseDtls = false,
                        Players = 0,
                        ConnectionFailures = 0
                    }
                },
                TranslateName = 1003
            };

            regions.Add(newServer);
            SaveData();
            UpdateServerList();
            ServerListBox.SelectedIndex = ServerListBox.Items.Count - 1;
            ServerListBox.ScrollIntoView(ServerListBox.SelectedItem);
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPresets = PresetServerListBox.Items.Cast<PresetServerDisplayItem>()
                .Where(item => item.IsSelected)
                .Select(item => presetServers[item.PresetIndex])
                .ToList();

            if (!selectedPresets.Any())
            {
                MessageBox.Show("请至少勾选一个预设服务器！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int startIndex = regions.Count;
            foreach (var preset in selectedPresets)
            {
                int port = 22023; // 默认端口
                if (!string.IsNullOrEmpty(preset.Port) && int.TryParse(preset.Port, out int parsedPort))
                {
                    port = parsedPort;
                }
                else
                {
                    WriteLog($"AddPresetButton_Click: Invalid or missing port for {preset.Name}, using default 22023");
                }

                var newServer = new RegionInfo
                {
                    Name = preset.Name,
                    PingServer = preset.PingServer,
                    Servers = new List<ServerInfo>
            {
                new ServerInfo
                {
                    Name = "Http-1",
                    Ip = $"https://{preset.PingServer}",
                    Port = port,
                    UseDtls = false,
                    Players = 0,
                    ConnectionFailures = 0
                }
            },
                    TranslateName = 1003
                };
                regions.Add(newServer);
            }

            WriteLog($"AddPresetButton_Click: Added {selectedPresets.Count} preset servers: [{string.Join(", ", selectedPresets.Select(p => StripColorTags(p.Name ?? "null")))}]");

            SaveData();
            UpdateServerList();
            ServerListBox.SelectedIndex = regions.Count - 1;
            ServerListBox.ScrollIntoView(ServerListBox.SelectedItem);

            foreach (PresetServerDisplayItem item in PresetServerListBox.Items)
            {
                item.IsSelected = false;
            }
            SelectAllPresetCheckBox.IsChecked = false;
            PresetServerListBox.Items.Refresh();
        }

        private void ServerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerListBox.SelectedIndex < 0 || ServerListBox.SelectedIndex >= regions.Count || isUpdatingSelection)
                return;

            currentRegion = regions[ServerListBox.SelectedIndex];
            CreateServerDetailForm();
        }

        private void CreateServerDetailForm()
        {
            DetailPanel.Children.Clear();

            var formGrid = new Grid { Margin = new Thickness(10) };
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 5; i++)
                formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameLabel = new TextBlock { Text = "服务器名称:", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(nameLabel, 0);
            Grid.SetColumn(nameLabel, 0);
            nameEntry = new TextBox { Text = currentRegion?.Name ?? "", Width = 250 };
            nameEntry.TextChanged += (s, e) => ValidateName();
            Grid.SetRow(nameEntry, 0);
            Grid.SetColumn(nameEntry, 1);
            nameStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(nameStatus, 0);
            Grid.SetColumn(nameStatus, 2);

            var pingLabel = new TextBlock { Text = "Ping服务器:", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(pingLabel, 1);
            Grid.SetColumn(pingLabel, 0);
            pingEntry = new TextBox { Text = currentRegion?.PingServer ?? "", Width = 250 };
            pingEntry.TextChanged += (s, e) => ValidatePingServer();
            Grid.SetRow(pingEntry, 1);
            Grid.SetColumn(pingEntry, 1);
            pingStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(pingStatus, 1);
            Grid.SetColumn(pingStatus, 2);

            var portLabel = new TextBlock { Text = "端口:", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(portLabel, 2);
            Grid.SetColumn(portLabel, 0);
            portEntry = new TextBox { Text = currentRegion?.Servers[0].Port.ToString() ?? "", Width = 80 };
            portEntry.TextChanged += (s, e) => ValidatePort();
            Grid.SetRow(portEntry, 2);
            Grid.SetColumn(portEntry, 1);
            portStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(portStatus, 2);
            Grid.SetColumn(portStatus, 2);

            var advancedButton = new Button { Content = "高级设置 ▼", Background = new SolidColorBrush(Colors.SlateGray), Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 10, 0, 0) };
            advancedButton.Click += (s, e) => ToggleAdvancedSettings(advancedButton);
            Grid.SetRow(advancedButton, 3);
            Grid.SetColumn(advancedButton, 0);
            Grid.SetColumnSpan(advancedButton, 3);

            formGrid.Children.Add(nameLabel);
            formGrid.Children.Add(nameEntry);
            formGrid.Children.Add(nameStatus);
            formGrid.Children.Add(pingLabel);
            formGrid.Children.Add(pingEntry);
            formGrid.Children.Add(pingStatus);
            formGrid.Children.Add(portLabel);
            formGrid.Children.Add(portEntry);
            formGrid.Children.Add(portStatus);
            formGrid.Children.Add(advancedButton);

            DetailPanel.Children.Add(formGrid);

            advancedPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(10, 0, 10, 0) };
            var advancedGrid = new Grid();
            advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 4; i++)
                advancedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var typeLabel = new TextBlock { Text = "$type:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(typeLabel, 0);
            Grid.SetColumn(typeLabel, 0);
            var typeEntry = new TextBox { Text = currentRegion?.Type ?? "", IsReadOnly = true, Width = 250 };
            Grid.SetRow(typeEntry, 0);
            Grid.SetColumn(typeEntry, 1);

            var translateLabel = new TextBlock { Text = "TranslateName:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(translateLabel, 1);
            Grid.SetColumn(translateLabel, 0);
            translateEntry = new TextBox { Text = currentRegion?.TranslateName.ToString() ?? "", Width = 80 };
            translateEntry.TextChanged += (s, e) => ValidateTranslateName();
            Grid.SetRow(translateEntry, 1);
            Grid.SetColumn(translateEntry, 1);
            translateStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(translateStatus, 1);
            Grid.SetColumn(translateStatus, 2);

            var playersLabel = new TextBlock { Text = "Players:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(playersLabel, 2);
            Grid.SetColumn(playersLabel, 0);
            var playersEntry = new TextBox { Text = currentRegion?.Servers[0].Players.ToString() ?? "", IsReadOnly = true, Width = 80 };
            Grid.SetRow(playersEntry, 2);
            Grid.SetColumn(playersEntry, 1);

            var failuresLabel = new TextBlock { Text = "ConnectionFailures:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(failuresLabel, 3);
            Grid.SetColumn(failuresLabel, 0);
            var failuresEntry = new TextBox { Text = currentRegion?.Servers[0].ConnectionFailures.ToString() ?? "", IsReadOnly = true, Width = 80 };
            Grid.SetRow(failuresEntry, 3);
            Grid.SetColumn(failuresEntry, 1);

            advancedGrid.Children.Add(typeLabel);
            advancedGrid.Children.Add(typeEntry);
            advancedGrid.Children.Add(translateLabel);
            advancedGrid.Children.Add(translateEntry);
            advancedGrid.Children.Add(translateStatus);
            advancedGrid.Children.Add(playersLabel);
            advancedGrid.Children.Add(playersEntry);
            advancedGrid.Children.Add(failuresLabel);
            advancedGrid.Children.Add(failuresEntry);

            advancedPanel.Children.Add(advancedGrid);
            DetailPanel.Children.Add(advancedPanel);

            ValidateName();
            ValidatePingServer();
            ValidatePort();
            ValidateTranslateName();
        }

        private void ToggleAdvancedSettings(Button button)
        {
            advancedVisible = !advancedVisible;
            advancedPanel!.Visibility = advancedVisible ? Visibility.Visible : Visibility.Collapsed;
            button.Content = advancedVisible ? "高级设置 ▲" : "高级设置 ▼";
        }

        private bool ValidateName()
        {
            string name = nameEntry!.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                nameStatus!.Text = "✖ 名称不能为空";
                nameStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }
            nameStatus!.Text = "✔ 有效";
            nameStatus.Foreground = new SolidColorBrush(Colors.Green);
            if (currentRegion != null)
            {
                currentRegion.Name = name;
                currentRegion.Servers[0].Name = "Http-1";
                SaveData();
                if (!isUpdatingSelection)
                    UpdateServerList();
            }
            return true;
        }

        private bool ValidatePingServer()
        {
            string server = pingEntry!.Text.Trim();
            if (string.IsNullOrEmpty(server))
            {
                pingStatus!.Text = "✖ 服务器地址不能为空";
                pingStatus.Foreground = new SolidColorBrush(Colors.Red);
                if (currentRegion != null)
                {
                    currentRegion.PingServer = server;
                    currentRegion.Servers[0].Ip = string.IsNullOrEmpty(server) ? "" : $"https://{server}";
                    SaveData();
                }
                return false;
            }
            if (server.Length < 1 || server.Length > 64)
            {
                pingStatus!.Text = "✖ 地址长度应为1-64个字符";
                pingStatus.Foreground = new SolidColorBrush(Colors.Red);
                if (currentRegion != null)
                {
                    currentRegion.PingServer = server;
                    currentRegion.Servers[0].Ip = string.IsNullOrEmpty(server) ? "" : $"https://{server}";
                    SaveData();
                }
                return false;
            }

            server = Regex.Replace(server, @"^https?://", "");
            if (server.Contains("/"))
                server = server.Split('/')[0];
            pingEntry!.Text = server;

            pingStatus!.Text = "✔ 有效";
            pingStatus.Foreground = new SolidColorBrush(Colors.Green);

            if (currentRegion != null)
            {
                currentRegion.PingServer = server;
                currentRegion.Servers[0].Ip = $"https://{server}";
                SaveData();
                if (!isUpdatingSelection)
                    UpdateServerList();
            }
            return true;
        }

        private bool ValidatePort()
        {
            string portStr = portEntry!.Text.Trim();
            if (string.IsNullOrEmpty(portStr))
            {
                portStatus!.Text = "✖ 端口不能为空";
                portStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (!int.TryParse(portStr, out int port) || port < 0 || port > 65535)
            {
                portStatus!.Text = "✖ 端口必须在0-65535之间";
                portStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (currentRegion != null)
            {
                currentRegion.Servers[0].Port = port;
                SaveData();
                if (!isUpdatingSelection)
                    UpdateServerList();
            }
            portStatus!.Text = "✔ 有效";
            portStatus.Foreground = new SolidColorBrush(Colors.Green);
            return true;
        }

        private bool ValidateTranslateName()
        {
            string transStr = translateEntry!.Text.Trim();
            if (string.IsNullOrEmpty(transStr))
            {
                translateStatus!.Text = "✖ 不能为空";
                translateStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (!int.TryParse(transStr, out int trans) || trans <= 1000)
            {
                translateStatus!.Text = "✖ 必须是大于1000的整数";
                translateStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (currentRegion != null)
            {
                currentRegion.TranslateName = trans;
                SaveData();
                if (!isUpdatingSelection)
                    UpdateServerList();
            }
            translateStatus!.Text = "✔ 有效";
            translateStatus.Foreground = new SolidColorBrush(Colors.Green);
            return true;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ServerListBox.Items.Cast<ServerDisplayItem>()
                .Where(item => item.IsSelected && !string.IsNullOrEmpty(item.DisplayText))
                .ToList();

            if (!selectedItems.Any())
                return;

            var serverNames = string.Join(", ", selectedItems.Select(item => StripColorTags(regions[item.RegionIndex].Name ?? "")));
            if (MessageBox.Show($"确定要删除以下服务器吗？\n{serverNames}", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var indicesToRemove = selectedItems.Select(item => item.RegionIndex).OrderByDescending(i => i).ToList();
                foreach (var index in indicesToRemove)
                {
                    regions.RemoveAt(index);
                }
                SaveData();
                UpdateServerList();
                DeleteButton.Visibility = Visibility.Hidden;
                DetailPanel.Children.Clear();
                var emptyLabel = new TextBlock
                {
                    Text = "请从左侧列表中选择一个服务器进行配置",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DetailPanel.Children.Add(emptyLabel);
                currentRegion = null;
            }
        }

        public class RegionData
        {
            public int CurrentRegionIdx { get; set; }
            public List<RegionInfo>? Regions { get; set; }
        }

        public class RegionInfo
        {
            public string Type { get; set; } = "StaticHttpRegionInfo, Assembly-CSharp";
            public string? Name { get; set; }
            public string? PingServer { get; set; }
            public List<ServerInfo>? Servers { get; set; }
            public object? TargetServer { get; set; }
            public int TranslateName { get; set; }
        }

        public class ServerInfo
        {
            public string? Name { get; set; } = "Http-1";
            public string? Ip { get; set; }
            public int Port { get; set; }
            public bool UseDtls { get; set; }
            public int Players { get; set; }
            public int ConnectionFailures { get; set; }
        }
    public class PresetServer
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("pingServer")]
        public string? PingServer { get; set; }

        [JsonPropertyName("port")]
        public string? Port { get; set; }
    }

    public class ServerDisplayItem : INotifyPropertyChanged
        {
            private string? _displayText;
            private string? _pingText;
            private Brush? _pingTextColor;

            public string? DisplayText
            {
                get => _displayText;
                set
                {
                    _displayText = value;
                    OnPropertyChanged();
                }
            }

            public string? PingText
            {
                get => _pingText;
                set
                {
                    _pingText = value;
                    OnPropertyChanged();
                }
            }

            public Brush? PingTextColor
            {
                get => _pingTextColor;
                set
                {
                    _pingTextColor = value;
                    OnPropertyChanged();
                }
            }

            public int RegionIndex { get; set; }
            public bool IsSelected { get; set; }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public class PresetServerDisplayItem : INotifyPropertyChanged
        {
            private string? _displayText;
            private bool _isSelected;

            public string? DisplayText
            {
                get => _displayText;
                set
                {
                    _displayText = value;
                    OnPropertyChanged();
                }
            }

            public int PresetIndex { get; set; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}