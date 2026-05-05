using System;
using System.Collections.Generic;

namespace ConsoleApp1
{
    /// <summary>
    /// 題庫總表：對應 questions.json 的最外層結構。
    /// </summary>
    public class QuestionBank
    {
        // 存放所有 Part 1 的主題
        public List<Part1Topic> Part1 { get; set; } = new();
        // 存放所有 Part 2 與 Part 3 的連動主題
        public List<Part2Topic> Part2 { get; set; } = new();
    }

    /// <summary>
    /// 雅思考試第一部分 (Part 1)：通常是簡單的日常問答。
    /// </summary>
    public class Part1Topic
    {
        // 主題名稱 (例如: Hobbies, Work)
        public string Topic { get; set; } = "";
        // 該主題下的一系列問題
        public List<string> Questions { get; set; } = new();
    }

    /// <summary>
    /// 雅思考試第二部分 (Part 2) 與第三部分 (Part 3)：
    /// Part 2 是個人獨白，Part 3 是針對同一個主題的深度討論。
    /// 所以這兩個部分在資料結構上是綁在一起的。
    /// </summary>
    public class Part2Topic
    {
        // 主題名稱
        public string Topic { get; set; } = "";
        // Part 2 的任務描述 (Cue Card)
        public string Description { get; set; } = "";
        // 提示點 (告訴考生應該包含哪些內容)
        public List<string> Prompts { get; set; } = new();
        // 延伸出的 Part 3 深度討論問題
        public List<string> Part3Questions { get; set; } = new();
    }
}
