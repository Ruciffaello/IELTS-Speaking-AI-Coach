using System;
using System.Collections.Generic;

namespace ConsoleApp1
{
    /// <summary>
    /// 題庫資料模型：對應 questions.json 的結構
    /// </summary>
    public class QuestionBank
    {
        // 第一部分題目列表
        public List<Part1Topic> Part1 { get; set; } = new();
        // 第二與第三部分題目列表
        public List<Part2Topic> Part2 { get; set; } = new();
    }

    /// <summary>
    /// Part 1 主題與題目
    /// </summary>
    public class Part1Topic
    {
        public string Topic { get; set; } = "";
        public List<string> Questions { get; set; } = new();
    }

    /// <summary>
    /// Part 2 與 Part 3 聯動題目
    /// </summary>
    public class Part2Topic
    {
        public string Topic { get; set; } = "";
        // Part 2 的 Cue Card 描述
        public string Description { get; set; } = "";
        // Part 2 的提示點 (You should say...)
        public List<string> Prompts { get; set; } = new();
        // 相關的 Part 3 深度討論問題
        public List<string> Part3Questions { get; set; } = new();
    }
}
