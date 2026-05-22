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
                Arguments = $"--model \"{_modelPath}\" --output_raw --json-input",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            
            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    // Piper 的日誌通常輸出到 stderr
                    // 只要看到 "Completed" 或 "Finished" 或甚至只是日誌輸出，都可能代表處理進度
                    // 這裡我們主要找 "Completed" 或 "samples" (Piper 輸出 "Finished generating ... samples")
                    if (e.Data.Contains("Completed") || e.Data.Contains("samples"))
                    {
                        _sentenceFinishedTcs?.TrySetResult();
                    }
                }
            };

            _process.Start();
            _process.BeginErrorReadLine();
            _stdin = _process.StandardInput;

            var waveFormat = new WaveFormat(22050, 16, 1);
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(30),
                ReadFully = true
            };

            _outputDevice = new WaveOutEvent();
            _outputDevice.Init(_waveProvider);
            _outputDevice.Play();
        }

        private void StartReadingOutput()
        {
            Task.Run(async () =>
            {
                byte[] buffer = new byte[8192];
                Stream baseStream = _process!.StandardOutput.BaseStream;

                try
                {
                    while (!_isDisposed && _process != null && !_process.HasExited)
                    {
                        int bytesRead = await baseStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            _waveProvider?.AddSamples(buffer, 0, bytesRead);
                        }
                        else
                        {
                            await Task.Delay(50);
                        }
                    }
                }
                catch { /* 忽略讀取異常 */ }
            });
        }

        public async Task SpeakAsync(string text, float speed = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_process == null || _process.HasExited || _stdin == null)
            {
                Console.WriteLine("[TTS 警告]: Piper 進程未啟動或已結束。");
                return;
            }

            if (_sentenceFinishedTcs == null) StartReadingOutput();

            _sentenceFinishedTcs = new TaskCompletionSource();

            float lengthScale = 1.0f / speed;
            var inputJson = JsonSerializer.Serialize(new { text = text, length_scale = lengthScale });

            await _stdin.WriteLineAsync(inputJson);
            await _stdin.FlushAsync();

            // 1. 等待 Piper 在後台生成完畢，增加 5 秒逾時防止永久卡死
            var completedTask = await Task.WhenAny(_sentenceFinishedTcs.Task, Task.Delay(5000));
            if (completedTask != _sentenceFinishedTcs.Task)
            {
                // 如果逾時，通常是因為沒捕捉到 "Completed" 字樣，但音訊可能已經在輸出了
                await Task.Delay(500); 
            }

            // 2. 等待 NAudio 緩衝區播放完畢
            // 增加安全計數器，防止因音訊設備問題導致的無限循環
            int safetyCounter = 0;
            while (_waveProvider!.BufferedBytes > 0 && safetyCounter < 100)
            {
                await Task.Delay(100);
                safetyCounter++;
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
