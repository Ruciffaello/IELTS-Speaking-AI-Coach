using NAudio.Wave;
using System.Text;
using System.Text.Json;
using Vosk;

namespace ConsoleApp1
{
    internal class Program
    {
        private static bool _isAiSpeaking = false;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== IELTS Speaking Practice Assistant ===");

            // 取得程式執行的根目錄
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 【安全性檢查】檢查路徑是否包含中文字元 (非 ASCII)
            if (baseDir.Any(c => c > 127))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[警告]: 偵測到程式路徑包含非英文(如中文)字元。");
                Console.WriteLine("這可能會導致語音合成(Piper)或辨識(Vosk)無法正常運作。");
                Console.WriteLine("建議將資料夾移動到純英文路徑(例如 C:\\IELTS_App\\)以獲得最佳體驗。");
                Console.ResetColor();
                Console.WriteLine("----------------------------------------------------------");
            }

            string voskModelPath;
            string piperExePath;
            string piperModelPath;

#if DEBUG
            // 【開發模式】 使用您電腦上的絕對路徑，方便直接偵錯
            voskModelPath = @"D:\vosk-model-small-en-us-0.15";
            piperExePath = @"D:\piper\piper.exe";
            piperModelPath = @"D:\piper\models\en_GB-northern_english_male-medium.onnx";
            Console.WriteLine("[DEBUG]: 正在使用開發環境路徑...");
#else
            // 【發佈模式】 使用相對路徑，方便打包給朋友
            voskModelPath = Path.Combine(baseDir, "resources", "vosk-model-small-en-us-0.15");
            piperExePath = Path.Combine(baseDir, "resources", "piper", "piper.exe");
            piperModelPath = Path.Combine(baseDir, "resources", "piper", "en_GB-northern_english_male-medium.onnx");
#endif

            // 1. 載入題庫
            var questionBank = LoadQuestionBank();
            if (questionBank == null)
            {
                Console.WriteLine("Error: Could not load questions.json");
                return;
            }

            // 檢查資源是否存在
            if (!Directory.Exists(voskModelPath) || !File.Exists(piperExePath))
            {
                Console.WriteLine("\n[錯誤]: 找不到必要的 AI 模型或工具檔案。");
                Console.WriteLine($"請確保以下目錄包含所需資源:\n{Path.Combine(baseDir, "resources")}");
                Console.WriteLine("\n按任意鍵退出...");
                Console.ReadKey();
                return;
            }

            // 2. 模式選擇
            Console.WriteLine("\nPlease select a practice mode:");
            Console.WriteLine("1. Part 1 (Daily Life Questions)");
            Console.WriteLine("2. Part 2 (Individual Long Turn / Cue Card)");
            Console.WriteLine("3. Part 3 (Two-way Discussion)");
            Console.Write("Enter choice (1-3): ");
            string? modeChoice = Console.ReadLine();

            string systemPrompt = "";
            string initialMessage = "";

            switch (modeChoice)
            {
                case "1":
                    var p1 = PickPart1(questionBank);
                    systemPrompt = $"You are an IELTS examiner. We are doing Part 1. Topic: {p1.Topic}. " +
                                   $"Ask me these questions one by one: {string.Join(", ", p1.Questions)}. " +
                                   "Provide feedback and move to the next question naturally.";
                    initialMessage = $"Let's start Part 1. The topic is '{p1.Topic}'. " + p1.Questions[0];
                    break;
                case "2":
                    var p2 = PickPart2(questionBank);
                    systemPrompt = $"You are an IELTS examiner. We are doing Part 2. Topic: {p2.Topic}. " +
                                   $"The Cue Card is: {p2.Description}. Prompts: {string.Join(", ", p2.Prompts)}. " +
                                   "Ask me to describe it and wait for my long turn. Then provide feedback.";
                    initialMessage = $"Let's move to Part 2. Here is your topic: {p2.Description}. " +
                                     $"You should talk about: {string.Join(", ", p2.Prompts)}.";
                    break;
                case "3":
                    var p3 = PickPart2(questionBank); // Part 3 is related to Part 2 topics
                    systemPrompt = $"You are an IELTS examiner. We are doing Part 3 (Discussion). Topic: {p3.Topic}. " +
                                   $"Discuss these deeper questions with me: {string.Join(", ", p3.Part3Questions)}. " +
                                   "Engage in a natural discussion and challenge my opinions.";
                    initialMessage = $"Now let's have a discussion about {p3.Topic}. " + p3.Part3Questions[0];
                    break;
                default:
                    Console.WriteLine("Invalid choice. Exiting.");
                    return;
            }

            // 3. 初始化 AI 與 TTS
            IAiService aiBrain = new OllamaService();
            aiBrain.SetSystemPrompt(systemPrompt);
            var piper = new PiperService(piperExePath, piperModelPath);

            Console.WriteLine("\n=== Simulation Started ===");
            Console.WriteLine($"[AI]: {initialMessage}");
            await piper.SpeakAsync(initialMessage);

            // 4. 啟動錄音與辨識
            if (WaveInEvent.DeviceCount == 0)
            {
                Console.WriteLine("\n[錯誤]: 找不到任何錄音裝置(麥克風)。請接上麥克風後重新啟動。");
                Console.ReadKey();
                return;
            }

            // 顯示目前使用的裝置名稱
            var capabilities = WaveInEvent.GetCapabilities(0);
            Console.WriteLine($"\n[系統]: 正在使用麥克風: {capabilities.ProductName}");

            using var model = new Model(voskModelPath);
            using var rec = new VoskRecognizer(model, 16000);

            using (var waveIn = new WaveInEvent())
            {
                waveIn.DeviceNumber = 0; // 預設使用第一個裝置
                waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
                waveIn.BufferMilliseconds = 800;

                waveIn.DataAvailable += (sender, e) =>
                {
                    if (_isAiSpeaking) return;

                    if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        var jsonResult = rec.Result();
                        string userText = ParseVoskText(jsonResult);

                        if (!string.IsNullOrWhiteSpace(userText))
                        {
                            Console.WriteLine($"\n[You]: {userText}");
                            Task.Run(async () => await ProcessConversation(userText, aiBrain, piper));
                        }
                    }
                };

                waveIn.StartRecording();
                Console.WriteLine("\n(Listening... Press any key to stop)");
                Console.ReadKey();
                waveIn.StopRecording();
            }
        }

        private static QuestionBank? LoadQuestionBank()
        {
            try
            {
                string json = File.ReadAllText("questions.json");
                return JsonSerializer.Deserialize<QuestionBank>(json);
            }
            catch { return null; }
        }

        private static Part1Topic PickPart1(QuestionBank bank)
        {
            var random = new Random();
            return bank.Part1[random.Next(bank.Part1.Count)];
        }

        private static Part2Topic PickPart2(QuestionBank bank)
        {
            var random = new Random();
            return bank.Part2[random.Next(bank.Part2.Count)];
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

                    if (IsEndOfSentence(chunk) || sentenceBuffer.Length > 60)
                    {
                        string currentSentence = sentenceBuffer.ToString().Replace("*", "").Trim();
                        if (!string.IsNullOrEmpty(currentSentence))
                        {
                            await piper.SpeakAsync(currentSentence);
                            sentenceBuffer.Clear();
                        }
                    }
                }

                if (sentenceBuffer.Length > 0)
                {
                    await piper.SpeakAsync(sentenceBuffer.ToString().Trim());
                }
                Console.WriteLine();
            }
            finally
            {
                _isAiSpeaking = false;
            }
        }

        private static bool IsEndOfSentence(string text)
        {
            return text.Contains(".") || text.Contains("?") || text.Contains("!") ||
                   text.Contains("。") || text.Contains("？") || text.Contains("！") ||
                   text.Contains("\n");
        }

        private static string ParseVoskText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                return textElement.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
    }
}
