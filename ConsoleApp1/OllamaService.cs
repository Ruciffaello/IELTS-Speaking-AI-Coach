using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ConsoleApp1
{
    public class OllamaService : IAiService
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string OllamaUrl = "http://localhost:11434/api/chat";

        // 用於存放對話歷史，讓 AI 有記憶
        private readonly List<object> _conversationHistory = new List<object>();

        private string _currentSystemPrompt = "You are an expert IELTS examiner. Your goal is to conduct a speaking simulation.";

        public OllamaService()
        {
            ResetToDefaultSystemPrompt();
        }

        private void ResetToDefaultSystemPrompt()
        {
            _currentSystemPrompt = "You are an expert IELTS examiner. Your goal is to conduct a speaking simulation. " +
                                  "1. Evaluate my sentences for grammar, vocabulary, and coherence. " +
                                  "2. Since I am using speech-to-text, if you notice any unusual word combinations that might be pronunciation errors, please point them out and suggest the correct pronunciation. " +
                                  "3. Keep your responses concise to maintain a natural conversation flow.";
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

        public async IAsyncEnumerable<string> GetStreamingResponseAsync(string prompt)
        {
            // 將使用者輸入加入歷史
            _conversationHistory.Add(new { role = "user", content = prompt });

            var requestBody = new
            {
                model = "gemma3:4b",
                messages = _conversationHistory,
                stream = true
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, OllamaUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            StringBuilder fullResponse = new StringBuilder();

            string? line;
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

            // 將 AI 的完整回覆存回歷史，以便下一輪對話使用
            _conversationHistory.Add(new { role = "assistant", content = fullResponse.ToString() });
            
            // 限制歷史長度，避免 Context 爆掉 (保留最近 10 輪)
            if (_conversationHistory.Count > 21) // 1 (system) + 10*2 (user+assistant)
            {
                _conversationHistory.RemoveRange(1, 2);
            }
        }
    }
}
