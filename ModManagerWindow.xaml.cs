using AULGK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace AULGK
{
    public partial class ModManagerWindow : Window
    {
        // 字段：模组管理器核心配置
        private readonly string? _gamePath; // 游戏安装目录，如 c:\program files (x86)\steam\steamapps\common\Among Us
        private readonly HttpClient _httpClient; // 用于下载模组列表和文件
        private readonly string _modsUrl = "https://mxzc.cloud:35249/mod_list.json"; // 模组列表 JSON 地址
        private readonly string _modDir; // 本地模组根目录：mod/
        private readonly string _infoDir; // 模组信息目录：mod/info/
        private readonly string _filesDir; // 模组文件目录：mod/files/
        private readonly string _statusFile; // 模组状态文件：mod/status.json
        private readonly ObservableCollection<ModInfo> _mods = new(); // 模组列表，绑定到 UI
        private readonly Dictionary<string, ModStatus> _modStatuses = new(); // 模组状态缓存
        private readonly Action<string>? _logger; // 日志记录回调
        private bool _isLoadingMods = false; // 防止重复加载模组

        // 构造函数：初始化模组管理器
        public ModManagerWindow(string? gamePath, HttpClient client, Action<string>? logger = null)
        {
            InitializeComponent();
            _gamePath = gamePath;
            _httpClient = client;
            _logger = logger;

            // 初始化目录结构
            string baseDir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod");
            _modDir = baseDir;
            _infoDir = IOPath.Combine(baseDir, "info");
            _filesDir = IOPath.Combine(baseDir, "files");
            _statusFile = IOPath.Combine(baseDir, "status.json");

            Directory.CreateDirectory(_modDir);
            Directory.CreateDirectory(_infoDir);
            Directory.CreateDirectory(_filesDir);

            // 绑定模组列表到 UI 并加载状态
            ModListBox.ItemsSource = _mods;
            LoadStatusFile();
            _ = LoadModsAsync();
        }

        // 日志记录：记录操作到日志文件
        private void Log(string message) => _logger?.Invoke($"[ModManager] {message}");

        // 加载状态文件：从 status.json 读取模组状态
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
                        Log($"加载 status.json，包含 {_modStatuses.Count} 个模组状态");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"加载 status.json 失败：{ex.Message}");
            }
        }

        // 保存状态文件：将模组状态写入 status.json
        private void SaveStatusFile()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_modStatuses, options);
                File.WriteAllText(_statusFile, json);
                Log($"保存 status.json");
            }
            catch (Exception ex)
            {
                Log($"保存 status.json 失败：{ex.Message}");
            }
        }

        // 加载模组列表：从远程 URL 获取模组并更新 UI
        private async Task LoadModsAsync()
        {
            if (_isLoadingMods) return; // 防止重复加载
            _isLoadingMods = true;

            try
            {
                // 清空现有模组列表
                await Dispatcher.InvokeAsync(() =>
                {
                    _mods.Clear();
                    Log($"清空 _mods 集合，当前模组数：{_mods.Count}");
                });

                // 获取远程模组列表
                var json = await _httpClient.GetStringAsync(_modsUrl);
                Log($"从 {_modsUrl} 获取 mod_list.json：{json}");
                var list = JsonSerializer.Deserialize<List<ModInfo>>(json) ?? new();
                Log($"解析到 {list.Count} 个模组：{string.Join(", ", list.Select(m => m.Name))}");

                // 去重模组（按 Name）
                var uniqueMods = list.GroupBy(m => m.Name).Select(g => g.First()).ToList();
                if (list.Count != uniqueMods.Count)
                {
                    Log($"发现重复模组，去重后剩余 {uniqueMods.Count} 个模组");
                }

                // 更新模组状态并添加到列表
                foreach (var modInfo in uniqueMods)
                {
                    if (!_modStatuses.ContainsKey(modInfo.Name))
                    {
                        _modStatuses[modInfo.Name] = new ModStatus
                        {
                            Info = "",
                            Downloaded = 0,
                            Installed = 0,
                            Version = modInfo.Version
                        };
                        SaveStatusFile();
                    }
                    else if (_modStatuses[modInfo.Name].Downloaded == 0 && _modStatuses[modInfo.Name].Version != modInfo.Version)
                    {
                        _modStatuses[modInfo.Name].Version = modInfo.Version;
                        SaveStatusFile();
                    }
                    UpdateModStatus(modInfo);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!_mods.Any(m => m.Name == modInfo.Name))
                        {
                            _mods.Add(modInfo);
                            Log($"添加模组：{modInfo.Name}，版本：{modInfo.Version}，有更新：{modInfo.HasUpdate}");
                        }
                    });
                }

                // 刷新 UI 显示
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = $"添加模组后首次启动可能会需要2~3分钟的时间，请耐心等待~";
                    ModListBox.Items.Refresh();
                });
                Log($"已加载 {_mods.Count} 个模组");
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = $"获取列表失败：{ex.Message}";
                });
                Log($"加载模组列表失败：{ex}");
            }
            finally
            {
                _isLoadingMods = false;
            }
        }

        // 更新模组状态：设置下载、安装和更新状态
        private void UpdateModStatus(ModInfo mod)
        {
            if (_modStatuses.TryGetValue(mod.Name, out var status))
            {
                mod.IsDownloaded = status.Downloaded == 1;
                mod.IsInstalled = status.Installed == 1;
                mod.InfoPath = status.Info;
                mod.HasUpdate = status.Downloaded == 1 && status.Version != mod.Version;
                mod.InstallState = status.Installed == 1 ? "模组已安装" :
                              status.Downloaded == 1 ? "模组已下载，但未启用" : "模组未下载";
                Log($"更新模组状态：{mod.Name}，Downloaded={status.Downloaded}，Installed={status.Installed}，Version={status.Version}，HasUpdate={mod.HasUpdate}");
            }
            else
            {
                mod.IsDownloaded = false;
                mod.IsInstalled = false;
                mod.HasUpdate = false;
                mod.InfoPath = "";
                mod.InstallState = "模组未下载";
            }
            mod.OnPropertyChanged(nameof(mod.InstallState));
            mod.OnPropertyChanged(nameof(mod.HasUpdate));
        }

        // 更新 UI 按钮：根据模组状态设置按钮内容和可用性
        private void UpdateButtonStates(ModInfo mod)
        {
            if (_modStatuses.TryGetValue(mod.Name, out var status))
            {
                InstallButton.Content = status.Downloaded == 1 && status.Version != mod.Version ? "⬇️ 更新" : "⬇️ 安装";
                InstallButton.IsEnabled = !mod.IsDownloaded || mod.HasUpdate;
                ToggleButton.IsEnabled = mod.IsDownloaded;
                UninstallButton.IsEnabled = mod.IsDownloaded;
                OpenFolderButton.IsEnabled = Directory.Exists(_filesDir);
                ToggleButton.Content = mod.IsInstalled ? "🔌 禁用" : "🔌 启用";
            }
            else
            {
                InstallButton.Content = "⬇️ 安装";
                InstallButton.IsEnabled = true;
                ToggleButton.IsEnabled = false;
                UninstallButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = Directory.Exists(_filesDir);
                ToggleButton.Content = "🔌 启用";
            }
        }

        // 模组选择事件：更新详情面板和按钮状态
        private void ModListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModListBox.SelectedItem is ModInfo selectedMod)
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

        // 安装按钮点击：安装或更新模组
        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo selectedMod) return;
            if (string.IsNullOrEmpty(selectedMod.DownloadUrl))
            {
                MessageBox.Show("未找到可下载的版本文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_modStatuses.TryGetValue(selectedMod.Name, out var status) && status.Downloaded == 1 && status.Version != selectedMod.Version)
            {
                await UpdateModAsync(selectedMod);
            }
            else
            {
                await InstallModAsync(selectedMod);
            }
            UpdateModStatus(selectedMod);
            await Dispatcher.InvokeAsync(() => ModListBox.Items.Refresh());
            UpdateButtonStates(selectedMod);
        }

        // 启用/禁用按钮点击：切换模组状态
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
            await Dispatcher.InvokeAsync(() => ModListBox.Items.Refresh());
            UpdateButtonStates(selectedMod);
        }

        // 刷新按钮点击：重新加载模组列表
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadModsAsync();
        }

        // 打开文件夹按钮：打开模组文件目录
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

        // 卸载按钮点击：删除模组文件和状态
        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo selectedMod) return;

            // 确认卸载
            if (MessageBox.Show($"即将删除 {selectedMod.Name} 模组的文件，是否继续？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                // 如果模组已启用，先禁用以清理 BepInEx/plugins/
                if (selectedMod.IsInstalled)
                {
                    await DisableModAsync(selectedMod);
                }

                // 删除游戏目录中非 BepInEx/plugins/ 的文件和相关文件夹
                string jsonPath = _modStatuses[selectedMod.Name].Info;
                if (File.Exists(jsonPath))
                {
                    string jsonContent = File.ReadAllText(jsonPath);
                    Log($"读取 JSON 文件内容：{jsonContent}");
                    var modInfo = JsonSerializer.Deserialize<ModFilesInfo>(jsonContent);
                    if (modInfo?.Files != null)
                    {
                        var directoriesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var file in modInfo.Files.Where(f => !f.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase)))
                        {
                            string filePath = IOPath.Combine(_gamePath!, file);
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                                Log($"删除游戏目录文件：{filePath}");
                            }

                            // 收集父目录
                            string? parentDir = IOPath.GetDirectoryName(file);
                            while (!string.IsNullOrEmpty(parentDir) && !parentDir.Equals(_gamePath, StringComparison.OrdinalIgnoreCase))
                            {
                                string fullParentDir = IOPath.Combine(_gamePath!, parentDir);
                                if (!fullParentDir.StartsWith(IOPath.Combine(_gamePath!, "BepInEx/plugins"), StringComparison.OrdinalIgnoreCase))
                                {
                                    directoriesToDelete.Add(fullParentDir);
                                }
                                parentDir = IOPath.GetDirectoryName(parentDir);
                            }
                        }

                        // 删除收集的目录（从最深层开始）
                        foreach (var dir in directoriesToDelete.OrderByDescending(d => d.Length))
                        {
                            if (Directory.Exists(dir))
                            {
                                try
                                {
                                    Directory.Delete(dir, true);
                                    Log($"删除游戏目录文件夹：{dir}");
                                }
                                catch (Exception ex)
                                {
                                    Log($"删除游戏目录文件夹 {dir} 失败：{ex.Message}");
                                }
                            }
                        }
                    }
                }

                // 删除本地模组目录和信息文件
                string modDir = IOPath.Combine(_filesDir, selectedMod.Name);
                if (Directory.Exists(modDir))
                {
                    Directory.Delete(modDir, true);
                    Log($"删除模组目录：{modDir}");
                }
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    Log($"删除 JSON 文件：{jsonPath}");
                }

                // 更新状态并保存
                _modStatuses[selectedMod.Name].Downloaded = 0;
                _modStatuses[selectedMod.Name].Info = "";
                _modStatuses[selectedMod.Name].Version = "";
                SaveStatusFile();
                StatusText.Text = $"{selectedMod.Name} 已卸载";
                Log($"已卸载 {selectedMod.Name}");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"卸载失败：{ex.Message}";
                Log($"卸载 {selectedMod.Name} 失败：{ex}");
            }
            finally
            {
                UpdateModStatus(selectedMod);
                await Dispatcher.InvokeAsync(() => ModListBox.Items.Refresh());
                UpdateButtonStates(selectedMod);
            }
        }

        // 安装模组：下载并解压模组文件
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
                    // 下载模组包
                    await using (var remote = await _httpClient.GetStreamAsync(downloadUrl))
                    await using (var local = File.Create(tmp))
                    {
                        await remote.CopyToAsync(local);
                    }
                    Log($"下载完成，临时文件：{tmp}，大小：{new FileInfo(tmp).Length} 字节");

                    // 清理并创建模组目录
                    string modDir = IOPath.Combine(_filesDir, selectedMod.Name);
                    if (Directory.Exists(modDir))
                    {
                        Directory.Delete(modDir, true);
                    }
                    Directory.CreateDirectory(modDir);

                    // 解压模组文件
                    string jsonPath = "";
                    ModFilesInfo? modInfo = null;
                    using (var zip = ZipFile.OpenRead(tmp))
                    {
                        var zipEntries = zip.Entries.Select(e => e.FullName).ToList();
                        Log($"ZIP 文件内容：{string.Join(", ", zipEntries)}");

                        // 提取 mod.json
                        var jsonEntry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("mod.json", StringComparison.OrdinalIgnoreCase));
                        if (jsonEntry == null)
                        {
                            throw new Exception("压缩包中未找到 mod.json 文件");
                        }
                        Log($"找到 JSON 文件：{jsonEntry.FullName}");

                        jsonPath = IOPath.Combine(_infoDir, $"{selectedMod.Name}.json");
                        using (var stream = jsonEntry.Open())
                        using (var file = File.Create(jsonPath))
                        {
                            await stream.CopyToAsync(file);
                        }
                        Log($"复制 JSON 文件到：{jsonPath}");

                        // 验证 mod.json
                        string jsonContent = File.ReadAllText(jsonPath);
                        Log($"JSON 文件内容：{jsonContent}");
                        modInfo = JsonSerializer.Deserialize<ModFilesInfo>(jsonContent);
                        if (modInfo?.Files == null)
                        {
                            throw new Exception("无效的 mod.json 文件，缺少 files 数组");
                        }
                        Log($"mod.json 中的文件列表：{string.Join(", ", modInfo.Files)}");

                        // 解压所有文件（除 mod.json）
                        foreach (var entry in zip.Entries.Where(e => !e.FullName.Equals(jsonEntry.FullName, StringComparison.OrdinalIgnoreCase)))
                        {
                            string destPath = IOPath.Combine(modDir, entry.FullName);
                            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                            {
                                Directory.CreateDirectory(destPath);
                                Log($"创建目录：{destPath}");
                                continue;
                            }

                            string parentDir = IOPath.GetDirectoryName(destPath)!;
                            Directory.CreateDirectory(parentDir);
                            Log($"解压文件：{entry.FullName} 到 {destPath}");
                            try
                            {
                                entry.ExtractToFile(destPath, true);
                            }
                            catch (Exception ex)
                            {
                                Log($"解压文件 {entry.FullName} 失败：{ex.Message}");
                                throw;
                            }
                        }
                    }

                    // 检查是否已启用其他模组
                    var otherEnabled = _modStatuses.Where(kvp => kvp.Key != selectedMod.Name && kvp.Value.Installed == 1).ToList();
                    if (otherEnabled.Any())
                    {
                        Log($"检测到其他已启用模组：{string.Join(", ", otherEnabled.Select(kvp => kvp.Key))}");
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
                            // 取消安装，清理已解压文件
                            if (Directory.Exists(modDir))
                            {
                                Directory.Delete(modDir, true);
                                Log($"取消安装，删除模组目录：{modDir}");
                            }
                            if (File.Exists(jsonPath))
                            {
                                File.Delete(jsonPath);
                                Log($"取消安装，删除 JSON 文件：{jsonPath}");
                            }
                            StatusText.Text = $"{selectedMod.Name} 安装已取消";
                            Log($"安装 {selectedMod.Name} 已取消");
                            return;
                        }
                        else if (result == MessageBoxResult.Cancel)
                        {
                            // 禁用其他模组
                            foreach (var kvp in otherEnabled)
                            {
                                var otherMod = _mods.FirstOrDefault(m => m.Name == kvp.Key);
                                if (otherMod != null)
                                {
                                    await DisableModAsync(otherMod);
                                    Log($"禁用其他模组：{otherMod.Name}");
                                }
                            }
                        }
                        // 继续安装（result == Yes 或禁用其他模组后）
                        Log($"用户选择继续安装 {selectedMod.Name}，选项：{result}");
                    }

                    // 复制所有 mod.json 中的 Files 到游戏目录
                    foreach (var file in modInfo!.Files)
                    {
                        string sourcePath = IOPath.Combine(modDir, file);
                        if (!File.Exists(sourcePath))
                        {
                            Log($"安装跳过：{sourcePath} 不存在");
                            continue;
                        }
                        string destPath = IOPath.Combine(_gamePath!, file);
                        Directory.CreateDirectory(IOPath.GetDirectoryName(destPath)!);
                        File.Copy(sourcePath, destPath, true);
                        Log($"安装模组：复制 {sourcePath} 到 {destPath}");
                    }

                    // 更新模组状态
                    _modStatuses[selectedMod.Name] = new ModStatus
                    {
                        Info = jsonPath,
                        Downloaded = 1,
                        Installed = 1, // 标记为已安装
                        Version = selectedMod.Version
                    };
                    SaveStatusFile();

                    File.Delete(tmp);
                    StatusText.Text = $"{selectedMod.Name} 安装完成";
                    Log($"已安装 {selectedMod.Name}，版本：{selectedMod.Version}");
                }
                finally
                {
                    progressWindow.Close();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"安装失败：{ex.Message}";
                Log($"安装 {selectedMod.Name} 失败：{ex}");
            }
        }

        // 更新模组：下载新版本并替换指定文件
        private async Task UpdateModAsync(ModInfo selectedMod)
        {
            try
            {
                StatusText.Text = $"更新 {selectedMod.Name}...";
                Log($"开始更新 {selectedMod.Name}");

                // 清理旧模组文件
                string modDir = IOPath.Combine(_filesDir, selectedMod.Name);
                string jsonPath = IOPath.Combine(_infoDir, $"{selectedMod.Name}.json");
                if (Directory.Exists(modDir))
                {
                    Directory.Delete(modDir, true);
                    Log($"删除目录：{modDir}");
                }
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    Log($"删除 JSON 文件：{jsonPath}");
                }

                string downloadUrl = selectedMod.DownloadUrl;
                string fileName = IOPath.GetFileName(new Uri(downloadUrl).LocalPath);
                string tmp = IOPath.Combine(IOPath.GetTempPath(), fileName);

                var progressWindow = new ProgressWindow($"正在更新 {selectedMod.Name} 模组");
                progressWindow.Show();

                try
                {
                    // 下载模组包
                    await using (var remote = await _httpClient.GetStreamAsync(downloadUrl))
                    await using (var local = File.Create(tmp))
                    {
                        await remote.CopyToAsync(local);
                    }
                    Log($"下载完成，临时文件：{tmp}，大小：{new FileInfo(tmp).Length} 字节");

                    // 解压 update 字段文件
                    Directory.CreateDirectory(modDir);
                    using (var zip = ZipFile.OpenRead(tmp))
                    {
                        var zipEntries = zip.Entries.Select(e => e.FullName).ToList();
                        Log($"ZIP 文件内容：{string.Join(", ", zipEntries)}");

                        // 提取 mod.json
                        var jsonEntry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("mod.json", StringComparison.OrdinalIgnoreCase));
                        if (jsonEntry == null)
                        {
                            throw new Exception("压缩包中未找到 mod.json 文件");
                        }
                        Log($"找到 JSON 文件：{jsonEntry.FullName}");

                        using (var stream = jsonEntry.Open())
                        using (var file = File.Create(jsonPath))
                        {
                            await stream.CopyToAsync(file);
                        }
                        Log($"复制 JSON 文件到：{jsonPath}");

                        // 读取 mod.json 的 update 字段
                        string jsonContent = File.ReadAllText(jsonPath);
                        Log($"JSON 文件内容：{jsonContent}");
                        var modInfo = JsonSerializer.Deserialize<ModFilesInfo>(jsonContent);
                        if (modInfo?.Files == null)
                        {
                            throw new Exception("无效的 mod.json 文件，缺少 files 数组");
                        }
                        Log($"mod.json 中的文件列表：{string.Join(", ", modInfo.Files)}");

                        var filesToUpdate = modInfo.Update ?? modInfo.Files;
                        Log($"更新文件列表：{string.Join(", ", filesToUpdate)}");

                        // 解压 update 字段中的文件
                        foreach (var file in filesToUpdate)
                        {
                            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals(file, StringComparison.OrdinalIgnoreCase));
                            if (entry == null)
                            {
                                Log($"更新文件中缺少：{file}");
                                continue;
                            }
                            string destPath = IOPath.Combine(modDir, entry.FullName);
                            string parentDir = IOPath.GetDirectoryName(destPath)!;
                            Directory.CreateDirectory(parentDir);
                            Log($"解压更新文件：{entry.FullName} 到 {destPath}");
                            entry.ExtractToFile(destPath, true);
                        }

                        // 如果模组已启用，更新游戏目录文件
                        if (_modStatuses[selectedMod.Name].Installed == 1)
                        {
                            foreach (var file in filesToUpdate)
                            {
                                string sourcePath = IOPath.Combine(modDir, file);
                                if (!File.Exists(sourcePath))
                                {
                                    Log($"游戏目录更新跳过：{sourcePath} 不存在");
                                    continue;
                                }
                                string destPath = IOPath.Combine(_gamePath!, file);
                                Directory.CreateDirectory(IOPath.GetDirectoryName(destPath)!);
                                File.Copy(sourcePath, destPath, true);
                                Log($"更新游戏目录：复制 {sourcePath} 到 {destPath}");
                            }
                        }

                        // 更新模组状态
                        _modStatuses[selectedMod.Name].Info = jsonPath;
                        _modStatuses[selectedMod.Name].Downloaded = 1;
                        _modStatuses[selectedMod.Name].Version = selectedMod.Version;
                        SaveStatusFile();
                    }

                    File.Delete(tmp);
                    StatusText.Text = $"{selectedMod.Name} 更新完成";
                    Log($"已更新 {selectedMod.Name} 到版本 {selectedMod.Version}");
                }
                finally
                {
                    progressWindow.Close();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"更新失败：{ex.Message}";
                Log($"更新 {selectedMod.Name} 失败：{ex}");
            }
        }

        // 启用模组：将 BepInEx/plugins/ 文件复制到游戏目录
        private async Task EnableModAsync(ModInfo mod)
        {
            // 检查是否允许多模组共存
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
                // 读取模组信息
                string jsonPath = _modStatuses[mod.Name].Info;
                if (!File.Exists(jsonPath))
                {
                    throw new Exception("模组信息文件不存在");
                }

                string json = File.ReadAllText(jsonPath);
                Log($"读取 JSON 文件内容：{json}");
                var modInfo = JsonSerializer.Deserialize<ModFilesInfo>(json);
                if (modInfo?.Files == null)
                {
                    throw new Exception("无效的模组信息文件");
                }

                // 复制 BepInEx/plugins/ 文件到游戏目录
                string sourceDir = IOPath.Combine(_filesDir, mod.Name);
                foreach (var file in modInfo.Files.Where(f => f.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase)))
                {
                    string sourcePath = IOPath.Combine(sourceDir, file);
                    string destPath = IOPath.Combine(_gamePath!, file);
                    Directory.CreateDirectory(IOPath.GetDirectoryName(destPath)!);
                    File.Copy(sourcePath, destPath, true);
                    Log($"启用模组：复制 {sourcePath} 到 {destPath}");
                }

                // 更新状态
                _modStatuses[mod.Name].Installed = 1;
                SaveStatusFile();
                StatusText.Text = $"{mod.Name} 已启用";
                Log($"已启用 {mod.Name}");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"启用失败：{ex.Message}";
                Log($"启用 {mod.Name} 失败：{ex}");
            }
            finally
            {
                progressWindow.Close();
                UpdateModStatus(mod);
                await Dispatcher.InvokeAsync(() => ModListBox.Items.Refresh());
                UpdateButtonStates(mod);
            }
        }

        // 禁用模组：删除游戏目录中的 BepInEx/plugins/ 文件
        private async Task DisableModAsync(ModInfo mod)
        {
            var progressWindow = new ProgressWindow($"正在禁用 {mod.Name} 模组");
            progressWindow.Show();

            try
            {
                // 读取模组信息
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

                // 删除 BepInEx/plugins/ 文件
                foreach (var file in modInfo.Files.Where(f => f.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase)))
                {
                    string filePath = IOPath.Combine(_gamePath!, file);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Log($"禁用模组：删除 {filePath}");
                    }
                }

                // 更新状态
                _modStatuses[mod.Name].Installed = 0;
                SaveStatusFile();
                StatusText.Text = $"{mod.Name} 已禁用";
                Log($"已禁用 {mod.Name}");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"禁用失败：{ex.Message}";
                Log($"禁用 {mod.Name} 失败：{ex}");
            }
            finally
            {
                progressWindow.Close();
                UpdateModStatus(mod);
                await Dispatcher.InvokeAsync(() => ModListBox.Items.Refresh());
                UpdateButtonStates(mod);
            }
        }

        // 辅助类：模组信息
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
            private bool _hasUpdate;

            [JsonPropertyName("name")] public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
            [JsonPropertyName("version")] public string Version { get => _version; set { _version = value; OnPropertyChanged(); } }
            [JsonPropertyName("description")] public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
            [JsonPropertyName("path")] public string DownloadUrl { get => _downloadUrl; set { _downloadUrl = value; OnPropertyChanged(); } }
            [JsonIgnore] public bool IsDownloaded { get => _isDownloaded; set { _isDownloaded = value; OnPropertyChanged(); } }
            [JsonIgnore] public bool IsInstalled { get => _isInstalled; set { _isInstalled = value; OnPropertyChanged(); } }
            [JsonIgnore] public string InstallState { get => _installState; set { _installState = value; OnPropertyChanged(); } }
            [JsonIgnore] public string InfoPath { get => _infoPath; set { _infoPath = value; OnPropertyChanged(); } }
            [JsonIgnore] public bool HasUpdate { get => _hasUpdate; set { _hasUpdate = value; OnPropertyChanged(); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            public void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        // 辅助类：模组状态
        public class ModStatus
        {
            public string Info { get; set; } = "";
            public int Downloaded { get; set; }
            public int Installed { get; set; }
            public string Version { get; set; } = "";
        }

        // 辅助类：模组文件信息
        public class ModFilesInfo
        {
            [JsonPropertyName("files")]
            public string[]? Files { get; set; }

            [JsonPropertyName("update")]
            public string[]? Update { get; set; }
        }

        // 辅助类：自定义消息框按钮
        public class CustomMessageBoxButton
        {
            public string Content { get; set; } = "";
            public MessageBoxResult Result { get; set; }
        }

        // 显示自定义消息框
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