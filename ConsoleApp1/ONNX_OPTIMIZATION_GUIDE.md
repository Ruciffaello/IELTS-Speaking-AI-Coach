# ONNX Runtime GenAI 效能與記憶體優化指南

這份指南專為 **16GB RAM (約 3GB 可用)** 且 **僅有 CPU (無獨立顯卡)** 的環境設計，旨在優化您的雅思口說練習助理。

---

## 一、 記憶體優化 (Memory Management)
目標：防止對話變長時導致記憶體溢位 (Out of Memory)。

### 1. 限制上下文長度 (`max_length`)
*   **概念**：這是 AI 生成內容（含問題與回答）的總字數上限。
*   **建議值**：`300` ~ `512` (Token 數)。
*   **原理**：AI 的記憶 (KV Cache) 與長度成正比。雅思口說通常是一問一答，不需要保留整場考試的所有對話細節。
*   **實作位置**：`OnnxAiService.cs` 的 `GetStreamingResponseAsync` 方法中。

### 2. 開啟記憶體共享緩存 (`past_present_share_buffer`)
*   **概念**：讓 ONNX Runtime 在處理當前和過去的 Token 時，重複使用同一塊記憶體。
*   **建議設定**：`true`。
*   **原理**：如果不開啟，每生成一個新字，系統可能都會嘗試申請新的記憶體空間。開啟後能顯著降低記憶體震盪。

### 3. 滑動視窗對話歷史 (Sliding Window History)
*   **概念**：限制傳送給 AI 的對話輪數。
*   **建議設定**：保留 `System Prompt` + 最近 3-5 輪對話。
*   **原理**：越長的歷史紀錄會導致推理時間呈幾何級數增加。

---

## 二、 延遲優化 (Latency / Speed Optimization)
目標：縮短 AI 思考時間，讓對話更自然。

### 1. 使用貪婪搜尋 (Greedy Search)
*   **概念**：關閉隨機抽樣 (`do_sample`)。
*   **建議設定**：`do_sample = false`。
*   **原理**：Greedy Search 每次只選機率最高的字，運算量最低，且對於「考官」這種需要穩定邏輯的角色來說非常適合。

### 2. 模型預熱 (Warm-up)
*   **概念**：程式啟動後，先偷偷跑一次推理。
*   **原理**：CPU 推理引擎在「冷啟動」時會載入各種計算核心 (Kernels)，這會導致第一次回答卡頓 1-2 秒。
*   **實作位置**：`OnnxAiService.cs` 的建構子最後一行。

### 3. 模型端：必須使用 4-bit 量化 (INT4)
*   **概念**：這不是程式碼改動，而是模型選擇。
*   **原理**：INT4 模型體積比原始模型小 75%，且專為 CPU 向量指令集優化。**如果您下載到 FP16 的模型，這份指南的其他優化將無濟於事。**

---

## 三、 實作參考 (C# 代碼片段)

您可以在 `OnnxAiService.cs` 中依序加入以下邏輯：

```csharp
// 在 GetStreamingResponseAsync 方法內
using var generatorParams = new GeneratorParams(_model);

// 1. 記憶體控制
generatorParams.SetSearchOption("max_length", 512); 
generatorParams.SetSearchOption("past_present_share_buffer", true);

// 2. 延遲控制 (Greedy Search)
generatorParams.SetSearchOption("do_sample", false); 
generatorParams.SetSearchOption("top_p", 1.0);
generatorParams.SetSearchOption("top_k", 1);
```

## 四、 預期效果
經過以上優化，您的程式應該能達成：
1. **穩定運算**：長時間對話後記憶體佔用依然穩定在 1.5GB - 2GB。
2. **即時反應**：AI 在您說完話後 0.5 秒內開始輸出文字。
3. **低熱量**：減少不必要的重複運算，降低 CPU 負載。
