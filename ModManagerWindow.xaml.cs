using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Windows;
using IOPath = System.IO.Path;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Linq;

namespace AULGK
{
    public partial class ModManagerWindow : Window
    {
        private readonly string? _gamePath;
        private readonly HttpClient _httpClient;
        private readonly string _modsUrl = "https://gh-proxy.com/github.com/Yar1991-Translation/BeplnEx/blob/main/mods.json"; // 模组列表
        private string _pluginsDir = "";
        private readonly ObservableCollection<ModInfo> _mods = new();
        public ModInfo? SelectedMod { get; set; }
        private readonly string? _gitHubToken;

        public ModManagerWindow(string? gamePath, HttpClient client, string? gitHubToken)
        {
            InitializeComponent();
            _gamePath = gamePath;
            _httpClient = client;
            _gitHubToken = gitHubToken;
            if (_gamePath != null)
            {
                _pluginsDir = IOPath.Combine(_gamePath, "BepInEx", "plugins");
                Directory.CreateDirectory(_pluginsDir);
            }
            ModListBox.ItemsSource = _mods;
            _ = LoadModsAsync();
        }

        private async Task LoadModsAsync()
        {
            _mods.Clear();
            StatusText.Text = "正在获取模组列表...";
            try
            {
                var json = await _httpClient.GetStringAsync(_modsUrl);
                var list = JsonSerializer.Deserialize<List<ModInfo>>(json) ?? new();
                foreach (var m in list)
                {
                    await UpdateModFromGitHubAsync(m);
                    CheckInstallState(m);
                    _mods.Add(m);
                }
                StatusText.Text = $"已加载 {_mods.Count} 个模组";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"获取列表失败：{ex.Message}";
            }
        }

        private void CheckInstallState(ModInfo mod)
        {
            if (string.IsNullOrEmpty(_pluginsDir)) return;
            string keyword = string.IsNullOrEmpty(mod.FileMatch) ? mod.Name : mod.FileMatch;

            bool Match(string path)
                => IOPath.GetFileName(path).Contains(keyword, StringComparison.OrdinalIgnoreCase);

            var matchedPath = Directory.EnumerateFiles(_pluginsDir, "*.dll*", SearchOption.AllDirectories)
                                        .FirstOrDefault(Match);

            if (matchedPath == null)
            {
                matchedPath = Directory.EnumerateFiles(_pluginsDir, "*.dll.disabled", SearchOption.AllDirectories)
                                        .FirstOrDefault(Match);
            }

            bool isInstalled = matchedPath != null;
            bool isEnabled = matchedPath != null && !matchedPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

            mod.IsInstalled = isInstalled;
            mod.IsEnabled = isEnabled;

            if (isInstalled)
            {
                mod.InstallState = mod.IsEnabled ? "✅ 已启用" : "☑️ 已禁用";
            }
            else
            {
                mod.InstallState = "➖ 未安装";
            }
        }

        private void ModListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selectedMod = ModListBox.SelectedItem as ModInfo;
            if (selectedMod != null)
            {
                DetailHint.Visibility = Visibility.Collapsed;
                DetailContent.Visibility = Visibility.Visible;
                DetailPanel.DataContext = selectedMod; // Set DataContext for the whole panel
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
            InstallButton.IsEnabled = true;
            ToggleButton.IsEnabled = mod.IsInstalled;
            UninstallButton.IsEnabled = mod.IsInstalled;
            OpenFolderButton.IsEnabled = Directory.Exists(_pluginsDir);
            ToggleButton.Content = mod.IsEnabled ? "🔌 禁用" : "🔌 启用";
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo mod) return;
            if (string.IsNullOrEmpty(mod.DownloadUrl))
            {
                await UpdateModFromGitHubAsync(mod);
                if (string.IsNullOrEmpty(mod.DownloadUrl))
                {
                    MessageBox.Show("未找到可下载的版本文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            await InstallModAsync(mod);
            CheckInstallState(mod);
            UpdateButtonStates(mod);
        }

        private async Task InstallModAsync(ModInfo mod)
        {
            try
            {
                StatusText.Text = $"下载 {mod.Name}...";
                string fileName = IOPath.GetFileName(new Uri(mod.DownloadUrl).LocalPath);
                string tmp = IOPath.Combine(IOPath.GetTempPath(), fileName);
                await using (var remote = await _httpClient.GetStreamAsync(mod.DownloadUrl))
                await using (var local = File.Create(tmp))
                {
                    await remote.CopyToAsync(local);
                }

                string ext = IOPath.GetExtension(fileName).ToLower();
                if (ext == ".zip")
                {
                    ZipFile.ExtractToDirectory(tmp, _pluginsDir, true);
                }
                else if (ext == ".dll")
                {
                    var dest = IOPath.Combine(_pluginsDir, mod.FileName);
                    File.Copy(tmp, dest, true);
                }

                File.Delete(tmp);
                StatusText.Text = $"已安装 {mod.Name}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"安装失败：{ex.Message}";
            }
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo mod) return;
            string dll = IOPath.Combine(_pluginsDir, mod.FileName);
            string disabled = dll + ".disabled";
            try
            {
                if (mod.IsEnabled)
                {
                    File.Move(dll, disabled, true);
                }
                else if (File.Exists(disabled))
                {
                    File.Move(disabled, dll, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"切换失败：{ex.Message}");
            }
            finally
            {
                CheckInstallState(mod);
                UpdateButtonStates(mod);
            }
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.DataContext is not ModInfo mod) return;

            string dllPath = IOPath.Combine(_pluginsDir, mod.FileName);
            string disabledPath = dllPath + ".disabled";

            try
            {
                if (File.Exists(dllPath)) File.Delete(dllPath);
                if (File.Exists(disabledPath)) File.Delete(disabledPath);
                StatusText.Text = $"{mod.Name} 已卸载。";
            }
            catch (Exception ex)
            {
                 StatusText.Text = $"卸载失败: {ex.Message}";
            }
            finally
            {
                CheckInstallState(mod);
                ModListBox.Items.Refresh(); // To update the state in the list
                UpdateButtonStates(mod);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadModsAsync();
        }

        private async Task UpdateModFromGitHubAsync(ModInfo mod)
        {
            if (string.IsNullOrEmpty(mod.Repo)) return;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{mod.Repo}/releases/latest");
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AULGK", "1.0"));
                if (!string.IsNullOrWhiteSpace(_gitHubToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("token", _gitHubToken);
                var resp = await _httpClient.SendAsync(request);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                mod.Version = root.GetProperty("tag_name").GetString() ?? mod.Version;

                var assets = root.GetProperty("assets");
                string? dllUrl = null, zipUrl = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        dllUrl = "https://ghproxy.com/" + url;
                        break; // 优先 DLL
                    }
                    if (zipUrl == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = "https://ghproxy.com/" + url;
                    }
                }
                mod.DownloadUrl = dllUrl ?? zipUrl ?? mod.DownloadUrl;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"读取 {mod.Name} GitHub 信息失败：{ex.Message}";
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_pluginsDir))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", _pluginsDir) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"无法打开目录: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public class ModInfo : INotifyPropertyChanged
        {
            private string _name = "";
            private string _version = "";
            private string _description = "";
            private string _downloadUrl = "";
            private string _installState = "";

            [JsonPropertyName("name")] public string Name { get => _name; set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileName)); } }
            [JsonPropertyName("version")] public string Version { get => _version; set { _version = value; OnPropertyChanged(); } }
            [JsonPropertyName("description")] public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
            [JsonPropertyName("downloadUrl")] public string DownloadUrl { get => _downloadUrl; set { _downloadUrl = value; OnPropertyChanged(); } } // 可为空，将由 GitHub API 填充
            [JsonPropertyName("repo")] public string Repo { get; set; } = ""; // owner/repo 格式
            [JsonPropertyName("fileMatch")] public string FileMatch { get; set; } = ""; // dll 文件名匹配关键字，可选
            private bool _isInstalled;
            public bool IsInstalled { get => _isInstalled; set { _isInstalled = value; OnPropertyChanged(); } }
            private bool _isEnabled;
            public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
            public string InstallState { get => _installState; set { _installState = value; OnPropertyChanged(); } }
            public string FileName => Name + ".dll";

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
} 