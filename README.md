# IELTS Speaking AI Coach 🎧

A high-performance, local-first AI application designed to help users practice for the IELTS Speaking exam. This tool integrates state-of-the-art AI models for speech recognition, conversational logic, and speech synthesis, all running entirely on your local machine for maximum privacy and low latency.

這是一個結合 AI 語言模型、語音辨識與語音合成的雅思口說練習助手。本工具完全在「本地端」執行，保障隱私且不需要連網。

## 🚀 Features | 功能特色

- **Realistic IELTS Simulation:** Supports Part 1, 2, and 3 with an AI examiner that has context memory.
- **Local-First:** All processing (LLM, STT, TTS) happens on your CPU/GPU. No data leaves your machine.
- **Low Latency:** Optimized streaming processing for near-human response speed.
- **Customizable:** Easily edit `questions.json` to add your own practice topics.

- **擬真雅思考試：** 支援 Part 1, 2, 3，AI 考官具備上下文記憶與追問能力。
- **本地執行：** 所有運算（大模型、語音辨識、語音合成）都在本地完成，保護隱私。
- **流暢對話：** 優化後的串流處理，讓 AI 的反應速度接近真人。
- **彈性題庫：** 可自行編輯 `questions.json` 增加練習題目。

## 🛠 Tech Stack | 技術棧

- **Runtime:** .NET 8.0
- **LLM Engine:** [OnnxRuntimeGenAI](https://github.com/microsoft/onnxruntime-genai) (running Llama-3.2-3B)
- **STT (Speech-to-Text):** [Vosk](https://alphacephei.com/vosk/)
- **TTS (Text-to-Speech):** [Piper](https://github.com/rhasspy/piper)
- **Audio I/O:** [NAudio](https://github.com/naudio/NAudio)

## 📦 Setup | 安裝說明

### 1. Requirements | 需求
- **RAM:** 16GB+ recommended.
- **OS:** Windows (Path must not contain non-ASCII/Chinese characters).
- **路徑限制：** 程式資料夾路徑不可包含中文（例如「桌面」）。

### 2. Prepare Resources | 準備模型資源
Create a `resources` folder in the executable directory with the following structure:
在執行檔目錄下建立 `resources` 資料夾，結構如下：

```text
/Project Root/
├── ConsoleApp1.exe
├── questions.json
└── resources/
    ├── llm/                          (Llama-3.2-3B-Instruct ONNX files)
    ├── vosk-model-small-en-us-0.15/  (Vosk STT model)
    └── piper/                        (Piper TTS tool & .onnx voices)
        ├── piper.exe
        └── en_GB-northern_english_male-medium.onnx
```

### 3. Run | 執行
Double-click `ConsoleApp1.exe` or use `dotnet run`.

---

## ⚠️ Important Notes | 注意事項

- **Path:** Ensure the project is located in a pure English path (e.g., `D:\IELTS-Coach\`).
- **Microphone:** Ensure your microphone is connected and set as the default recording device in Windows.
- **Hardware:** Close memory-heavy apps (like Chrome) before running to ensure smooth AI performance.

---
祝您雅思順利奪金！🎓
