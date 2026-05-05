using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace ConsoleApp1
{
    /// <summary>
    /// 本地 AI 服務：使用 Microsoft.ML.OnnxRuntimeGenAI 來執行 Llama 模型。
    /// 這是程式的「大腦」，負責理解使用者的話並產生回覆。
    /// </summary>
    public class OnnxAiService : IAiService, IDisposable
    {
        private OgaHandle? _ogaHandle; // 全域句柄，負責程式結束時的正確清理
        private Model? _model; // ONNX 模型實體
        private Tokenizer? _tokenizer; // 將文字轉換為數字（Tokens）的工具
        private string _currentSystemPrompt = "You are an expert IELTS examiner.";
        
        // 記憶對話歷史：這讓 AI 知道你剛剛說了什麼，才能進行追問。
        // 格式為 (角色, 內容)，角色包括 system (系統規則), user (你), assistant (AI)。
        private readonly List<(string role, string content)> _history = new();

        public OnnxAiService(string modelPath)
        {
            try
            {
                // 0. 初始化 OGA 句柄
                _ogaHandle = new OgaHandle();

                Console.WriteLine($"[系統]: 正在載入 Llama-3.2 3B 模型 ({modelPath})...");
                
                // 1. 載入模型檔案
                _model = new Model(modelPath);
                
                // 2. 載入 Tokenizer (分詞器)
                // 電腦看不懂文字，Tokenizer 會把 "Hello" 變成像 [15043] 這樣的數字。
                _tokenizer = new Tokenizer(_model);
                
                ClearContext();
                
                // 3. 模型預熱 (Warm-up)
                // 第一次執行推理通常很慢（因為要配置記憶體），我們先隨便跑一個小任務讓它「熱身」。
                WarmUp();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[錯誤]: 無法載入模型。請確認 resources/llm 資料夾內有 model.onnx 等檔案。");
                Console.WriteLine($"詳細訊息: {ex.Message}");
                Console.ResetColor();
            }
        }

        private void WarmUp()
        {
            try {
                if (_tokenizer == null || _model == null) return;
                // 隨便餵一個單字給它跑
                using var tokens = _tokenizer.Encode("<|begin_of_text|>Hi");
                using var generatorParams = new GeneratorParams(_model);
                generatorParams.SetSearchOption("max_length", 10);
                generatorParams.SetInputSequences(tokens);
                using var generator = new Generator(_model, generatorParams);
                generator.ComputeLogits();
                generator.GenerateNextToken();
            } catch { /* 預熱失敗不影響主程式運作 */ }
        }

        public void SetSystemPrompt(string systemPrompt)
        {
            _currentSystemPrompt = systemPrompt;
            // 當規則改變時（例如從 Part 1 變到 Part 2），清空記憶重新開始。
            ClearContext();
        }

        public void ClearContext()
        {
            _history.Clear();
            // 第一條歷史永遠是 System Prompt，它決定了 AI 的行為模式。
            _history.Add(("system", _currentSystemPrompt));
        }

        /// <summary>
        /// 核心方法：將使用者的輸入轉為 AI 的串流回覆
        /// </summary>
        public async IAsyncEnumerable<string> GetStreamingResponseAsync(string prompt)
        {
            if (_model == null || _tokenizer == null)
            {
                yield return "錯誤: AI 模型未正確載入。";
                yield break;
            }

            // A. 把你的話加入歷史紀錄
            _history.Add(("user", prompt));
            
            // B. 依照 Llama 3 的格式組合所有對話
            // Llama 3 期待的格式是：<|start_header_id|>user<|end_header_id|>\n\n內容<|eot_id|>
            string fullPrompt = BuildLlama3Prompt();

            // C. 將文字轉成數字 (Tokens)
            using var tokens = _tokenizer.Encode(fullPrompt);
            
            // D. 設定生成參數 (控制 AI 怎麼說話)
            using var generatorParams = new GeneratorParams(_model);
            
            // max_length: 限制 AI 最多說多少字，防止它沒完沒了地說下去（消耗記憶體）。
            generatorParams.SetSearchOption("max_length", 300); 
            
            // past_present_share_buffer: 優化記憶體，讓它重複使用之前的計算結果。
            generatorParams.SetSearchOption("past_present_share_buffer", true); 
            
            // do_sample: 如果設為 false (Greedy Search)，AI 每次都會選機率最高的分支，回答最穩定、速度也最快。
            generatorParams.SetSearchOption("do_sample", false); 

            generatorParams.SetInputSequences(tokens);
            
            // E. 啟動生成器
            using var generator = new Generator(_model, generatorParams);
            StringBuilder fullResponse = new StringBuilder();

            // F. 進入循環，一個字一個字產出
            while (!generator.IsDone())
            {
                // yield 讓出控制權，避免這個迴圈把整個電腦卡死。
                await Task.Yield(); 
                
                // 計算下一個字的機率
                generator.ComputeLogits();
                // 決定下一個字是什麼
                generator.GenerateNextToken();

                // 取得最後產生的那個 Token (數字)
                var nextToken = generator.GetSequence(0)[^1];
                // 把數字轉回人類看得懂的文字 (例如把 15043 轉回 "Hello")
                var word = _tokenizer.Decode(new[] { nextToken });

                if (!string.IsNullOrEmpty(word))
                {
                    fullResponse.Append(word);
                    yield return word; // 即時回傳這個字
                }
            }

            // G. 生成結束後，把 AI 說的完整話語也存入歷史
            _history.Add(("assistant", fullResponse.ToString()));

            // H. 歷史長度控制：只保留最近幾次對話。
            // 如果歷史太長，AI 的運算壓力會指數級增長，導致電腦變卡。
            if (_history.Count > 5) 
            {
                _history.RemoveRange(1, 2); // 刪除最舊的一輪 (User + AI)
            }
        }

        /// <summary>
        /// 輔助方法：將歷史對話封裝成 Llama 3.2 官方規定的特殊標籤格式
        /// </summary>
        private string BuildLlama3Prompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<|begin_of_text|>");
            foreach (var msg in _history)
            {
                // Llama 3 的標籤系統：讓模型知道誰在說話，哪裡是結尾。
                sb.Append($"<|start_header_id|>{msg.role}<|end_header_id|>\n\n{msg.content}<|eot_id|>");
            }
            sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
            return sb.ToString();
        }

        public void Dispose()
        {
            // 釋放模型與分詞器。這些佔用了約 2GB 的 RAM，不用的時候一定要釋放。
            _tokenizer?.Dispose();
            _model?.Dispose();
            _ogaHandle?.Dispose(); // 必須最後釋放，通知 GenAI 引擎關閉
        }
    }
}
