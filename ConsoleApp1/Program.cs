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

            // 5. 初始化語音辨識 (STT) - 放在循環外，避免重複載入模型
            Console.WriteLine("[系統]: 正在載入 Vosk 語音辨識模型...");
            using var voskModel = new Model(voskModelPath);
            using var rec = new VoskRecognizer(voskModel, 16000);
            using var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 500 };

            StringBuilder part2Accumulator = new StringBuilder();

            // 事件：當麥克風收到聲音資料時
            waveIn.DataAvailable += (sender, e) =>
            {
                if (_isAiSpeaking) return;

                if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                {
                    var userText = ParseVoskText(rec.Result());
                    if (!string.IsNullOrWhiteSpace(userText))
                    {
                        // 這裡可以視需求決定是否即時顯示辨識結果
                        // Console.WriteLine($"[DEBUG STT]: {userText}"); 
                        
                        // 我們需要處理模式 (modeChoice) 邏輯，但這裡在事件內，
                        // 我們可以透過一個外部變數來判斷目前的模式。
                        // (為了簡化，我們先維持原本的邏輯，但要注意變數作用域)
                    }
                }
            };

            // 4. 主選單循環
            bool exitProgram = false;
            while (!exitProgram)
            {
                Console.Clear();
                Console.WriteLine("=== 雅思口說練習助手 (IELTS Speaking Assistant) ===");
                // ... (選單內容相同)
                Console.WriteLine("\n請選擇練習模式:");
                Console.WriteLine("1. Part 1 (日常問答)");
                Console.WriteLine("2. Part 2 (個人獨白)");
                Console.WriteLine("3. Part 3 (深度對話)");
                Console.WriteLine("4. 退出程式");
                Console.Write("\n請輸入選擇 (1-4): ");
                
                string? modeChoice = Console.ReadLine();
                if (modeChoice == "4") { exitProgram = true; continue; }

                string systemPrompt = "";
                string initialMessage = "";

                if (modeChoice == "1")
                {
                    var p1 = PickPart1(questionBank);
                    _currentQuestions = p1.Questions;
                    _currentQuestionIndex = 0;
                    // 更加嚴格的角色定義，要求它只給 1 句回饋 + 1 個問題
                    systemPrompt = "You are an expert IELTS Speaking examiner. \n" +
                                   "Format: [Feedback] [Next Question]. \n" +
                                   "Rules: \n" +
                                   "1. Feedback must be exactly 1 concise sentence. \n" +
                                   "2. Ask the provided question directly. \n" +
                                   "3. DO NOT add any conversational filler (e.g., 'That's interesting', 'Let's move on') unless it is the feedback itself. \n" +
                                   "4. DO NOT add any closing remarks or questions like 'What do you think?' after the question.";
                    initialMessage = $"Let's start Part 1. The topic is '{p1.Topic}'. " + _currentQuestions[0];
                }
                else if (modeChoice == "2")
                {
                    var p2 = PickPart2(questionBank);
                    systemPrompt = "You are an expert IELTS examiner. Evaluate the student's Part 2 long turn. Provide a band score (1-9) and brief suggestions.";
                    string intro = $"Part 2: {p2.Description}. You should say: {string.Join(", ", p2.Prompts)}. You have 1 minute to prepare.";
                    Console.WriteLine($"\n[AI]: {intro}");
                    _isAiSpeaking = true;
                    await piper.SpeakAsync(intro, 0.95f);
                    _isAiSpeaking = false;
                    await RunTimer(60);
                    initialMessage = "Preparation time is up. Please start speaking now.";
                }
                else if (modeChoice == "3")
                {
                    var p3 = PickPart2(questionBank);
                    systemPrompt = "You are an expert IELTS examiner for Part 3. Ask abstract and challenging questions based on the topic. Be concise.";
                    initialMessage = $"Now let's discuss {p3.Topic} in depth. {p3.Part3Questions[0]}";
                }
                else { continue; }

                aiBrain.SetSystemPrompt(systemPrompt);
                Console.WriteLine($"\n--- 練習開始 (說完話請停頓 1.5 秒，按任意鍵結束) ---");
                Console.WriteLine($"[AI]: {initialMessage}");
                
                _isAiSpeaking = true;
                await piper.SpeakAsync(initialMessage, 0.95f);
                _isAiSpeaking = false;

                // 重置狀態
                _speechAccumulator = "";
                part2Accumulator.Clear();
                
                // 重新綁定事件
                EventHandler<WaveInEventArgs> dataHandler = (sender, e) =>
                {
                    if (_isAiSpeaking) return;
                    if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        var userText = ParseVoskText(rec.Result());
                        if (!string.IsNullOrWhiteSpace(userText))
                        {
                            if (modeChoice == "2")
                            {
                                Console.WriteLine($"\n[You]: {userText}");
                                part2Accumulator.Append(userText + " ");
                            }
                            else
                            {
                                _speechAccumulator += userText + " ";
                                _speechCts?.Cancel();
                                _speechCts = new CancellationTokenSource();
                                var token = _speechCts.Token;

                                Task.Run(async () =>
                                {
                                    try {
                                        await Task.Delay(1500, token);
                                        if (!token.IsCancellationRequested)
                                        {
                                            string finalInput = _speechAccumulator.Trim();
                                            _speechAccumulator = "";
                                            Console.WriteLine($"\n[You]: {finalInput}");

                                            // 使用更明確的指令格式
                                            string instruction = $"[User Response]: \"{finalInput}\"\n";
                                            if (modeChoice == "1")
                                            {
                                                if (_currentQuestionIndex < _currentQuestions.Count - 1)
                                                {
                                                    _currentQuestionIndex++;
                                                    instruction += $"[Instruction]: Give feedback and then ask this question: \"{_currentQuestions[_currentQuestionIndex]}\"";
                                                }
                                                else
                                                {
                                                    instruction += "[Instruction]: Give feedback and then state that Part 1 is finished.";
                                                }
                                            }
                                            
                                            await ProcessConversation(instruction, aiBrain, piper);
                                        }
                                    } catch (TaskCanceledException) { }
                                });
                            }
                        }
                    }
                };

                waveIn.DataAvailable += dataHandler;
                waveIn.StartRecording();
                
                Console.ReadKey(true);
                
                waveIn.StopRecording();
                waveIn.DataAvailable -= dataHandler; // 解除綁定，避免重複

                if (modeChoice == "2")
                {
                    string finalSpeech = part2Accumulator.ToString().Trim();
                    if (!string.IsNullOrEmpty(finalSpeech))
                        await ProcessConversation(finalSpeech + "\n(Provide your evaluation now.)", aiBrain, piper);
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
        /// 清理文字中的 Markdown 符號 (如 * 或 #) 或 AI 標籤，避免 TTS 把它們讀出來。
        /// </summary>
        private static string CleanTextForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            // 移除 Markdown
            string cleaned = text.Replace("*", "").Replace("#", "");
            
            // 移除 AI 可能產生的標籤格式，例如 [Feedback] 或 [Next Question]
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[.*?\]", "");
            
            // 移除括號內容 (有時 AI 會加註解)
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\(.*?\)", "");

            return cleaned.Trim();
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
