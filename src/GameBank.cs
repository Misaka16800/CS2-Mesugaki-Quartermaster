using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cs2Roulette
{
    /// <summary>
    /// 游戏大题库：每次答题从全部题目里随机抽一道。
    ///
    /// 题库存放在外部文件 data/quiz.txt，每行一道题，用竖线分隔：
    ///     分类|题干|选项A|选项B|选项C|选项D|正确答案序号(0-3)|解析
    ///
    /// 这样题目可以随时用文本编辑器修改，不需要重新编译。
    /// 若文件缺失或解析失败，会退回到内置的最小可用题库，保证游戏仍能玩。
    /// </summary>
    public sealed class QuizQuestion
    {
        public string Question;
        public string[] Options;
        public int CorrectIndex;
        public string Explain;
        /// <summary>题目来源分类，便于筛选与统计。</summary>
        public string Source;
    }

    public static class GameBank
    {
        private static List<QuizQuestion> _all;
        private static string _loadedFrom;

        /// <summary>题库文件名（相对 data 目录）。</summary>
        public const string FileName = "quiz.txt";

        /// <summary>题库来源路径，便于排查。</summary>
        public static string LoadedFrom { get { return _loadedFrom; } }

        public static List<QuizQuestion> All
        {
            get
            {
                if (_all == null) _all = Build();
                return _all;
            }
        }

        public static QuizQuestion Random(Random rnd)
        {
            var all = All;
            return all[rnd.Next(all.Count)];
        }

        public static int Count { get { return All.Count; } }

        /// <summary>按分类统计题量。</summary>
        public static Dictionary<string, int> CountBySource()
        {
            var d = new Dictionary<string, int>();
            foreach (var q in All)
            {
                if (!d.ContainsKey(q.Source)) d[q.Source] = 0;
                d[q.Source]++;
            }
            return d;
        }

        /// <summary>从题库文件中读取。找不到或解析为空则返回 null。</summary>
        public static List<QuizQuestion> LoadFrom(string path)
        {
            if (!File.Exists(path)) return null;
            var list = new List<QuizQuestion>();
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch { return null; }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#")) continue;          // 注释行

                var parts = line.Split('|');
                if (parts.Length < 8) continue;              // 字段不够，跳过

                int correct;
                if (!int.TryParse(parts[6].Trim(), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out correct)) continue;
                if (correct < 0 || correct > 3) continue;

                list.Add(new QuizQuestion
                {
                    Source = parts[0].Trim(),
                    Question = parts[1].Trim(),
                    Options = new[] { parts[2].Trim(), parts[3].Trim(),
                                      parts[4].Trim(), parts[5].Trim() },
                    CorrectIndex = correct,
                    Explain = parts[7].Trim(),
                });
            }
            return list.Count > 0 ? list : null;
        }

        private static List<QuizQuestion> Build()
        {
            // 1) 优先从外部题库文件读取
            string[] candidates = new string[0];
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                candidates = new[]
                {
                    Path.Combine(baseDir, "data", FileName),
                    Path.Combine(baseDir, FileName),
                    Path.Combine(baseDir, "..", "data", FileName),
                };
            }
            catch { }

            foreach (var p in candidates)
            {
                try
                {
                    var loaded = LoadFrom(p);
                    if (loaded != null)
                    {
                        _loadedFrom = p;
                        return loaded;
                    }
                }
                catch { }
            }

            // 2) 回退：内置最小题库（保证外部文件丢失时游戏仍可玩）
            _loadedFrom = "(内置回退题库)";
            return Fallback();
        }

        private static List<QuizQuestion> Fallback()
        {
            var L = new List<QuizQuestion>();
            Action<string, string, string, string, string, string, int, string> add =
                (src, q, a, b, c, d, ci, ex) => L.Add(new QuizQuestion
                {
                    Source = src,
                    Question = q,
                    Options = new[] { a, b, c, d },
                    CorrectIndex = ci,
                    Explain = ex,
                });

            add("东方Project", "东方 Project 系列的唯一作者（含剧本、程序、美术、音乐）是谁？",
                "ZUN", "奈须蘑菇", "麻枝准", "龙骑士07", 0,
                "东方 Project 几乎全部由 ZUN 一人创作。");
            add("东方Project", "东方 Project 故事的舞台主要位于哪里？",
                "幻想乡", "常世", "学园都市", "异世界 SEKAI", 0,
                "幻想乡是被结界隔离的世外之地，人类与妖怪共存。");
            add("蔚蓝档案", "《蔚蓝档案》中，玩家扮演的角色身份是？",
                "老师（Sensei）", "学生会长", "校长", "记者", 0,
                "玩家以「老师」身份在基沃托斯指导学生。");
            add("蔚蓝档案", "《蔚蓝档案》的故事主要发生在哪座城市？",
                "基沃托斯", "学园都市", "米花町", "幻想乡", 0,
                "基沃托斯是由众多学园构成的自治都市。");
            return L;
        }
    }
}
