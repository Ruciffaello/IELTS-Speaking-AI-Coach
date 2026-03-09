using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ConsoleApp1
{
    public class OllamaService
    {
        private readonly HttpClient _http = new HttpClient();
        //private const string OllamaUrl = "http://localhost:11434/api/generate";

        private const string OllamaUrl = "http://localhost:11434/api/chat";

        public async IAsyncEnumerable<string> GetStreamingResponse(string prompt)
        {
            //var requestBody = new
            //{
            //    model = "gemma3:4b", // 請確保你已用 ollama pull llama3
            //    prompt = prompt,
            //    stream = true
            //};

            // 建議的 System Prompt
            var systemPrompt = "You are an expert IELTS examiner. Your goal is to conduct a speaking simulation. " +
                               "1. Evaluate my sentences for grammar, vocabulary, and coherence. " +
                               "2. Since I am using speech-to-text, if you notice any unusual word combinations that might be pronunciation errors, please point them out and suggest the correct pronunciation.";

            var requestBody = new
            {
                model = "gemma3:4b",
                messages = new[]
                {
                    // 這裡放 System Role，定義模型的人格或規則
                    new { role = "system", content = systemPrompt },
        
                    // 這裡放 User Role，也就是你原本的 prompt 變數
                    new { role = "user", content = prompt }
                },
                stream = true // 保持串流模式
            };



            var jsonPayload = JsonSerializer.Serialize(requestBody);

            // 建立 HttpRequestMessage 以便精細控制
            var request = new HttpRequestMessage(HttpMethod.Post, OllamaUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            // 關鍵修正：使用 SendAsync 並傳入 HttpCompletionOption.ResponseHeadersRead
            // 這樣程式才不會等整個回覆抓完才繼續，能達成真正的串流
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            // 在 OllamaService.cs 內修改
            while (!reader.EndOfStream)
            {
                string line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                string textToYield = null;

                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;

                    // 針對新的格式：message -> content
                    if (root.TryGetProperty("message", out var messageProp) &&
                        messageProp.TryGetProperty("content", out var contentProp))
                    {
                        textToYield = contentProp.GetString();
                    }
                    // 相容舊格式 (預防萬一)
                    else if (root.TryGetProperty("response", out var respProp))
                    {
                        textToYield = respProp.GetString();
                    }
                }
                catch (JsonException)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(textToYield))
                {
                    yield return textToYield;
                }
            }
        }
    }
}
