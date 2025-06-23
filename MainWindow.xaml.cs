using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO.Compression;
using IOPath = System.IO.Path;
using System.Net.Http.Headers;
using System.Linq;

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
        // 私有字段
        private readonly HttpClient _httpClient = new();
        private readonly string _presetServersUrl = "https://mxzc.cloud:35249/preset_servers.json";
        private readonly string _appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private readonly string _filePath;
        private readonly string _logPath;
        private readonly ObservableCollection<ServerDisplayItem> _serverDisplayItems = new();
        private readonly ObservableCollection<PresetServerDisplayItem> _presetServerDisplayItems = new();
        private List<RegionInfo> _regions = new();
        private List<PresetServer> _presetServers = new();
        private int _currentRegionIdx = 3;
        private RegionInfo? _currentRegion;
        private TextBox? _nameEntry, _pingEntry, _portEntry, _translateEntry;
        private TextBlock? _nameStatus, _pingStatus, _portStatus, _translateStatus;
        private StackPanel? _advancedPanel;
        private bool _advancedVisible;
        private bool _isUpdatingSelection;
        private bool _isDragging;
        private Point? _dragStartPoint;
        private int _lastInsertIndex = -1;
        private readonly string _bepInExDownloadUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip"; // BepInEx x64 5.4.21 版本
        private string? _gameInstallPath; // Among Us 安装目录
        private readonly string _settingsPath;
        private AppSettings _settings = new();

        public MainWindow()
        {
            InitializeComponent();
            _filePath = System.IO.Path.Combine(_appDataPath, @"..\LocalLow\Innersloth\Among Us\regionInfo.json");
            _logPath = System.IO.Path.Combine(_appDataPath, @"..\LocalLow\Innersloth\Among Us\AULGK.log");
            _settingsPath = System.IO.Path.Combine(_appDataPath, @"..\LocalLow\Innersloth\Among Us\AULGK.settings.json");
            InitializeApplication();
        }

        // 初始化应用程序
        private void InitializeApplication()
        {
            LoadSettings();
            LoadData();
            DetectGameInstallPath();
            InitializePresetServers();
            Dispatcher.InvokeAsync(async () =>
            {
                await LoadPresetServersAsync();
                EvaluateBepInExUI();
                ShowMainPage();
            }, DispatcherPriority.Background);
            WriteLog("应用程序初始化完成");
        }

        // 加载本地服务器数据
        private void LoadData()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_filePath)!);
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<RegionData>(json);
                    if (data != null)
                    {
                        _regions = data.Regions ?? new();
                        _currentRegionIdx = data.CurrentRegionIdx;
                        WriteLog($"加载 {_regions.Count} 个服务器，当前索引：{_currentRegionIdx}");
                    }
                }
                else
                {
                    SaveData();
                    WriteLog("未找到 regionInfo.json，创建默认文件");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _regions = new();
                SaveData();
                WriteLog($"加载数据失败：{ex.Message}");
            }
        }

        // 保存服务器数据到文件
        private void SaveData()
        {
            try
            {
                var data = new RegionData
                {
                    CurrentRegionIdx = _currentRegionIdx,
                    Regions = _regions
                };
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_filePath, json);
                WriteLog("服务器数据已保存");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                WriteLog($"保存数据失败：{ex.Message}");
            }
        }

        // 初始化预设服务器列表
        private void InitializePresetServers()
        {
            _presetServerDisplayItems.Clear();
            foreach (var (server, index) in _presetServers.Select((s, i) => (s, i)))
            {
                _presetServerDisplayItems.Add(new PresetServerDisplayItem
                {
                    DisplayText = StripColorTags(server.Name ?? ""),
                    PresetIndex = index,
                    IsSelected = false
                });
            }
            PresetServerListBox.ItemsSource ??= _presetServerDisplayItems;
            PresetServerListBox.Items.Refresh();
            WriteLog($"初始化 {_presetServers.Count} 个预设服务器");
        }

        // 异步加载预设服务器
        private async Task LoadPresetServersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(_presetServersUrl);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var servers = JsonSerializer.Deserialize<List<PresetServer>>(json);
                if (servers?.Any() == true)
                {
                    _presetServers = servers;
                    WriteLog($"从 {_presetServersUrl} 加载 {servers.Count} 个预设服务器");
                }
                else
                {
                    InitializeDefaultPresetServers();
                    WriteLog("预设服务器响应为空，使用默认服务器");
                }
            }
            catch (Exception ex)
            {
                InitializeDefaultPresetServers();
                WriteLog($"加载预设服务器失败：{ex.Message}");
            }
            InitializePresetServers();
        }

        // 初始化默认预设服务器
        private void InitializeDefaultPresetServers()
        {
            _presetServers = new List<PresetServer>
            {
                new() { Name = "Niko233(CN)", PingServer = "au-cn.niko233.me", Port = "443" }
            };
            WriteLog("加载默认预设服务器");
        }

        // 显示主页面
        private void ShowMainPage()
        {
            var slideOutAnimation = (Storyboard)FindResource("SlideOutServerPanel");
            slideOutAnimation.Completed += (_, _) =>
            {
                ServerPanel.Visibility = Visibility.Collapsed;
                MainPanel.Visibility = Visibility.Visible;
            };
            slideOutAnimation.Begin();
            UpdateServerList();
            WriteLog("显示主页面");
        }

        // 更新服务器列表
        private void UpdateServerList()
        {
            var selectedIndices = _serverDisplayItems
                .Where(item => item.IsSelected && !string.IsNullOrEmpty(item.DisplayText))
                .Select(item => item.RegionIndex)
                .ToList();
            var oldSelectedIndex = ServerListBox.SelectedIndex;
            var newSelectedIndex = -1;
            var oldSelectedRegions = selectedIndices
                .Where(i => i >= 0 && i < _regions.Count)
                .Select(i => _regions[i])
                .ToList();

            if (oldSelectedIndex >= 0 && oldSelectedIndex < _regions.Count)
            {
                oldSelectedRegions.Add(_regions[oldSelectedIndex]);
            }

            _serverDisplayItems.Clear();
            for (int i = 0; i < _regions.Count; i++)
            {
                _serverDisplayItems.Add(new ServerDisplayItem
                {
                    DisplayText = StripColorTags(_regions[i].Name ?? ""),
                    RegionIndex = i,
                    IsSelected = selectedIndices.Contains(i)
                });
            }

            if (_isDragging)
            {
                _serverDisplayItems.Add(new ServerDisplayItem
                {
                    DisplayText = "",
                    RegionIndex = -1,
                    IsSelected = false
                });
            }

            foreach (var item in _serverDisplayItems)
            {
                if (!string.IsNullOrEmpty(item.DisplayText) && item.RegionIndex < _regions.Count)
                {
                    item.IsSelected = oldSelectedRegions.Contains(_regions[item.RegionIndex]);
                    if (item.IsSelected && newSelectedIndex == -1)
                    {
                        newSelectedIndex = item.RegionIndex;
                    }
                }
            }

            if (newSelectedIndex == -1 && oldSelectedRegions.Any())
            {
                newSelectedIndex = _regions.FindIndex(r => oldSelectedRegions.Contains(r));
            }

            ServerCountText.Text = $"当前共有 {_regions.Count} 个服务器";
            ServerWarningText.Visibility = _regions.Count > 14 ? Visibility.Visible : Visibility.Collapsed;
            ServerListBox.ItemsSource ??= _serverDisplayItems;

            if (newSelectedIndex >= 0 && newSelectedIndex < _regions.Count)
            {
                _isUpdatingSelection = true;
                ServerListBox.SelectedIndex = newSelectedIndex;
                _isUpdatingSelection = false;
            }

            UpdateDeleteButtonVisibility();
            WriteLog($"更新服务器列表，共有 {_regions.Count} 个服务器");
        }

        // 移除字符串中的颜色标签
        private string StripColorTags(string input)
        {
            return string.IsNullOrEmpty(input)
                ? ""
                : Regex.Replace(input, @"<color=#[0-9A-Fa-f]{6,8}>(.*?)</color>", "$1").Trim();
        }

        // 启动游戏
        private void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("steam://rungameid/945360") { UseShellExecute = true });
                MessageBox.Show("正在启动 Among Us...", "启动游戏", MessageBoxButton.OK, MessageBoxImage.Information);
                WriteLog("启动 Among Us 游戏");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动游戏失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                WriteLog($"启动游戏失败：{ex.Message}");
            }
        }

        // 打开服务器编辑页面
        private void OpenServerEditor_Click(object sender, RoutedEventArgs e)
        {
            MainPanel.Visibility = Visibility.Collapsed;
            ServerPanel.Visibility = Visibility.Visible;
            var slideInAnimation = (Storyboard)FindResource("SlideInServerPanel");
            slideInAnimation.Completed += async (_, _) =>
            {
                await LoadPresetServersAsync();
                UpdateServerList();
                ServerListBox.SelectedIndex = -1;
                foreach (ServerDisplayItem item in ServerListBox.Items)
                {
                    item.IsSelected = false;
                }
                _currentRegion = null;
                DeleteButton.Visibility = Visibility.Hidden;
                DetailPanel.Children.Clear();
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = "请从左侧列表中选择一个服务器进行配置",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            };
            slideInAnimation.Begin();
            WriteLog("打开服务器编辑页面");
        }

        // 返回主页面
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ShowMainPage();
            WriteLog("返回主页面");
        }

        // 新建服务器
        private void NewServerButton_Click(object sender, RoutedEventArgs e)
        {
            var newServer = new RegionInfo
            {
                Name = "新服务器",
                PingServer = "example.com",
                Servers = new List<ServerInfo>
                {
                    new()
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

            _regions.Add(newServer);
            SaveData();
            UpdateServerList();
            ServerListBox.SelectedIndex = ServerListBox.Items.Count - 1;
            ServerListBox.ScrollIntoView(ServerListBox.SelectedItem);
            WriteLog("新建服务器");
        }

        // 添加预设服务器
        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPresets = PresetServerListBox.Items.Cast<PresetServerDisplayItem>()
                .Where(item => item.IsSelected)
                .Select(item => _presetServers[item.PresetIndex])
                .ToList();

            if (!selectedPresets.Any())
            {
                MessageBox.Show("请至少勾选一个预设服务器！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var preset in selectedPresets)
            {
                int port = int.TryParse(preset.Port, out var parsedPort) ? parsedPort : 22023;
                var newServer = new RegionInfo
                {
                    Name = preset.Name,
                    PingServer = preset.PingServer,
                    Servers = new List<ServerInfo>
                    {
                        new()
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
                _regions.Add(newServer);
            }

            SaveData();
            UpdateServerList();
            ServerListBox.SelectedIndex = _regions.Count - 1;
            ServerListBox.ScrollIntoView(ServerListBox.SelectedItem);

            foreach (PresetServerDisplayItem item in PresetServerListBox.Items)
            {
                item.IsSelected = false;
            }
            SelectAllPresetCheckBox.IsChecked = false;
            PresetServerListBox.Items.Refresh();
            WriteLog($"添加 {selectedPresets.Count} 个预设服务器");
        }

        // 删除选中的服务器
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ServerListBox.Items.Cast<ServerDisplayItem>()
                .Where(item => item.IsSelected && !string.IsNullOrEmpty(item.DisplayText))
                .ToList();

            if (!selectedItems.Any())
            {
                return;
            }

            var serverNames = string.Join(", ", selectedItems.Select(item => StripColorTags(_regions[item.RegionIndex].Name ?? "")));
            if (MessageBox.Show($"确定要删除以下服务器吗？\n{serverNames}", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var indicesToRemove = selectedItems.Select(item => item.RegionIndex).OrderByDescending(i => i).ToList();
                foreach (var index in indicesToRemove)
                {
                    _regions.RemoveAt(index);
                }
                SaveData();
                UpdateServerList();
                DeleteButton.Visibility = Visibility.Hidden;
                DetailPanel.Children.Clear();
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = "请从左侧列表中选择一个服务器进行配置",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                _currentRegion = null;
                WriteLog($"删除 {selectedItems.Count} 个服务器");
            }
        }

        // 服务器列表选择改变
        private void ServerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerListBox.SelectedIndex < 0 || ServerListBox.SelectedIndex >= _regions.Count || _isUpdatingSelection)
            {
                return;
            }

            _currentRegion = _regions[ServerListBox.SelectedIndex];
            CreateServerDetailForm();
            WriteLog($"选择服务器：{_currentRegion.Name}");
        }

        // 创建服务器详情表单
        private void CreateServerDetailForm()
        {
            DetailPanel.Children.Clear();
            var formGrid = new Grid { Margin = new Thickness(10) };
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 5; i++)
            {
                formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var nameLabel = new TextBlock { Text = "服务器名称:", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(nameLabel, 0);
            Grid.SetColumn(nameLabel, 0);
            _nameEntry = new TextBox { Text = _currentRegion?.Name ?? "", Width = 250 };
            _nameEntry.TextChanged += (_, _) => ValidateName();
            Grid.SetRow(_nameEntry, 0);
            Grid.SetColumn(_nameEntry, 1);
            _nameStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(_nameStatus, 0);
            Grid.SetColumn(_nameStatus, 2);

            var pingLabel = new TextBlock { Text = "Ping服务器:", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(pingLabel, 1);
            Grid.SetColumn(pingLabel, 0);
            _pingEntry = new TextBox { Text = _currentRegion?.PingServer ?? "", Width = 250 };
            _pingEntry.TextChanged += (_, _) => ValidatePingServer();
            Grid.SetRow(_pingEntry, 1);
            Grid.SetColumn(_pingEntry, 1);
            _pingStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(_pingStatus, 1);
            Grid.SetColumn(_pingStatus, 2);

            var portLabel = new TextBlock { Text = "端口:", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(portLabel, 2);
            Grid.SetColumn(portLabel, 0);
            _portEntry = new TextBox { Text = _currentRegion?.Servers?[0]?.Port.ToString() ?? "", Width = 80 };
            _portEntry.TextChanged += (_, _) => ValidatePort();
            Grid.SetRow(_portEntry, 2);
            Grid.SetColumn(_portEntry, 1);
            _portStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(_portStatus, 2);
            Grid.SetColumn(_portStatus, 2);

            var advancedButton = new Button { Content = "高级设置 ▼", Background = new SolidColorBrush(Colors.SlateGray), Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 10, 0, 0) };
            advancedButton.Click += ToggleAdvancedSettings;
            Grid.SetRow(advancedButton, 3);
            Grid.SetColumn(advancedButton, 0);
            Grid.SetColumnSpan(advancedButton, 3);

            formGrid.Children.Add(nameLabel);
            formGrid.Children.Add(_nameEntry);
            formGrid.Children.Add(_nameStatus);
            formGrid.Children.Add(pingLabel);
            formGrid.Children.Add(_pingEntry);
            formGrid.Children.Add(_pingStatus);
            formGrid.Children.Add(portLabel);
            formGrid.Children.Add(_portEntry);
            formGrid.Children.Add(_portStatus);
            formGrid.Children.Add(advancedButton);

            DetailPanel.Children.Add(formGrid);

            _advancedPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(10, 0, 10, 0) };
            var advancedGrid = new Grid();
            advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 4; i++)
            {
                advancedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var typeLabel = new TextBlock { Text = "$type:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(typeLabel, 0);
            Grid.SetColumn(typeLabel, 0);
            var typeEntry = new TextBox { Text = _currentRegion?.Type ?? "", IsReadOnly = true, Width = 250 };
            Grid.SetRow(typeEntry, 0);
            Grid.SetColumn(typeEntry, 1);

            var translateLabel = new TextBlock { Text = "TranslateName:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(translateLabel, 1);
            Grid.SetColumn(translateLabel, 0);
            _translateEntry = new TextBox { Text = _currentRegion?.TranslateName.ToString() ?? "", Width = 80 };
            _translateEntry.TextChanged += (_, _) => ValidateTranslateName();
            Grid.SetRow(_translateEntry, 1);
            Grid.SetColumn(_translateEntry, 1);
            _translateStatus = new TextBlock { Foreground = new SolidColorBrush(Colors.Red) };
            Grid.SetRow(_translateStatus, 1);
            Grid.SetColumn(_translateStatus, 2);

            var playersLabel = new TextBlock { Text = "Players:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(playersLabel, 2);
            Grid.SetColumn(playersLabel, 0);
            var playersEntry = new TextBox { Text = _currentRegion?.Servers?[0]?.Players.ToString() ?? "", IsReadOnly = true, Width = 80 };
            Grid.SetRow(playersEntry, 2);
            Grid.SetColumn(playersEntry, 1);

            var failuresLabel = new TextBlock { Text = "ConnectionFailures:", Foreground = new SolidColorBrush(Colors.Navy) };
            Grid.SetRow(failuresLabel, 3);
            Grid.SetColumn(failuresLabel, 0);
            var failuresEntry = new TextBox { Text = _currentRegion?.Servers?[0]?.ConnectionFailures.ToString() ?? "", IsReadOnly = true, Width = 80 };
            Grid.SetRow(failuresEntry, 3);
            Grid.SetColumn(failuresEntry, 1);

            advancedGrid.Children.Add(typeLabel);
            advancedGrid.Children.Add(typeEntry);
            advancedGrid.Children.Add(translateLabel);
            advancedGrid.Children.Add(_translateEntry);
            advancedGrid.Children.Add(_translateStatus);
            advancedGrid.Children.Add(playersLabel);
            advancedGrid.Children.Add(playersEntry);
            advancedGrid.Children.Add(failuresLabel);
            advancedGrid.Children.Add(failuresEntry);

            _advancedPanel.Children.Add(advancedGrid);
            DetailPanel.Children.Add(_advancedPanel);

            ValidateName();
            ValidatePingServer();
            ValidatePort();
            ValidateTranslateName();
            WriteLog("创建服务器详情表单");
        }

        // 切换高级设置显示
        private void ToggleAdvancedSettings(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            _advancedVisible = !_advancedVisible;
            _advancedPanel!.Visibility = _advancedVisible ? Visibility.Visible : Visibility.Collapsed;
            button.Content = _advancedVisible ? "高级设置 ▲" : "高级设置 ▼";
            WriteLog($"切换高级设置显示：{_advancedVisible}");
        }

        // 验证服务器名称
        private bool ValidateName()
        {
            if (_nameEntry == null || _nameStatus == null)
            {
                return false;
            }

            string name = _nameEntry.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                _nameStatus.Text = "✖ 名称不能为空";
                _nameStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            _nameStatus.Text = "✔ 有效";
            _nameStatus.Foreground = new SolidColorBrush(Colors.Green);
            if (_currentRegion != null)
            {
                _currentRegion.Name = name;
                _currentRegion.Servers[0].Name = "Http-1";
                SaveData();
                if (!_isUpdatingSelection)
                {
                    UpdateServerList();
                }
            }
            return true;
        }

        // 验证 Ping 服务器地址
        private bool ValidatePingServer()
        {
            if (_pingEntry == null || _pingStatus == null)
            {
                return false;
            }

            string server = _pingEntry.Text.Trim();
            if (string.IsNullOrEmpty(server))
            {
                _pingStatus.Text = "✖ 服务器地址不能为空";
                _pingStatus.Foreground = new SolidColorBrush(Colors.Red);
                if (_currentRegion != null)
                {
                    _currentRegion.PingServer = server;
                    _currentRegion.Servers[0].Ip = "";
                    SaveData();
                }
                return false;
            }

            if (server.Length is < 1 or > 64)
            {
                _pingStatus.Text = "✖ 地址长度应为1-64个字符";
                _pingStatus.Foreground = new SolidColorBrush(Colors.Red);
                if (_currentRegion != null)
                {
                    _currentRegion.PingServer = server;
                    _currentRegion.Servers[0].Ip = $"https://{server}";
                    SaveData();
                }
                return false;
            }

            server = Regex.Replace(server, @"^https?://", "");
            if (server.Contains('/'))
            {
                server = server.Split('/')[0];
            }
            _pingEntry.Text = server;

            _pingStatus.Text = "✔ 有效";
            _pingStatus.Foreground = new SolidColorBrush(Colors.Green);
            if (_currentRegion != null)
            {
                _currentRegion.PingServer = server;
                _currentRegion.Servers[0].Ip = $"https://{server}";
                SaveData();
                if (!_isUpdatingSelection)
                {
                    UpdateServerList();
                }
            }
            return true;
        }

        // 验证端口
        private bool ValidatePort()
        {
            if (_portEntry == null || _portStatus == null)
            {
                return false;
            }

            string portStr = _portEntry.Text.Trim();
            if (string.IsNullOrEmpty(portStr))
            {
                _portStatus.Text = "✖ 端口不能为空";
                _portStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (!int.TryParse(portStr, out int port) || port is < 0 or > 65535)
            {
                _portStatus.Text = "✖ 端口必须在0-65535之间";
                _portStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (_currentRegion != null)
            {
                _currentRegion.Servers[0].Port = port;
                SaveData();
                if (!_isUpdatingSelection)
                {
                    UpdateServerList();
                }
            }
            _portStatus.Text = "✔ 有效";
            _portStatus.Foreground = new SolidColorBrush(Colors.Green);
            return true;
        }

        // 验证 TranslateName
        private bool ValidateTranslateName()
        {
            if (_translateEntry == null || _translateStatus == null)
            {
                return false;
            }

            string transStr = _translateEntry.Text.Trim();
            if (string.IsNullOrEmpty(transStr))
            {
                _translateStatus.Text = "✖ 不能为空";
                _translateStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (!int.TryParse(transStr, out int trans) || trans <= 1000)
            {
                _translateStatus.Text = "✖ 必须是大于1000的整数";
                _translateStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            if (_currentRegion != null)
            {
                _currentRegion.TranslateName = trans;
                SaveData();
                if (!_isUpdatingSelection)
                {
                    UpdateServerList();
                }
            }
            _translateStatus.Text = "✔ 有效";
            _translateStatus.Foreground = new SolidColorBrush(Colors.Green);
            return true;
        }

        // 服务器列表加载完成
        private void ServerListBox_Loaded(object sender, RoutedEventArgs e)
        {
            ServerListBox.UpdateLayout();
            WriteLog("服务器列表加载完成");
        }

        // 服务器复选框点击
        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ServerDisplayItem item)
            {
                item.IsSelected = checkBox.IsChecked ?? false;
                UpdateDeleteButtonVisibility();
                WriteLog($"服务器 {_regions[item.RegionIndex].Name} 选中状态：{item.IsSelected}");
            }
        }

        // 预设服务器复选框点击
        private void PresetCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is PresetServerDisplayItem item)
            {
                item.IsSelected = checkBox.IsChecked ?? false;
                UpdateSelectAllCheckBoxState();
                WriteLog($"预设服务器 {_presetServers[item.PresetIndex].Name} 选中状态：{item.IsSelected}");
            }
        }

        // 全选预设服务器
        private void SelectAllPresetCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox)
            {
                return;
            }

            bool isChecked = checkBox.IsChecked ?? false;
            foreach (PresetServerDisplayItem item in PresetServerListBox.Items)
            {
                item.IsSelected = isChecked;
            }
            PresetServerListBox.Items.Refresh();
            WriteLog($"全选预设服务器：{isChecked}");
        }

        // 更新全选复选框状态
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

        // 更新删除按钮显示
        private void UpdateDeleteButtonVisibility()
        {
            DeleteButton.Visibility = _serverDisplayItems.Any(item => item.IsSelected && !string.IsNullOrEmpty(item.DisplayText))
                ? Visibility.Visible
                : Visibility.Hidden;
        }

        // 拖拽开始
        private void ServerListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        // 拖拽移动
        private void ServerListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint == null)
            {
                return;
            }

            Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint.Value - currentPosition;

            if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (sender is not ListBox listBox)
            {
                return;
            }

            var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (listBoxItem?.DataContext is not ServerDisplayItem draggedItem || string.IsNullOrEmpty(draggedItem.DisplayText))
            {
                return;
            }

            foreach (ServerDisplayItem item in ServerListBox.Items)
            {
                item.IsSelected = false;
            }
            UpdateDeleteButtonVisibility();
            ServerListBox.Items.Refresh();

            _serverDisplayItems.Add(new ServerDisplayItem
            {
                DisplayText = "",
                RegionIndex = -1,
                IsSelected = false
            });
            _isDragging = true;

            DragDrop.DoDragDrop(listBoxItem, draggedItem, DragDropEffects.Move);
            _dragStartPoint = null;
            WriteLog("开始拖拽服务器");
        }

        // 拖拽进入
        private void ServerListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ServerDisplayItem)) is not ServerDisplayItem)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (sender is not ListBox listBox)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;
            Point mousePos = e.GetPosition(listBox);
            int insertIndex = -1;

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

                    if (mousePos.Y >= itemTop + itemHeight / 2 && mousePos.Y < itemTop + itemHeight)
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

            if (insertIndex == _lastInsertIndex)
            {
                return;
            }

            for (int i = 0; i < listBox.Items.Count; i++)
            {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                {
                    if (FindVisualChild<Rectangle>(item, "InsertLine") is { } insertLine)
                    {
                        insertLine.Visibility = Visibility.Collapsed;
                    }
                }
            }

            if (insertIndex >= 0 && insertIndex <= listBox.Items.Count)
            {
                int displayIndex = insertIndex < listBox.Items.Count ? insertIndex : listBox.Items.Count - 1;
                if (listBox.ItemContainerGenerator.ContainerFromIndex(displayIndex) is ListBoxItem targetItem)
                {
                    if (FindVisualChild<Rectangle>(targetItem, "InsertLine") is { } insertLine)
                    {
                        insertLine.Visibility = Visibility.Visible;
                    }
                }
            }

            _lastInsertIndex = insertIndex;
            e.Handled = true;
        }

        // 拖拽释放
        private void ServerListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ServerDisplayItem)) is not ServerDisplayItem draggedItem)
            {
                return;
            }

            if (sender is not ListBox listBox)
            {
                return;
            }

            var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            int newIndex = -1;
            if (targetItem != null)
            {
                var targetData = (ServerDisplayItem)targetItem.DataContext;
                newIndex = string.IsNullOrEmpty(targetData.DisplayText) ? _regions.Count : targetData.RegionIndex;
                Point mousePos = e.GetPosition(targetItem);
                if (mousePos.Y > targetItem.ActualHeight / 2 && !string.IsNullOrEmpty(targetData.DisplayText))
                {
                    newIndex++;
                }
            }
            else
            {
                newIndex = _regions.Count;
            }

            int oldIndex = draggedItem.RegionIndex;
            if (oldIndex == newIndex || oldIndex + 1 == newIndex)
            {
                ResetDragVisualEffects(listBox);
                return;
            }

            var draggedRegion = _regions[oldIndex];
            _regions.RemoveAt(oldIndex);
            if (newIndex > oldIndex)
            {
                newIndex--;
            }
            _regions.Insert(newIndex, draggedRegion);

            SaveData();
            UpdateServerList();
            ResetDragVisualEffects(listBox);
            WriteLog($"拖拽服务器 {_regions[newIndex].Name} 到位置 {newIndex}");
        }

        // 重置拖拽视觉效果
        private void ResetDragVisualEffects(ListBox? listBox)
        {
            if (listBox == null)
            {
                return;
            }

            for (int i = 0; i < listBox.Items.Count; i++)
            {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                {
                    if (FindVisualChild<Rectangle>(item, "InsertLine") is { } insertLine)
                    {
                        insertLine.Visibility = Visibility.Collapsed;
                    }
                }
            }
            _lastInsertIndex = -1;

            if (_isDragging)
            {
                var placeholder = ServerListBox.Items.Cast<ServerDisplayItem>().LastOrDefault(item => string.IsNullOrEmpty(item.DisplayText));
                if (placeholder != null)
                {
                    ServerListBox.Items.Remove(placeholder);
                }
                _isDragging = false;
            }
            WriteLog("重置拖拽视觉效果");
        }

        // 查找视觉树中的控件
        private T? FindVisualChild<T>(DependencyObject obj, string name) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T target && (string)child.GetValue(FrameworkElement.NameProperty) == name)
                {
                    return target;
                }
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        // 查找父级控件
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null && current is not T)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }

        // 记录日志
        private void WriteLog(string message)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                File.AppendAllText(_logPath, logEntry);
            }
            catch
            {
                // 忽略日志写入错误
            }
        }

        // 数据模型
        public class RegionData
        {
            public int CurrentRegionIdx { get; set; }
            public List<RegionInfo>? Regions { get; set; }
        }

        public class RegionInfo
        {
            [JsonPropertyName("$type")]
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

            public string? DisplayText
            {
                get => _displayText;
                set
                {
                    _displayText = value;
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

        // 检测 Among Us 安装路径
        private void DetectGameInstallPath()
        {
            _gameInstallPath = GetAmongUsInstallPath();
            if (_gameInstallPath == null)
            {
                WriteLog("未在 Steam 库中检测到 Among Us 安装目录");
            }
            else
            {
                WriteLog($"检测到 Among Us 安装目录：{_gameInstallPath}");
            }
        }

        // 通过读取注册表与 libraryfolders.vdf 寻找游戏目录，仅支持 Steam
        private string? GetAmongUsInstallPath()
        {
            try
            {
                var steamPath = GetSteamPathFromRegistry();
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return null;

                // 默认库
                var defaultPath = IOPath.Combine(steamPath, "steamapps", "common", "Among Us");
                if (Directory.Exists(defaultPath)) return defaultPath;

                // 额外库
                var libraryFile = IOPath.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFile)) return null;
                var lines = File.ReadAllLines(libraryFile);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"");
                    if (match.Success)
                    {
                        var path = match.Groups[1].Value.Replace("\\\\", "\\");
                        var gamePath = IOPath.Combine(path, "steamapps", "common", "Among Us");
                        if (Directory.Exists(gamePath)) return gamePath;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"检测游戏路径时出错：{ex.Message}");
            }
            return null;
        }

        // 从注册表读取 Steam 安装路径
        private static string? GetSteamPathFromRegistry()
        {
            string? path = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\\Valve\\Steam")?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(path)) return path.Replace('/', '\\');
            path = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\\WOW6432Node\\Valve\\Steam")?.GetValue("InstallPath") as string;
            return path?.Replace('/', '\\');
        }

        // 确保 BepInEx 已安装，如未安装则自动下载并解压
        private async Task EnsureBepInExAsync()
        {
            if (_gameInstallPath == null) return; // 未检测到游戏
            var bepInExDir = IOPath.Combine(_gameInstallPath, "BepInEx");
            if (Directory.Exists(bepInExDir))
            {
                WriteLog("已检测到 BepInEx，无需安装");
                MessageBox.Show("已检测到 BepInEx，无需安装", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                WriteLog("开始下载 BepInEx...");
                string tempZip = IOPath.GetTempFileName();
                await using (var remote = await _httpClient.GetStreamAsync(_bepInExDownloadUrl))
                await using (var local = File.Create(tempZip))
                {
                    await remote.CopyToAsync(local);
                }

                WriteLog("下载完成，开始解压...");
                ZipFile.ExtractToDirectory(tempZip, _gameInstallPath, true);
                File.Delete(tempZip);
                WriteLog("BepInEx 安装完成");
                MessageBox.Show("BepInEx 安装完成！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WriteLog($"安装 BepInEx 失败：{ex.Message}");
                MessageBox.Show($"自动安装 BepInEx 失败：{ex.Message}\n请尝试手动安装。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 加载用户设置
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                WriteLog($"加载设置失败：{ex.Message}");
                _settings = new();
            }
        }

        // 保存用户设置
        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(_settingsPath)!);
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                WriteLog($"保存设置失败：{ex.Message}");
            }
        }

        // 根据状态更新安装 BepInEx 的按钮与复选框可见性
        private void EvaluateBepInExUI()
        {
            if (_settings.SuppressBepInExPrompt)
            {
                InstallBepInExButton.Visibility = Visibility.Collapsed;
                UninstallBepInExButton.Visibility = Visibility.Collapsed;
                SuppressBepInExCheckBox.Visibility = Visibility.Collapsed;
                BepInExInfoText.Visibility = Visibility.Collapsed;
                return;
            }

            if (_gameInstallPath == null)
            {
                InstallBepInExButton.Visibility = Visibility.Collapsed;
                UninstallBepInExButton.Visibility = Visibility.Collapsed;
                SuppressBepInExCheckBox.Visibility = Visibility.Collapsed;
                BepInExInfoText.Visibility = Visibility.Collapsed;
                return;
            }

            bool installed = Directory.Exists(IOPath.Combine(_gameInstallPath, "BepInEx"));
            InstallBepInExButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            UninstallBepInExButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            SuppressBepInExCheckBox.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            BepInExInfoText.Visibility = (InstallBepInExButton.Visibility == Visibility.Visible || UninstallBepInExButton.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            SuppressBepInExCheckBox.IsChecked = _settings.SuppressBepInExPrompt;
        }

        // "安装 BepInEx"按钮点击
        private async void InstallBepInEx_Click(object sender, RoutedEventArgs e)
        {
            InstallBepInExButton.IsEnabled = false;
            await EnsureBepInExAsync();
            EvaluateBepInExUI();
        }

        // "卸载 BepInEx"按钮点击
        private void UninstallBepInEx_Click(object sender, RoutedEventArgs e)
        {
            if (_gameInstallPath == null)
            {
                MessageBox.Show("未检测到游戏安装路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var bepDir = IOPath.Combine(_gameInstallPath, "BepInEx");
            if (!Directory.Exists(bepDir))
            {
                MessageBox.Show("未检测到 BepInEx，无需卸载。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                EvaluateBepInExUI();
                return;
            }

            if (MessageBox.Show("确定要卸载 BepInEx 吗？这将删除 BepInEx 整个目录。", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                Directory.Delete(bepDir, true);
                WriteLog("已卸载 BepInEx");
                MessageBox.Show("已卸载 BepInEx。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WriteLog($"卸载 BepInEx 失败：{ex.Message}");
                MessageBox.Show($"卸载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            EvaluateBepInExUI();
        }

        // "不再提示"复选框点击
        private void SuppressBepInExCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _settings.SuppressBepInExPrompt = SuppressBepInExCheckBox.IsChecked ?? false;
            SaveSettings();
            EvaluateBepInExUI();
        }

        // 用户设置模型
        private class AppSettings
        {
            public bool SuppressBepInExPrompt { get; set; }
            public string? GitHubToken { get; set; }
        }

        // 打开模组管理窗口
        private void OpenModManager_Click(object sender, RoutedEventArgs e)
        {
            var win = new ModManagerWindow(_gameInstallPath, _httpClient, _settings.GitHubToken);
            win.Owner = this;
            win.ShowDialog();
            EvaluateBepInExUI(); // 安装或卸载模组可能影响BepInEx目录
        }
    }
}