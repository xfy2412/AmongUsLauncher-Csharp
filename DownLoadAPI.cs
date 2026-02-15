using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AULGK
{
    internal class DownLoadAPI
    {
        private readonly HttpClient _httpClient = new();
        private readonly string _apipath = "https://aul.xfyweb.cn/api/download?";

        // 方法名建议遵循 PascalCase 规范
        public async Task<string> GetDownloadUrl(string type, string name, string version)
        {
            try
            {
                string apiUrl = $"{_apipath}type={type}&name={name}&version={version}";
                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                using JsonDocument document = JsonDocument.Parse(responseBody);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("url", out JsonElement urlElement))
                {
                    return urlElement.GetString();
                }

                throw new Exception("JSON 响应中未找到 'url' 字段");
            }
            catch (Exception ex)
            {
                throw new Exception($"获取下载链接失败: {ex.Message}");
            }
        }

        public async Task<string> GetDownloadUrlNoName(string type, string version)
        {
            try
            {
                string apiUrl = $"{_apipath}type={type}&version={version}";
                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                using JsonDocument document = JsonDocument.Parse(responseBody);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("url", out JsonElement urlElement))
                {
                    return urlElement.GetString();
                }

                throw new Exception("JSON 响应中未找到 'url' 字段");
            }
            catch (Exception ex)
            {
                throw new Exception($"获取下载链接失败: {ex.Message}");
            }
        }
    }
}