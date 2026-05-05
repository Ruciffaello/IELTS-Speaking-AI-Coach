using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ConsoleApp1
{
    /// <summary>
    /// 遠端 AI 服務：透過 HTTP API 連接到本地執行的 Ollama 伺服器。
    /// 如果你電腦記憶體不足 (小於 16GB)，可以使用這個服務來減輕負擔。
    /// </summary>
    public class OllamaService : IAiService
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string OllamaUrl = "http://localhost:11434/api/chat";

        // 用於存放對話歷史，讓 AI 有記憶
        private readonly List<object> _conversationHistory = new List<object>();

        private string _currentSystemPrompt = "You are an expert IELTS examiner.";

        public OllamaService()
        {
            ResetToDefaultSystemPrompt();
        }

        private void ResetToDefaultSystemPrompt()
        {
            _currentSystemPrompt = "You are an expert IELTS examiner. Give feedback on grammar and pronunciation.";
            ClearContext();
        }

        public void SetSystemPrompt(string systemPrompt)
        {
            _currentSystemPrompt = systemPrompt;
            ClearContext();
        }

        public void ClearContext()
        {
            _conversationHistory.Clear();
            _conversationHistory.Add(new { role = "system", content = _currentSystemPrompt });
        }

        /// <summary>
        /// 透過 HTTP POST 請求向 Ollama 索取答案
        /// </summary>
        public async IAsyncEnumerable<string> GetStreamingResponseAsync(string prompt)
        {
            // 將使用者輸入加入歷史
            _conversationHistory.Add(new { role = "user", content = prompt });

            var requestBody = new
            {
                model = "gemma3:4b", // 指定模型名稱，需先執行 ollama pull gemma3:4b
                messages = _conversationHistory,
                stream = true // 開啟串流模式
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, OllamaUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            // 發送請求並準備讀取回應流
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            StringBuilder fullResponse = new StringBuilder();

            string? line;
            // Ollama 在串流模式下會一行一行回傳 JSON
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string? contentToYield = null;

                try
                {
                    using var json = JsonDocument.Parse(line);
                    if (json.RootElement.TryGetProperty("message", out var messageProp) &&
                        messageProp.TryGetProperty("content", out var contentProp))
                    {
                        contentToYield = contentProp.GetString() ?? "";
                    }
                }
                catch (JsonException) { continue; }

                if (contentToYield != null)
                {
                    fullResponse.Append(contentToYield);
                    yield return contentToYield;
                }
            }

            // 最後把 AI 的完整回覆存回歷史，以便下一輪對話使用
            _conversationHistory.Add(new { role = "assistant", content = fullResponse.ToString() });
            
            // 限制歷史長度
            if (_conversationHistory.Count > 11) 
            {
                _conversationHistory.RemoveRange(1, 2);
            }
        }
    }
}
