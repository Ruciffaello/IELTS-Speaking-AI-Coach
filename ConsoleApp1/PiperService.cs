using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    /// <summary>
    /// 優化後的語音合成服務：採用長駐進程模式，避免重複啟動 piper.exe 的開銷。
    /// </summary>
    public class PiperService : IDisposable
    {
        private readonly string _piperExe;
        private readonly string _modelPath;
        
        private Process? _process;
        private StreamWriter? _stdin;
        private BufferedWaveProvider? _waveProvider;
        private WaveOutEvent? _outputDevice;
        
        private bool _isDisposed = false;

        // 用來追蹤 Piper 是否處理完目前的句子
        private TaskCompletionSource? _sentenceFinishedTcs;

        public PiperService(string exePath, string modelPath)
        {
            _piperExe = exePath;
            _modelPath = modelPath;
            
            // 預先啟動 Piper 進程
            StartPiperProcess();
        }

        private void StartPiperProcess()
        {
            if (!File.Exists(_piperExe))
            {
                Console.WriteLine($"\n[TTS 錯誤]: 找不到 Piper 執行檔: {_piperExe}");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _piperExe,
                // 使用 --json-input 模式，這樣我們可以透過 stdin 傳送語速等參數
                // 使用 --output_raw 將音訊流輸出到 stdout
                Arguments = $"--model \"{_modelPath}\" --output_raw --json-input",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // 用於讀取 Piper 的日誌來判斷何時生成結束
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            
            // 監聽 StandardError 來捕捉 "Completed" 訊息
            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data) && e.Data.Contains("Completed"))
                {
                    _sentenceFinishedTcs?.TrySetResult();
                }
            };

            _process.Start();
            _process.BeginErrorReadLine();
            _stdin = _process.StandardInput;

            // 初始化音訊播放器
            // Piper 預設輸出 22050Hz, 16bit, Mono
            var waveFormat = new WaveFormat(22050, 16, 1);
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(20),
                ReadFully = true // 沒資料時播放靜音，保持設備開啟
            };

            _outputDevice = new WaveOutEvent();
            _outputDevice.Init(_waveProvider);
            _outputDevice.Play();
        }

        /// <summary>
        /// 讀取 Piper 輸出的音訊資料流，並餵入 NAudio 的緩衝區。
        /// </summary>
        private void StartReadingOutput()
        {
            Task.Run(async () =>
            {
                byte[] buffer = new byte[4096];
                Stream baseStream = _process!.StandardOutput.BaseStream;

                while (!_isDisposed && _process != null && !_process.HasExited)
                {
                    int bytesRead = await baseStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        _waveProvider?.AddSamples(buffer, 0, bytesRead);
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
            });
        }

        public async Task SpeakAsync(string text, float speed = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(text) || _stdin == null || _process == null) return;

            // 確保讀取執行緒已啟動（僅執行一次）
            if (_sentenceFinishedTcs == null) StartReadingOutput();

            _sentenceFinishedTcs = new TaskCompletionSource();

            // 構造 JSON 輸入，動態調整語速
            // length_scale 是 Piper 的參數：1.0 是原速，0.8 較快，1.2 較慢
            float lengthScale = 1.0f / speed;
            var inputJson = JsonSerializer.Serialize(new { text = text, length_scale = lengthScale });

            // 傳送給 Piper
            await _stdin.WriteLineAsync(inputJson);

            // 1. 等待 Piper 在後台生成完畢 (透過 Stderr 的 "Completed" 訊號)
            await _sentenceFinishedTcs.Task;

            // 2. 等待 NAudio 緩衝區播放完畢
            // 我們計算剩餘毫秒數來決定等待時間
            while (_waveProvider!.BufferedBytes > 0)
            {
                await Task.Delay(100);
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
            try
            {
                _outputDevice?.Stop();
                _outputDevice?.Dispose();
                _stdin?.Dispose();
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.Dispose();
                }
            }
            catch { /* 忽略釋放異常 */ }
        }
    }
}
