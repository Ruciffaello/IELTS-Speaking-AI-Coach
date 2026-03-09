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

            // 設定 Piper 啟動參數
            var startInfo = new ProcessStartInfo
            {
                FileName = _piperExe,
                Arguments = $"--model {_modelPath} --output_raw", // 使用 raw 格式方便處理
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            using var sw = process.StandardInput;

            // 1. 將文字餵給 Piper
            await sw.WriteLineAsync(text);
            sw.Close(); // 告訴 Piper 文字輸入結束

            // 2. 讀取 Piper 產出的 Raw PCM 資料並播放
            // 注意：Piper 預設通常是 22050Hz, 16-bit, Mono (視模型而定)
            using var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);
            ms.Position = 0;

            using var waveProvider = new RawSourceWaveStream(ms, new WaveFormat(22050, 16, 1));
            using var outputDevice = new WaveOutEvent();

            outputDevice.Init(waveProvider);
            outputDevice.Play();

            // 等待播放完畢
            while (outputDevice.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(100);
            }
        }
    }
}
