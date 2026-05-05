using NAudio.Wave;
using System.Text;
using System.Text.Json;
using Vosk;

namespace ConsoleApp1
{
    /// <summary>
    /// 程式主進入點：擔任「導演」角色，控制練習流程、錄音計時與選單。
    /// </summary>
    internal class Program
    {
        private static bool _isAiSpeaking = false; // 標記 AI 是否正在說話，防止它「自己聽到自己說話」
        private static int _currentQuestionIndex = 0; // 目前進展到第幾個問題 (Part 1 專用)
        private static List<string> _currentQuestions = new(); // 目前選中的問題清單

        // --- 語音緩衝與停頓處理 ---
        // 語音辨識 STT 可能會分段傳回文字（例如：先傳 "I like", 再傳 "apples"）。
        // 我們需要一個緩衝區把這些字接起來，並且設定一個 1.5 秒的「冷卻時間」，
        // 確定你超過 1.5 秒沒說話了，AI 才會開始回覆。
        private static string _speechAccumulator = ""; 
        private static CancellationTokenSource? _speechCts; 

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== 雅思口說練習助手 (IELTS Speaking Assistant) ===");

            // 1. 設定模型檔案路徑
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string voskModelPath;
            string piperExePath;
            string piperModelPath;
            string llmModelPath;

            // 這裡的邏輯是為了方便開發人員 (DEBUG) 與使用者 (RELEASE)
#if DEBUG
            voskModelPath = @"D:\models\vosk-model-small-en-us-0.15";
            piperExePath = @"D:\piper\piper.exe";
            piperModelPath = @"D:\piper\models\en_GB-northern_english_male-medium.onnx";
            llmModelPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Llama-3.2-3B-Instruct-awq-uint4-float16-cpu-onnx"));
            Console.WriteLine($"[DEBUG]: 正在載入 3B 模型: {llmModelPath}");
#else
            // Release 模式下，預設檔案應該放在 resources 資料夾內
            string relLlmPath = Path.Combine(baseDir, "resources", "llm");
            voskModelPath = Path.Combine(baseDir, "resources", "vosk-model-small-en-us-0.15");
            piperExePath = Path.Combine(baseDir, "resources", "piper", "piper.exe");
            piperModelPath = Path.Combine(baseDir, "resources", "piper", "en_GB-northern_english_male-medium.onnx");
            llmModelPath = relLlmPath;
#endif

            // 2. 載入題庫
            var questionBank = LoadQuestionBank();
            if (questionBank == null) 
            {
                Console.WriteLine("[錯誤]: 找不到 questions.json 檔案。");
                return;
            }

            // 3. 初始化 AI 與語音服務
            if (!Directory.Exists(voskModelPath) || !File.Exists(piperExePath) || !Directory.Exists(llmModelPath))
            {
                Console.WriteLine("\n[錯誤]: 找不到必要的 AI 模型或工具檔案。請參閱 README.md 檢查 resources 資料夾。");
                return;
            }

            using var aiBrain = new OnnxAiService(llmModelPath); // 啟動本地 AI 大腦 (Llama)
            var piper = new PiperService(piperExePath, piperModelPath); // 啟動語音合成 (TTS)

            // 4. 主選單循環
            bool exitProgram = false;
            while (!exitProgram)
            {
                Console.Clear();
                Console.WriteLine("=== 雅思口說練習助手 (IELTS Speaking Assistant) ===");
                Console.WriteLine("\n請選擇練習模式:");
                Console.WriteLine("1. Part 1 (日常問答 - AI 會主動追問)");
                Console.WriteLine("2. Part 2 (個人獨白 - 你有 1 分鐘準備，2 分鐘說話)");
                Console.WriteLine("3. Part 3 (深度對話 - 針對 Part 2 的主題深入探討)");
                Console.WriteLine("4. 退出程式");
                Console.Write("\n請輸入選擇 (1-4): ");
                
                string? modeChoice = Console.ReadLine();
                if (modeChoice == "4") { exitProgram = true; continue; }

                string systemPrompt = ""; // AI 的角色定義
                string initialMessage = ""; // AI 開場白

                // 模式 1: Part 1
                if (modeChoice == "1")
                {
                    var p1 = PickPart1(questionBank);
                    _currentQuestions = p1.Questions;
                    _currentQuestionIndex = 0;
                    systemPrompt = "You are an expert IELTS examiner for a SPEAKING test. \n" +
                                   "Note: Input is from an STT engine (ignore typos). \n" +
                                   "Rules: 1. Be concise. 2. Give 1 sentence feedback. 3. Ask the next question.";
                    initialMessage = $"Let's start Part 1. The topic is '{p1.Topic}'. " + _currentQuestions[0];
                }
                // 模式 2: Part 2
                else if (modeChoice == "2")
                {
                    var p2 = PickPart2(questionBank);
                    systemPrompt = "You are an expert IELTS examiner. Evaluate the student's long turn talk.";
                    string intro = $"Part 2: {p2.Description}. You should say: {string.Join(", ", p2.Prompts)}. You have 1 minute to prepare.";
                    Console.WriteLine($"\n[AI]: {intro}");
                    await piper.SpeakAsync(intro, 0.95f);
                    await RunTimer(60); // 準備倒數 60 秒
                    initialMessage = "Preparation time is up. Please start speaking now.";
                }
                // 模式 3: Part 3
                else if (modeChoice == "3")
                {
                    var p3 = PickPart2(questionBank); // Part 3 通常與 Part 2 主題相關
                    systemPrompt = "You are an expert IELTS examiner. Challenge the student's ideas with deep questions.";
                    initialMessage = $"Now let's discuss {p3.Topic} in depth. {p3.Part3Questions[0]}";
                }
                else { continue; }

                // 設定 AI 行為並開始對話
                aiBrain.SetSystemPrompt(systemPrompt);
                Console.WriteLine($"\n--- 練習開始 (按任意鍵結束並回到選單) ---");
                Console.WriteLine($"[AI]: {initialMessage}");
                await piper.SpeakAsync(initialMessage, 0.95f);

                // 5. 初始化語音辨識 (STT)
                using var model = new Model(voskModelPath); // 載入 Vosk 模型
                using var rec = new VoskRecognizer(model, 16000); // 辨識器，16kHz 採樣率
                using var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 800 };

                StringBuilder part2Accumulator = new StringBuilder();
                _speechAccumulator = ""; // 重置

                // 事件：當麥克風收到聲音資料時
                waveIn.DataAvailable += (sender, e) =>
                {
                    if (_isAiSpeaking) return; // 如果 AI 正在說話，就不要聽

                    // 將音訊送入辨識器
                    if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        var userText = ParseVoskText(rec.Result()); // 取得辨識結果
                        if (!string.IsNullOrWhiteSpace(userText))
                        {
                            if (modeChoice == "2")
                            {
                                // Part 2 是獨白，我們只負責記錄，等按鍵結束才一次性講評
                                Console.WriteLine($"\n[You]: {userText}");
                                part2Accumulator.Append(userText + " ");
                            }
                            else
                            {
                                // Part 1 & 3 需要「停頓緩衝」：
                                // 你說一段話後，我們等待 1.5 秒。如果你繼續說，計時重啟；
                                // 如果 1.5 秒沒聲音，就代表你講完了，把整段話丟給 AI。
                                _speechAccumulator += userText + " ";
                                _speechCts?.Cancel(); // 取消之前的計時
                                _speechCts = new CancellationTokenSource();
                                var token = _speechCts.Token;

                                Task.Run(async () =>
                                {
                                    try {
                                        await Task.Delay(1500, token); // 等待 1.5 秒
                                        if (!token.IsCancellationRequested)
                                        {
                                            string finalInput = _speechAccumulator.Trim();
                                            _speechAccumulator = "";
                                            Console.WriteLine($"\n[You]: {finalInput}");

                                            // 如果是 Part 1，我們可以主動告訴 AI 下一個問題是什麼，方便它引導流程。
                                            string instruction = finalInput;
                                            if (modeChoice == "1" && _currentQuestionIndex < _currentQuestions.Count - 1)
                                            {
                                                _currentQuestionIndex++;
                                                instruction += $"\n(Note to AI: After feedback, ask this question: {_currentQuestions[_currentQuestionIndex]})";
                                            }
                                            
                                            // 呼叫 AI 處理對話
                                            await ProcessConversation(instruction, aiBrain, piper);
                                        }
                                    } catch (TaskCanceledException) { }
                                });
                            }
                        }
                    }
                };

                // 開始錄音
                waveIn.StartRecording();
                Console.ReadKey(true); // 等待使用者按任意鍵結束練習
                waveIn.StopRecording();

                // 如果是 Part 2，在結束後給予講評
                if (modeChoice == "2")
                {
                    string finalSpeech = part2Accumulator.ToString().Trim();
                    if (!string.IsNullOrEmpty(finalSpeech))
                    {
                        await ProcessConversation(finalSpeech + "\n(Provide your evaluation now.)", aiBrain, piper);
                    }
                }

                Console.WriteLine("\n練習結束。1 秒後返回選單...");
                await Task.Delay(1000);
            }
        }

        /// <summary>
        /// 處理與 AI 的互動：取得回覆、同步顯示並合成語音。
        /// </summary>
        private static async Task ProcessConversation(string userText, IAiService aiService, PiperService piper)
        {
            _isAiSpeaking = true;
            try
            {
                StringBuilder sentenceBuffer = new StringBuilder();
                Console.Write("[AI]: ");

                // 串流獲取 AI 回覆 (Stream)
                await foreach (var chunk in aiService.GetStreamingResponseAsync(userText))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    Console.Write(chunk); // 螢幕上即時顯示文字
                    sentenceBuffer.Append(chunk);

                    // 句子偵測：為了讓語音合成更自然，我們不要一個字一個字讀，
                    // 而是等 AI 說完一小句（有標點符號時）就開始讀那一句。
                    if (IsEndOfSentence(chunk))
                    {
                        string sentence = CleanTextForSpeech(sentenceBuffer.ToString());
                        if (sentence.Length > 2)
                        {
                            await piper.SpeakAsync(sentence, 0.95f); // 讀出這一句
                            sentenceBuffer.Clear();
                        }
                    }
                }

                // 處理最後剩下的殘留文字
                if (sentenceBuffer.Length > 0)
                {
                    string final = CleanTextForSpeech(sentenceBuffer.ToString());
                    if (final.Length > 0) await piper.SpeakAsync(final, 0.95f);
                }
                Console.WriteLine();
            }
            finally { _isAiSpeaking = false; }
        }

        /// <summary>
        /// 倒數計時器
        /// </summary>
        private static async Task RunTimer(int seconds)
        {
            for (int i = seconds; i > 0; i--)
            {
                if (Console.KeyAvailable) { Console.ReadKey(true); break; }
                Console.Write($"\r剩餘時間: {i}秒 (按任意鍵立即開始回答)   ");
                await Task.Delay(1000);
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 清理文字中的 Markdown 符號 (如 * 或 #)，避免 TTS 把它們讀出來。
        /// </summary>
        private static string CleanTextForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("*", "").Replace("#", "").Trim();
        }

        /// <summary>
        /// 判斷是否為句子的結尾（標點符號）
        /// </summary>
        private static bool IsEndOfSentence(string text) => text.Any(c => ".?!。？！\n".Contains(c));

        /// <summary>
        /// 從 questions.json 讀取題庫
        /// </summary>
        private static QuestionBank? LoadQuestionBank()
        {
            try { return JsonSerializer.Deserialize<QuestionBank>(File.ReadAllText("questions.json")); }
            catch { return null; }
        }

        // 隨機抽選題目
        private static Part1Topic PickPart1(QuestionBank bank) => bank.Part1[new Random().Next(bank.Part1.Count)];
        private static Part2Topic PickPart2(QuestionBank bank) => bank.Part2[new Random().Next(bank.Part2.Count)];
        
        /// <summary>
        /// 解析 Vosk 傳回的 JSON 結果，提取文字內容。
        /// </summary>
        private static string ParseVoskText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }
    }
}
