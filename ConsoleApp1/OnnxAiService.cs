using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace ConsoleApp1
{
    /// <summary>
    /// AI 大腦服務：擔任「老師」角色。負責載入 ONNX 模型、處理對話歷史與產生回覆。
    /// </summary>
    public class OnnxAiService : IAiService, IDisposable
    {
        private Model? _model;
        private Tokenizer? _tokenizer;
        private string _currentSystemPrompt = "You are an expert IELTS examiner.";
        // 對話歷史列表，格式為 (角色, 內容)
        private readonly List<(string role, string content)> _history = new();

        public OnnxAiService(string modelPath)
        {
            try
            {
                Console.WriteLine($"[系統]: 正在載入 Llama-3.2 3B 模型 ({modelPath})...");
                // 載入 ONNX 模型與對應的 Tokenizer
                _model = new Model(modelPath);
                _tokenizer = new Tokenizer(_model);
                ClearContext();
                
                // 模型預熱 (Warm-up)：第一次推理通常較慢，先跑一次小任務讓 CPU 熱身
                WarmUp();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[錯誤]: 無法載入 3B 模型。請確認路徑與檔案格式。");
                Console.WriteLine($"詳細訊息: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// 預熱方法：執行一次微小的生成任務
        /// </summary>
        private void WarmUp()
        {
            try {
                if (_tokenizer == null || _model == null) return;
                using var tokens = _tokenizer.Encode("<|begin_of_text|>Hi");
                using var generatorParams = new GeneratorParams(_model);
                generatorParams.SetSearchOption("max_length", 10);
                generatorParams.SetInputSequences(tokens);
                using var generator = new Generator(_model, generatorParams);
                generator.ComputeLogits();
                generator.GenerateNextToken();
            } catch { /* 忽略預熱階段的錯誤 */ }
        }

        /// <summary>
        /// 設定系統指令 (決定 AI 的個性與規則)
        /// </summary>
        public void SetSystemPrompt(string systemPrompt)
        {
            _currentSystemPrompt = systemPrompt;
            ClearContext(); // 切換模式時，清空對話歷史
        }

        /// <summary>
        /// 清空對話歷史，重置為只有 System Prompt
        /// </summary>
        public void ClearContext()
        {
            _history.Clear();
            _history.Add(("system", _currentSystemPrompt));
        }

        /// <summary>
        /// 獲取 AI 的非同步流式回覆
        /// </summary>
        public async IAsyncEnumerable<string> GetStreamingResponseAsync(string prompt)
        {
            if (_model == null || _tokenizer == null)
            {
                yield return "錯誤: AI 模型未正確載入。";
                yield break;
            }

            // 將使用者輸入加入歷史
            _history.Add(("user", prompt));
            
            // 構建 Llama 3 專用的對話模板 (Chat Template)
            string fullPrompt = BuildLlama3Prompt();

            using var tokens = _tokenizer.Encode(fullPrompt);
            using var generatorParams = new GeneratorParams(_model);
            
            // --- 效能與記憶體優化 (針對 16GB RAM) ---
            // 限制最大字數，避免 KV Cache 佔用過多記憶體
            generatorParams.SetSearchOption("max_length", 300); 
            // 開啟記憶體共享緩存，減少重複申請記憶體的開銷
            generatorParams.SetSearchOption("past_present_share_buffer", true); 
            // 關閉隨機抽樣 (Greedy Search)，速度最快且回覆最穩定
            generatorParams.SetSearchOption("do_sample", false); 
            // ------------------------------------------

            generatorParams.SetInputSequences(tokens);
            using var generator = new Generator(_model, generatorParams);
            StringBuilder fullResponse = new StringBuilder();

            // 進入生成迴圈
            while (!generator.IsDone())
            {
                // Task.Yield 讓出執行權，避免同步迴圈完全卡死 UI 執行緒
                await Task.Yield(); 
                
                generator.ComputeLogits();
                generator.GenerateNextToken();

                // 獲取最後一個生成的 Token 並解碼成文字
                var nextToken = generator.GetSequence(0)[^1];
                var word = _tokenizer.Decode(new[] { nextToken });

                if (!string.IsNullOrEmpty(word))
                {
                    fullResponse.Append(word);
                    yield return word;
                }
            }

            // 將 AI 的完整回覆存入歷史
            _history.Add(("assistant", fullResponse.ToString()));

            // 嚴格控制歷史長度：保留最近 2 輪對話
            // 3B 模型較吃運算，歷史越長，CPU 負擔越重
            if (_history.Count > 5) 
            {
                _history.RemoveRange(1, 2);
            }
        }

        /// <summary>
        /// 構建 Llama 3 專用的 Prompt 格式 (<|start_header_id|> 等標籤)
        /// </summary>
        private string BuildLlama3Prompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<|begin_of_text|>");
            foreach (var msg in _history)
            {
                sb.Append($"<|start_header_id|>{msg.role}<|end_header_id|>\n\n{msg.content}<|eot_id|>");
            }
            sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
            return sb.ToString();
        }

        /// <summary>
        /// 釋放模型與 Tokenizer 佔用的記憶體
        /// </summary>
        public void Dispose()
        {
            _tokenizer?.Dispose();
            _model?.Dispose();
        }
    }
}
