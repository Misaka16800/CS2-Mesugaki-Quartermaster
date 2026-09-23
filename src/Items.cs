using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cs2Roulette
{
    /// <summary>一个可抽取的具体物品 = 皮肤 + 磨损档位（+ 可选 StatTrak/纪念品）。</summary>
    public sealed class Item
    {
        public int Id;
        public string Name = "";          // 饰品名（不含磨损）
        public string DisplayName = "";   // 含磨损的完整名
        public string Weapon = "";
        public string Category = "";
        public string Rarity = "";
        public string Wear = "";
        public int WearIndex;
        public bool StatTrak;
        public bool Souvenir;
        public string Image = "";

        public long PriceUsdCents = -1;   // -1 = 无价格，用估值
        public bool Estimated;            // 是否使用了估值

        public double PriceUsd { get { return PriceUsdCents / 100.0; } }

        /// <summary>磨损的中文名（数据里存的是英文枚举，展示时统一转中文）。</summary>
        public static string ChineseWear(string wear)
        {
            switch (wear)
            {
                case "Factory New": return "崭新出厂";
                case "Minimal Wear": return "略有磨损";
                case "Field-Tested": return "久经沙场";
                case "Well-Worn": return "破损不堪";
                case "Battle-Scarred": return "战痕累累";
                default: return wear;
            }
        }

        /// <summary>用于展示的完整名称：把英文磨损替换为中文。</summary>
        public string PrettyName
        {
            get
            {
                string cn = ChineseWear(Wear);
                if (!string.IsNullOrEmpty(cn) && Wear != cn && DisplayName.EndsWith("(" + Wear + ")"))
                    return DisplayName.Substring(0, DisplayName.Length - Wear.Length - 2) + "(" + cn + ")";
                return DisplayName;
            }
        }
    }

    /// <summary>稀有度基准价（美元），用于无真实价格时估值。</summary>
    internal static class RarityValue
    {
        // 依据市场大致量级设定，仅作回退估算
        public static double Base(string rarity)
        {
            switch (rarity)
            {
                case "消费级": return 0.08;
                case "工业级": return 0.20;
                case "军规级": return 0.60;
                case "受限级": return 2.50;
                case "保密级": return 9.00;
                case "隐秘级": return 42.00;
                case "非凡": return 480.00;   // 刀/手套
                case "违禁": return 1600.00;
                default: return 1.00;
            }
        }

        // 磨损系数：崭新最贵，战痕最便宜
        public static double WearFactor(int wearIndex)
        {
            switch (wearIndex)
            {
                case 0: return 1.00;   // Factory New
                case 1: return 0.78;   // Minimal Wear
                case 2: return 0.58;   // Field-Tested
                case 3: return 0.50;   // Well-Worn
                case 4: return 0.45;   // Battle-Scarred
                default: return 1.00;
            }
        }
    }

    /// <summary>一个抽奖池。</summary>
    public sealed class Pool
    {
        public string Key = "";
        public string Name = "";
        public string Kind = "";          // weapon / category / special / miracle
        public readonly List<Item> Items = new List<Item>();
        public Color Accent = Color.FromArgb(0, 204, 187);

        public Item Cover;                // 池内最贵物品，作封面
        public long AvgPriceCents;        // 池内均价
        public long CostCents;            // 单抽花费 = 均价 × 120%
        public long RecycleCents;         // 均价成交回收 = 均价 × 90%

        // ---- 奇迹池专用（Kind == "miracle"）----
        /// <summary>极品饰品（1~3 件）。</summary>
        public readonly List<Item> RareItems = new List<Item>();
        /// <summary>便宜保底饰品。</summary>
        public Item CheapItem;
        /// <summary>抽中极品的概率（极低）。</summary>
        public double RareChance;

        public bool IsMiracle { get { return Kind == "miracle"; } }

        // ---- 加权抽取（Kind == "weighted"）----
        /// <summary>与 Items 一一对应的抽取权重；为空表示均匀随机。</summary>
        public double[] Weights;
        /// <summary>累计权重表（加速抽取，Pick 时惰性构建）。</summary>
        double[] _cum;

        /// <summary>是否按权重抽取。</summary>
        public bool IsWeighted { get { return Weights != null && Weights.Length == Items.Count; } }

        /// <summary>按权重抽一次。</summary>
        Item PickWeighted(Random rnd)
        {
            if (_cum == null || _cum.Length != Weights.Length)
            {
                _cum = new double[Weights.Length];
                double acc = 0;
                for (int i = 0; i < Weights.Length; i++)
                {
                    acc += Weights[i];
                    _cum[i] = acc;
                }
            }
            double total = _cum[_cum.Length - 1];
            if (total <= 0) return Items[rnd.Next(Items.Count)];
            double r = rnd.NextDouble() * total;
            // 二分查找
            int lo = 0, hi = _cum.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cum[mid] < r) lo = mid + 1; else hi = mid;
            }
            return Items[lo];
        }

        /// <summary>
        /// 按给定权重计算「期望回收」（美分），用于反解成本。
        /// 权重不必归一化。
        /// </summary>
        public long ExpectedRecycleCents()
        {
            if (!IsWeighted)
            {
                long s0 = 0;
                foreach (var it in Items) s0 += it.PriceUsdCents;
                return s0 / Math.Max(1, Items.Count);
            }
            double sum = 0, acc = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                sum += Weights[i];
                acc += Weights[i] * Items[i].PriceUsdCents;
            }
            if (sum <= 0) return 0;
            return (long)Math.Round(acc / sum);
        }

        /// <summary>极品均价（美分）。</summary>
        public long RareAvgCents
        {
            get
            {
                if (RareItems.Count == 0) return 0;
                long s = 0;
                foreach (var it in RareItems) s += it.PriceUsdCents;
                return s / RareItems.Count;
            }
        }

        public int AffordCount(long coins)
        {
            return CostCents <= 0 ? 0 : (int)(coins / CostCents);
        }

        /// <summary>
        /// 按权重抽取：奇迹池以 RareChance 出极品，否则出保底便宜饰品；
        /// 普通池则在池内均匀随机。
        /// </summary>
        public Item Pick(Random rnd)
        {
            if (IsMiracle && CheapItem != null && RareItems.Count > 0)
            {
                if (rnd.NextDouble() < RareChance)
                    return RareItems[rnd.Next(RareItems.Count)];
                return CheapItem;
            }
            if (IsWeighted) return PickWeighted(rnd);
            return Items[rnd.Next(Items.Count)];
        }
    }

    /// <summary>仓库中的一件已拥有饰品。</summary>
    public sealed class OwnedItem
    {
        public Item Item;
        public long CostPaidCents;        // 抽到时花的金币（等值美分）
        public long AcquiredTicks;
    }

    public sealed class ItemDatabase
    {
        public readonly List<Item> Items = new List<Item>();
        public readonly List<Pool> Pools = new List<Pool>();
        public string DataDir = "";
        public int PricedCount;
        public int EstimatedCount;
        public int ExcludedCount;
        /// <summary>被踢出奖池的物品（例如 7 天内无成交记录）。</summary>
        public readonly HashSet<int> ExcludedIds = new HashSet<int>();

        public static ItemDatabase Load(string dataDir)
        {
            var db = new ItemDatabase();
            db.DataDir = dataDir;

            string itemsPath = Path.Combine(dataDir, "items.json");
            if (!File.Exists(itemsPath))
                throw new FileNotFoundException("缺少饰品数据: " + itemsPath);

            // 显式 UTF-8，避免中文名乱码
            string json = Crypto.ReadText(itemsPath);
            var root = Json.Obj(Json.Parse(json));

            foreach (var o in Json.ArrOf(root, "items"))
            {
                var d = Json.Obj(o);
                var it = new Item
                {
                    Id = Json.Int(d, "id"),
                    Name = Json.Str(d, "name"),
                    DisplayName = Json.Str(d, "displayName"),
                    Weapon = Json.Str(d, "weapon"),
                    Category = Json.Str(d, "category"),
                    Rarity = Json.Str(d, "rarity"),
                    Wear = Json.Str(d, "wear"),
                    WearIndex = Json.Int(d, "wearIndex"),
                    StatTrak = Json.Bool(d, "statTrak"),
                    Souvenir = Json.Bool(d, "souvenir"),
                    Image = Json.Str(d, "image"),
                };
                db.Items.Add(it);
            }

            // 顺序很重要：先读剔除名单，再算价格，这样统计口径才准确
            // （被剔除的物品不计入 有价格/估值 的统计）
            db.LoadExcluded(Path.Combine(dataDir, "excluded.json"));
            db.ApplyPrices(Path.Combine(dataDir, "prices.json"));
            db.BuildPools();
            return db;
        }

        /// <summary>
        /// 读取剔除名单（如「7 天内无成交记录」的物品）。这些物品不进入任何奖池，
        /// 但仍保留在物品表中，便于日后成交恢复后重新纳入。
        /// </summary>
        public void LoadExcluded(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                string json = Crypto.ReadText(path);
                var root = Json.Obj(Json.Parse(json));
                foreach (var o in Json.ArrOf(root, "ids"))
                {
                    if (o == null) continue;
                    int id;
                    if (int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out id))
                        ExcludedIds.Add(id);
                }
                ExcludedCount = ExcludedIds.Count;
            }
            catch (Exception ex)
            {
                Console.WriteLine("excluded.json 解析失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 读取用户提供的 prices.json 覆盖价格；没有真实价格的物品按稀有度估值。
        /// prices.json 格式: { "prices": { "物品ID": 12.34, ... } }
        /// 也支持以 displayName 为键（便于手工填表）。
        /// </summary>
        public void ApplyPrices(string pricesPath)
        {
            var byId = new Dictionary<int, long>();
            var byName = new Dictionary<string, long>(StringComparer.Ordinal);

            if (File.Exists(pricesPath))
            {
                try
                {
                    string json = Crypto.ReadText(pricesPath);
                    var root = Json.Obj(Json.Parse(json));
                    object pv;
                    if (root.TryGetValue("prices", out pv) && pv != null)
                    {
                        foreach (var kv in Json.Obj(pv))
                        {
                            double v;
                            if (!double.TryParse(kv.Value == null ? "" : Convert.ToString(kv.Value),
                                    NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                                continue;
                            int id;
                            if (int.TryParse(kv.Key, out id)) byId[id] = (long)Math.Round(v * 100);
                            else byName[kv.Key] = (long)Math.Round(v * 100);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("prices.json 解析失败: " + ex.Message);
                }
            }

            foreach (var it in Items)
            {
                // 被剔除（如 7 天无成交）的物品不参与价格统计，避免统计口径混淆
                if (ExcludedIds.Contains(it.Id)) continue;

                long cents;
                if (byId.TryGetValue(it.Id, out cents))
                {
                    it.PriceUsdCents = cents;
                    it.Estimated = false;
                    PricedCount++;
                    continue;
                }
                if (byName.TryGetValue(it.DisplayName, out cents) || byName.TryGetValue(it.PrettyName, out cents))
                {
                    it.PriceUsdCents = cents;
                    it.Estimated = false;
                    PricedCount++;
                    continue;
                }

                // 回退估值
                double v2 = RarityValue.Base(it.Rarity) * RarityValue.WearFactor(it.WearIndex);
                if (it.StatTrak) v2 *= 1.35;
                if (it.Souvenir) v2 *= 0.85;
                it.PriceUsdCents = Math.Max(1, (long)Math.Round(v2 * 100));
                it.Estimated = true;
                EstimatedCount++;
            }
        }

        private static Color AccentFor(string key, int i)
        {
            Color[] palette = {
                Color.FromArgb(0, 204, 187), Color.FromArgb(240, 78, 120),
                Color.FromArgb(168, 110, 240), Color.FromArgb(255, 153, 0),
                Color.FromArgb(136, 221, 68), Color.FromArgb(68, 85, 221),
                Color.FromArgb(236, 72, 66), Color.FromArgb(72, 180, 236),
            };
            return palette[Math.Abs(key.GetHashCode()) % palette.Length];
        }

        /// <summary>其它奖池的回报口径：单抽成本 = 均价 × 120%，回收 = 均价 × 90%，故回报率 75%。</summary>
        private const double StandardReturnRate = 0.75;

        /// <summary>
        /// 奇迹池中「抽中极品」的概率。这是唯一需要调整的设计参数：
        ///   调高 → 极品更容易出，但为了守住 75% 回报率，单抽成本会同步上升。
        ///   调低 → 极品更稀有，单抽成本也随之下降。
        /// </summary>
        private const double MiracleRareChance = 0.02;   // 2%，约 50 抽一次

        /// <summary>
        /// 构造「奇迹池」：k 个极品 + 1 个便宜保底。
        ///
        /// 定价逻辑（与其它池保持完全相同的 75% 回报率）：
        ///   期望回收 E = 0.9 · [ p·R + (1−p)·B ]
        ///   单抽成本 K = E / 0.75
        /// 其中 R 为极品均价、B 为便宜保底价值、p 为极品概率。
        /// 该式对任意 B 都成立，因此便宜饰品可以真的"便宜"。
        /// </summary>
        private Pool MakeMiraclePool(string key, string name, List<Item> rares,
                                     Item cheap, int colorSeed)
        {
            var p = new Pool
            {
                Key = key,
                Name = name,
                Kind = "miracle",
                Accent = AccentFor(key, colorSeed),
                CheapItem = cheap,
            };
            p.RareItems.AddRange(rares);
            p.RareItems.Sort((a, b) => b.PriceUsdCents.CompareTo(a.PriceUsdCents));

            // 展示用物品列表（不含便宜饰品的重复项）
            p.Items.AddRange(p.RareItems);
            p.Items.Add(cheap);
            p.Items.Sort((a, b) => b.PriceUsdCents.CompareTo(a.PriceUsdCents));
            p.Cover = p.Items[0];

            long B = cheap.PriceUsdCents;
            long R = p.RareAvgCents;

            // 极品概率是设计输入（每次抽中原价最高的那批饰品的机会）
            p.RareChance = MiracleRareChance;

            // 期望均价（不加 0.9）：这是「抽一次平均拿到多少市价」
            long avgPrice = (long)Math.Round(p.RareChance * R + (1.0 - p.RareChance) * B);

            // 与加权池统一口径：成本 = 期望均价 ÷ 0.75
            //   于是回报率 = 期望均价 / 成本 = 0.75，精确 75%。
            //   【坑】不要把 0.9（回收折扣）乘进来：
            //   早期写成 cost = 0.9·EV / 0.75，导致回报率被抬到 83%。
            long K = (long)Math.Round(avgPrice / StandardReturnRate);
            p.CostCents = K < 1 ? 1 : K;
            long expect = avgPrice;
            if (p.CostCents < 1) p.CostCents = 1;

            // 展示用的"均价"与回收价（按成本口径反推，保持与其它池一致）
            p.AvgPriceCents = avgPrice;
            p.RecycleCents = (long)Math.Round(avgPrice * 0.90);
            return p;
        }

        /// <summary>
        /// 建一个「按权重抽取」的池子。
        ///
        /// 关键：成本不是拍脑袋定的，而是由权重算出的期望回收反解：
        ///     成本 K = 期望回收 E ÷ 0.75
        /// 因此无论权重怎么设计，回报率都恒等于 75%，与其它池同一口径。
        /// </summary>
        private Pool MakeWeightedPool(string key, string name, List<Item> items,
                                      double[] weights, int colorSeed)
        {
            var p = new Pool
            {
                Key = key,
                Name = name,
                Kind = "weighted",
                Accent = AccentFor(key, colorSeed),
            };
            p.Items.AddRange(items);
            // Items 按价格降序排，权重必须跟着一起重排
            var idx = new List<int>();
            for (int i = 0; i < items.Count; i++) idx.Add(i);
            idx.Sort((a, b) => items[b].PriceUsdCents.CompareTo(items[a].PriceUsdCents));

            var sortedItems = new List<Item>();
            var sortedW = new double[idx.Count];
            for (int i = 0; i < idx.Count; i++)
            {
                sortedItems.Add(items[idx[i]]);
                sortedW[i] = weights[idx[i]];
            }
            p.Items.Clear();
            p.Items.AddRange(sortedItems);
            p.Weights = sortedW;
            p.Cover = p.Items.Count > 0 ? p.Items[0] : null;

            long E = p.ExpectedRecycleCents();          // 期望回收（已含匹配率无关，纯按权重）
            p.CostCents = (long)Math.Round(E / StandardReturnRate);
            if (p.CostCents < 1) p.CostCents = 1;
            p.AvgPriceCents = (long)Math.Round(p.CostCents * 1.20);
            p.RecycleCents = E;
            return p;
        }

        /// <summary>
        /// 排名衰减权重：按「价格从低到高」的名次给权重。
        ///
        ///   w_i = ratio ^ (rank_i / halfLife)
        ///
        /// rank 0 是最便宜的（权重最高），名次越大权重越小。
        /// 用名次而非价格比，避免被 $0.003 这类极值把分布压垮。
        ///   ratio=0.5, halfLife=2000  →  第 2000 名权重减半，最贵件约 0.5%
        /// </summary>
        private static double[] RankWeights(List<Item> items, double ratio, double halfLife)
        {
            int n = items.Count;
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Array.Sort(idx, (a, b) => items[a].PriceUsdCents.CompareTo(items[b].PriceUsdCents));

            var w = new double[n];
            for (int rank = 0; rank < n; rank++)
                w[idx[rank]] = Math.Pow(ratio, rank / halfLife);
            return w;
        }

        /// <summary>
        /// 价格阶梯权重：按价位区间给固定概率。
        /// brackets 形如 { {100, 0.60}, {500, 0.25}, {2000, 0.12}, {无穷, 0.03} }
        /// 含义是「价格 < 上界」的区间占总概率的该比例。
        /// </summary>
        private static double[] BracketWeights(List<Item> items, double[][] brackets)
        {
            var w = new double[items.Count];
            // 先数每个区间有多少件
            var cnt = new int[brackets.Length];
            var slot = new int[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                // 注意：brackets 的阈值是「美元」，这里必须用 PriceUsd，
                // 早期误用 PriceUsdCents（分）导致所有物品都落进最后一档。
                double p = items[i].PriceUsd;
                int b = brackets.Length - 1;
                for (int k = 0; k < brackets.Length; k++)
                {
                    if (p < brackets[k][0]) { b = k; break; }
                }
                slot[i] = b;
                cnt[b]++;
            }
            // 区间总概率平摊到区间内每件 -> 实现「该区间的总命中率 = 设定值」
            for (int i = 0; i < items.Count; i++)
            {
                int b = slot[i];
                w[i] = cnt[b] > 0 ? brackets[b][1] / cnt[b] : 0;
            }
            return w;
        }

        private Pool MakePool(string key, string name, string kind, List<Item> items, int colorSeed)
        {
            var p = new Pool { Key = key, Name = name, Kind = kind, Accent = AccentFor(key, colorSeed) };
            p.Items.AddRange(items);
            p.Items.Sort((a, b) => b.PriceUsdCents.CompareTo(a.PriceUsdCents));
            if (p.Items.Count > 0) p.Cover = p.Items[0];

            long sum = 0;
            foreach (var it in p.Items) sum += it.PriceUsdCents;
            p.AvgPriceCents = p.Items.Count > 0 ? sum / p.Items.Count : 0;
            p.CostCents = (long)Math.Round(p.AvgPriceCents * 1.20);
            p.RecycleCents = (long)Math.Round(p.AvgPriceCents * 0.90);
            return p;
        }

        private void BuildPools()
        {
            int seed = 0;

            // 剔除名单中的物品（如 7 天内无成交）不进入任何奖池
            var usable = new List<Item>();
            foreach (var it in Items)
                if (!ExcludedIds.Contains(it.Id)) usable.Add(it);

            // 1) 按武器类型建池（只保留物品数够多的，避免碎片池）
            var byWeapon = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
            foreach (var it in usable)
            {
                if (string.IsNullOrEmpty(it.Weapon)) continue;
                List<Item> list;
                if (!byWeapon.TryGetValue(it.Weapon, out list))
                {
                    list = new List<Item>();
                    byWeapon[it.Weapon] = list;
                }
                list.Add(it);
            }

            var weaponNames = new List<string>(byWeapon.Keys);
            weaponNames.Sort(StringComparer.Ordinal);
            foreach (var w in weaponNames)
            {
                var list = byWeapon[w];
                if (list.Count < 40) continue;   // 太小的池子意义不大
                Pools.Add(MakePool("w:" + w, w + " 池", "weapon", list, seed++));
            }

            // 2) 匕首池 / 手套池
            var knives = new List<Item>();
            var gloves = new List<Item>();
            foreach (var it in usable)
            {
                if (it.Category == "匕首") knives.Add(it);
                else if (it.Category == "手套") gloves.Add(it);
            }
            if (knives.Count > 0) Pools.Add(MakePool("c:knife", "匕首池", "category", knives, seed++));
            if (gloves.Count > 0) Pools.Add(MakePool("c:glove", "手套池", "category", gloves, seed++));

            // 3) 顶级池：单价 1000 美元以上
            var top = new List<Item>();
            foreach (var it in usable)
                if (it.PriceUsdCents >= 100000) top.Add(it);
            if (top.Count > 0) Pools.Add(MakePool("s:top", "顶级池（$1000+）", "special", top, seed++));

            // 4) 全物品总池
            if (usable.Count > 0) Pools.Add(MakePool("s:all", "全饰品池", "special", usable, seed++));

            // 5) 奇迹池：极品极低概率 + 便宜保底，回报率与其它池持平
            BuildWeightedPools(usable, ref seed);
            BuildMiraclePools(usable, ref seed);

            // 按“封面价值”从高到低排列，顶级池排前面
            Pools.Sort((a, b) =>
            {
                int k = KindRank(a.Kind).CompareTo(KindRank(b.Kind));
                if (k != 0) return k;
                return b.AvgPriceCents.CompareTo(a.AvgPriceCents);
            });
        }

        /// <summary>
        /// 建立若干「奇迹池」。
        /// 每个池子 = 若干极品 + 1 个便宜保底，极品以极低概率掉落。
        /// 保底价位取「最便宜的一档」的均价，使便宜饰品本身不至于毫无价值。
        /// </summary>
        /// <summary>
        /// 4 个「特点池」：每个池子对应一种概率分布。
        /// 成本全部由「期望回收 ÷ 0.75」反解，回报率恒为 75%。
        /// </summary>
        private void BuildWeightedPools(List<Item> usable, ref int seed)
        {
            if (usable.Count < 200) return;

            var byPrice = new List<Item>(usable);
            byPrice.Sort((a, b) => a.PriceUsdCents.CompareTo(b.PriceUsdCents));
            int n = byPrice.Count;

            // ---------- 1) 白嫖极限池：幂律偏斜 ----------
            // 越贵越难出，但尾巴够长 —— 一直抽一直有小钱，偶尔爆一次
            {
                var items = new List<Item>(usable);
                // 中位名次附近权重减半 -> 便宜件占大头，但尾巴够长
                var w = RankWeights(items, 0.5, 1500);
                AddPool(MakeWeightedPool("w:payoff", "白嫖极限池", items, w, seed++));
            }

            // ---------- 2) 阶梯池：按价位给固定概率 ----------
            // 60% 便宜 / 25% 中档 / 12% 高档 / 3% 顶级
            {
                var items = new List<Item>(usable);
                var br = new double[][]
                {
                    new double[] { 50,    0.60 },
                    new double[] { 500,   0.25 },
                    new double[] { 2000,  0.12 },
                    new double[] { double.MaxValue, 0.03 },
                };
                var w = BracketWeights(items, br);
                AddPool(MakeWeightedPool("w:bracket", "阶梯池", items, w, seed++));
            }

            // ---------- 3) 精英池：只收最贵的前 10% ----------
            // 池内 20% 直接命中池里最顶尖的那批，其余按价倒数
            {
                int cnt = Math.Max(50, (int)(n * 0.10));
                var items = byPrice.GetRange(n - cnt, cnt);
                var w = new double[items.Count];
                // 池内最贵的 20% 给高权重
                int topCnt = Math.Max(1, (int)(items.Count * 0.20));
                for (int i = 0; i < items.Count; i++)
                {
                    bool isTop = i >= items.Count - topCnt;
                    w[i] = isTop ? 3.0 : 1.0;
                }
                AddPool(MakeWeightedPool("w:elite", "精英池", items, w, seed++));
            }

            // ---------- 4) 赌徒池 ----------
            // 大奖权重显著抬高（约 12%），其余按价倒数 —— 最容易翻身的池
            {
                var items = new List<Item>(usable);
                // 衰减更缓（halfLife 更大）-> 高档件本身就有可观份额
                var w = RankWeights(items, 0.5, 4000);
                // 再把最贵的 2% 名次额外加权，做成「赌大奖」的观感
                int topCnt = Math.Max(1, (int)(items.Count * 0.02));
                var rank = new int[items.Count];
                for (int i = 0; i < items.Count; i++) rank[i] = i;
                Array.Sort(rank, (a, b) => items[a].PriceUsdCents.CompareTo(items[b].PriceUsdCents));
                for (int k = items.Count - topCnt; k < items.Count; k++)
                    w[rank[k]] *= 12.0;
                AddPool(MakeWeightedPool("w:riddle", "赌徒池", items, w, seed++));
            }
        }

        void AddPool(Pool p) { Pools.Add(p); }

        private void BuildMiraclePools(List<Item> usable, ref int seed)
        {
            if (usable.Count < 200) return;

            var byPrice = new List<Item>(usable);
            byPrice.Sort((a, b) => a.PriceUsdCents.CompareTo(b.PriceUsdCents));

            // 便宜保底：取价格最低的一批的均价水平，选其中最便宜的一个作为实际掉落物
            Item cheap = byPrice[0];

            // 各分类的候选（按价格降序，取最贵的几件作极品）
            var desc = new List<Item>(usable);
            desc.Sort((a, b) => b.PriceUsdCents.CompareTo(a.PriceUsdCents));

            var knives = new List<Item>();
            var gloves = new List<Item>();
            var rifles = new List<Item>();
            // 同名去重：多普勒各相位（蓝宝石/红宝石/黑珍珠等）displayName 相同，
            // 若不去重，「最贵的 3 把」会出现三个同名条目。
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in desc)
            {
                if (!seen.Add(it.DisplayName)) continue;
                if (it.Category == "匕首" && knives.Count < 3) knives.Add(it);
                else if (it.Category == "手套" && gloves.Count < 3) gloves.Add(it);
                else if (rifles.Count < 3 && (it.Weapon == "AK-47" || it.Weapon == "M4A4" ||
                                              it.Weapon == "M4A1消音版" || it.Weapon == "AWP"))
                    rifles.Add(it);
            }

            if (rifles.Count >= 1)
                Pools.Add(MakeMiraclePool("m:rifle", "步枪奇迹池", rifles, cheap, seed++));
            if (knives.Count >= 1)
                Pools.Add(MakeMiraclePool("m:knife", "匕首奇迹池", knives, cheap, seed++));
            if (gloves.Count >= 1)
                Pools.Add(MakeMiraclePool("m:glove", "手套奇迹池", gloves, cheap, seed++));
            // 极·奇迹池：全站最贵的一件 + 保底
            if (desc.Count >= 1)
                Pools.Add(MakeMiraclePool("m:ultra", "极·奇迹池", new List<Item> { desc[0] }, cheap, seed++));
        }

        private static int KindRank(string kind)
        {
            if (kind == "special") return 0;
            if (kind == "miracle") return 1;
            if (kind == "weighted") return 2;
            if (kind == "category") return 3;
            return 4;
        }

        public Item FindById(int id)
        {
            foreach (var it in Items) if (it.Id == id) return it;
            return null;
        }
    }
}
