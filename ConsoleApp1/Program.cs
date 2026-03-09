using NAudio.Wave;
using System.Text;
using System.Text.Json;
using Vosk;

namespace ConsoleApp1
{
    internal class Program
    {

        // 系統狀態
        private static bool _isAiSpeaking = false;
        private static StringBuilder _userSpeechBuffer = new StringBuilder();

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== AI 語音助手啟動中 ===");

            //Model modelPath = new Model("D:\\vosk-model-en-us-0.22");

            Model modelPath = new Model("D:\\vosk-model-small-en-us-0.15");


            var ollama = new OllamaService();
            var piper = new PiperService(@"D:\piper\piper.exe", @"D:\piper\models\en_GB-alan-low.onnx");

            var rec = new VoskRecognizer(modelPath,16000);

            using (var waveIn = new WaveInEvent()) {
                waveIn.DeviceNumber = 0;
                waveIn.WaveFormat = new WaveFormat(16000,16, 1);
                waveIn.BufferMilliseconds = 800;

                waveIn.DataAvailable += (sender, e) =>
                {

                    // 【關鍵】如果 AI 正在說話，我們忽略麥克風輸入，避免聽到自己
                    if (_isAiSpeaking) return;

                    // 送入 Vosk 辨識
                    if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        // 取得完整句子的 JSON
                        var jsonResult = rec.Result();

                        // 解析 JSON 取得純文字
                        string userText = ParseVoskText(jsonResult);

                        // 3. 只有當文字不為空時，才送交給 AI
                        if (!string.IsNullOrWhiteSpace(userText))
                        {
                            Console.WriteLine($"\n[Detected]: {userText}");

                            // 使用 Task.Run 避免阻塞語音接收執行緒
                            Task.Run(async () => await ProcessConversation(userText, ollama, piper));
                        }
                    }
                    else
                    {
                        // 這裡可以選擇性顯示「辨識中」的文字，增加互動感
                        // var partial = vosk.PartialResult();
                        // Console.Write("."); 
                    }

                };

                waveIn.StartRecording();

                Console.WriteLine("Say something");

                Console.ReadKey();

                waveIn.StopRecording();
            }


        }


        // 處理對話的核心邏輯
        private static async Task ProcessConversation(string userText, OllamaService ollama, PiperService piper)
        {
            _isAiSpeaking = true;
            StringBuilder sentenceBuffer = new StringBuilder();

            await foreach (var chunk in ollama.GetStreamingResponse(userText))
            {
                if (string.IsNullOrEmpty(chunk)) continue;

                Console.Write(chunk);
                sentenceBuffer.Append(chunk);

                // 在 ProcessConversation 內的 IsEndOfSentence 判斷
                if (IsEndOfSentence(chunk) || sentenceBuffer.Length > 50)
                {
                    string currentSentence = sentenceBuffer.ToString()
                        .Replace("*", "") // 過濾 Markdown 符號
                        .Trim();

                    if (!string.IsNullOrEmpty(currentSentence))
                    {
                        // 這裡可以檢查，如果句子太短（例如只有 "1."），就先不要唸
                        await piper.SpeakAsync(currentSentence);
                        sentenceBuffer.Clear();
                    }
                }
            }

            // 【重要】處理最後剩下的「尾巴」
            if (sentenceBuffer.Length > 0)
            {
                await piper.SpeakAsync(sentenceBuffer.ToString().Trim());
            }

            _isAiSpeaking = false;
        }

        private static bool IsEndOfSentence(string text)
        {
            return text.Contains("。") || text.Contains("？") || text.Contains("！") || text.Contains("\n");
        }

        private static string ParseVoskText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                return textElement.GetString();
            }
            return string.Empty;
        }


    }
}
