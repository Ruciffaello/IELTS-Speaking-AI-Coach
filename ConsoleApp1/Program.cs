using NAudio.Wave;
using System.Text;
using System.Text.Json;
using Vosk;

namespace ConsoleApp1
{
    /// <summary>
    /// 程式主進入點：擔任「導演」角色，控制練習流程、計時與選單。
    /// </summary>
    internal class Program
    {
        private static bool _isAiSpeaking = false;
        private static int _currentQuestionIndex = 0; 
        private static List<string> _currentQuestions = new(); 

        // --- 語音緩衝變數：防止結巴與停頓導致的頻繁中斷 ---
        private static string _speechAccumulator = ""; 
        private static CancellationTokenSource? _speechCts; 

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== 雅思口說練習助手 (IELTS Speaking Assistant) ===");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string voskModelPath;
            string piperExePath;
            string piperModelPath;
            string llmModelPath;

#if DEBUG
            voskModelPath = @"D:\vosk-model-small-en-us-0.15";
            piperExePath = @"D:\piper\piper.exe";
            piperModelPath = @"D:\piper\models\en_GB-northern_english_male-medium.onnx";
            llmModelPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Llama-3.2-3B-Instruct-awq-uint4-float16-cpu-onnx"));
            Console.WriteLine($"[DEBUG]: 正在載入 3B 模型: {llmModelPath}");
#else
            string relLlmPath = Path.Combine(baseDir, "resources", "llm");
            string devLlmPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Llama-3.2-3B-Instruct-awq-uint4-float16-cpu-onnx"));
            voskModelPath = Path.Combine(baseDir, "resources", "vosk-model-small-en-us-0.15");
            piperExePath = Path.Combine(baseDir, "resources", "piper", "piper.exe");
            piperModelPath = Path.Combine(baseDir, "resources", "piper", "en_GB-northern_english_male-medium.onnx");
            
            if (Directory.Exists(relLlmPath)) llmModelPath = relLlmPath;
            else llmModelPath = devLlmPath;

            if (!Directory.Exists(voskModelPath)) voskModelPath = @"D:\vosk-model-small-en-us-0.15";
            if (!File.Exists(piperExePath)) piperExePath = @"D:\piper\piper.exe";
            if (!File.Exists(piperModelPath)) piperModelPath = @"D:\piper\models\en_GB-northern_english_male-medium.onnx";
#endif

            var questionBank = LoadQuestionBank();
            if (questionBank == null) return;

            if (!Directory.Exists(voskModelPath) || !File.Exists(piperExePath) || !Directory.Exists(llmModelPath))
            {
                Console.WriteLine("\n[錯誤]: 找不到必要的 AI 模型或工具檔案。");
                return;
            }

            using var aiBrain = new OnnxAiService(llmModelPath);
            var piper = new PiperService(piperExePath, piperModelPath);

            bool exitProgram = false;
            while (!exitProgram)
            {
                Console.Clear();
                Console.WriteLine("=== 雅思口說練習助手 (IELTS Speaking Assistant) ===");
                Console.WriteLine("\n請選擇練習模式:");
                Console.WriteLine("1. Part 1 (日常問答 - 1.5秒停頓緩衝)");
                Console.WriteLine("2. Part 2 (個人獨白 - 手動結束)");
                Console.WriteLine("3. Part 3 (深度對話 - 1.5秒停頓緩衝)");
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
                    // 強化 Prompt：強調是 SPEAKING test，忽略大小寫與結巴雜訊
                    systemPrompt = "You are an expert IELTS examiner for a SPEAKING test. \n" +
                                   "Note: Input is from an STT engine. \n" +
                                   "Rules: \n" +
                                   "1. IGNORE capitalization, minor STT typos, and stutters (like 'I I'). \n" +
                                   "2. Focus ONLY on providing 1-2 concise feedback on Grammar and Vocabulary. \n" +
                                   "3. ALWAYS end by asking the next question provided in brackets.";
                    initialMessage = $"Let's start Part 1. The topic is '{p1.Topic}'. " + _currentQuestions[0];
                }
                else if (modeChoice == "2")
                {
                    var p2 = PickPart2(questionBank);
                    systemPrompt = "You are an expert IELTS examiner. Evaluate the student's long turn talk. \n" +
                                   "Rules: Ignore STT artifacts. Focus on fluency and coherence.";
                    string intro = $"Part 2: {p2.Description}. You should say: {string.Join(", ", p2.Prompts)}. You have 1 minute to prepare.";
                    Console.WriteLine($"\n[AI]: {intro}");
                    await piper.SpeakAsync(intro, 0.95f);
                    await RunTimer(60); 
                    initialMessage = "Preparation time is up. Please start speaking now.";
                }
                else if (modeChoice == "3")
                {
                    var p3 = PickPart2(questionBank);
                    systemPrompt = "You are an expert IELTS examiner for Part 3 (Discussion). \n" +
                                   "Rules: Ignore STT disfluencies. Challenge the student's ideas with deep questions. Be professional.";
                    initialMessage = $"Now let's discuss {p3.Topic} in depth. {p3.Part3Questions[0]}";
                }
                else { continue; }

                aiBrain.SetSystemPrompt(systemPrompt);
                Console.WriteLine($"\n--- 練習開始 (按任意鍵結束並回到選單) ---");
                Console.WriteLine($"[AI]: {initialMessage}");
                await piper.SpeakAsync(initialMessage, 0.95f);

                using var model = new Model(voskModelPath);
                using var rec = new VoskRecognizer(model, 16000);
                using var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 800 };

                StringBuilder part2Accumulator = new StringBuilder();
                _speechAccumulator = ""; // 重置緩衝區

                waveIn.DataAvailable += (sender, e) =>
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
                                // Part 1 & 3: 停頓緩衝邏輯
                                _speechAccumulator += userText + " ";
                                _speechCts?.Cancel();
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

                                            string instruction = finalInput;
                                            if (modeChoice == "1" && _currentQuestionIndex < _currentQuestions.Count - 1)
                                            {
                                                _currentQuestionIndex++;
                                                instruction += $"\n(Next question: {_currentQuestions[_currentQuestionIndex]})";
                                            }
                                            await ProcessConversation(instruction, aiBrain, piper);
                                        }
                                    } catch (TaskCanceledException) { }
                                });
                            }
                        }
                    }
                };

                waveIn.StartRecording();
                Console.ReadKey(true); 
                waveIn.StopRecording();

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

        private static async Task ProcessConversation(string userText, IAiService aiService, PiperService piper)
        {
            _isAiSpeaking = true;
            try
            {
                StringBuilder sentenceBuffer = new StringBuilder();
                Console.Write("[AI]: ");

                await foreach (var chunk in aiService.GetStreamingResponseAsync(userText))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    Console.Write(chunk);
                    sentenceBuffer.Append(chunk);
                    if (IsEndOfSentence(chunk))
                    {
                        string sentence = CleanTextForSpeech(sentenceBuffer.ToString());
                        if (sentence.Length > 2)
                        {
                            await piper.SpeakAsync(sentence, 0.95f);
                            sentenceBuffer.Clear();
                        }
                    }
                }

                if (sentenceBuffer.Length > 0)
                {
                    string final = CleanTextForSpeech(sentenceBuffer.ToString());
                    if (final.Length > 0) await piper.SpeakAsync(final, 0.95f);
                }
                Console.WriteLine();
            }
            finally { _isAiSpeaking = false; }
        }

        private static string CleanTextForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string cleaned = text.Replace("*", "").Replace("#", "");
            StringBuilder sb = new StringBuilder();
            foreach (var c in cleaned) if (c >= 32 || c == '\n') sb.Append(c);
            return sb.ToString().Trim();
        }

        private static bool IsEndOfSentence(string text) => text.Any(c => ".?!。？！\n".Contains(c));

        private static QuestionBank? LoadQuestionBank()
        {
            try { return JsonSerializer.Deserialize<QuestionBank>(File.ReadAllText("questions.json")); }
            catch { return null; }
        }

        private static Part1Topic PickPart1(QuestionBank bank) => bank.Part1[new Random().Next(bank.Part1.Count)];
        private static Part2Topic PickPart2(QuestionBank bank) => bank.Part2[new Random().Next(bank.Part2.Count)];
        
        private static string ParseVoskText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }
    }
}
