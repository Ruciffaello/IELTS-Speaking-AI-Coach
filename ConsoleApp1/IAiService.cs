using System.Collections.Generic;

namespace ConsoleApp1
{
    public interface IAiService
    {
        /// <summary>
        /// 取得串流回覆
        /// </summary>
        /// <param name="prompt">使用者的輸入文字</param>
        IAsyncEnumerable<string> GetStreamingResponseAsync(string prompt);

        /// <summary>
        /// 設定系統提示詞
        /// </summary>
        void SetSystemPrompt(string systemPrompt);

        /// <summary>
        /// 清除對話紀錄 (如果服務有實作上下文記憶)
        /// </summary>
        void ClearContext();
    }
}
