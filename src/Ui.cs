using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Cs2Roulette
{
    internal static class Round
    {
        public static GraphicsPath Path(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>图片缓存：按文件名加载，限制总量避免内存膨胀。</summary>
    public sealed class ImageCache
    {
        private readonly string _root;
        private readonly Dictionary<string, Image> _map = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = new List<string>();
        private const int Limit = 260;

        public ImageCache(string root) { _root = root; }

        public Image Get(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            Image img;
            if (_map.TryGetValue(file, out img)) return img;
            try
            {
                string p = Path.Combine(_root, file);
                if (!File.Exists(p)) return null;
                byte[] bytes = File.ReadAllBytes(p);
                using (var ms = new MemoryStream(bytes))
                using (var src = Image.FromStream(ms, true, true))
                {
                    var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(copy))
                        g.DrawImage(src, 0, 0, src.Width, src.Height);
                    Trim();
                    _map[file] = copy;
                    _order.Add(file);
                    return copy;
                }
            }
            catch { return null; }
        }

        private void Trim()
        {
            while (_order.Count > Limit)
            {
                string k = _order[0];
                _order.RemoveAt(0);
                Image v;
                if (_map.TryGetValue(k, out v))
                {
                    _map.Remove(k);
                    try { v.Dispose(); } catch { }
                }
            }
        }

        public static void DrawFit(Graphics g, Image img, Rectangle dest)
        {
            if (img == null) return;
            double s = Math.Min((double)dest.Width / img.Width, (double)dest.Height / img.Height);
            int w = (int)(img.Width * s), h = (int)(img.Height * s);
            int x = dest.X + (dest.Width - w) / 2, y = dest.Y + (dest.Height - h) / 2;
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, new Rectangle(x, y, w, h));
            g.InterpolationMode = old;
        }
    }

    /// <summary>池子卡片：封面用池内最贵饰品，显示均价 / 单抽花费 / 回收价。</summary>
    /// <summary>
    /// 统一主题色板：蓝 + 黑。
    /// 主色实测自素材立绘（藏蓝 #3C3C78 / 蓝 #4860A8 / 深蓝 #242454 / 亮蓝 #486CC0）。
    /// 改配色只改这里即可。
    /// </summary>
    /// <summary>
    /// 立绘容器：背景与父容器一致，并叠一层柔和的蓝色径向光晕。
    /// 之所以自绘背景，是为了避免普通 Panel 的矩形底色在深色界面上显出方块。
    /// </summary>
    public sealed class MascotPane : Panel
    {
        public MascotPane()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            // 1) 先把父容器的背景画上来（Parent.BackColor 或让父级自己画）
            if (Parent != null)
            {
                var st = g.Save();
                try
                {
                    g.TranslateTransform(-Left, -Top);
                    var pe = new PaintEventArgs(g, new Rectangle(Left, Top, Width, Height));
                    InvokePaintBackground(Parent, pe);
                    InvokePaint(Parent, pe);
                }
                catch { }
                g.Restore(st);
            }
            else
            {
                using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            }

            // 2) 叠柔和光晕：用同心椭圆渐隐，边缘提前透明 -> 无硬边
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            int steps = 14;
            for (int i = steps; i >= 1; i--)
            {
                double f = i / (double)steps;             // 1 -> 0
                int ew = (int)(w * f);
                int eh = (int)(h * 0.92 * f);
                if (ew <= 0 || eh <= 0) continue;
                int alpha = (int)(16 * (1.0 - f) * (1.0 - f) * 10);   // 外圈几乎透明
                if (alpha <= 0) continue;
                var r = new Rectangle((w - ew) / 2, (h - eh) / 2, ew, eh);
                using (var b = new SolidBrush(Color.FromArgb(alpha, Theme.Accent)))
                    g.FillEllipse(b, r);
            }
        }
    }

    /// <summary>界面素材（立绘 / 背景），按目录加载并缓存。</summary>
    public sealed class UiAssets
    {
        readonly string _dir;
        readonly Dictionary<string, Image> _cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public UiAssets(string dir) { _dir = dir; }

        public string Dir { get { return _dir; } }

        /// <summary>取图；不存在返回 null（界面自行退化为纯色）。</summary>
        public Image Get(string name)
        {
            if (string.IsNullOrEmpty(_dir)) return null;
            Image img;
            if (_cache.TryGetValue(name, out img)) return img;
            try
            {
                string p = Path.Combine(_dir, name);
                if (!File.Exists(p)) { _cache[name] = null; return null; }
                // 从文件流加载并复制，避免文件被占用
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                using (var tmp = Image.FromStream(fs))
                    img = new Bitmap(tmp);
            }
            catch { img = null; }
            _cache[name] = img;
            return img;
        }

        /// <summary>按目录候选列表建实例（先找到的先用）。</summary>
        public static UiAssets Locate(params string[] candidates)
        {
            foreach (var c in candidates)
            {
                try { if (!string.IsNullOrEmpty(c) && Directory.Exists(c)) return new UiAssets(c); }
                catch { }
            }
            return new UiAssets(null);
        }
    }

    public static class Theme
    {
        // 背景层（越往下越亮）
        public static readonly Color BgDeep = Color.FromArgb(8, 11, 20);        // 最底
        public static readonly Color BgTop = Color.FromArgb(14, 20, 36);        // 渐变上
        public static readonly Color BgBottom = Color.FromArgb(8, 11, 20);      // 渐变下
        public static readonly Color CardBg = Color.FromArgb(17, 23, 40);       // 卡片
        public static readonly Color CardBg2 = Color.FromArgb(13, 18, 32);      // 卡片渐变下
        public static readonly Color PanelBg = Color.FromArgb(15, 21, 36);      // 面板
        public static readonly Color SlotBg = Color.FromArgb(20, 28, 50);       // 内容槽
        public static readonly Color SlotBg2 = Color.FromArgb(13, 19, 34);

        // 描边
        public static readonly Color Border = Color.FromArgb(38, 52, 88);
        public static readonly Color BorderSoft = Color.FromArgb(26, 36, 62);

        // 文字
        public static readonly Color TextMain = Color.FromArgb(232, 240, 255);
        public static readonly Color TextDim = Color.FromArgb(146, 166, 204);
        public static readonly Color TextFaint = Color.FromArgb(104, 122, 158);

        // 主强调色（蓝）
        public static readonly Color Accent = Color.FromArgb(72, 132, 240);      // #4884F0
        public static readonly Color AccentDim = Color.FromArgb(48, 96, 190);
        public static readonly Color AccentDeep = Color.FromArgb(34, 68, 148);
        public static readonly Color AccentLite = Color.FromArgb(120, 176, 255);
        public static readonly Color AccentGlow = Color.FromArgb(96, 168, 255);

        // 金色（金币相关，保留金色以区分）
        public static readonly Color Gold = Color.FromArgb(242, 196, 84);
        public static readonly Color GoldDeep = Color.FromArgb(190, 140, 40);

        // 语义色
        public static readonly Color Ok = Color.FromArgb(96, 200, 140);
        public static readonly Color Bad = Color.FromArgb(224, 96, 104);
        public static readonly Color Warn = Color.FromArgb(226, 168, 72);
        public static readonly Color Muted = Color.FromArgb(110, 124, 152);
    }

    public sealed class PoolCard : Control
    {
        private bool _hover;
        private bool _selected;
        public Pool Pool;
        public ImageCache Cache;

        public PoolCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(216, 176);
        }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected != value) { _selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 最小化时控件可能塌成 0 尺寸，GDI+ 会抛异常
            if (ClientSize.Width <= 1 || ClientSize.Height <= 1) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color accent = Pool != null ? Pool.Accent : Color.Gray;

            using (var gp = Round.Path(r, 12))
            using (var lg = new LinearGradientBrush(r, Theme.SlotBg, Theme.SlotBg2, 90f))
                g.FillPath(lg, gp);

            // 封面区
            var cover = new Rectangle(8, 8, Width - 16, 96);
            using (var gp = Round.Path(cover, 8))
                g.SetClip(gp);
            using (var b = new SolidBrush(Theme.BgTop))
                g.FillRectangle(b, cover);
            if (Pool != null && Pool.Cover != null && Cache != null)
            {
                var img = Cache.Get(Pool.Cover.Image);
                ImageCache.DrawFit(g, img, Rectangle.Inflate(cover, -6, -6));
            }
            // 封面底部渐隐
            using (var lg = new LinearGradientBrush(
                new Rectangle(cover.X, cover.Bottom - 34, cover.Width, 34),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(210, 18, 22, 30), 90f))
                g.FillRectangle(lg, new Rectangle(cover.X, cover.Bottom - 34, cover.Width, 34));
            g.ResetClip();

            // 池名
            var nameFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            using (var b = new SolidBrush(Theme.TextMain))
                g.DrawString(Pool != null ? Pool.Name : "", nameFont, b,
                    new RectangleF(12, 108, Width - 24, 20));
            nameFont.Dispose();

            // 数据行
            var sf = new Font("Segoe UI", 8f, FontStyle.Regular);
            var sfB = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using (var b = new SolidBrush(Theme.TextDim))
            {
                string line1;
                if (Pool != null && Pool.IsMiracle)
                {
                    // 奇迹池：均价会被极品严重拉高，改为显示真实掉落构成
                    line1 = string.Format(CultureInfo.InvariantCulture,
                        "极品 {0:0.#}%  ·  保底 {1:0.#}%",
                        Pool.RareChance * 100, (1 - Pool.RareChance) * 100);
                }
                else
                {
                    line1 = string.Format(CultureInfo.InvariantCulture, "{0} 件 · 均价 ${1:N2}",
                        Pool != null ? Pool.Items.Count : 0,
                        Pool != null ? Pool.AvgPriceCents / 100.0 : 0);
                }
                g.DrawString(line1, sf, b, new RectangleF(12, 129, Width - 24, 16));
            }
            using (var b = new SolidBrush(ColorUtilLight(accent)))
            {
                g.DrawString(string.Format(CultureInfo.InvariantCulture, "抽一次 {0} 金币",
                    Pool != null ? Pool.CostCents : 0), sfB, b, new RectangleF(12, 147, Width - 24, 18));
            }
            sf.Dispose(); sfB.Dispose();

            // 边框
            using (var gp = Round.Path(r, 12))
            using (var pen = new Pen(_selected ? Color.FromArgb(255, accent)
                                     : Color.FromArgb(_hover ? 170 : 60, accent), _selected ? 2.4f : 1.3f))
                g.DrawPath(pen, gp);
            if (_selected)
            {
                using (var gp = Round.Path(new Rectangle(0, 0, Width - 1, Height - 1), 12))
                using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                    g.DrawPath(pen, gp);
            }
        }

        private static Color ColorUtilLight(Color c)
        {
            return Color.FromArgb(
                Math.Min(255, c.R + 70), Math.Min(255, c.G + 70), Math.Min(255, c.B + 70));
        }
    }

    /// <summary>
    /// 横向滚动卷轴：中奖物品停在正中。
    /// 用“匀速+末端减速”的位移曲线，配合滴答音效。
    /// </summary>
    public sealed class ReelPanel : Control
    {
        private readonly Timer _timer = new Timer { Interval = 16 };
        private readonly List<Item> _strip = new List<Item>();
        private readonly Random _rnd = new Random();
        private double _offset;           // 当前滚动位移（像素）
        private double _target;           // 目标位移
        private double _start;            // 起始位移
        private double _t;                // 0..1 动画进度
        private double _duration;         // 毫秒
        private bool _running;
        private Item _result;
        private int _winSlot = -1;        // 中奖物的精确槽位（用 IndexOf 会被同名物品误导）

        // 金光特效状态
        private readonly Timer _glowTimer = new Timer { Interval = 33 };   // 约 30fps
        private bool _goldGlow;
        private double _glowTime;
        private double _glowIntensity;    // 0..1 渐入
        private readonly List<double[]> _sparks = new List<double[]>();   // {x, y, vx, vy, life, size}

        // 预缩放缩略图：避免每帧对每个槽位做双三次缩放（否则绘制过慢会让动画掉帧）
        private readonly Dictionary<string, Image> _thumbs = new Dictionary<string, Image>(StringComparer.Ordinal);
        private int _thumbW, _thumbH;
        // 滴答音效节流：密集滚动时不要每帧都新建 SoundPlayer
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private Bitmap _stripBmp;         // 预渲染的整条卷轴

        public ImageCache Cache;
        public int SlotWidth = 118;
        public int SlotHeight = 140;
        public Color Accent = Theme.Accent;

        public event EventHandler Settled;
        /// <summary>预渲染完成、动画即将开始的那一刻触发（用于同步音乐）。</summary>
        public event EventHandler RollStarted;

        public ReelPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgDeep;
            _timer.Tick += Tick;
            _glowTimer.Tick += GlowTick;
        }

        public Item Result { get { return _result; } }

        /// <summary>静态预览：未抽奖时也显示一排饰品，避免开场空白。</summary>
        public void ShowPreview(IList<Item> pool)
        {
            _running = false;
            _timer.Stop();
            StopGlow();
            _result = null;
            _winSlot = -1;
            _strip.Clear();
            if (pool == null || pool.Count == 0) { Invalidate(); return; }
            int need = Math.Max(6, Width / Math.Max(1, SlotWidth) + 3);
            for (int i = 0; i < need; i++)
                _strip.Add(pool[i % pool.Count]);
            _offset = 0;
            BuildStripBitmap();
            Invalidate();
        }

        /// <summary>开始一次滚动。durationMs 为滚动时长，golden 为金光大奖。</summary>
        public void Spin(IList<Item> pool, Item winner, double durationMs, bool golden)
        {
            StopGlow();
            _goldGlow = golden;
            _result = winner;
            _strip.Clear();

            // 构造卷轴内容：中奖物放在第 N 个槽位，前面用随机物品填充
            // 按 10 秒的滚动距离需要足够多的槽位：位移距离 ≈ 速度 × 时间
            int pre = Math.Max(26, (int)(durationMs / 1000.0 * 14));
            for (int i = 0; i < pre; i++)
                _strip.Add(pool[_rnd.Next(pool.Count)]);
            int winSlotIndex = _strip.Count;
            _winSlot = winSlotIndex;
            _strip.Add(winner);
            for (int i = 0; i < 8; i++)
                _strip.Add(pool[_rnd.Next(pool.Count)]);

            // 目标位移：让中奖槽位对齐面板中心（含少量随机偏移，避免永远正中看着假）
            int centerX = Width / 2;
            int jitter = _rnd.Next(-(SlotWidth / 5), SlotWidth / 5 + 1);
            _target = winSlotIndex * SlotWidth + SlotWidth / 2 - centerX + jitter;

            // 起始：从更靠前的位置开始滚，制造长距离滚动感
            _start = -800;
            _offset = _start;
            _t = 0;
            _duration = Math.Max(800, durationMs);
            // 预渲染（不计入动画时长）
            BuildStripBitmap();

            _running = true;

            if (StartDelayMs > 0)
            {
                // 错峰启动：延后 StartDelayMs 再真正开始转。
                // 计时器必须存进字段——局部变量的 WinForms Timer 可能在触发前被回收，
                // 那样 BeginRoll 永不执行，卷轴会一直停着。
                if (_delayTimer != null) { _delayTimer.Stop(); _delayTimer.Dispose(); }
                _delayTimer = new Timer { Interval = (int)Math.Max(1, StartDelayMs) };
                _delayTimer.Tick += (s, e) =>
                {
                    _delayTimer.Stop();
                    BeginRoll();
                };
                _delayTimer.Start();      // 必须启动，否则卷轴永远不动
                return;
            }
            BeginRoll();
        }

        /// <summary>
        /// 真正开始滚动：通知外部（启动音乐 / 记录同步时刻），再启动计时器。
        /// 抽离出来是为了让「延迟启动」与「立即启动」走同一条路径。
        /// </summary>
        private void BeginRoll()
        {
            // 通知外部；外部会在此事件里启动音乐并把「音乐启动时刻」
            // 通过 MarkExternalStart() 写回 _syncStartMs。
            var rs = RollStarted;
            if (rs != null) rs(this, EventArgs.Empty);
            // 动画起点 = 外部给出的音乐启动时刻（所有卷轴共用同一值，必然同步）；
            // 若外部没给（例如单测），退化为当前时刻。
            _animStartMs = (_syncStartMs >= 0) ? _syncStartMs : _clock.Elapsed.TotalMilliseconds;
            _timer.Start();
        }

        public void Stop()
        {
            _running = false;
            _timer.Stop();
        }

        private double _animStartMs;       // 动画起始时刻（挂钟驱动）
        private Timer _delayTimer;         // 错峰启动用的延迟计时器（必须持引用）
        /// <summary>延迟启动毫秒数（五连抽错峰用）。0 表示立即启动。</summary>
        public double StartDelayMs;
        private double _syncStartMs = -1;  // 外部（音乐）启动时刻，用于与动画对齐

        /// <summary>
        /// 由外部在启动音乐的同一时刻调用，使动画以该时刻为起点。
        /// 这是「音频与动画同时播放」的关键：两者共用同一个时间原点。
        /// </summary>
        public void MarkExternalStart()
        {
            _syncStartMs = _clock.Elapsed.TotalMilliseconds;
        }

        private void Tick(object sender, EventArgs e)
        {
            double nowMs = _clock.Elapsed.TotalMilliseconds;
            // 用挂钟时间驱动，而不是累加固定步长：
            // WinForms Timer 的实际间隔可能远大于设定值（实测 16ms 设置下约 26ms），
            // 若按步长累加，10 秒动画会被拖成 16 秒以上。
            _t = (nowMs - _animStartMs) / _duration;
            if (_t >= 1.0)
            {
                _t = 1.0;
                _running = false;
                _timer.Stop();
                _offset = _target;
                Invalidate();
                // 出货音乐由上层在 Settled 后播放（需要先判定是否金光大奖）
                if (_goldGlow) StartGlow();
                var h = Settled;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }

            // 缓出曲线：整体由快到慢。指数 4.2 使末段减速更明显
            double eased = 1.0 - Math.Pow(1.0 - _t, 4.2);
            _offset = _start + (_target - _start) * eased;
            Invalidate();
        }

        /// <summary>取预缩放的缩略图（按槽位大小缓存），显著降低每帧绘制开销。</summary>
        private Image Thumb(Item it)
        {
            if (it == null || Cache == null) return null;
            int w = Math.Max(8, SlotWidth - 16);
            int h = Math.Max(8, SlotHeight - 44);
            if (_thumbW != w || _thumbH != h) { _thumbs.Clear(); _thumbW = w; _thumbH = h; }
            Image cached;
            string key = it.Image ?? "";
            if (key.Length == 0) return null;
            if (_thumbs.TryGetValue(key, out cached)) return cached;

            var src = Cache.Get(key);
            if (src == null) { _thumbs[key] = null; return null; }
            try
            {
                var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g2 = Graphics.FromImage(bmp))
                {
                    g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g2.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    double s = Math.Min((double)w / src.Width, (double)h / src.Height);
                    int dw = (int)(src.Width * s), dh = (int)(src.Height * s);
                    g2.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
                }
                _thumbs[key] = bmp;
                return bmp;
            }
            catch { _thumbs[key] = null; return null; }
        }

        /// <summary>
        /// 把整条卷轴一次性预渲染成一张横向长图。
        /// 之后每帧只按位移贴图（一次 DrawImageUnscaled），
        /// 避免逐槽渐变/文字绘制导致掉帧（实测逐槽绘制会把 10 秒动画拖到 16.6 秒）。
        /// </summary>
        private void BuildStripBitmap()
        {
            if (_stripBmp != null) { _stripBmp.Dispose(); _stripBmp = null; }
            int count = _strip.Count;
            if (count == 0 || Width <= 0 || Height <= 0) return;

            int totalW = count * SlotWidth + SlotWidth;
            try
            {
                _stripBmp = new Bitmap(totalW, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            }
            catch { _stripBmp = null; return; }

            using (var g = Graphics.FromImage(_stripBmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BackColor);

                int cy = Height / 2;
                int top = cy - SlotHeight / 2;
                var fName = new Font("Segoe UI", 7.5f);

                for (int i = 0; i < count; i++)
                {
                    var it = _strip[i];
                    int x = i * SlotWidth;
                    var rect = new Rectangle(x + 4, top, SlotWidth - 8, SlotHeight);
                    Color accent = RarityColor(it.Rarity);

                    using (var gp = Round.Path(rect, 10))
                    using (var lg = new LinearGradientBrush(rect,
                        Theme.SlotBg, Theme.SlotBg2, 90f))
                        g.FillPath(lg, gp);

                    var thumb = Thumb(it);
                    if (thumb != null) g.DrawImageUnscaled(thumb, rect.X + 8, rect.Y + 8);

                    using (var b = new SolidBrush(Theme.TextMain))
                        g.DrawString(Trim(it.Name, 12), fName, b,
                            new RectangleF(rect.X + 7, rect.Bottom - 34, rect.Width - 14, 14));
                    using (var b = new SolidBrush(Theme.TextDim))
                        g.DrawString(WearShort(it.Wear), fName, b,
                            new RectangleF(rect.X + 7, rect.Bottom - 20, rect.Width - 14, 14));

                    using (var b = new SolidBrush(accent))
                        g.FillRectangle(b, rect.X, rect.Y, 4, rect.Height);
                    using (var gp = Round.Path(rect, 10))
                    using (var pen = new Pen(Color.FromArgb(70, accent), 1.2f))
                        g.DrawPath(pen, gp);
                }
                fName.Dispose();
            }
        }

        // ============================================================ 金光特效
        private void StartGlow()
        {
            _goldGlow = true;
            _glowTime = 0;
            _glowIntensity = 0;
            _sparks.Clear();
            var rnd = new Random();
            for (int i = 0; i < 90; i++)
            {
                double ang = rnd.NextDouble() * Math.PI * 2;
                double spd = 1.2 + rnd.NextDouble() * 5.5;
                _sparks.Add(new double[] {
                    Width / 2.0, Height / 2.0,
                    Math.Cos(ang) * spd, Math.Sin(ang) * spd,
                    1.0, 1.5 + rnd.NextDouble() * 3.5
                });
            }
            _glowTimer.Start();
        }

        private void StopGlow()
        {
            _glowTimer.Stop();
            _goldGlow = false;
            _glowIntensity = 0;
            _sparks.Clear();
        }

        private void GlowTick(object sender, EventArgs e)
        {
            _glowTime += _glowTimer.Interval / 1000.0;
            _glowIntensity = Math.Min(1.0, _glowIntensity + _glowTimer.Interval / 450.0);

            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s[0] += s[2];
                s[1] += s[3];
                s[3] += 0.16;                      // 轻微重力
                s[2] *= 0.985;
                s[4] -= _glowTimer.Interval / 2600.0;
                if (s[4] <= 0) _sparks.RemoveAt(i);
            }
            // 补充新粒子，保持持续喷发
            if (_sparks.Count < 120 && _glowTime < 4.0)
            {
                var rnd = new Random();
                double ang = rnd.NextDouble() * Math.PI * 2;
                double spd = 1.2 + rnd.NextDouble() * 5.5;
                _sparks.Add(new double[] {
                    Width / 2.0, Height / 2.0,
                    Math.Cos(ang) * spd, Math.Sin(ang) * spd,
                    1.0, 1.5 + rnd.NextDouble() * 3.5
                });
            }
            Invalidate();
        }

        /// <summary>绘制金光：旋转射线 + 脉冲光晕 + 金色粒子。</summary>
        private void DrawGoldGlow(Graphics g)
        {
            if (!_goldGlow || _glowIntensity <= 0) return;
            double a = _glowIntensity;
            int cx = Width / 2, cy = Height / 2;
            double pulse = 0.72 + 0.28 * Math.Sin(_glowTime * 6.0);

            var old = g.Clip;
            g.SetClip(ClientRectangle);

            // 1) 中心径向光晕
            int maxR = (int)(Math.Max(Width, Height) * 0.75);
            for (int i = 10; i >= 1; i--)
            {
                int r = maxR * i / 10;
                int alpha = (int)(a * pulse * (11 - i) * 3.2);
                if (alpha <= 0) continue;
                using (var b = new SolidBrush(Color.FromArgb(Math.Min(60, alpha), 255, 214, 96)))
                    g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            }

            // 2) 旋转射线
            double rot = _glowTime * 0.9;
            int rays = 16;
            for (int i = 0; i < rays; i++)
            {
                double ang = rot + i * (Math.PI * 2 / rays);
                double len = maxR * (0.55 + 0.45 * ((i % 2 == 0) ? 1.0 : 0.6));
                int alpha = (int)(a * pulse * ((i % 2 == 0) ? 46 : 26));
                if (alpha <= 0) continue;
                using (var pen = new Pen(Color.FromArgb(Math.Min(90, alpha), 255, 226, 130), 2.2f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(pen, cx, cy,
                        cx + (int)(Math.Cos(ang) * len), cy + (int)(Math.Sin(ang) * len));
                }
            }

            // 3) 金色粒子
            foreach (var s in _sparks)
            {
                int alpha = (int)(a * Math.Max(0, Math.Min(1, s[4])) * 235);
                if (alpha <= 0) continue;
                double sz = s[5] * Math.Max(0.2, s[4]);
                using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, alpha), 255, 232, 150)))
                    g.FillEllipse(b, (float)(s[0] - sz / 2), (float)(s[1] - sz / 2),
                                  (float)sz, (float)sz);
            }

            // 4) 全屏金色面纱
            int veil = (int)(a * pulse * 34);
            if (veil > 0)
                using (var b = new SolidBrush(Color.FromArgb(veil, 255, 200, 70)))
                    g.FillRectangle(b, ClientRectangle);

            g.Clip = old;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 最小化时控件可能塌成 0 尺寸，GDI+ 会抛异常
            if (ClientSize.Width <= 1 || ClientSize.Height <= 1) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);

            int cy = Height / 2;
            int top = cy - SlotHeight / 2;

            // 主体：一次位移贴图（预渲染长图），保证 60fps 不掉帧
            if (_stripBmp != null)
            {
                // 源矩形必须裁剪到 [0, 位图宽) —— 越界时 GDI+ 会缩放并错位，
                // 表现为卷轴只画出右侧一条。
                int sx = (int)Math.Round(_offset);
                int sw = Width;
                int dx = 0, dw = Width;

                if (sx < 0) { dw += sx; dx = -sx; sw += sx; sx = 0; }
                int maxW = _stripBmp.Width - sx;
                if (sw > maxW) { sw = maxW; dw = sw; }

                if (sw > 0 && dw > 0)
                    g.DrawImage(_stripBmp, new Rectangle(dx, 0, dw, Height),
                                new Rectangle(sx, 0, sw, Height), GraphicsUnit.Pixel);
            }

            // 中奖高亮框（单独绘制，避免污染预渲染图）
            if (!_running && _result != null)
            {
                // 必须用记录的精确槽位：IndexOf 会命中第一个同名物品，导致高亮框大幅偏移
                int winIdx = _winSlot;
                if (winIdx >= 0 && winIdx < _strip.Count)
                {
                    int wx = (int)(winIdx * SlotWidth - _offset);
                    var wrect = new Rectangle(wx + 4, top, SlotWidth - 8, SlotHeight);
                    if (wrect.Right > 0 && wrect.Left < Width)
                    {
                        using (var gp = Round.Path(wrect, 10))
                        using (var pen = new Pen(Color.FromArgb(255, Accent), 3f))
                            g.DrawPath(pen, gp);
                        using (var gp = Round.Path(wrect, 10))
                        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1.2f))
                            g.DrawPath(pen, gp);
                    }
                }
            }

            // 中央指示器
            int cxi = Width / 2;
            using (var pen = new Pen(Color.FromArgb(235, Accent), 3f))
            {
                g.DrawLine(pen, cxi, top - 16, cxi, top - 4);
                g.DrawLine(pen, cxi, top + SlotHeight + 4, cxi, top + SlotHeight + 16);
            }
            using (var pen = new Pen(Color.FromArgb(90, Accent), 1.5f))
                g.DrawRectangle(pen, cxi - SlotWidth / 2, top - 3, SlotWidth, SlotHeight + 6);

            // 两侧渐隐，强化"滚动"观感
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, 90, Height),
                Color.FromArgb(230, BackColor), Color.FromArgb(0, BackColor), 0f))
                g.FillRectangle(lg, 0, 0, 90, Height);
            using (var lg = new LinearGradientBrush(new Rectangle(Width - 90, 0, 90, Height),
                Color.FromArgb(0, BackColor), Color.FromArgb(230, BackColor), 0f))
                g.FillRectangle(lg, Width - 90, 0, 90, Height);

            // 金光大奖特效（最上层）
            DrawGoldGlow(g);
        }

        private static string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        private static string WearShort(string wear)
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

        public static Color RarityColor(string r)
        {
            switch (r)
            {
                case "消费级": return Color.FromArgb(176, 195, 217);
                case "工业级": return Color.FromArgb(94, 152, 217);
                case "军规级": return Color.FromArgb(75, 105, 255);
                case "受限级": return Color.FromArgb(136, 71, 255);
                case "保密级": return Color.FromArgb(211, 44, 230);
                case "隐秘级": return Theme.Bad;
                case "非凡": return Theme.Bad;
                case "违禁": return Color.FromArgb(228, 174, 57);
                default: return Color.Gray;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Stop(); _timer.Dispose(); } catch { }
                try { _glowTimer.Stop(); _glowTimer.Dispose(); } catch { }
                try { if (_stripBmp != null) { _stripBmp.Dispose(); _stripBmp = null; } } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
