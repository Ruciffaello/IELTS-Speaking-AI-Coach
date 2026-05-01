using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ConsoleApp1
{
    public class PiperService
    {
        private readonly string _piperExe;
        private readonly string _modelPath;

        public PiperService(string exePath, string modelPath)
        {
            _piperExe = exePath;
            _modelPath = modelPath;
        }

        public async Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // 1. 先在 C# 層級檢查檔案是否存在
            if (!File.Exists(_piperExe))
            {
                Console.WriteLine($"\n[TTS Error]: 找不到 Piper 執行檔: {_piperExe}");
                return;
            }
            if (!File.Exists(_modelPath))
            {
                Console.WriteLine($"\n[TTS Error]: 找不到模型檔: {_modelPath}");
                return;
            }

            // 設定 Piper 啟動參數
            var startInfo = new ProcessStartInfo
            {
                FileName = _piperExe,
                Arguments = $"--model \"{_modelPath}\" --output_raw",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = null // 保持原始 Byte 流
            };

            using var process = Process.Start(startInfo);
            if (process == null) return;

            try 
            {
                // 非同步讀取錯誤訊息，避免阻塞
                var errorTask = process.StandardError.ReadToEndAsync();

                await using (var sw = process.StandardInput)
                {
                    await sw.WriteLineAsync(text);
                }

                // 播放音訊
                using var waveProvider = new RawSourceWaveStream(process.StandardOutput.BaseStream, new WaveFormat(22050, 16, 1));
                using var outputDevice = new WaveOutEvent();

                outputDevice.Init(waveProvider);
                outputDevice.Play();

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    await Task.Delay(50);
                }

                // 檢查是否有錯誤
                string errorMsg = await errorTask;
                if (!string.IsNullOrEmpty(errorMsg) && !errorMsg.Contains("DEBUG"))
                {
                    Console.WriteLine($"\n[Piper Message]: {errorMsg.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[TTS Exception]: {ex.Message}");
            }
            finally 
            {
                if (!process.HasExited) process.Kill();
            }
        }
    }
}
