using System.Collections.Generic;

namespace ConsoleApp1
{
    public class QuestionBank
    {
        public List<Part1Topic> Part1 { get; set; } = new();
        public List<Part2Topic> Part2 { get; set; } = new();
    }

    public class Part1Topic
    {
        public string Topic { get; set; } = "";
        public List<string> Questions { get; set; } = new();
    }

    public class Part2Topic
    {
        public string Topic { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Prompts { get; set; } = new();
        public List<string> Part3Questions { get; set; } = new();
    }
}
