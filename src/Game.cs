using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cs2Roulette
{
    // ============================================================ 金币
    public sealed class CoinWallet
    {
        public long Coins;
        public long TotalEarned;
        public long TotalSpent;

        public void Earn(long amount)
        {
            if (amount <= 0) return;
            Coins += amount;
            TotalEarned += amount;
        }

        public bool TrySpend(long amount)
        {
            if (amount <= 0 || Coins < amount) return false;
            Coins -= amount;
            TotalSpent += amount;
            return true;
        }
    }

    // ============================================================ 加减乘除题
    /// <summary>
    /// 比大小题：给出两个两位数，判断前者与后者的大小关系。
    /// </summary>
    public sealed class CompareProblem
    {
        public int Left;
        public int Right;
        /// <summary>1 = 左大于右，-1 = 左小于右，0 = 相等</summary>
        public int Result;

        public static CompareProblem Generate(Random rnd)
        {
            // 两个两位数（10..99）
            int a = rnd.Next(10, 100);
            int b = rnd.Next(10, 100);

            // 约 1/6 概率出「相等」，让三个按钮都有意义
            if (rnd.Next(6) == 0) b = a;

            return new CompareProblem
            {
                Left = a,
                Right = b,
                Result = a > b ? 1 : (a < b ? -1 : 0),
            };
        }

        public string Text
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0}  ○  {1}", Left, Right); }
        }

        /// <summary>把结果转成符号，用于解析显示。</summary>
        public string Symbol
        {
            get { return Result > 0 ? "＞" : (Result < 0 ? "＜" : "＝"); }
        }
    }

    /// <summary>以下为旧的算术题实现，保留以备需要时恢复。</summary>
    public sealed class MathProblem
    {
        public string Text = "";
        public long Answer;

        public static MathProblem Generate(Random rnd, int difficulty)
        {
            // difficulty 0..3 递增
            int lvl = Math.Max(0, Math.Min(3, difficulty));
            int span = new[] { 10, 20, 50, 100 }[lvl];
            int op = rnd.Next(4);
            long a, b, ans;
            string sym;

            switch (op)
            {
                case 0: // 加
                    a = rnd.Next(1, span + 1); b = rnd.Next(1, span + 1);
                    ans = a + b; sym = "+"; break;
                case 1: // 减（保证非负）
                    a = rnd.Next(1, span + 1); b = rnd.Next(1, (int)a + 1);
                    ans = a - b; sym = "−"; break;
                case 2: // 乘（控制规模）
                    a = rnd.Next(2, Math.Max(3, span / 2) + 1);
                    b = rnd.Next(2, 13);
                    ans = a * b; sym = "×"; break;
                default: // 除（保证整除）
                    b = rnd.Next(2, 13);
                    ans = rnd.Next(2, Math.Max(3, span / 2) + 1);
                    a = b * ans; sym = "÷"; break;
            }

            return new MathProblem
            {
                Text = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} = ?", a, sym, b),
                Answer = ans,
            };
        }
    }

    // ============================================================ 安全知识题
    public sealed class SafetyQuestion
    {
        public string Question = "";
        public string[] Options = new string[0];
        public int CorrectIndex;
        public string Explain = "";
    }

    public static class SafetyBank
    {
        private static readonly List<SafetyQuestion> All = new List<SafetyQuestion>
        {
            new SafetyQuestion { Question = "发现有人触电，首先应该做什么？",
                Options = new[] { "直接用手拉开触电者", "立即切断电源或用绝缘物挑开电线", "先泼水降温", "拍照发朋友圈" },
                CorrectIndex = 1, Explain = "必须先断电或用干燥绝缘物分离电源，直接接触会导致连带触电。" },

            new SafetyQuestion { Question = "家用电器着火时，正确的做法是？",
                Options = new[] { "立即用水扑灭", "先切断电源再用干粉灭火器", "打开窗户让它自己烧完", "用湿毛巾直接盖住" },
                CorrectIndex = 1, Explain = "带电灭火可能触电，水导电会扩大事故；应先断电再用干粉或二氧化碳灭火器。" },

            new SafetyQuestion { Question = "燃气泄漏时，下列哪种行为最危险？",
                Options = new[] { "关闭燃气阀门", "打开门窗通风", "在室内拨打手机或开关电灯", "迅速离开现场" },
                CorrectIndex = 2, Explain = "手机与电器开关产生的电火花可能引燃燃气，应在室外安全处报警。" },

            new SafetyQuestion { Question = "过马路时正确做法是？",
                Options = new[] { "低头看手机快速通过", "走人行横道，确认安全后通过", "从车流间隙穿行", "红灯时只要没车就过" },
                CorrectIndex = 1, Explain = "走人行横道并观察信号与来车，是基本的交通规则。" },

            new SafetyQuestion { Question = "使用灭火器时，喷射方向应对准？",
                Options = new[] { "火焰上方", "火焰中部", "火焰根部", "浓烟处" },
                CorrectIndex = 2, Explain = "对准火焰根部才能切断燃烧，喷向火苗上部基本无效。" },

            new SafetyQuestion { Question = "在宿舍使用大功率电器的主要风险是？",
                Options = new[] { "电费变高", "线路过载引发火灾", "影响室友休息", "没什么风险" },
                CorrectIndex = 1, Explain = "宿舍线路按普通负载设计，大功率电器易导致线路过热起火。" },

            new SafetyQuestion { Question = "地震发生时在室内，较安全的做法是？",
                Options = new[] { "立即乘电梯下楼", "躲到坚固桌下或承重墙角护住头部", "站在窗户旁边", "跳窗逃生" },
                CorrectIndex = 1, Explain = "就近躲避、护住头颈，远离玻璃与外墙；震后再有序撤离。" },

            new SafetyQuestion { Question = "收到「客服」索要验证码的电话，应该？",
                Options = new[] { "直接告知以便核实", "挂断并通过官方渠道自行核实", "发给对方但要求保密", "先给一半" },
                CorrectIndex = 1, Explain = "验证码等同于账户钥匙，任何情况下都不应告知他人。" },

            new SafetyQuestion { Question = "发现有人溺水，不会游泳时应该？",
                Options = new[] { "立即跳下去救人", "大声呼救并抛投救生圈、长杆等漂浮物", "站在岸边等待", "先脱衣服再下水" },
                CorrectIndex = 1, Explain = "不会游泳下水极易双双遇险，应呼救并借助工具间接施救。" },

            new SafetyQuestion { Question = "食品保质期已过但外观正常，正确做法是？",
                Options = new[] { "闻一下没味就吃", "加热后食用", "直接丢弃，不食用", "喂给宠物" },
                CorrectIndex = 2, Explain = "过期食品可能已滋生致病菌，外观与气味正常也不能保证安全。" },
        };

        public static SafetyQuestion Random(Random rnd)
        {
            return All[rnd.Next(All.Count)];
        }

        public static int Count { get { return All.Count; } }
    }

    // ============================================================ 仓库
    public sealed class Inventory
    {
        public readonly List<OwnedItem> Items = new List<OwnedItem>();

        /// <summary>入仓；返回新建的对象，便于调用方后续处理（如限时回收）。</summary>
        public OwnedItem Add(Item item, long costPaidCents)
        {
            var o = new OwnedItem
            {
                Item = item,
                CostPaidCents = costPaidCents,
                AcquiredTicks = DateTime.Now.Ticks,
            };
            Items.Add(o);
            return o;
        }

        public bool Remove(OwnedItem o) { return Items.Remove(o); }

        public long TotalValueCents
        {
            get
            {
                long s = 0;
                foreach (var o in Items) s += o.Item.PriceUsdCents;
                return s;
            }
        }
    }

    // ============================================================ 存档
    public sealed class SaveData
    {
        public long Coins;
        public long TotalEarned;
        public long TotalSpent;
        public int DrawCount;
        public int RecycledCount;
        public int MathCorrect;
        public int QuizCorrect;
        public readonly List<int> OwnedItemIds = new List<int>();

        private static string PathFor(string dir)
        {
            return Path.Combine(dir, "save.json");
        }

        public static SaveData Load(string dir)
        {
            var s = new SaveData();
            try
            {
                string p = PathFor(dir);
                if (!File.Exists(p)) return s;
                var root = Json.Obj(Json.Parse(File.ReadAllText(p, new UTF8Encoding(false))));
                s.Coins = Json.Long(root, "coins");
                s.TotalEarned = Json.Long(root, "totalEarned");
                s.TotalSpent = Json.Long(root, "totalSpent");
                s.DrawCount = Json.Int(root, "drawCount");
                s.RecycledCount = Json.Int(root, "recycledCount");
                s.MathCorrect = Json.Int(root, "mathCorrect");
                s.QuizCorrect = Json.Int(root, "quizCorrect");
                foreach (var o in Json.ArrOf(root, "owned"))
                    s.OwnedItemIds.Add(Convert.ToInt32(o, CultureInfo.InvariantCulture));
            }
            catch { }
            return s;
        }

        public void Save(string dir)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\"coins\":").Append(Coins)
                  .Append(",\"totalEarned\":").Append(TotalEarned)
                  .Append(",\"totalSpent\":").Append(TotalSpent)
                  .Append(",\"drawCount\":").Append(DrawCount)
                  .Append(",\"recycledCount\":").Append(RecycledCount)
                  .Append(",\"mathCorrect\":").Append(MathCorrect)
                  .Append(",\"quizCorrect\":").Append(QuizCorrect)
                  .Append(",\"owned\":[");
                for (int i = 0; i < OwnedItemIds.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(OwnedItemIds[i].ToString(CultureInfo.InvariantCulture));
                }
                sb.Append("]}");
                File.WriteAllText(PathFor(dir), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
