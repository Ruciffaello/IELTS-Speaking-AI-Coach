# IELTS Speaking Practice Assistant 🎧

這是一個結合 AI (Ollama)、語音辨識 (Vosk) 與語音合成 (Piper) 的雅思口說練習助手。

## 🚀 快速開始 (Quick Start)

在使用本程式之前，請務必完成以下三個步驟：

### 第一步：安裝 Ollama (AI 大腦)
1. 前往 [Ollama 官網](https://ollama.com/) 下載並安裝。
2. 安裝完成後，打開終端機 (CMD 或 PowerShell)，輸入以下指令下載模型：
   ```bash
   ollama pull gemma3:4b
   ```
   *註：本程式預設使用 `gemma3:4b`。*

### 第二步：準備語音資源 (Resources)
請確保程式執行檔目錄下有一個 `resources` 資料夾，結構如下：
```text
/您的程式資料夾/
├── ConsoleApp1.exe
├── questions.json
└── resources/
    ├── vosk-model-small-en-us-0.15/  (Vosk 語音辨識模型)
    └── piper/                        (Piper 語音合成工具)
        ├── piper.exe
        ├── en_GB-northern_english_male-medium.onnx
        └── en_GB-northern_english_male-medium.onnx.json
```

### 第三步：執行程式
點擊 `ConsoleApp1.exe` 即可啟動！

---

## 🛠 功能特色
- **三種模式：** 支援 IELTS Part 1, 2, 3 專項練習。
- **真實記憶：** AI 考官具備上下文記憶，能針對您的回答進行追問。
- **流暢對話：** 優化後的串流處理，讓 AI 的反應速度接近真人。
- **本地執行：** 所有語音處理都在本地完成，保障您的隱私。

## 📝 題庫修改
您可以自行編輯 `questions.json` 來增加或修改練習題目。

## ⚠️ 注意事項
- 請確保麥克風已正確連接。
- 如果 AI 說話速度太快或太慢，可以檢查麥克風與喇叭的設定。
- 若更換 Piper 語音模型，請確保檔名與程式內的設定一致。

---
祝您雅思順利奪金！🎓
