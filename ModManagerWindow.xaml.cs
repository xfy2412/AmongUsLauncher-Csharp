using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Windows;
using IOPath = System.IO.Path;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;

namespace AULGK
{
    public partial class ModManagerWindow : Window
    {
        private readonly string? _gamePath;
        private readonly HttpClient _httpClient;
        private readonly string _modsUrl = "https://mxzc.cloud:35249/mod_list.json";
        private readonly string _modDir;
        private readonly string _infoDir;
        private readonly string _filesDir;
        private readonly string _statusFile;
        private readonly ObservableCollection<ModInfo> _mods = new();
        private readonly Dictionary<string, ModStatus> _modStatuses = new();
        private readonly Action<string>? _logger;

        public ModManagerWindow(string? gamePath, HttpClient client, Action<string>? logger = null)
        {
            InitializeComponent();
            _gamePath = gamePath;
            _httpClient = client;
            _logger = logger;

            // 初始化目录
            string baseDir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod");
            _modDir = baseDir;
            _infoDir = IOPath.Combine(baseDir, "info");
            _filesDir = IOPath.Combine(baseDir, "files");
            _statusFile = IOPath.Combine(baseDir, "status.json");

            Directory.CreateDirectory(_modDir);
            Directory.CreateDirectory(_infoDir);
            Directory.CreateDirectory(_filesDir);

            ModListBox.ItemsSource = _mods;
            LoadStatusFile();
            _ = LoadModsAsync();
        }

        private void LoadStatusFile()
        {
            try
            {
                if (File.Exists(_statusFile))
                {
                    string json = File.ReadAllText(_statusFile);
                    var statuses = JsonSerializer.Deserialize<Dictionary<string, ModStatus>>(json);
                    if (statuses != null)
                    {
                        _modStatuses.Clear();
                        foreach (var kvp in statuses)
                        {
                            _modStatuses[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"加载 status.json 失败：{ex.Message}");
            }
        }

        private void SaveStatusFile()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_modStatuses, options);
                File.WriteAllText(_statusFile, json);
            }
            catch (Exception ex)
            {
                Log($"保存 status.json 失败：{ex.Message}");
            }
        }

        private async Task LoadModsAsync()
        {
            _mods.Clear();
            StatusText.Text = "正在获取模组列表...";
            Log("开始获取模组列表");
            try
            {
                var json = await _httpClient.GetStringAsync(_modsUrl);
                var list = JsonSerializer.Deserialize<List<ModInfo>>(json) ?? new();

                foreach (var modInfo in list)
                {
                    if (!_modStatuses.ContainsKey(modInfo.Name))
                    {
                        _modStatuses[modInfo.Name] = new ModStatus
                        {
                            Info = "",
                            Downloaded = 0,
                            Installed = 0
                        };
                        SaveStatusFile();
                    }
                    UpdateModStatus(modInfo);
                    _mods.Add(modInfo);
                }

                StatusText.Text = $"已加载 {_mods.Count} 个模组";
                Log($"已加载 {_mods.Count} 个模组");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"获取列表失败：{ex.Message}";
                Log($"加载模组列表失败：{ex.Message}");
            }
        }

        private void UpdateModStatus(ModInfo mod)
        {
            if (_modStatuses.TryGetValue(mod.Name, out var status))
            {
                mod.IsDownloaded = status.Downloaded == 1;
                mod.IsInstalled = status.Installed == 1;
                mod.InfoPath = status.Info;
                mod.InstallState = status.Installed == 1 ? "模组已安装" :
                                   status.Downloaded == 1 ? "模组已下载，但未启用" : "模组未下载";
            }
            else
            {
                mod.InstallState = "模组未下载";
            }
            mod.OnPropertyChanged(nameof(mod.InstallState));
        }

        private void ModListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedMod = ModListBox.SelectedItem as ModInfo;
            if (selectedMod != null)
            {
                DetailHint.Visibility = Visibility.Collapsed;
                DetailContent.Visibility = Visibility.Visible;
                DetailPanel.DataContext = selectedMod;
                UpdateButtonStates(selectedMod);
            }
            else
            {
                DetailHint.Visibility = Visibility.Visible;
                DetailContent.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateButtonStates(ModInfo mod)
        {
            InstallButton.IsEnabled = !mod.IsDownloaded;
            ToggleButton.IsEnabled = mod.IsDownloaded;
            UninstallButton.IsEnabled = mod.IsDownloaded;
            OpenFolderButton.IsEnabled = Directory.Exists(_filesDir);
            ToggleButton.Content = mod.IsInstalled ? "🔌 禁用" : "🔌 启用";
        }

        private void Window_Activated(object sender, System.EventArgs e)
        {
            foreach (var mod in _mods)
            {
                UpdateModStatus(mod);
            }
            ModListBox.Items.Refresh();
            if (DetailPanel.DataContext is ModInfo selectedMod)
            {
                UpdateButtonStates(selectedMod);
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo selectedMod) return;
            if (string.IsNullOrEmpty(selectedMod.DownloadUrl))
            {
                MessageBox.Show("未找到可下载的版本文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await InstallModAsync(selectedMod);
            UpdateModStatus(selectedMod);
            ModListBox.Items.Refresh();
            UpdateButtonStates(selectedMod);
        }

        private async Task InstallModAsync(ModInfo selectedMod)
        {
            try
            {
                StatusText.Text = $"下载 {selectedMod.Name}...";
                Log($"开始下载 {selectedMod.Name}");

                string downloadUrl = selectedMod.DownloadUrl;
                string fileName = IOPath.GetFileName(new Uri(downloadUrl).LocalPath);
                string tmp = IOPath.Combine(IOPath.GetTempPath(), fileName);

                var progressWindow = new ProgressWindow($"正在下载 {selectedMod.Name} 模组");
                progressWindow.Show();

                try
                {
                    await using (var remote = await _httpClient.GetStreamAsync(downloadUrl))
                    await using (var local = File.Create(tmp))
                    {
                        await remote.CopyToAsync(local);
                    }

                    string modDir = IOPath.Combine(_filesDir, selectedMod.Name);
                    if (Directory.Exists(modDir))
                    {
                        Directory.Delete(modDir, true);
                    }
                    Directory.CreateDirectory(modDir);

                    using (var zip = ZipFile.OpenRead(tmp))
                    {
                        var jsonEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                        if (jsonEntry == null)
                        {
                            throw new Exception("压缩包中未找到 JSON 文件");
                        }

                        string jsonPath = IOPath.Combine(_infoDir, $"{selectedMod.Name}.json");
                        using (var stream = jsonEntry.Open())
                        using (var file = File.Create(jsonPath))
                        {
                            await stream.CopyToAsync(file);
                        }

                        foreach (var entry in zip.Entries.Where(e => !e.FullName.Equals(jsonEntry.FullName, StringComparison.OrdinalIgnoreCase)))
                        {
                            string destPath = IOPath.Combine(modDir, entry.FullName);
                            Directory.CreateDirectory(IOPath.GetDirectoryName(destPath)!);
                            entry.ExtractToFile(destPath, true);
                        }

                        _modStatuses[selectedMod.Name].Info = jsonPath;
                        _modStatuses[selectedMod.Name].Downloaded = 1;
                        SaveStatusFile();
                    }

                    File.Delete(tmp);
                    StatusText.Text = $"{selectedMod.Name} 下载完成";
                    Log($"已下载 {selectedMod.Name}");

                    // 自动尝试启用
                    await EnableModAsync(selectedMod);
                }
                finally
                {
                    progressWindow.Close();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"安装失败：{ex.Message}";
                Log($"安装 {selectedMod.Name} 失败：{ex.Message}");
            }
        }

        private async Task EnableModAsync(ModInfo mod)
        {
            var otherEnabled = _modStatuses.Where(kvp => kvp.Key != mod.Name && kvp.Value.Installed == 1).ToList();
            if (otherEnabled.Any())
            {
                var result = ShowCustomMessageBox(
                    "您的模组下载完毕，但是同时启用多个模组可能会发生意料之外的问题，开发者不会处理这些问题，是否继续？",
                    "警告",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No,
                    MessageBoxOptions.None,
                    new[]
                    {
                        new CustomMessageBoxButton { Content = "是", Result = MessageBoxResult.Yes },
                        new CustomMessageBoxButton { Content = "否", Result = MessageBoxResult.No },
                        new CustomMessageBoxButton { Content = "禁用其他模组", Result = MessageBoxResult.Cancel }
                    });

                if (result == MessageBoxResult.No)
                {
                    return;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    foreach (var kvp in otherEnabled)
                    {
                        var otherMod = _mods.FirstOrDefault(m => m.Name == kvp.Key);
                        if (otherMod != null)
                        {
                            await DisableModAsync(otherMod);
                        }
                    }
                }
            }

            var progressWindow = new ProgressWindow($"正在启用 {mod.Name} 模组");
            progressWindow.Show();

            try
            {
                string jsonPath = _modStatuses[mod.Name].Info;
                if (!File.Exists(jsonPath))
                {
                    throw new Exception("模组信息文件不存在");
                }

                string json = File.ReadAllText(jsonPath);
                var modInfo = JsonSerializer.Deserialize<ModFilesInfo>(json);
                if (modInfo?.Files == null)
                {
                    throw new Exception("无效的模组信息文件");
                }

                string sourceDir = IOPath.Combine(_filesDir, mod.Name);
                foreach (var file in modInfo.Files)
                {
                    string sourcePath = IOPath.Combine(sourceDir, file);
                    string destPath = IOPath.Combine(_gamePath!, file);
                    Directory.CreateDirectory(IOPath.GetDirectoryName(destPath)!);
                    File.Copy(sourcePath, destPath, true);
                }

                _modStatuses[mod.Name].Installed = 1;
                SaveStatusFile();
                StatusText.Text = $"{mod.Name} 已启用";
                Log($"已启用 {mod.Name}");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"启用失败：{ex.Message}";
                Log($"启用 {mod.Name} 失败：{ex.Message}");
            }
            finally
            {
                progressWindow.Close();
                UpdateModStatus(mod);
                ModListBox.Items.Refresh();
                UpdateButtonStates(mod);
            }
        }

        private async void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo selectedMod) return;
            if (selectedMod.IsInstalled)
            {
                await DisableModAsync(selectedMod);
            }
            else if (selectedMod.IsDownloaded)
            {
                await EnableModAsync(selectedMod);
            }
            UpdateModStatus(selectedMod);
            ModListBox.Items.Refresh();
            UpdateButtonStates(selectedMod);
        }

        private async Task DisableModAsync(ModInfo mod)
        {
            var progressWindow = new ProgressWindow($"正在禁用 {mod.Name} 模组");
            progressWindow.Show();

            try
            {
                string jsonPath = _modStatuses[mod.Name].Info;
                if (!File.Exists(jsonPath))
                {
                    throw new Exception("模组信息文件不存在");
                }

                string json = File.ReadAllText(jsonPath);
                var modInfo = JsonSerializer.Deserialize<ModFilesInfo>(json);
                if (modInfo?.Files == null)
                {
                    throw new Exception("无效的模组信息文件");
                }

                foreach (var file in modInfo.Files)
                {
                    string filePath = IOPath.Combine(_gamePath!, file);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }

                _modStatuses[mod.Name].Installed = 0;
                SaveStatusFile();
                StatusText.Text = $"{mod.Name} 已禁用";
                Log($"已禁用 {mod.Name}");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"禁用失败：{ex.Message}";
                Log($"禁用 {mod.Name} 失败：{ex.Message}");
            }
            finally
            {
                progressWindow.Close();
                UpdateModStatus(mod);
                ModListBox.Items.Refresh();
                UpdateButtonStates(mod);
            }
        }

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo selectedMod) return;

            if (MessageBox.Show($"即将删除 {selectedMod.Name} 模组的文件，是否继续？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            if (selectedMod.IsInstalled)
            {
                await DisableModAsync(selectedMod);
            }

            try
            {
                string modDir = IOPath.Combine(_filesDir, selectedMod.Name);
                if (Directory.Exists(modDir))
                {
                    Directory.Delete(modDir, true);
                }

                string jsonPath = _modStatuses[selectedMod.Name].Info;
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                }

                _modStatuses[selectedMod.Name].Downloaded = 0;
                _modStatuses[selectedMod.Name].Info = "";
                SaveStatusFile();
                StatusText.Text = $"{selectedMod.Name} 已卸载";
                Log($"已卸载 {selectedMod.Name}");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"卸载失败：{ex.Message}";
                Log($"卸载 {selectedMod.Name} 失败：{ex.Message}");
            }
            finally
            {
                UpdateModStatus(selectedMod);
                ModListBox.Items.Refresh();
                UpdateButtonStates(selectedMod);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadModsAsync();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_filesDir))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", _filesDir) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"无法打开目录: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Log(string message) => _logger?.Invoke($"[ModManager] {message}");

        public class ModInfo : INotifyPropertyChanged
        {
            private string _name = "";
            private string _version = "";
            private string _description = "";
            private string _downloadUrl = "";
            private string _installState = "";
            private bool _isDownloaded;
            private bool _isInstalled;
            private string _infoPath = "";

            [JsonPropertyName("name")] public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
            [JsonPropertyName("version")] public string Version { get => _version; set { _version = value; OnPropertyChanged(); } }
            [JsonPropertyName("description")] public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
            [JsonPropertyName("path")] public string DownloadUrl { get => _downloadUrl; set { _downloadUrl = value; OnPropertyChanged(); } }
            [JsonIgnore] public bool IsDownloaded { get => _isDownloaded; set { _isDownloaded = value; OnPropertyChanged(); } }
            [JsonIgnore] public bool IsInstalled { get => _isInstalled; set { _isInstalled = value; OnPropertyChanged(); } }
            [JsonIgnore] public string InstallState { get => _installState; set { _installState = value; OnPropertyChanged(); } }
            [JsonIgnore] public string InfoPath { get => _infoPath; set { _infoPath = value; OnPropertyChanged(); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            public void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public class ModStatus
        {
            public string Info { get; set; } = "";
            public int Downloaded { get; set; }
            public int Installed { get; set; }
        }

        public class ModFilesInfo
        {
            [JsonPropertyName("files")]
            public string[]? Files { get; set; }
        }

        public class CustomMessageBoxButton
        {
            public string Content { get; set; } = "";
            public MessageBoxResult Result { get; set; }
        }

        private static MessageBoxResult ShowCustomMessageBox(string message, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options, CustomMessageBoxButton[] customButtons)
        {
            var window = new Window
            {
                Title = caption,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(44, 62, 80))
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(textBlock, 0);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonPanel, 1);

            foreach (var btn in customButtons)
            {
                var customButton = new Button
                {
                    Content = btn.Content,
                    Margin = new Thickness(5, 0, 5, 0),
                    Padding = new Thickness(10, 5, 10, 5),
                    MinWidth = 80
                };
                customButton.Click += (s, e) => { window.DialogResult = btn.Result == MessageBoxResult.Yes; window.Close(); };
                buttonPanel.Children.Add(customButton);
            }

            grid.Children.Add(textBlock);
            grid.Children.Add(buttonPanel);
            window.Content = grid;

            return window.ShowDialog() == true ? MessageBoxResult.Yes : defaultResult;
        }
    }
}