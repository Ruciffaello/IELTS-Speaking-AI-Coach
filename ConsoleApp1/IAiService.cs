using System.Collections.Generic;

namespace ConsoleApp1
{
    /// <summary>
    /// AI 服務介面：定義了 AI 大腦必須具備的能力。
    /// 無論是使用本地 ONNX 模型還是遠端 Ollama 伺服器，都必須實作這些方法。
    /// </summary>
    public interface IAiService
    {
        /// <summary>
        /// 取得 AI 的串流回覆。
        /// 使用 IAsyncEnumerable 可以讓文字像打字機一樣，產生一個字就回傳一個字，
        /// 這樣使用者就不需要等到整句話說完才能看到或聽到回覆。
        /// </summary>
        /// <param name="prompt">使用者說的話或給 AI 的指令</param>
        IAsyncEnumerable<string> GetStreamingResponseAsync(string prompt);

        /// <summary>
        /// 設定系統提示詞 (System Prompt)。
        /// 這用來定義 AI 的角色（例如：你現在是一個雅思考官）與規則。
        /// </summary>
        void SetSystemPrompt(string systemPrompt);

        /// <summary>
        /// 清除對話紀錄。
        /// 當我們更換練習模式（例如從 Part 1 換到 Part 2）時，需要讓 AI 忘記之前的對話，避免混亂。
        /// </summary>
        void ClearContext();
    }
}
