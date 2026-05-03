using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ConsoleApp1
{
    /// <summary>
    /// 語音合成服務：擔任「廣播員」角色。負責將 AI 的文字透過 Piper 引擎轉成聲音並播放。
    /// </summary>
    public class PiperService
    {
        private readonly string _piperExe;
        private readonly string _modelPath;

        /// <summary>
        /// 初始化語音合成器
        /// </summary>
        /// <param name="exePath">piper.exe 的路徑</param>
        /// <param name="modelPath">.onnx 語音模型檔案的路徑</param>
        public PiperService(string exePath, string modelPath)
        {
            _piperExe = exePath;
            _modelPath = modelPath;
        }

        /// <summary>
        /// 將文字朗讀出來
        /// </summary>
        /// <param name="text">要朗讀的內容</param>
        /// <param name="speed">語速 (1.0 為正常，1.05 稍微放慢，適合練習聽力)</param>
        public async Task SpeakAsync(string text, float speed = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // 1. 安全檢查：確認必要的工具是否存在
            if (!File.Exists(_piperExe))
            {
                Console.WriteLine($"\n[TTS 錯誤]: 找不到 Piper 執行檔: {_piperExe}");
                return;
            }

            // 2. 計算 Piper 的語速參數 (length_scale)
            // length_scale 越大越慢 (例如 1.1)，越小越快 (例如 0.9)
            string lengthScale = (1.0f / speed).ToString("F2");

            // 3. 設定 Piper 的外部行程啟動參數
            var startInfo = new ProcessStartInfo
            {
                FileName = _piperExe,
                // --output_raw: 輸出原始音訊流到 StandardOutput
                // --length_scale: 調整語速
                Arguments = $"--model \"{_modelPath}\" --output_raw --length_scale {lengthScale}",
                RedirectStandardInput = true,  // 用來輸入文字
                RedirectStandardOutput = true, // 用來讀取音訊 Byte
                RedirectStandardError = true,  // 用來讀取 Log/錯誤
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = null  // 重要：保持音訊流的 Byte 完整性
            };

            using var process = Process.Start(startInfo);
            if (process == null) return;

            try 
            {
                // 非同步讀取錯誤輸出，避免阻塞
                var errorTask = process.StandardError.ReadToEndAsync();

                // A. 將文字餵進 Piper 的輸入端
                await using (var sw = process.StandardInput)
                {
                    await sw.WriteLineAsync(text);
                }

                // B. 使用 NAudio 播放 Piper 產生的原始音訊流
                // Piper 預設輸出格式為 22050Hz, 16bit, 單聲道 (Mono)
                using var waveProvider = new RawSourceWaveStream(process.StandardOutput.BaseStream, new WaveFormat(22050, 16, 1));
                using var outputDevice = new WaveOutEvent();

                outputDevice.Init(waveProvider);
                outputDevice.Play();

                // 等待音訊播放完畢
                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    await Task.Delay(50);
                }

                // C. 錯誤處理：只有當錯誤訊息包含 "error" 時才顯示 (忽略一般的 info)
                string errorMsg = await errorTask;
                if (!string.IsNullOrEmpty(errorMsg) && errorMsg.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Piper 錯誤]: {errorMsg.Trim()}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[語音異常]: {ex.Message}");
            }
            finally 
            {
                // 強制結束 Piper 行程，避免資源殘留
                if (!process.HasExited) process.Kill();
            }
        }
    }
}
