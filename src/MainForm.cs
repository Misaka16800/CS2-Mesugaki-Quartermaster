using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Cs2Roulette
{
    /// <summary>仓库条目行：图片 + 名称 + 价格 + 回收按钮。</summary>
    public sealed class VaultRow : Control
    {
        public OwnedItem Owned;
        public ImageCache Cache;
        public Color Accent = Theme.Accent;
        /// <summary>是否可汰换（超过上限时为 false，按钮置灰且点击无效）。</summary>
        public bool CanTrade = true;

        public event EventHandler<OwnedItem> RecycleClicked;
        public event EventHandler<OwnedItem> TradeClicked;

        private Rectangle _recycleRect;
        private Rectangle _tradeRect;
        private bool _hoverRecycle;
        private bool _hoverTrade;

        public VaultRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 72;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hr = _recycleRect.Contains(e.Location);
            bool ht = _tradeRect.Contains(e.Location);
            if (hr != _hoverRecycle || ht != _hoverTrade)
            {
                _hoverRecycle = hr;
                _hoverTrade = ht;
                Cursor = (hr || ht) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverRecycle = false;
            _hoverTrade = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Owned != null)
            {
                if (_recycleRect.Contains(e.Location))
                {
                    var h = RecycleClicked;
                    if (h != null) h(this, Owned);
                }
                else if (_tradeRect.Contains(e.Location) && CanTrade)
                {
                    var h = TradeClicked;
                    if (h != null) h(this, Owned);
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Owned == null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var gp = Round.Path(r, 10))
            using (var lg = new LinearGradientBrush(r, Theme.CardBg, Theme.CardBg2, 90f))
                g.FillPath(lg, gp);

            var it = Owned.Item;
            Color rarity = ReelPanel.RarityColor(it.Rarity);
            using (var b = new SolidBrush(rarity))
                g.FillRectangle(b, r.X, r.Y, 4, r.Height);

            // 图片
            var imgRect = new Rectangle(14, 8, 78, 56);
            if (Cache != null) ImageCache.DrawFit(g, Cache.Get(it.Image), imgRect);

            // 名称与信�?
            var fName = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            var fInfo = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            using (var b = new SolidBrush(Theme.TextMain))
                g.DrawString(it.PrettyName, fName, b, new RectangleF(104, 12, Width - 440, 20));
            using (var b = new SolidBrush(Theme.TextDim))
                g.DrawString(string.Format(CultureInfo.InvariantCulture,
                "{0} · {1} · {2} 品质{3}", it.Weapon, it.Category, it.Rarity,
                    it.Estimated ? "（瞎猜的价）" : ""), fInfo, b, new RectangleF(104, 36, Width - 440, 18));

            // 市价
            // 市价 + 汰换倍率（紧凑单行，避免文字被截断）
            using (var b = new SolidBrush(Theme.TextMain))
                g.DrawString(string.Format(CultureInfo.InvariantCulture,
                        "市价 ${0:N2}  {1:N1}-{2:N1}x",
                        it.PriceUsd, 1.7, 2.3),
                    fInfo, b, new RectangleF(Width - 452, 26, 236, 18));

            // 汰换按钮（左）：48% 概率变成 1.7~2.3 倍价值的随机饰品
            // 不可汰换时置灰并改文案
            _tradeRect = new Rectangle(Width - 258, 20, 122, 32);
            Color tradeTop, tradeBot;
            if (!CanTrade)
            {
                tradeTop = Color.FromArgb(58, 62, 72);
                tradeBot = Color.FromArgb(44, 48, 57);
            }
            else if (_hoverTrade)
            {
                tradeTop = Theme.AccentGlow;
                tradeBot = Theme.Accent;
            }
            else
            {
                tradeTop = Theme.Accent;
                tradeBot = Theme.AccentDeep;
            }
            using (var gp = Round.Path(_tradeRect, 8))
            using (var lg = new LinearGradientBrush(_tradeRect, tradeTop, tradeBot, 90f))
                g.FillPath(lg, gp);
            using (var b = new SolidBrush(CanTrade ? Color.White : Theme.TextFaint))
            using (var fBtn = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
                g.DrawString(CanTrade ? "赌 48%" : "不给赌", fBtn, b,
                    new RectangleF(_tradeRect.X, _tradeRect.Y + 7, _tradeRect.Width, 20),
                    new StringFormat { Alignment = StringAlignment.Center });

            // 回收按钮（右）
            long recycle = (long)Math.Round(it.PriceUsdCents * 0.90);
            _recycleRect = new Rectangle(Width - 128, 20, 112, 32);
            using (var gp = Round.Path(_recycleRect, 8))
            using (var lg = new LinearGradientBrush(_recycleRect,
                _hoverRecycle ? Color.FromArgb(250, 160, 70) : Theme.Accent,
                _hoverRecycle ? Theme.Accent : Theme.AccentDeep, 90f))
                g.FillPath(lg, gp);
            using (var b = new SolidBrush(Color.FromArgb(24, 20, 16)))
                g.DrawString(string.Format(CultureInfo.InvariantCulture, "卖 {0}", recycle),
                    new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), b,
                    new RectangleF(_recycleRect.X, _recycleRect.Y + 7, _recycleRect.Width, 20),
                    new StringFormat { Alignment = StringAlignment.Center });

            fName.Dispose(); fInfo.Dispose();
            using (var gp = Round.Path(r, 10))
            using (var pen = new Pen(Color.FromArgb(55, rarity), 1.2f))
                g.DrawPath(pen, gp);
        }
    }

    public sealed class MainForm : Form
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        // 主�?
        static readonly Color BgTop = Theme.BgTop;
        static readonly Color BgBottom = Theme.BgBottom;
        static readonly Color CardBg = Theme.CardBg;
        static readonly Color TextMain = Theme.TextMain;
        static readonly Color TextDim = Theme.TextDim;
        static readonly Color Accent = Theme.Accent;      // CS2 �?
        static readonly Color CoinGold = Theme.Gold;

        readonly UiAssets _ui;
        readonly ItemDatabase _db;
        readonly ImageCache _cache;
        readonly CoinWallet _wallet = new CoinWallet();
        readonly Inventory _inv = new Inventory();
        readonly Random _rnd = new Random();
        readonly SaveData _save;
        readonly string _dataDir;
        readonly bool _autoDraw;
        readonly int _startTab;

        // 控件
        Panel _navBar, _pageHost;

        // ---- 限时高价回收 ----
        readonly List<OwnedItem> _lastWon = new List<OwnedItem>();   // 本次抽奖入仓的对象
        Panel _offerPane;                    // 浮层容器
        Label _lblOfferList, _lblOfferPrice, _lblOfferCountdown, _lblOfferHint;
        NeonButton _btnOfferSell, _btnOfferKeep;
        Timer _offerTimer, _offerTick;
        DateTime _offerDeadline;
        long _offerGain;
        // 限时收购的时限（秒）。用户要求 5 秒。
        const int OfferSeconds = 5;
        const double OfferRate = 0.95;      // 限时价 = 市价 ×95%（仓库常规是 90%）
        PictureBox _mascot;      // 菜单页立绘
        Panel _heroPane;         // 立绘容器（负责裁切）
        NeonButton[] _navButtons;
        readonly List<Panel> _pages = new List<Panel>();
        int _currentPage;
        Panel _drawRoot, _menuPane, _detailPane;
        FlowLayoutPanel _poolGrid;
        ReelPanel _reel;                    // 单抽用的第一个卷轴
        ReelPanel[] _reels;                 // 单抽 / 五连抽共用的卷轴组
        Panel _reelHost;                    // 容纳 5 个堆叠卷轴
        NeonButton _btnDraw5;               // 五连抽按钮
        Label _lblCoins, _lblPoolInfo, _lblDrawCost, _lblStatus;
        Label _lblCorner;                   // 右下角标识（淡淡的）
        Label _lblDebug;
        TextBox _txtDebug;
        string _vaultSummaryText = "";
        Label _lblMenuHint, _lblDetailTitle;
        NeonButton _btnDraw, _btnMute, _btnBack;
        Pool _selected;
        bool _lastWasJackpot;
        readonly bool _forceJackpot;
        readonly int _autoPoolIndex;
        readonly string _autoPoolKey;
        readonly bool _testCd;
        readonly bool _testOffer;
        readonly bool _autoSell;
        readonly bool _autoKeep;
        readonly bool _showAbout;
        readonly int _poolStat;
        readonly int _drawCount;
        readonly string _poolSwitch;
        readonly bool _benchDraw;
        readonly bool _testVault;
        readonly bool _testDebug;
        readonly int _testTrade;
        readonly bool _testLimit;
        readonly int _testQuiz;
        readonly int _quizDist;
        readonly int _testCmp;
        readonly bool _poolDump;
        readonly long _forceCoins;

        // 赚金�?
        Label _lblMathQuestion, _lblMathFeedback, _lblQuizQuestion, _lblQuizFeedback, _lblEarnHint;
        CompareProblem _cmp;
        NeonButton _btnCmpGreater, _btnCmpLess, _btnCmpEqual;
        NeonButton _btnNewQuestion, _btnCooldown, _btnQuizA, _btnQuizB, _btnQuizC, _btnQuizD;
        QuizQuestion _quiz;
        int _mathStreak;
        DateTime _cooldownUntil = DateTime.MinValue;
        Timer _cooldownTimer;

        // 仓库
        FlowLayoutPanel _vaultList;
        NeonButton _btnRecycleAll;

        public MainForm(ItemDatabase db, string dataDir, SaveData save, bool autoDraw = false, int startTab = 0, bool forceJackpot = false, int autoPoolIndex = 0, bool benchDraw = false, bool testVault = false, bool testDebug = false, int testTrade = 0, bool testLimit = false, int testQuiz = 0, int quizDist = 0, int testCmp = 0, bool poolDump = false, long forceCoins = -1, string autoPoolKey = null, bool testCd = false, int drawCount = 1, bool testOffer = false, bool autoSell = false, bool autoKeep = false, string poolSwitch = null, bool showAbout = false, int poolStat = 0)
        {
            _ui = InitUiAssets(dataDir);
            // F1 打开「关于」
            this.KeyPreview = true;
            this.KeyDown += (s, ev) => { if (ev.KeyCode == Keys.F1) ShowAbout(); };
            // 注意：必须用构造函数参数判断，不能读 _showAbout —— 它在本行之后才赋值
            if (showAbout) Shown += (s, ev) => BeginInvoke((Action)ShowAbout);
            _db = db;
            _dataDir = dataDir;
            _save = save;
            _autoDraw = autoDraw;
            _startTab = startTab;
            _forceJackpot = forceJackpot;
            _autoPoolIndex = autoPoolIndex;
            _autoPoolKey = autoPoolKey;
            _testCd = testCd;
            _testOffer = testOffer;
            _autoSell = autoSell;
            _autoKeep = autoKeep;
            _showAbout = showAbout;
            _poolStat = poolStat;
            _drawCount = drawCount;
            _poolSwitch = poolSwitch;
            _benchDraw = benchDraw;
            _testVault = testVault;
            _testDebug = testDebug;
            _testTrade = testTrade;
            _testLimit = testLimit;
            _testQuiz = testQuiz;
            _quizDist = quizDist;
            _testCmp = testCmp;
            _poolDump = poolDump;
            _forceCoins = forceCoins;
            if (benchDraw) { try { AttachConsole(-1); } catch { } }
            _cache = new ImageCache(System.IO.Path.Combine(dataDir, "images"));

            Text = "CS2 雌小鬼军需 ｜ 免费分享 ｜ 才不是给你白嫖的呢";
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1280, wa.Width - 60), Math.Min(820, wa.Height - 60));
            MinimumSize = new Size(1040, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgTop;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            DoubleBuffered = true;
            KeyPreview = true;

            // 音乐合成放到后台线程�? �?10 秒曲�?��计约 9 秒，
            // 若在首�?点击抽�?时同步合成，会�?动画延后�?10 秒�??
            System.Threading.ThreadPool.QueueUserWorkItem(_ => Music.WarmUp());
            LoadSave();
            BuildUi();

            Shown += (s, e) =>
            {
                // 主界�?���?��任何音乐（音乐只在点「抽奖�?�后�?���?
                NewMathProblem();
                NewQuiz();
                RefreshCooldown();

                // 直接打开指定页面（用于截图验�?/ �??�进入某页）
                if (_startTab > 0 && _startTab < _pages.Count)
                    SwitchPage(_startTab);

                // 测试�?���?仓库注入若干物品，便于验证仓库页布局
                // 测试用：注入「超上限」与「可汰换」各一件，验证汰换按钮状态
                if (_testLimit && _inv.Items.Count == 0)
                {
                    var all = new List<Item>();
                    foreach (var p in _db.Pools)
                        if (p.Key == "s:all") { all.AddRange(p.Items); break; }
                    Item over = null, okItem = null;
                    foreach (var it in all)
                    {
                        if (over == null && !CanTrade(it)) over = it;
                        if (okItem == null && CanTrade(it)) okItem = it;
                        if (over != null && okItem != null) break;
                    }
                    if (over != null)
                    {
                        _inv.Add(over, over.PriceUsdCents);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "LIMIT over {0} = ${1:F2} canTrade={2} limit=${3:F2}",
                            over.DisplayName, over.PriceUsd, CanTrade(over), _maxTradeableCents / 100.0));
                    }
                    if (okItem != null)
                    {
                        _inv.Add(okItem, okItem.PriceUsdCents);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "LIMIT ok   {0} = ${1:F2} canTrade={2}",
                            okItem.DisplayName, okItem.PriceUsd, CanTrade(okItem)));
                    }
                }
                if (_testVault && _inv.Items.Count == 0)
                {
                    for (int k = 0; k < 3 && k < _db.Items.Count; k++)
                    {
                        var sample = _db.Items[_db.Items.Count - 1 - k * 37];
                        _inv.Add(sample, sample.PriceUsdCents);
                    }
                }
                // 测试用：依次输入全部调试口令（验证每条都能发币）
                if (_testDebug && _txtDebug != null)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "DEBUG start coins={0}", _wallet.Coins));
                    int delay = 400;
                    for (int ci = 0; ci < DebugCodes.Length; ci++)
                    {
                        int idx = ci;
                        var tD = new Timer { Interval = delay };
                        tD.Tick += (s3, e3) =>
                        {
                            tD.Stop(); tD.Dispose();
                            _txtDebug.Text = DebugCodes[idx];
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "DEBUG input[{0}] = {1}  ->  coins={2}",
                                idx, DebugCodes[idx], _wallet.Coins));
                        };
                        tD.Start();
                        delay += 500;
                    }
                }
                RefreshVault();

                // 自动答对 N 道比大小（--testcmp N）
                if (_testCmp > 0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "CMP start coins={0}", _wallet.Coins));
                    int delay = 300;
                    for (int ci = 0; ci < _testCmp; ci++)
                    {
                        // 闭包变量需复制，否则各 Timer 触发时 ci 都是终值
                        int ci2 = ci;
                        var tC = new Timer { Interval = delay };
                        tC.Tick += (sc, ec) =>
                        {
                            tC.Stop(); tC.Dispose();
                            if (_cmp != null) SubmitCompare(_cmp.Result);
                        };
                        tC.Start();
                        delay += 320;   // 与 250ms 换题间隔匹配
                    }
                    var tEnd = new Timer { Interval = delay + 1200 };
                    tEnd.Tick += (sc, ec) =>
                    {
                        tEnd.Stop(); tEnd.Dispose();
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "CMP final coins={0}", _wallet.Coins));
                        BeginInvoke((Action)Close);
                    };
                    tEnd.Start();
                    return;
                }

                // 测试用：把金币设成指定值（--coins N），便于验证低金币弹窗
                if (_forceCoins >= 0)
                {
                    long delta = _forceCoins - _wallet.Coins;
                    if (delta > 0) _wallet.Earn(delta);
                    else if (delta < 0) _wallet.TrySpend(-delta);
                    UpdateCoinLabel();
                    SetStatus(string.Format(CultureInfo.InvariantCulture,
                        "测试金币已设为 {0:N0}", _wallet.Coins));
                    MaybeWarnLowCoins();
                }




                // 直接触发限时高价回收浮层（--testoffer），用于截图与验证
                if (_testOffer)
                {
                    var pool0 = _db.Pools.Count > 3 ? _db.Pools[3]
                              : (_db.Pools.Count > 0 ? _db.Pools[0] : null);
                    if (pool0 != null && pool0.Items.Count > 0)
                    {
                        EnterPool(pool0);
                        _lastWon.Clear();
                        for (int k = 0; k < 3; k++)
                        {
                            var it = pool0.Items[Math.Min(k * 7 + 3, pool0.Items.Count - 1)];
                            _lastWon.Add(_inv.Add(it, pool0.CostCents));
                        }
                        _selected = pool0;
                        ShowLimitedOffer();
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "OFFER items={0} gain={1}", _lastWon.Count, _offerGain));
                    }
                    return;
                }

                // 自动领一次休息奖励（--testcd）
                if (_testCd)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "CD before={0}", _wallet.Coins));
                    ClaimCooldown();
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "CD after={0} next={1:HH:mm:ss}", _wallet.Coins, _cooldownUntil));
                    BeginInvoke((Action)Close);
                    return;
                }

                // 依次进入两个奖池，验证切换时预览是否刷新（--poolswitch "k1,k2"）
                if (!string.IsNullOrEmpty(_poolSwitch))
                {
                    var keys = _poolSwitch.Split(',');
                    int delay = 600;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        string key = keys[i].Trim();
                        var tSw = new Timer { Interval = delay };
                        tSw.Tick += (ss, ee) =>
                        {
                            tSw.Stop(); tSw.Dispose();
                            foreach (var p in _db.Pools)
                                if (p.Key == key) { EnterPool(p); break; }
                        };
                        tSw.Start();
                        delay += 3500;
                    }
                    var tEnd2 = new Timer { Interval = delay + 500 };
                    tEnd2.Tick += (ss, ee) => { tEnd2.Stop(); tEnd2.Dispose(); };
                    tEnd2.Start();
                    return;
                }


                // 奖池统计压测（--poolstat N）：每池抽 N 次，输出实测分布
                if (_poolStat > 0)
                {
                    var rnd2 = new Random(12345);
                    Console.WriteLine("POOLSTAT times=" + _poolStat);
                    foreach (var pool in _db.Pools)
                    {
                        if (pool.Items.Count == 0) continue;
                        // 只统计特点池与奇迹池，避免刷屏
                        if (pool.Kind != "weighted" && pool.Kind != "miracle") continue;

                        long sum = 0, spent = 0;
                        int c0 = 0, c1 = 0, c2 = 0, c3 = 0, c4 = 0, cTop = 0;
                        double maxP = 0;
                        foreach (var it in pool.Items)
                            if (it.PriceUsdCents > maxP) maxP = it.PriceUsdCents;

                        for (int k = 0; k < _poolStat; k++)
                        {
                            var it = pool.Pick(rnd2);
                            double p = it.PriceUsd;
                            sum += it.PriceUsdCents;
                            spent += pool.CostCents;
                            if (p < 2) c0++;
                            else if (p < 50) c1++;
                            else if (p < 500) c2++;
                            else if (p < 2000) c3++;
                            else c4++;
                            if (p >= maxP - 0.01) cTop++;
                        }
                        double N = _poolStat;
                        double avg = sum / N / 100.0;
                        double costGold = pool.CostCents;
                        double ret = spent > 0 ? (double)sum / spent : 0;   // 成本已由期望回收反解，这里直接比
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "POOLSTAT {0}|cost={1}|avg=${2:F2}|ret={3:P2}|"
                            + "<2={4:P1}|2-50={5:P1}|50-500={6:P1}|500-2000={7:P1}|2000+={8:P1}|top={9:P3}",
                            pool.Name, costGold, avg, ret,
                            c0 / N, c1 / N, c2 / N, c3 / N, c4 / N, cTop / N));
                    }
                    BeginInvoke((Action)Close);
                    return;
                }

                // 奖池统计（--pooldump）
                if (_poolDump)
                {
                    RunPoolDump();
                    BeginInvoke((Action)Close);
                    return;
                }

                // 题库随机性采样（--quizdist N）
                if (_quizDist > 0)
                {
                    RunQuizDist(_quizDist);
                    BeginInvoke((Action)Close);
                    return;
                }

                // 自动答对 N 道题（--testquiz N）
                if (_testQuiz != 0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "QUIZ start coins={0} bankSize={1}", _wallet.Coins, GameBank.Count));
                    int delay = 400;
                    int quizN = Math.Abs(_testQuiz);
                    // 负值 = 故意答错（用于验证答错反馈路径不崩溃）
                    bool forceWrong = _testQuiz < 0;
                    for (int qi = 0; qi < quizN; qi++)
                    {
                        // 必须复制：for 循环变量被闭包共享，否则各 Timer 触发时 qi 都是终值
                        int idx = qi;
                        var tQ = new Timer { Interval = delay };
                        tQ.Tick += (sq, eq) =>
                        {
                            tQ.Stop(); tQ.Dispose();
                            if (_quiz != null)
                            {
                                int pick = forceWrong
                                    ? (_quiz.CorrectIndex + 1 + idx) % 4   // 保证选中错误项
                                    : _quiz.CorrectIndex;
                                AnswerQuiz(pick);
                            }
                        };
                        tQ.Start();
                        delay += 250;
                    }
                    var tEnd = new Timer { Interval = delay + 400 };
                    tEnd.Tick += (sq, eq) =>
                    {
                        tEnd.Stop(); tEnd.Dispose();
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "QUIZ final coins={0}", _wallet.Coins));
                        BeginInvoke((Action)Close);
                    };
                    tEnd.Start();
                    return;
                }

                // 批量汰换测试（--testtrade N）
                if (_testTrade > 0)
                {
                    RunTradeBench(_testTrade);
                    BeginInvoke((Action)Close);
                    return;
                }

                // 自动抽一次：用于验证动画（--autodraw）
                if (_autoDraw)
                {
                    var t = new Timer { Interval = 900 };
                    t.Tick += (s2, e2) =>
                    {
                        t.Stop(); t.Dispose();
                        // �?��抽�?时进入指定池（默认�?�?�?��并给足金币，避免因余额不足中�?���?
                        if (_db.Pools.Count > 0)
                        {
                            int pi = ResolvePoolIndex();
                            EnterPool(_db.Pools[pi]);
                            _wallet.Earn(_db.Pools[pi].CostCents * Math.Max(1, _drawCount));
                        }
                        UpdateCoinLabel();
                        DoDraw(_drawCount);
                    };
                    t.Start();
                }
            };
        }

        // ==================================================== 存档
        private void LoadSave()
        {
            _wallet.Coins = _save.Coins;
            _wallet.TotalEarned = _save.TotalEarned;
            _wallet.TotalSpent = _save.TotalSpent;
            foreach (var id in _save.OwnedItemIds)
            {
                var it = _db.FindById(id);
                if (it != null) _inv.Add(it, it.PriceUsdCents);
            }
        }

        private void PersistSave()
        {
            _save.Coins = _wallet.Coins;
            _save.TotalEarned = _wallet.TotalEarned;
            _save.TotalSpent = _wallet.TotalSpent;
            _save.OwnedItemIds.Clear();
            foreach (var o in _inv.Items) _save.OwnedItemIds.Add(o.Item.Id);
            _save.Save(_dataDir);
        }

        // ==================================================== 界面
        private void BuildUi()
        {
            // 顶栏
            var header = new Panel { BackColor = Color.Transparent, Height = 110 };
            var title = new Label
            {
                Text = "CS2 雌小鬼军需",
                Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 12),
            };
            var sub = new Label
            {
                Text = "本小姐免费赏你玩的 · 金币是本小姐赏的，不能换钱啦",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Accent,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(23, 40),
            };
            header.Controls.Add(title);
            header.Controls.Add(sub);

            _lblCoins = new Label
            {
                Text = "",
                Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
                ForeColor = CoinGold,
                BackColor = Color.Transparent,
                // 固定宽度 + 右对齐：文字再长也只向左扩展，不会压到右侧的音效按钮
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
            };
            header.Controls.Add(_lblCoins);

            _btnMute = new NeonButton
            {
                Text = "🔊 出声",
                Accent = Accent,
                Ghost = true,
                Size = new Size(96, 34),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            };
            _btnMute.Click += (s, e) =>
            {
                Music.Muted = !Music.Muted;
                _btnMute.Text = Music.Muted ? "🔇 闭嘴" : "🔊 出声";
                _btnMute.Ghost = Music.Muted;
                _btnMute.Invalidate();
                // 静音时立即停掉�?在播放的音乐；取消静音不主动�?��（音乐只在抽奖时响）
                if (Music.Muted) Music.StopAll();
            };
            header.Controls.Add(_btnMute);
            header.Resize += (s, e) =>
            {
                // 音效按钮固定在右侧
                _btnMute.SetBounds(header.Width - 116, 14, 96, 34);
                // 币数右边界 = 音效按钮左侧 − 14px，右对齐；宽度给足，超出会自动向左占位
                _lblCoins.SetBounds(header.Width - 620, 16, 490, 30);
                _lblCoins.BringToFront();
            };
            _header = header;
            Controls.Add(header);

            // 结构：把 header 与导航栏合并进一个固定高度的顶部区域
            // 页面容器�?��其余空间。显式指定高度，避免停靠顺序造成重叠�?
            _navBar = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Theme.SlotBg2 };
            var navItems = new[] { "抽一发", "去搬砖", "仓库" };
            _navButtons = new NeonButton[navItems.Length];
            for (int i = 0; i < navItems.Length; i++)
            {
                int idx = i;
                var nb = new NeonButton
                {
                    Text = navItems[i],
                    Accent = Accent,
                    Ghost = true,
                    Size = new Size(132, 36),
                    Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                    Location = new Point(16 + i * 140, 6),
                };
                nb.Click += (s, e) => SwitchPage(idx);
                _navButtons[i] = nb;
                _navBar.Controls.Add(nb);
            }
            header.Controls.Add(_navBar);

            // 页面容器：三个页面叠放，靠可见性切换
            _pageHost = new Panel { BackColor = Theme.CardBg2 };
            Controls.Add(_pageHost);

            _pages.Add(BuildDrawTab());
            _pages.Add(BuildEarnTab());
            _pages.Add(BuildVaultTab());
            foreach (var p in _pages)
            {
                p.Dock = DockStyle.Fill;
                p.Visible = false;
                _pageHost.Controls.Add(p);
            }
            SwitchPage(0);

            _lblStatus = new Label
            {
                Height = 30,
                ForeColor = TextDim,
                BackColor = Theme.CardBg2,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Font = new Font("Microsoft YaHei UI", 8.5f),
            };
            Controls.Add(_lblStatus);

            // 角落标识：淡淡的，看得见但不碍事
            _lblCorner = new Label
            {
                Height = StatusH,
                Width = 150,
                Text = "HFUT2026_CKX",
                ForeColor = Color.FromArgb(58, 58, 92),   // 比背景略亮一点点
                BackColor = Theme.CardBg2,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 14, 0),
                Font = new Font("Segoe UI", 7.5f),
            };
            Controls.Add(_lblCorner);

            // 结构：顶部 header 固定高 110（含导航栏），底部状态栏 30
            // 页面容器�?���?��。用固定高度 + Padding 双重保险�?
            // 避免停靠顺序导致页面内�?盖住顶部�?
            // 固定停靠顺序：header 最外，其次页面容器，最后状态栏




            UpdateCoinLabel();
            UpdateStatus();
            LayoutChrome();      // 所有控件就绪后再摆一次，确保 pageHost 让出顶部空间

            
            ComputeTradeLimit();
        }

        /// <summary>切换顶部导航页面。�?�中项高�?��其余为幽灵�?��??/summary>
        private void SwitchPage(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            _currentPage = index;
            for (int i = 0; i < _pages.Count; i++)
                _pages[i].Visible = (i == index);
            _pages[index].BringToFront();
            for (int i = 0; i < _navButtons.Length; i++)
            {
                var nb = _navButtons[i];
                if (nb == null) continue;
                // 选中：实心高�?���??�中：幽灵�??
                nb.Ghost = (i != index);
                nb.Accent = (i == index) ? Accent : Theme.Muted;
                nb.Invalidate();
            }
            if (index == 2) RefreshVault();     // 进入仓库时刷新列�?
            if (index != 0) Music.StopAll();    // 离开抽�?页时停�?音乐
        }

        // ------------------------------------------------ 抽�?�?
        private Panel BuildDrawTab()
        {
            var page = new Panel { BackColor = Theme.CardBg2, Padding = new Padding(12) };

            // 二级菜单容器：主页（选池） / 详情页（抽奖）
            _drawRoot = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            // ==================== 一级：奖池主菜单（无音乐） ====================
            _menuPane = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            var menuHead = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.Transparent };
            var lblPools = new Label
            {
                Text = "自己挑个池子啦",
                Dock = DockStyle.Left,
                Width = 220,
                ForeColor = TextMain,
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
            };
            _lblMenuHint = new Label
            {
                Text = "点一个呀～ 磨蹭什么",
                Dock = DockStyle.Fill,
                ForeColor = TextDim,
                Font = new Font("Microsoft YaHei UI", 9.5f),
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 10, 0),
            };
            menuHead.Controls.Add(_lblMenuHint);
            menuHead.Controls.Add(lblPools);

            _poolGrid = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent,
                // 右侧留出立绘栏的宽度，避免卡片被立绘遮住
                Padding = new Padding(4, 4, 308, 4),
            };
            // ---- 右侧立绘栏（只在奖池菜单显示）----
            _heroPane = new MascotPane
            {
                Dock = DockStyle.Right,
                Width = 268,   // 全身立绘所需宽度
            };

            _mascot = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
            };
            var art = _ui.Get("mascot_002.png") ?? _ui.Get("mascot_002_upper.png")
                      ?? _ui.Get("mascot_001.png") ?? _ui.Get("mascot_001_upper.png");
            if (art != null) _mascot.Image = art;
            _heroPane.Controls.Add(_mascot);

            _menuPane.Controls.Add(_heroPane);
            _menuPane.Controls.Add(_poolGrid);
            _menuPane.Controls.Add(menuHead);

            // ==================== 二级：单个奖池详情 ====================
            _detailPane = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };

            // 顶部：返回按钮 + 池名
            var detailHead = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.Transparent };
            _btnBack = new NeonButton
            {
                Text = "← 回去啦",
                Accent = Theme.Muted,
                Ghost = true,
                Size = new Size(140, 38),
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                Location = new Point(2, 4),
            };
            _btnBack.Click += (s, e) => BackToMenu();
            _lblDetailTitle = new Label
            {
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(156, 10),
            };
            detailHead.Controls.Add(_btnBack);
            detailHead.Controls.Add(_lblDetailTitle);

            // 底部：信息 + 抽奖按钮
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 140, BackColor = Color.Transparent };
            _lblPoolInfo = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                ForeColor = TextMain,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                Padding = new Padding(4, 4, 0, 0),
                AutoEllipsis = true,
            };
            _lblDrawCost = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                ForeColor = TextDim,
                Font = new Font("Microsoft YaHei UI", 9f),
                Padding = new Padding(4, 2, 0, 0),
            };
            _btnDraw = new NeonButton
            {
                Text = "抽一发嘛～",
                Accent = Accent,
                Size = new Size(240, 50),
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(4, 82),
            };
            _btnDraw.Click += (s, e) => DoDraw(1);

            _btnDraw5 = new NeonButton
            {
                Text = "五连抽！",
                Accent = Theme.AccentGlow,   // 五连抽
                Size = new Size(260, 50),
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(256, 82),
            };
            _btnDraw5.Click += (s, e) => DoDraw(5);
            // Dock 顺序：先加的靠外
            bottom.Controls.Add(_lblPoolInfo);
            bottom.Controls.Add(_lblDrawCost);
            bottom.Controls.Add(_btnDraw);
            bottom.Controls.Add(_btnDraw5);

            // ---- 卷轴组：5 个堆叠，单抽只用第一个，五连抽全用 ----
            _reelHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            _reelHost.Resize += (s, e) => LayoutReels();
            _reels = new ReelPanel[5];
            for (int i = 0; i < _reels.Length; i++)
            {
                var r = new ReelPanel
                {
                    Cache = _cache,
                    Accent = Accent,
                    SlotWidth = 78,
                    SlotHeight = 84,
                    Visible = i == 0,
                };
                var cap = r;
                r.Settled += (s, e) => OnReelSettled(cap);
                r.RollStarted += (s, e) =>
                {
                    // 只有第一个卷轴负责启动音乐（只播一次），
                    // 并在启动音乐的同一刻记下时间原点；随后所有卷轴共用它，
                    // 做到「音乐一响，动画就动」。
                    // 第一个卷轴负责启动音乐（只播一次）；随后每个卷轴
                    // 在各自开转的这一刻记录时间原点，保证与音乐同源。
                    if (!_rollMusicStarted)
                    {
                        _rollMusicStarted = true;
                        Music.StartRollMusic();
                    }
                    cap.MarkExternalStart();

                };
                _reels[i] = r;
                _reelHost.Controls.Add(r);
            }
            _reel = _reels[0];
            LayoutReels();

            _detailPane.Controls.Add(_reelHost);
            _detailPane.Controls.Add(bottom);
            _detailPane.Controls.Add(detailHead);

            _detailPane.Controls.Add(MakeMascotColumn("mascot_001.png", 300));
            _drawRoot.Controls.Add(_detailPane);
            _drawRoot.Controls.Add(_menuPane);
            page.Controls.Add(_drawRoot);

            foreach (var p in _db.Pools)
            {
                var card = new PoolCard { Pool = p, Cache = _cache, Margin = new Padding(5) };
                var captured = p;
                card.Click += (s, e) => EnterPool(captured);
                _poolGrid.Controls.Add(card);
            }
            return page;
        }

        /// <summary>进入某个奖池的二级菜单�??/summary>
        private void EnterPool(Pool p)
        {
            _selected = p;
            _menuPane.Visible = false;
            _detailPane.Visible = true;
            _detailPane.BringToFront();

            _lblDetailTitle.Text = p.Name;
            _lblDetailTitle.ForeColor = ColorUtil.Lighten(p.Accent, 0.35);
            if (p.IsMiracle && p.CheapItem != null)
            {
                var rares = new List<string>();
                foreach (var it in p.RareItems)
                    rares.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}（${1:N2}）", it.PrettyName, it.PriceUsd));
                _lblPoolInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "极品 {0} 件（均价 ${1:N2}），才 {2:0.#}% 出哦   ·   剩下全是这货：{3}（${4:N2}）",
                    p.RareItems.Count, p.RareAvgCents / 100.0, p.RareChance * 100,
                    p.CheapItem.PrettyName, p.CheapItem.PriceUsd);
            }
            else
            {
                _lblPoolInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} 件   ·   均价 ${1:N2}   ·   最贵的那个：{2}",
                    p.Items.Count, p.AvgPriceCents / 100.0,
                    p.Cover != null ? p.Cover.PrettyName : "—");
            }
            // 按要求：不展示任何回报率 / 回收金额提示
            _lblDrawCost.Text = string.Format(CultureInfo.InvariantCulture,
                "抽一次 {0} 金币   ·   最贵 ${1:N2}\r\n"
                + "转 10 秒   ·   抽到最贵前 20% 才有金光，想得美",
                p.CostCents, p.Cover != null ? p.Cover.PriceUsd : 0);

            _btnDraw.Enabled = true;
            if (_btnDraw5 != null) _btnDraw5.Enabled = true;
            // 5 个卷轴都要换成新池子的预览；只刷第一个会让其余 4 个残留上个池子的结果画面
            for (int i = 0; i < _reels.Length; i++)
            {
                _reels[i].Visible = i == 0;
                _reels[i].ShowPreview(p.Items);
            }
            LayoutReels();
            UpdateCoinLabel();
            MaybeWarnLowCoins();
            SetStatus("进「" + p.Name + "」了，抽不抽？");
        }

        /// <summary>返回奖池主菜单�?�主菜单不播放音乐�??/summary>
        private void BackToMenu()
        {
            Music.StopAll();
            _reel.Stop();
            _detailPane.Visible = false;
            _menuPane.Visible = true;
            _menuPane.BringToFront();
            _btnDraw.Enabled = true;
            SetStatus("选择奖池");
        }

        /// <summary>五连抽时，相邻两个卷轴的启动间隔（毫秒）。</summary>
        private const double MultiStaggerMs = 1000;

        /// <summary>把可见的卷轴纵向铺满。</summary>
        private void LayoutReels()
        {
            if (_reelHost == null || _reels == null) return;
            int shown = 0;
            for (int i = 0; i < _reels.Length; i++) if (_reels[i].Visible) shown++;
            if (shown == 0) return;

            int gap = 6;
            int h = Math.Max(40, (_reelHost.ClientSize.Height - gap * (shown - 1)) / shown);
            int y = 0;
            for (int i = 0; i < _reels.Length; i++)
            {
                if (!_reels[i].Visible) continue;
                _reels[i].SetBounds(0, y, _reelHost.ClientSize.Width, h);
                y += h + gap;
            }
        }

        /// <summary>本次抽奖待结算的卷轴数。</summary>
        private int _pendingReels;
        /// <summary>本次抽奖是否已启动转动音乐（保证只播一次）。</summary>
        private bool _rollMusicStarted;

        /// <summary>
        /// 抽奖入口。count = 1 单抽，count = 5 五连抽。
        ///
        /// 五连抽错峰规则：第 i 个卷轴延迟 i 秒启动，但**共享同一结束时刻**，
        /// 因此各卷轴滚动时长依次递减 1 秒（10 / 9 / 8 / 7 / 6 秒）。
        /// </summary>
        private void DoDraw(int count)
        {
            if (_selected == null) { SetStatus(Pick(MsgNoPool)); return; }
            if (count < 1) count = 1;
            if (count > _reels.Length) count = _reels.Length;

            long need = _selected.CostCents * count;
            if (!_wallet.TrySpend(need))
            {
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "金币不够啦～ 要 {0}，你才 {1}。  {2}",
                    need, _wallet.Coins, Pick(MsgPoor)));
                System.Media.SystemSounds.Exclamation.Play();
                return;
            }

            // 每个卷轴各抽一个结果
            var winners = new List<Item>();
            for (int i = 0; i < count; i++) winners.Add(_selected.Pick(_rnd));

            // 测试用：强制第一个为大奖
            if (_forceJackpot && _selected.Items.Count > 0) winners[0] = _selected.Items[0];

            // 任一结果为前 20%（奇迹池为极品）即算金光大奖
            _lastWasJackpot = false;
            for (int i = 0; i < count; i++)
                if (IsJackpot(winners[i])) _lastWasJackpot = true;

            _pendingReels = count;
            _rollMusicStarted = false;   // 本次抽奖的音乐尚未启动

            for (int i = 0; i < _reels.Length; i++) _reels[i].Visible = i < count;
            LayoutReels();

            _drawStartMs = _drawWatch.Elapsed.TotalMilliseconds;

            // 先把抽到的东西立刻入仓——这样动画期间切走也不会白扣金币
            long unitPrice = _selected.CostCents;
            _lastWon.Clear();
            foreach (var w in winners)
                _lastWon.Add(_inv.Add(w, unitPrice > 0 ? unitPrice : w.PriceUsdCents));
            _save.DrawCount += winners.Count;
            PersistSave();
            RefreshVault();

            // 注意：转动音乐不在这里启动。
            // Spin() 内部要先预渲染约 1 秒，若此处先放音乐就会与动画错开。
            // 改由第一个卷轴在预渲染完成的那一刻（RollStarted）启动音乐。
            double totalMs = Music.RollSeconds * 1000.0;
            for (int i = 0; i < count; i++)
            {
                _reels[i].StartDelayMs = i * MultiStaggerMs;
                double dur = totalMs - i * MultiStaggerMs;   // 保证同时结束
                _reels[i].Spin(_selected.Items, winners[i], dur, IsJackpot(winners[i]));
            }

            _btnDraw.Enabled = false;
            if (_btnDraw5 != null) _btnDraw5.Enabled = false;
            SetStatus(count == 1
                ? "转啊转…… 等 10 秒啦，别催"
                : string.Format(CultureInfo.InvariantCulture,
                    "五连抽！{0} 个卷轴一起停，看着点～", count));
            UpdateCoinLabel();
        }

        /// <summary>该结果是否算金光大奖。</summary>
        private bool IsJackpot(Item it)
        {
            if (_selected == null || it == null) return false;
            if (_selected.IsMiracle) return _selected.RareItems.Contains(it);
            int topCount = Math.Max(1, (int)Math.Ceiling(_selected.Items.Count * 0.20));
            return _selected.Items.IndexOf(it) < topCount;
        }

        /// <summary>某个卷轴结束：收齐全部结果后再统一结算。</summary>
        private void OnReelSettled(ReelPanel r)
        {
            if (_pendingReels <= 0) return;
            _pendingReels--;
            if (_pendingReels > 0) return;

            var results = new List<Item>();
            foreach (var x in _reels)
                if (x.Visible && x.Result != null) results.Add(x.Result);
            OnSettled(results);
        }

        private readonly System.Diagnostics.Stopwatch _drawWatch = System.Diagnostics.Stopwatch.StartNew();
        private double _drawStartMs;

        private void OnSettled(List<Item> results)
        {
            _btnDraw.Enabled = true;
            if (_btnDraw5 != null) _btnDraw5.Enabled = true;
            if (results == null || results.Count == 0) return;

            // 出货音乐：只要有一个金光大奖就播大奖曲
            Music.PlayWin(_lastWasJackpot);

            // 饰品已在 DoDraw 里入仓，这里只统计与刷新
            double totalUsd = 0;
            foreach (var w in results) totalUsd += w.PriceUsd;
            RefreshVault();
            MaybeWarnLowCoins();

            // 出货后弹出限时高价回收（比仓库常规价高 5%，限时 10 秒）
            ShowLimitedOffer();

            if (results.Count == 1)
            {
                var w = results[0];
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "{0}{1}   ·   市价 ${2:N2}{3}   ·   丢进仓库了   ·   {4}",
                    _lastWasJackpot ? "★ 哇！金光大奖！★ " : "哼，勉强给你：",
                    w.PrettyName, w.PriceUsd, w.Estimated ? "（瞎猜的价）" : "",
                    CommentOn(w)));
                return;
            }

            // 五连抽：状态栏列出全部结果
            var sb = new System.Text.StringBuilder();
            sb.Append(_lastWasJackpot ? "★ 金光大奖！★ 五连抽：" : "五连抽～ 拿好：");
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append("  |  ");
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} ${1:N2}",
                    results[i].PrettyName, results[i].PriceUsd));
            }
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "   ·   加起来 ${0:N2}   ·   都丢仓库了", totalUsd));
            double best = 0;
            foreach (var w in results) if (w.PriceUsd > best) best = w.PriceUsd;
            string tip = _lastWasJackpot ? Pick(MsgJackpot)
                       : (best >= 300 ? Pick(MsgGood) : (best < 5 ? Pick(MsgCheap) : ""));
            if (tip.Length > 0) sb.Append("   ·   " + tip);
            SetStatus(sb.ToString());
        }

        private void RefreshVault()
        {
            if (_vaultList == null) return;
            _vaultList.SuspendLayout();
            foreach (Control c in _vaultList.Controls) c.Dispose();
            _vaultList.Controls.Clear();

            // �?近获得的排前�?
            var list = new List<OwnedItem>(_inv.Items);
            list.Reverse();
            foreach (var o in list)
            {
                var row = new VaultRow
                {
                    Owned = o,
                    Cache = _cache,
                    Width = Math.Max(400, _vaultList.ClientSize.Width - 26),
                    Margin = new Padding(2, 2, 2, 2),
                };
                row.RecycleClicked += (s, owned) => RecycleOne(owned);
                row.CanTrade = CanTrade(o.Item);
                row.TradeClicked += (s, owned) => TradeOne(owned);
                _vaultList.Controls.Add(row);
            }
            // 先算出汇总文本（必须在创建汇总行之前，否则拿到的是上一次的值）
            _vaultSummaryText = string.Format(CultureInfo.InvariantCulture,
                "你攒了 {0} 件   ·   值 ${1:N2}   ·   全卖掉能换 {2} 金币",
                _inv.Items.Count, _inv.TotalValueCents / 100.0,
                (long)Math.Round(_inv.TotalValueCents * 0.90));

            // 汇总作为列表首行插入：位于列表内部，天然不受层级遮挡影响
            {
                var head = new Label
                {
                    Text = _vaultSummaryText,
                    ForeColor = TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                    Height = 30,
                    Width = Math.Max(400, _vaultList.ClientSize.Width - 26),
                    Margin = new Padding(2, 4, 2, 6),
                    TextAlign = ContentAlignment.MiddleLeft,
                };
                _vaultList.Controls.Add(head);
                _vaultList.Controls.SetChildIndex(head, 0);
            }
            _vaultList.ResumeLayout();

        }

        // ------------------------------------------------ 汰换
        /// <summary>汰换成功率。</summary>
        private const double TradeSuccessRate = 0.48;
        /// <summary>成功时新饰品的价值倍率区间。</summary>
        private const double TradeMinMult = 1.7;
        private const double TradeMaxMult = 2.3;
        /// <summary>可汰换的价格上限 = 池内最贵价 / 1.7（超过则无法达到 1.7 倍）。</summary>
        private long _maxTradeableCents;

        /// <summary>
        /// 汰换：48% 概率变成原价 1.7~2.3 倍的随机饰品；否则原饰品消失（无补偿）。
        /// </summary>
        /// <summary>算出可汰换的价格上限：池内最贵价 / 1.7。</summary>
        private void ComputeTradeLimit()
        {
            long maxCents = 0;
            foreach (var p in _db.Pools)
            {
                if (p.Key != "s:all") continue;
                foreach (var it in p.Items)
                    if (it.PriceUsdCents > maxCents) maxCents = it.PriceUsdCents;
                break;
            }
            _maxTradeableCents = (long)Math.Floor(maxCents / TradeMinMult);
        }

        /// <summary>该饰品是否可以汰换。</summary>
        private bool CanTrade(Item it)
        {
            if (it == null || _maxTradeableCents <= 0) return false;
            return it.PriceUsdCents <= _maxTradeableCents;
        }

        private void TradeOne(OwnedItem o)
        {
            if (o == null || o.Item == null) return;

            if (!CanTrade(o.Item))
            {
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "这个不能赌啦：${0:N2} 超过上限 ${1:N2}（×1.7 都比池里最贵的还贵了）",
                    o.Item.PriceUsd, _maxTradeableCents / 100.0));
                MessageBox.Show(
                    string.Format(CultureInfo.InvariantCulture,
                        "这个不能赌哦～\n\n{0}\n市价 ${1:N2}\n\n能赌的上限是 ${2:N2}\n"
                        + "（就是池里最贵的 ${3:N2} 除以 1.7 啦）",
                        o.Item.PrettyName, o.Item.PriceUsd, _maxTradeableCents / 100.0,
                        _maxTradeableCents / 100.0 * 1.7),
                    "不给赌啦", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long baseCents = o.Item.PriceUsdCents;
            long targetCents = (long)Math.Round(baseCents * (TradeMinMult + _rnd.NextDouble() * (TradeMaxMult - TradeMinMult)));

            // 不管成败，原饰品先消耗掉
            _inv.Remove(o);

            bool success = _rnd.NextDouble() < TradeSuccessRate;

            if (!success)
            {
                PersistSave();
                RefreshVault();
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "赌输了～ {0} 没了。48% 都赌不中，手气真差。", o.Item.PrettyName));
                Music.PlayWin(false);
                MessageBox.Show(
                    string.Format(CultureInfo.InvariantCulture,
                        "赌输了呜呜～\n\n{0}\n\n市价 ${1:N2} 的饰品没了。\n{2}",
                        o.Item.PrettyName, o.Item.PriceUsd, Pick(MsgTradeBad)),
                    "赌输了……", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var gained = PickItemNear(baseCents);
            if (gained == null)
            {
                // 理论上不会发生（池子非空），稳妥起见退还金币
                _wallet.Earn((long)Math.Round(baseCents * 0.90));
                PersistSave();
                RefreshVault();
                SetStatus("找不到能换的，钱退你了，哼。");
                return;
            }

            _inv.Add(gained, gained.PriceUsdCents);
            _save.DrawCount++;
            PersistSave();
            UpdateCoinLabel();
            RefreshVault();

            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "赌赢了！{0} (${1:N2})  →  {2} (${3:N2})",
                o.Item.PrettyName, o.Item.PriceUsd,
                gained.PrettyName, gained.PriceUsd));
            Music.PlayWin(true);
            MessageBox.Show(
                string.Format(CultureInfo.InvariantCulture,
                    "哇，赢了耶！\n\n丢掉：{0}\n市价 ${1:N2}\n\n换到：{2}\n市价 ${3:N2}\n\n{4}",
                    o.Item.PrettyName, o.Item.PriceUsd,
                    gained.PrettyName, gained.PriceUsd, Pick(MsgTradeOk)),
                "赌赢了！", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 找出价格最接近目标的可用饰品（优先取区间内，其次取最接近的）。
        /// 只从当前奖池使用的物品集合里挑，保证不会拿到已被剔除的物品。
        /// </summary>
        /// <summary>
        /// 按「倍率」挑选汰换结果：优先 1.7~2.3 倍，其次最接近 2 倍。
        /// 若池内无任何物品能达到 1.7 倍（原饰品已是最贵的一档），返回 null。
        /// </summary>
        private Item PickItemNear(long baseCents)
        {
            var usable = new List<Item>();
            foreach (var p in _db.Pools)
            {
                if (p.Key != "s:all") continue;
                usable.AddRange(p.Items);
                break;
            }
            if (usable.Count == 0 || baseCents <= 0) return null;

            long floor = (long)Math.Round(baseCents * TradeMinMult);

            // 先找落在理想倍率区间的
            var ideal = new List<Item>();
            foreach (var it in usable)
                if (it.PriceUsdCents >= floor) ideal.Add(it);

            if (ideal.Count == 0) return null;   // 数学上无法达标

            // 按与 2.0 倍的距离排序，取最接近的一批
            ideal.Sort((a, b) => Math.Abs(a.PriceUsdCents - baseCents * 2.0)
                                  .CompareTo(Math.Abs(b.PriceUsdCents - baseCents * 2.0)));
            int take = Math.Min(20, ideal.Count);
            var pool = ideal.GetRange(0, take);

            // 这批里若有落在理想区间的，优先从其中挑（保证结果稳定在 1.7~2.3）
            long hi = (long)Math.Round(baseCents * TradeMaxMult);
            var inBand = new List<Item>();
            foreach (var it in pool)
                if (it.PriceUsdCents <= hi) inBand.Add(it);
            if (inBand.Count > 0) return inBand[_rnd.Next(inBand.Count)];

            return pool[_rnd.Next(pool.Count)];
        }

        /// <summary>批量汰换测试：统计成功率与价值倍率。</summary>
        private void RunTradeBench(int times)
        {
            var pool = new List<Item>();
            foreach (var p in _db.Pools)
                if (p.Key == "s:all") { pool.AddRange(p.Items); break; }
            if (pool.Count == 0) { Console.WriteLine("TRADEBENCH 池子为空"); return; }

            int ok = 0, bad = 0;
            double sumMult = 0, minMult = double.MaxValue, maxMult = 0;
            var mults = new List<double>();

            for (int i = 0; i < times; i++)
            {
                var baseItem = pool[_rnd.Next(pool.Count)];
                long baseCents = baseItem.PriceUsdCents;
                bool success = _rnd.NextDouble() < TradeSuccessRate;
                if (!success) { bad++; continue; }
                var gained = PickItemNear(baseCents);
                if (gained == null) { bad++; continue; }
                ok++;
                double m = gained.PriceUsdCents / (double)baseCents;
                mults.Add(m); sumMult += m;
                if (m < minMult) minMult = m;
                if (m > maxMult) maxMult = m;
            }

            mults.Sort();
            double median = mults.Count > 0 ? mults[mults.Count / 2] : 0;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "TRADEBENCH times={0} success={1} rate={2:F1}% avgMult={3:F2} medianMult={4:F2} min={5:F2} max={6:F2}",
                times, ok, ok * 100.0 / Math.Max(1, times), sumMult / Math.Max(1, ok), median,
                minMult == double.MaxValue ? 0 : minMult, maxMult));
        }

        /// <summary>题库随机性采样：统计各分类被抽中的次数。</summary>
        private void RunQuizDist(int times)
        {
            var cnt = new Dictionary<string, int>();
            for (int i = 0; i < times; i++)
            {
                var q = GameBank.Random(_rnd);
                if (!cnt.ContainsKey(q.Source)) cnt[q.Source] = 0;
                cnt[q.Source]++;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "QUIZDIST times={0} bankSize={1}", times, GameBank.Count));
            foreach (var kv in cnt)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "QUIZDIST   {0} = {1} ({2:F1}%)", kv.Key, kv.Value, kv.Value * 100.0 / times));
        }

        /// <summary>输出奖池统计，便于核对重新分配结果。</summary>
        private void RunPoolDump()
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "POOLS total={0} items={1} priced={2} excluded={3}",
                _db.Pools.Count, _db.Items.Count, _db.PricedCount, _db.ExcludedCount));
            foreach (var p in _db.Pools)
            {
                long sum = 0;
                int n = 0;
                foreach (var it in p.Items) { sum += it.PriceUsdCents; n++; }
                double avg = n > 0 ? sum / 100.0 / n : 0;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "POOL|{0}|count={1}|avg=${2:F2}|cost={3}|cover={4}",
                    p.Name, n, avg, p.CostCents, p.Cover != null ? p.Cover.PrettyName : "-"));
            }
        }

        /// <summary>按 key 或 index 找池；index 越界时退回原逻辑。</summary>
        private int ResolvePoolIndex()
        {
            if (!string.IsNullOrEmpty(_autoPoolKey))
            {
                for (int i = 0; i < _db.Pools.Count; i++)
                    if (string.Equals(_db.Pools[i].Key, _autoPoolKey, StringComparison.Ordinal))
                        return i;
                Console.WriteLine("未找到奖池 key: " + _autoPoolKey);
            }
            return Math.Max(0, Math.Min(_autoPoolIndex, _db.Pools.Count - 1));
        }

        private void RecycleOne(OwnedItem o)
        {
            long gain = (long)Math.Round(o.Item.PriceUsdCents * 0.90);
            _inv.Remove(o);
            _wallet.Earn(gain);
            _save.RecycledCount++;
            PersistSave();
            UpdateCoinLabel();
            RefreshVault();
            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "把 {0} 卖了，到手 {1} 金币～  {2}", o.Item.PrettyName, gain, Pick(MsgRecycle)));
            MaybeWarnLowCoins();
        }

        private void RecycleAll()
        {
            if (_inv.Items.Count == 0) { SetStatus("仓库空空的耶，你还没抽过吧？"); return; }
            long total = 0;
            int n = _inv.Items.Count;
            foreach (var o in new List<OwnedItem>(_inv.Items))
                total += (long)Math.Round(o.Item.PriceUsdCents * 0.90);
            _inv.Items.Clear();
            _wallet.Earn(total);
            _save.RecycledCount += n;
            PersistSave();
            UpdateCoinLabel();
            RefreshVault();
            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "{0} 件全卖了，一共 {1} 金币～ 满意了吧？", n, total));
        }

        // ==================================================== 顶部/页面布局
        private Panel _header;
        private const int HeaderH = 110;
        private const int StatusH = 30;

        /// <summary>
        /// 显式摆放顶部 header、中部页面容器、底部状态栏。
        /// 不用 Dock：实测嵌套层级下 Dock 不会让出顶部空间，
        /// 会把 pageHost 顶到 Y=0 从而被导航栏遮住。
        /// </summary>
        private void LayoutChrome()
        {
            if (_header == null || _pageHost == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            _header.SetBounds(0, 0, w, HeaderH);
            int pageH = Math.Max(80, h - HeaderH - StatusH);
            _pageHost.SetBounds(0, HeaderH, w, pageH);
            if (_lblStatus != null) _lblStatus.SetBounds(0, h - StatusH, w, StatusH);
            if (_lblCorner != null) _lblCorner.SetBounds(w - 150, h - StatusH, 150, StatusH);
            _header.BringToFront();
            _pageHost.BringToFront();
            if (_lblStatus != null) _lblStatus.BringToFront();
            if (_lblCorner != null) _lblCorner.BringToFront();
        }

        // ------------------------------------------------ 赚金币页
        private Panel BuildEarnTab()
        {
            var page = new Panel
            {
                BackColor = Theme.CardBg2,
                Padding = new Padding(20),
                AutoScroll = true,
            };

            var fHead = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            var fBig = new Font("Microsoft YaHei UI", 26f, FontStyle.Bold);
            var fSmall = new Font("Microsoft YaHei UI", 9f);

            // ---- 算术题 ----
            var gbMath = new GroupBox
            {
                Text = "  比大小  ·  答对 50，连对最多叠到 250  ",
                ForeColor = TextMain,
                Font = fHead,
                Location = new Point(20, 16),
                Size = new Size(600, 258),
                BackColor = Color.Transparent,
            };
            _lblMathQuestion = new Label
            {
                Text = "",
                Font = fBig,
                ForeColor = CoinGold,
                BackColor = Color.Transparent,
                Location = new Point(24, 42),
                Size = new Size(552, 52),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            // 比大小：三个按钮 大于 / 小于 / 等于
            _btnCmpGreater = new NeonButton
            {
                Text = "大 ＞",
                Accent = Theme.Accent,
                Location = new Point(48, 104),
                Size = new Size(150, 44),
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            };
            _btnCmpGreater.Click += (s, e) => SubmitCompare(1);

            _btnCmpLess = new NeonButton
            {
                Text = "小 ＜",
                Accent = Theme.Accent,
                Location = new Point(212, 104),
                Size = new Size(150, 44),
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            };
            _btnCmpLess.Click += (s, e) => SubmitCompare(-1);

            _btnCmpEqual = new NeonButton
            {
                Text = "一样 ＝",
                Accent = Theme.Accent,
                Location = new Point(376, 104),
                Size = new Size(150, 44),
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            };
            _btnCmpEqual.Click += (s, e) => SubmitCompare(0);
            _btnNewQuestion = new NeonButton
            {
                Text = "换一题（不要钱）",
                Accent = Theme.Muted,
                Ghost = true,
                Location = new Point(48, 158),
                Size = new Size(170, 30),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            };
            _btnNewQuestion.Click += (s, e) => NewMathProblem();
            _lblMathFeedback = new Label
            {
                Text = "",
                Font = fSmall,
                ForeColor = TextDim,
                BackColor = Color.Transparent,
                Location = new Point(24, 196),
                Size = new Size(552, 44),
            };
            gbMath.Controls.Add(_lblMathQuestion);
            gbMath.Controls.Add(_btnCmpGreater);
            gbMath.Controls.Add(_btnCmpLess);
            gbMath.Controls.Add(_btnCmpEqual);
            gbMath.Controls.Add(_btnNewQuestion);
            gbMath.Controls.Add(_lblMathFeedback);
            page.Controls.Add(gbMath);

            // ---- 安全知识 ----
            var gbQuiz = new GroupBox
            {
                Text = "  游戏大题库  ·  答对给你 1500  ",
                ForeColor = TextMain,
                Font = fHead,
                Location = new Point(640, 16),
                Size = new Size(560, 420),
                BackColor = Color.Transparent,
            };
            _lblQuizQuestion = new Label
            {
                Text = "",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                Location = new Point(20, 40),
                Size = new Size(520, 56),
            };
            gbQuiz.Controls.Add(_lblQuizQuestion);
            _btnQuizA = MakeQuizButton(0, 20, 104, gbQuiz);
            _btnQuizB = MakeQuizButton(1, 20, 150, gbQuiz);
            _btnQuizC = MakeQuizButton(2, 20, 196, gbQuiz);
            _btnQuizD = MakeQuizButton(3, 20, 242, gbQuiz);
            _lblQuizFeedback = new Label
            {
                Text = "",
                Font = fSmall,
                ForeColor = TextDim,
                BackColor = Color.Transparent,
                Location = new Point(20, 288),
                Size = new Size(520, 110),
            };
            gbQuiz.Controls.Add(_lblQuizFeedback);
            page.Controls.Add(gbQuiz);

            // ---- 休息奖励 ----
            var gbCd = new GroupBox
            {
                Text = "  施舍时间  ·  过一会儿能领一次  ",
                ForeColor = TextMain,
                Font = fHead,
                Location = new Point(20, 284),
                Size = new Size(600, 154),
                BackColor = Color.Transparent,
            };
            _lblEarnHint = new Label
            {
                Text = "金币是本小姐给的，只能在里面花，换不了真钱啦。",
                Font = fSmall,
                ForeColor = TextDim,
                BackColor = Color.Transparent,
                Location = new Point(20, 34),
                Size = new Size(560, 20),
            };
            _btnCooldown = new NeonButton
            {
                Text = "领施舍 (+" + CooldownReward + ")",
                Accent = CoinGold,
                Location = new Point(20, 62),
                Size = new Size(200, 44),
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            };
            _btnCooldown.Click += (s, e) => ClaimCooldown();
            gbCd.Controls.Add(_lblEarnHint);
            gbCd.Controls.Add(_btnCooldown);
            page.Controls.Add(gbCd);

            _cooldownTimer = new Timer { Interval = 1000 };
            _cooldownTimer.Tick += (s, e) => RefreshCooldown();
            _cooldownTimer.Start();

            // 下方空白区放立绘（该页是绝对定位，这里不会压到任何控件）
            page.Controls.Add(MakeMascotPanel("mascot_001.png", 780, 430, 300, 350));

            return page;
        }

        private NeonButton MakeQuizButton(int index, int x, int y, Control parent)
        {
            var b = new NeonButton
            {
                Text = "",
                Accent = Theme.AccentDim,
                Ghost = true,
                Location = new Point(x, y),
                Size = new Size(520, 40),
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            b.Click += (s, e) => AnswerQuiz(index);
            parent.Controls.Add(b);
            return b;
        }

        /// <summary>出一题新的比大小（两个两位数）。</summary>
        private void NewMathProblem()
        {
            if (_lblMathQuestion == null) return;
            _cmp = CompareProblem.Generate(_rnd);
            _lblMathQuestion.Text = _cmp.Text;
            SetCompareButtonsEnabled(true);
        }

        /// <summary>比大小答对的金币：50，连对每次 +50，单次最高 250。</summary>
        private const int CompareBase = 50;
        private const int CompareMax = 250;

        private static int CompareReward(int streak)
        {
            int r = CompareBase * streak;      // 连对第 n 次 = 50 * n
            return Math.Min(CompareMax, Math.Max(CompareBase, r));
        }

        private void SetCompareButtonsEnabled(bool on)
        {
            foreach (var b in new[] { _btnCmpGreater, _btnCmpLess, _btnCmpEqual })
            {
                if (b == null) continue;
                b.Enabled = on;
                b.Accent = on ? Theme.Accent : Theme.Muted;
                b.Invalidate();
            }
        }

        /// <summary>玩家选了大小关系：pick 为 1(大于) / -1(小于) / 0(等于)。</summary>
        private void SubmitCompare(int pick)
        {
            if (_cmp == null || _lblMathFeedback == null) return;

            if (pick == _cmp.Result)
            {
                _mathStreak++;
                _save.MathCorrect++;
                int reward = CompareReward(_mathStreak);
                _wallet.Earn(reward);
                PersistSave();
                UpdateCoinLabel();

                bool capped = (_mathStreak * CompareBase) >= CompareMax;
                if (_benchDraw)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "CMP t={0} streak={1} reward={2} coins={3}",
                        _drawWatch.Elapsed.TotalMilliseconds,
                        _mathStreak, reward, _wallet.Coins));
                _lblMathFeedback.ForeColor = Theme.Ok;
                _lblMathFeedback.Text = string.Format(CultureInfo.InvariantCulture,
                    "哼，算你答对了～ +{0}（连对 {1} 题{2}）",
                    reward, _mathStreak, capped ? "，到顶啦" : "");
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "比大小蒙对了，给你 {0} 金币。", reward));
            }
            else
            {
                _mathStreak = 0;
                _lblMathFeedback.ForeColor = Color.FromArgb(240, 160, 120);
                _lblMathFeedback.Text = string.Format(CultureInfo.InvariantCulture,
                    "笨！{0} {1} {2} 啦。连对清零，重新开始吧。",
                    _cmp.Left, _cmp.Symbol, _cmp.Right);
            }

            SetCompareButtonsEnabled(false);
            // 换题间隔：0.25 秒（此前 900ms 太慢）
            var t = new Timer { Interval = 250 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); NewMathProblem(); };
            t.Start();
        }

        private void NewQuiz()
        {
            if (_lblQuizQuestion == null) return;
            _quiz = GameBank.Random(_rnd);
            _lblQuizQuestion.Text = _quiz.Question;
            var btns = new[] { _btnQuizA, _btnQuizB, _btnQuizC, _btnQuizD };
            for (int i = 0; i < btns.Length; i++)
            {
                if (btns[i] == null) continue;
                btns[i].Text = string.Format(CultureInfo.InvariantCulture, "{0}. {1}",
                    (char)('A' + i), i < _quiz.Options.Length ? _quiz.Options[i] : "");
                btns[i].Accent = Theme.AccentDim;
                btns[i].Enabled = true;
                btns[i].Invalidate();
            }
            if (_lblQuizFeedback != null) _lblQuizFeedback.Text = "";
        }

        private void AnswerQuiz(int index)
        {
            if (_quiz == null) return;
            var btns = new[] { _btnQuizA, _btnQuizB, _btnQuizC, _btnQuizD };
            foreach (var b in btns) if (b != null) b.Enabled = false;

            if (index == _quiz.CorrectIndex)
            {
                int reward = 1500;
                if (_benchDraw)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "QUIZ correct source={0} reward={1} coins={2}",
                        _quiz.Source, reward, _wallet.Coins + reward));
                _wallet.Earn(reward);
                _save.QuizCorrect++;
                PersistSave();
                UpdateCoinLabel();
                if (btns[index] != null) btns[index].Accent = Theme.Ok;
                _lblQuizFeedback.ForeColor = Theme.Ok;
                _lblQuizFeedback.Text = string.Format(CultureInfo.InvariantCulture,
                    "答对啦～ +{0} 金币。\r\n听好了：{1}", reward, _quiz.Explain);
                SetStatus(string.Format(CultureInfo.InvariantCulture, "{0}  +{1} 金币", Pick(MsgQuizOk), reward));
            }
            else
            {
                if (btns[_quiz.CorrectIndex] != null) btns[_quiz.CorrectIndex].Accent = Theme.Ok;
                if (btns[index] != null) btns[index].Accent = Theme.Bad;
                _lblQuizFeedback.ForeColor = Color.FromArgb(240, 160, 120);
                _lblQuizFeedback.Text = string.Format(CultureInfo.InvariantCulture,
                    "就这？正确答案是 {0}。\r\n记住啦：{1}\r\n\r\n{2}",
                    (char)('A' + _quiz.CorrectIndex), _quiz.Explain, Pick(MsgQuizBad));
            }
            foreach (var b in btns) if (b != null) b.Invalidate();

            var t = new Timer { Interval = 3000 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); NewQuiz(); };
            t.Start();
        }

        /// <summary>休息奖励金额与冷却时长。</summary>
        private const int CooldownReward = 10000;
        private const int CooldownMinutes = 3;

        private void ClaimCooldown()
        {
            if (DateTime.Now < _cooldownUntil)
            {
                var left = _cooldownUntil - DateTime.Now;
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "急什么～ 还要等 {0} 分 {1} 秒", left.Minutes, left.Seconds));
                return;
            }
            int reward = CooldownReward;
            _wallet.Earn(reward);
            _cooldownUntil = DateTime.Now.AddMinutes(CooldownMinutes);
            PersistSave();
            UpdateCoinLabel();
            SetStatus(string.Format(CultureInfo.InvariantCulture, "施舍你 {0} 金币。  {1}", reward, Pick(MsgCooldown)));
        }

        private void RefreshCooldown()
        {
            if (_btnCooldown == null) return;
            if (DateTime.Now >= _cooldownUntil)
            {
                _btnCooldown.Text = "领施舍 (+" + CooldownReward + ")";
                _btnCooldown.Enabled = true;
                _btnCooldown.Accent = CoinGold;
            }
            else
            {
                var left = _cooldownUntil - DateTime.Now;
                _btnCooldown.Text = string.Format(CultureInfo.InvariantCulture,
                    "等我 {0}:{1:D2}", left.Minutes, left.Seconds);
                _btnCooldown.Enabled = false;
                _btnCooldown.Accent = Theme.Muted;
            }
            _btnCooldown.Invalidate();
        }

        // ------------------------------------------------ 仓库页
        private Panel BuildVaultTab()
        {
            var page = new Panel { BackColor = Theme.CardBg2, Padding = new Padding(16) };

            _btnRecycleAll = new NeonButton
            {
                Text = "全卖掉（市价 90%）",
                Accent = Accent,
                Size = new Size(240, 36),
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            };
            _btnRecycleAll.Click += (s, e) => RecycleAll();

            // 调试口令输入框（放在回收按钮下方）
            _lblDebug = new Label
            {
                Text = "偷偷说个口令：",
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5f),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _txtDebug = new TextBox
            {
                BackColor = Theme.PanelBg,
                ForeColor = Theme.TextDim,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                MaxLength = 32,
            };
            _txtDebug.TextChanged += (s, e) => CheckDebugCode();

            _vaultList = new FlowLayoutPanel
            {
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _vaultList.Resize += (s, e) =>
            {
                foreach (Control c in _vaultList.Controls) c.Width = _vaultList.ClientSize.Width - 26;
            };

            // 后添加的控件在上层，顺序：列表 -> 按钮 -> 调试输入
            page.Controls.Add(_vaultList);
            page.Controls.Add(_btnRecycleAll);
            page.Controls.Add(_txtDebug);
            page.Controls.Add(_lblDebug);
            page.Resize += (s, e) => LayoutVaultPage(page);
            LayoutVaultPage(page);
            return page;
        }

        // ------------------------------------------------ 调试口令
        /// <summary>调试口令表：口令 -> 发放金币数（每个口令各自只能领一次）。</summary>
        private static readonly string[] DebugCodes = {
            "chenkaixvanbaba",
            "feiniaocoumama",
        };
        private static readonly long[] DebugCodeCoins = {
            300000,
            250000,
        };
        private readonly bool[] _debugCodeUsed = new bool[DebugCodes.Length];

        /// <summary>是否所有口令都已使用（用于决定输入框是否置灰）。</summary>
        private bool AllDebugCodesUsed()
        {
            for (int i = 0; i < _debugCodeUsed.Length; i++)
                if (!_debugCodeUsed[i]) return false;
            return true;
        }

        private void CheckDebugCode()
        {
            if (_txtDebug == null || _txtDebug.Enabled == false) return;

            string input = _txtDebug.Text.Trim();
            if (input.Length == 0) return;

            for (int i = 0; i < DebugCodes.Length; i++)
            {
                if (_debugCodeUsed[i]) continue;
                if (!string.Equals(input, DebugCodes[i], StringComparison.Ordinal)) continue;

                _debugCodeUsed[i] = true;
                long coins = DebugCodeCoins[i];
                _wallet.Earn(coins);
                PersistSave();
                UpdateCoinLabel();

                if (AllDebugCodesUsed())
                {
                    SetStatus(string.Format(CultureInfo.InvariantCulture,
                        "口令对啦～ +{0:N0} 金币（都被你用光了，哼）", coins));
                    _txtDebug.Text = "";
                    _txtDebug.Enabled = false;
                    _lblDebug.Text = "没得领啦";
                }
                else
                {
                    int left = 0;
                    for (int k = 0; k < _debugCodeUsed.Length; k++) if (!_debugCodeUsed[k]) left++;
                    SetStatus(string.Format(CultureInfo.InvariantCulture,
                        "口令对啦～ +{0:N0} 金币（还剩 {1} 个呢）", coins, left));
                    _txtDebug.Text = "";
                }
                return;
            }
        }

        private void LayoutVaultPage(Panel page)
        {
            int w = page.ClientSize.Width;
            int h = page.ClientSize.Height;
            const int pad = 16;
            const int barH = 44;

            if (_btnRecycleAll != null)
                _btnRecycleAll.SetBounds(pad, 62, 240, 36);
            // 调试口令：回收按钮右侧
            if (_lblDebug != null)
                _lblDebug.SetBounds(pad + 260, 70, 74, 22);
            if (_txtDebug != null)
                _txtDebug.SetBounds(pad + 336, 68, 170, 24);
            if (_vaultList != null)
            {
                int top = 62 + barH;
                _vaultList.SetBounds(pad, top, Math.Max(100, w - pad * 2), Math.Max(60, h - top - pad));
            }
        }

        // ==================================================== 绘制 / 状�??
        /// <summary>
        /// 造一个「立绘栏」：靠右停靠、自带蓝色光晕。
        /// 因为是 Dock.Right 参与布局（而不是浮在上面），
        /// 同一容器里后加进去的填充控件会自动让出位置，所以不会遮挡任何界面。
        /// </summary>
        // ============================================================ 限时高价回收
        /// <summary>
        /// 出货后弹出限时高价回收。
        /// 报价 = 市价 ×95%，比仓库常规回收（×90%）高 5%，限时 10 秒。
        /// </summary>
        private void ShowLimitedOffer()
        {
            // 测试模式不弹，避免干扰自动化
            if (_lastWon.Count == 0) return;
            if (_benchDraw && !_testOffer && !_autoSell && !_autoKeep) return;   // 自动化测试默认不弹
            if (_selected == null) return;

            EnsureOfferPane();
            if (_offerPane == null) return;

            // 报价：市价 ×95%（仓库常规 ×90% → 高 5%）
            long market = 0;
            foreach (var o in _lastWon) market += o.Item.PriceUsdCents;
            _offerGain = (long)Math.Round(market * OfferRate);
            long baseVal = (long)Math.Round(market * 0.90);
            long bonus = _offerGain - baseVal;

            // 列表
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            foreach (var o in _lastWon)
            {
                if (shown >= 6) { sb.Append("\n… 还有 " + (_lastWon.Count - 6) + " 件"); break; }
                if (shown > 0) sb.Append("\n");
                sb.Append("· " + o.Item.PrettyName
                          + "   $" + o.Item.PriceUsd.ToString("N2", CultureInfo.InvariantCulture));
                shown++;
            }
            if (_lblOfferList != null) _lblOfferList.Text = sb.ToString();

            if (_lblOfferPrice != null)
                _lblOfferPrice.Text = _offerGain.ToString("N0", CultureInfo.InvariantCulture) + " 金币";

            if (_lblOfferHint != null)
                _lblOfferHint.Text = string.Format(CultureInfo.InvariantCulture,
                    "比仓库常规回收（市价 90%）高 5%\n"
                    + "常规只能拿 {0:N0}，本小姐多给你 {1:N0} 哦～",
                    baseVal, bonus);

            _offerDeadline = DateTime.Now.AddSeconds(OfferSeconds);
            _offerPane.Visible = true;
            _offerPane.BringToFront();
            PositionOfferPane();
            UpdateOfferCountdown();

            if (_offerTimer != null) { _offerTimer.Stop(); _offerTimer.Dispose(); }
            _offerTimer = new Timer { Interval = OfferSeconds * 1000 + 120 };
            _offerTimer.Tick += (s, e) => { _offerTimer.Stop(); CloseOffer(false); };
            _offerTimer.Start();

            if (_offerTick != null) { _offerTick.Stop(); _offerTick.Dispose(); }
            _offerTick = new Timer { Interval = 100 };
            _offerTick.Tick += (s, e) => UpdateOfferCountdown();
            _offerTick.Start();

            // 端到端测试：自动接受（--autosell）或自动放弃（--autokeep）
            if (_autoSell)
            {
                var tA = new Timer { Interval = 1200 };
                tA.Tick += (s, e) => { tA.Stop(); tA.Dispose(); OfferSell(); };
                tA.Start();
            }
            else if (_autoKeep)
            {
                var tK = new Timer { Interval = 1200 };
                tK.Tick += (s, e) => { tK.Stop(); tK.Dispose(); OfferKeep(); };
                tK.Start();
            }
        }

        private void UpdateOfferCountdown()
        {
            double left = (_offerDeadline - DateTime.Now).TotalSeconds;
            if (left < 0) left = 0;
            if (_lblOfferCountdown != null)
            {
                _lblOfferCountdown.Text = string.Format(CultureInfo.InvariantCulture,
                    "还剩 {0:0.0} 秒", left);
                _lblOfferCountdown.ForeColor = left <= 3 ? Theme.Bad : Theme.AccentLite;
            }
            if (!_offerPane.Visible) return;
        }

        /// <summary>sold=true 表示玩家接受了高价回收。</summary>
        private void CloseOffer(bool sold)
        {
            if (_offerTimer != null) { _offerTimer.Stop(); _offerTimer.Dispose(); _offerTimer = null; }
            if (_offerTick != null) { _offerTick.Stop(); _offerTick.Dispose(); _offerTick = null; }
            if (_offerPane != null) _offerPane.Visible = false;
        }

        private void OfferSell()
        {
            if (_lastWon.Count == 0) { CloseOffer(false); return; }

            long gain = 0;
            int n = 0;
            foreach (var o in _lastWon)
            {
                if (!_inv.Items.Contains(o)) continue;      // 保险：已被处理过则跳过
                gain += (long)Math.Round(o.Item.PriceUsdCents * OfferRate);
                _inv.Remove(o);
                n++;
            }
            _lastWon.Clear();

            if (n > 0)
            {
                _wallet.Earn(gain);
                _save.RecycledCount += n;
                PersistSave();
                UpdateCoinLabel();
                RefreshVault();
                MaybeWarnLowCoins();
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "{0} 件当场卖掉了，拿到 {1:N0} 金币（比仓库多 5%）～  {2}",
                    n, gain, Pick(MsgOfferSell)));
                if (_benchDraw || _autoSell)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "OFFERSELL n={0} gain={1} coins={2} invLeft={3}",
                        n, gain, _wallet.Coins, _inv.Items.Count));
            }
            CloseOffer(true);
        }

        private void OfferKeep()
        {
            if (_benchDraw || _autoKeep)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "OFFERKEEP coins={0} invLeft={1}", _wallet.Coins, _inv.Items.Count));
            _lastWon.Clear();
            CloseOffer(false);
            SetStatus(Pick(MsgOfferKeep));
        }

        /// <summary>建浮层（只建一次）。</summary>
        private void EnsureOfferPane()
        {
            if (_offerPane != null) return;
            var host = _drawRoot != null ? (Control)_drawRoot : this;

            _offerPane = new Panel
            {
                Size = new Size(430, 300),
                BackColor = Color.FromArgb(16, 22, 40),
                Visible = false,
            };
            _offerPane.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, _offerPane.Width - 1, _offerPane.Height - 1);
                using (var pen = new Pen(Color.FromArgb(200, Theme.AccentGlow), 2f))
                    g.DrawRectangle(pen, r);
                using (var pen = new Pen(Color.FromArgb(70, Theme.AccentLite), 1f))
                    g.DrawRectangle(pen, Rectangle.Inflate(r, -3, -3));
            };

            var fTitle = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            var fBody = new Font("Microsoft YaHei UI", 9f);
            var fPrice = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold);
            var fClock = new Font("Consolas", 12f, FontStyle.Bold);

            var lblTitle = new Label
            {
                Text = "限时高价回收！",
                Font = fTitle,
                ForeColor = Theme.AccentLite,
                BackColor = Color.Transparent,
                Location = new Point(18, 14),
                Size = new Size(394, 30),
            };
            _lblOfferList = new Label
            {
                Font = fBody,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Location = new Point(18, 48),
                Size = new Size(394, 84),
            };
            _lblOfferPrice = new Label
            {
                Font = fPrice,
                ForeColor = Theme.Gold,
                BackColor = Color.Transparent,
                Location = new Point(18, 134),
                Size = new Size(394, 34),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _lblOfferHint = new Label
            {
                Font = fBody,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Location = new Point(18, 170),
                Size = new Size(394, 42),
            };
            _lblOfferCountdown = new Label
            {
                Font = fClock,
                ForeColor = Theme.AccentLite,
                BackColor = Color.Transparent,
                Location = new Point(18, 216),
                Size = new Size(160, 26),
            };

            _btnOfferSell = new NeonButton
            {
                Text = "卖掉！",
                Accent = Theme.Gold,
                Size = new Size(190, 42),
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                Location = new Point(18, 250),
            };
            _btnOfferSell.Click += (s, e) => OfferSell();

            _btnOfferKeep = new NeonButton
            {
                Text = "算了，先留着",
                Accent = Theme.Muted,
                Ghost = true,
                Size = new Size(190, 42),
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                Location = new Point(222, 250),
            };
            _btnOfferKeep.Click += (s, e) => OfferKeep();

            _offerPane.Controls.Add(lblTitle);
            _offerPane.Controls.Add(_lblOfferList);
            _offerPane.Controls.Add(_lblOfferPrice);
            _offerPane.Controls.Add(_lblOfferHint);
            _offerPane.Controls.Add(_lblOfferCountdown);
            _offerPane.Controls.Add(_btnOfferSell);
            _offerPane.Controls.Add(_btnOfferKeep);

            host.Controls.Add(_offerPane);
            _offerPane.BringToFront();
        }

        /// <summary>把浮层摆在抽奖详情区右侧上方（不遮挡卷轴与按钮）。</summary>
        private void PositionOfferPane()
        {
            if (_offerPane == null || _drawRoot == null) return;
            var cur = _drawRoot;
            int x = cur.ClientSize.Width - _offerPane.Width - 22;
            int y = 14;
            if (x < 8) x = 8;
            _offerPane.Location = new Point(x, y);
        }

        /// <summary>关于对话框：展示分享水印，防止来源被抹掉。</summary>
        private void ShowAbout()
        {
            var f = new Form
            {
                Text = "关于 · 本小姐是谁",
                ClientSize = new Size(430, 300),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Theme.BgDeep,
                ForeColor = Theme.TextMain,
            };
            var fTitle = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            var fMark = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            var fBody = new Font("Microsoft YaHei UI", 9f);

            var lbl = new Label
            {
                Text = "CS2 雌小鬼军需\nCS2 Mesugaki Quartermaster",
                Font = fTitle,
                ForeColor = Theme.AccentLite,
                Location = new Point(22, 16),
                Size = new Size(386, 56),
            };
            var lblMark = new Label
            {
                Text = "免费分享 · 禁止倒卖收费",
                Font = fMark,
                ForeColor = Theme.Gold,
                Location = new Point(22, 76),
                Size = new Size(386, 30),
            };
            var lblInfo = new Label
            {
                Text = "哼，本小姐是免费分享的，谁都不许拿本小姐去卖钱。\n"
                     + "金币和饰品全是假的，不能换钱，也换不到真的。\n"
                     + "C# WinForms 手写，没有任何第三方依赖。\n\n"
                     + "你要是花钱买来的 —— 那你可真是个大笨蛋呢。\n\n"
                     + "（按 F1 可以随时叫本小姐出来）",
                Font = fBody,
                ForeColor = Theme.TextDim,
                Location = new Point(22, 116),
                Size = new Size(386, 130),
            };
            var btn = new NeonButton
            {
                Text = "知道啦",
                Accent = Theme.Accent,
                Size = new Size(120, 36),
                Location = new Point(155, 248),
            };
            btn.Click += (s, e) => f.Close();

            f.Controls.Add(lbl);
            f.Controls.Add(lblMark);
            f.Controls.Add(lblInfo);
            f.Controls.Add(btn);

            f.ShowDialog(this);
        }

        /// <summary>造一个带蓝色光晕的立绘控件（固定位置版，用于绝对定位页面）。</summary>
        private Panel MakeMascotPanel(string imgName, int x, int y, int w, int h)
        {
            var pane = new MascotPane
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
            };
            var pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
            };
            var art = _ui.Get(imgName);
            if (art != null) pic.Image = art;
            pane.Controls.Add(pic);
            return pane;
        }

        private Panel MakeMascotColumn(string imgName, int width)
        {
            var pane = new MascotPane
            {
                Dock = DockStyle.Right,
                Width = width,
            };
            var pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
            };
            var art = _ui.Get(imgName);
            if (art != null) pic.Image = art;
            pane.Controls.Add(pic);
            return pane;
        }

        /// <summary>定位界面素材目录（立绘 / 背景）。</summary>
        private static UiAssets InitUiAssets(string dataDir)
        {
            var cands = new List<string>();
            if (!string.IsNullOrEmpty(dataDir))
            {
                cands.Add(Path.Combine(dataDir, "ui"));
                cands.Add(Path.Combine(Path.Combine(dataDir, ".."), "ui"));
            }
            cands.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ui"));
            cands.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "ui"));
            cands.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "assets", "ui"));
            return UiAssets.Locate(cands.ToArray());
        }

        // ================= 雌小鬼语气消息池 =================
        static readonly string[] MsgJackpot = {
            "哇——？？你这种手气也能出金光？本小姐不服。",
            "金光！？……哼，走狗屎运而已，别得意。",
            "哇哦，大奖。……看在你花的金币份上，勉强恭喜一下。",
            "出金了？行吧行吧，这次算你赢，下次就没这么好运气了。",
        };
        static readonly string[] MsgGood = {
            "嗯……还行吧，不算太亏，勉强及格。",
            "这价还行哦？不过离回本还差得远呢，继续抽。",
            "哟，比刚才强点了。就这样保持？本小姐可不信。",
            "不错嘛。可惜你前面亏的那些，这个补不回来哦。",
        };
        static readonly string[] MsgCheap = {
            "噗——就这？几毛钱的东西，你手气真的很稳定呢。",
            "哈哈哈，又是这种货色。本小姐都替你心疼金币。",
            "……你确定你点的是抽奖，不是领垃圾？",
            "便宜货 +1。再抽下去仓库要变成废品站咯。",
            "唉，本小姐都懒得多看一眼。下一个。",
        };
        static readonly string[] MsgPoor = {
            "没钱了吧？早就说了抽得越多亏得越多。",
            "金币见底咯～ 去「去搬砖」答几道题吧，别在这儿硬撑。",
            "哦？破产了？本小姐可不会借你钱。",
            "没金币了耶。要不要本小姐大发慈悲告诉你……去答题？",
        };
        static readonly string[] MsgNoPool = {
            "池子都没选，你是想抽空气吗？",
            "先挑个池子呀，愣着干嘛。",
            "点都不会点？上面那么多池子呢。",
        };
        static readonly string[] MsgRecycle = {
            "卖了卖了～ 亏成这样还想回本？",
            "回收完成。钱是回来了，心是碎的。",
            "这点钱够你抽几次？自己算算账吧。",
        };
        static readonly string[] MsgCooldown = {
            "拿好，施舍你的，别嫌少。",
            "领到了？本小姐心情好才给的。",
            "白给的钱，收着吧。",
        };
        static readonly string[] MsgQuizOk = {
            "答对了？看来你也不是完全没救。",
            "哟，知识还在。金币拿好。",
            "这题都会？行吧，赏你。",
        };
        static readonly string[] MsgQuizBad = {
            "笨死了，这都不会。金币没了，好好反思。",
            "错了哦～ 连这种题都答不出来，还想抽金光？",
            "哎呀，答错了。脑子是个好东西，多用用。",
        };
        static readonly string[] MsgTradeOk = {
            "赌赢了？运气不错嘛……下次可不一定哦。",
            "哦——翻倍了。本小姐承认这次你挺走运的。",
            "赢了赢了，高兴什么，迟早还回来。",
        };
        static readonly string[] MsgTradeBad = {
            "噗，没了。都说了只有 48%，非要赌。",
            "蒸发咯～ 这下知道本小姐为什么不让你乱赌了吧？",
            "没了。……嗯，本小姐一点都不同情你。",
        };
        static readonly string[] MsgOfferSell = {
            "痛快！本小姐就喜欢你这种识相的。",
            "卖了卖了～ 钱到手才是真的，留着看它跌吗？",
            "爽快。比那些抱着破枪当宝的人强多了。",
            "成交！金币拿好，别又转头全抽光了。",
            "哼，算你懂行情。这个价过了这村没这店。",
        };
        static readonly string[] MsgOfferKeep = {
            "留着吧留着吧，跌了可别来找本小姐哭。",
            "哦，舍不得？随你咯，反正亏的又不是我。",
            "不卖是吧。行，等你后悔。",
            "哼，犹犹豫豫的。本小姐收摊了。",
        };
        static readonly string[] MsgTradeBan = {
            "这个太贵了，不给赌。规矩就是规矩。",
            "不行哦，超过上限了。挑个便宜点的来。",
        };

        readonly Random _msgRnd = new Random();

        /// <summary>从消息池里随机取一句吐槽。</summary>
        private string Pick(string[] pool)
        {
            if (pool == null || pool.Length == 0) return "";
            return pool[_msgRnd.Next(pool.Length)];
        }

        /// <summary>按抽到的饰品价格给一句吐槽。</summary>
        private string CommentOn(Item it)
        {
            if (it == null) return "";
            if (_lastWasJackpot) return Pick(MsgJackpot);
            double v = it.PriceUsd;
            if (v >= 300) return Pick(MsgGood);
            if (v < 5) return Pick(MsgCheap);
            return "";
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            // 最小化时 ClientSize 会变成 0x0，GDI+ 建渐变会抛 ArgumentException
            if (r.Width <= 1 || r.Height <= 1) return;
            using (var lg = new LinearGradientBrush(r, BgTop, BgBottom, 62f))
                g.FillRectangle(lg, r);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 5; i >= 1; i--)
            {
                int rr = 300 * i / 5;
                int a = 14 * (6 - i) / 10;
                using (var b = new SolidBrush(Color.FromArgb(a, Accent)))
                    g.FillEllipse(b, ClientSize.Width - 200 - rr, -120 - rr / 2, rr * 2, rr * 2);
            }
        }

        private void UpdateCoinLabel()
        {
            if (_lblCoins != null)
                _lblCoins.Text = string.Format(CultureInfo.InvariantCulture, "🪙 {0:N0} 金币～本小姐赏的", _wallet.Coins);
        }

        // ------------------------------------------------ 低金币劝诫弹窗
        /// <summary>低于此金币数时弹出劝诫；回到该线以上后允许再次触发。</summary>
        private const long WarnCoinThreshold = 1000;
        private bool _lowCoinWarned;

        /// <summary>
        /// 全站总体回报率 = 所有池子的「回收价值 / 单抽成本」（按池平均）。
        /// 回收为市价 90%、单抽成本为均价 120%，理论值约 0.75。
        /// </summary>
        private double OverallReturnRate()
        {
            double sumCost = 0, sumBack = 0;
            foreach (var p in _db.Pools)
            {
                if (p.Items.Count == 0) continue;
                sumCost += p.CostCents;
                sumBack += p.RecycleCents;
            }
            if (sumCost <= 0) return 0;
            return sumBack / sumCost;
        }

        /// <summary>金币不足时检查是否要弹出劝诫。</summary>
        private void MaybeWarnLowCoins()
        {
            if (_wallet == null) return;

            if (_wallet.Coins >= WarnCoinThreshold)
            {
                _lowCoinWarned = false;
                return;
            }
            if (_lowCoinWarned) return;
            _lowCoinWarned = true;
            ShowLowCoinWarn();
        }

        private void ShowLowCoinWarn()
        {
            double rate = OverallReturnRate();
            int pct = (int)Math.Round(rate * 100);

            var f = new Form
            {
                Text = "喂喂喂——",
                ClientSize = new Size(470, 300),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                BackColor = Theme.SlotBg2,
                ForeColor = TextMain,
                Font = new Font("Microsoft YaHei UI", 10f),
            };

            var lblTitle = new Label
            {
                Text = "喂喂喂～ 钱包快见底了哦？",
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 150, 90),
                BackColor = Color.Transparent,
                Location = new Point(20, 16),
                Size = new Size(430, 32),
            };

            var lblBody = new Label
            {
                Text = string.Format(CultureInfo.InvariantCulture,
                    "你现在只剩 {0:N0} 金币了耶～ 好可怜哦。\r\n\r\n"
                    + "本小姐大发慈悲告诉你一个数字：\r\n"
                    + "这里的总体回报率只有约 {1}%。\r\n"
                    + "也就是每花 100 金币，平均只拿得回 {2} 金币。\r\n\r\n"
                    + "懂了吗？抽得越多，亏得越多。\r\n"
                    + "这就是个模拟器，别真把它当赌场玩啊。\r\n"
                    + "去「去搬砖」答几道题不好吗？哼。",
                    _wallet.Coins, pct, pct),
                Font = new Font("Microsoft YaHei UI", 9.5f),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Location = new Point(20, 56),
                Size = new Size(430, 172),
            };

            var btn = new NeonButton
            {
                Text = "知道了啦……",
                Accent = Theme.Accent,
                Size = new Size(168, 38),
                Location = new Point(151, 242),
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            };
            btn.Click += (s, e) => f.Close();

            f.Controls.Add(lblTitle);
            f.Controls.Add(lblBody);
            f.Controls.Add(btn);
            f.Shown += (s, e) => { f.BringToFront(); f.Activate(); };

            f.Show(this);   // 非模态，不打断抽奖动画
        }

        private void SetStatus(string s)
        {
            if (_lblStatus != null) _lblStatus.Text = s;
        }

        private void UpdateStatus()
        {
            // 池内实际�?��物品数（已排�?7 天无成交等�?剔除项）
            int inPool = 0;
            foreach (var p in _db.Pools)
            {
                if (p.Key == "s:all") { inPool = p.Items.Count; break; }
            }
            string est = _db.EstimatedCount > 0
                ? string.Format(CultureInfo.InvariantCulture, " / 瞎估的 {0:N0} 件", _db.EstimatedCount)
                : "";
            string excluded = _db.ExcludedCount > 0
                ? string.Format(CultureInfo.InvariantCulture, "   ·   7 天没人要，踢了 {0:N0} 件", _db.ExcludedCount)
                : "";
            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "{0} 个池子   ·   一共 {1:N0} 件   ·   有价 {2:N0} 件{3}{4}",
                _db.Pools.Count, inPool, _db.PricedCount, est, excluded));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Music.StopAll();
            PersistSave();
            base.OnFormClosing(e);
        }
    }
}
