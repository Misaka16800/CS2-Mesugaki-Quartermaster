using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Cs2Roulette
{
    internal static class Program
    {
        /// <summary>把崩溃信息写入 error.log（不弹窗，便于自动化抓取）。</summary>
        private static void LogCrash(string tag, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("====================");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + tag + "]");
                sb.AppendLine(ex == null ? "(null exception)" : ex.ToString());
                // 附上一些窗口状态，便于判断是否与最小化/尺寸有关
                try
                {
                    foreach (Form f in Application.OpenForms)
                        sb.AppendLine("  Form: " + f.GetType().Name
                            + " WindowState=" + f.WindowState
                            + " ClientSize=" + f.ClientSize
                            + " Bounds=" + f.Bounds);
                }
                catch { }
                sb.AppendLine();
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"),
                    sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // ---- 全局异常记录：写日志而不是弹窗，便于排查 ----
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    LogCrash("UI", e.Exception);
                    MessageBox.Show("出错了：\r\n\r\n" + e.Exception.Message
                        + "\r\n\r\n详细信息已写入 error.log",
                        "CS2 雌小鬼军需", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    LogCrash("Domain", e.ExceptionObject as Exception);
                };

                // 数据目录：优先 exe 同级的 data，其次当前目录，最后上一级
                string dataDir = null;
                foreach (var c in new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"),
                    Path.Combine(Directory.GetCurrentDirectory(), "data"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data"),
                })
                {
                    if (File.Exists(Path.Combine(c, "items.json"))) { dataDir = c; break; }
                }
                if (dataDir == null)
                {
                    MessageBox.Show("找不到 data\\items.json，请确认程序与数据在同一目录。",
                        "CS2 抽奖模拟器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var db = ItemDatabase.Load(dataDir);
                if (db == null || db.Pools == null || db.Pools.Count == 0)
                {
                    MessageBox.Show("奖池数据加载失败。",
                        "CS2 抽奖模拟器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var save = SaveData.Load(dataDir);
                // 首次运行给一笔启动金币。
                // 注：奖池按真实市场均价 ×120% 定价，中高档池单抽动辄数万金币，
                //     这里给 30000 让玩家能立刻体验中档池，而不是只能抽最便宜的。
                if (save.TotalEarned == 0 && save.Coins == 0 && save.OwnedItemIds.Count == 0)
                {
                    save.Coins = 30000;
                    save.TotalEarned = 30000;
                }

                bool autoDraw = false;
                bool forceJackpot = false;
                int autoPool = 0;
                bool benchDraw = false;
                bool testVault = false;
                bool testDebug = false;
                int testTrade = 0;
                bool testLimit = false;
                int testQuiz = 0;
                int quizDist = 0;
                int testCmp = 0;
                bool poolDump = false;
                long forceCoins = -1;
                string autoPoolKey = null;
                bool testCd = false;
                bool testOffer = false;
                bool autoSell = false;
                bool autoKeep = false;
                bool showAbout = false;
                int poolStat = 0;
                int drawCount = 1;
                string poolSwitch = null;
                int startTab = 0;

                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--autodraw", StringComparison.OrdinalIgnoreCase))
                        autoDraw = true;
                    else if (string.Equals(args[i], "--jackpot", StringComparison.OrdinalIgnoreCase))
                        forceJackpot = true;
                    else if (string.Equals(args[i], "--benchdraw", StringComparison.OrdinalIgnoreCase))
                        benchDraw = true;
                    else if (string.Equals(args[i], "--testvault", StringComparison.OrdinalIgnoreCase))
                        testVault = true;
                    else if (string.Equals(args[i], "--testdebug", StringComparison.OrdinalIgnoreCase))
                        testDebug = true;
                    else if (string.Equals(args[i], "--testlimit", StringComparison.OrdinalIgnoreCase))
                        testLimit = true;
                    else if (string.Equals(args[i], "--poolswitch", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        poolSwitch = args[i + 1];
                    else if (string.Equals(args[i], "--drawcount", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int dc;
                        if (int.TryParse(args[i + 1], out dc)) drawCount = dc;
                    }
                    else if (string.Equals(args[i], "--autokeep", StringComparison.OrdinalIgnoreCase))
                        autoKeep = true;
                    else if (string.Equals(args[i], "--autosell", StringComparison.OrdinalIgnoreCase))
                        autoSell = true;
                    else if (string.Equals(args[i], "--testoffer", StringComparison.OrdinalIgnoreCase))
                        testOffer = true;
                    else if (string.Equals(args[i], "--about", StringComparison.OrdinalIgnoreCase))
                        showAbout = true;
                    else if (string.Equals(args[i], "--testcd", StringComparison.OrdinalIgnoreCase))
                        testCd = true;
                    else if (string.Equals(args[i], "--poolstat", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int pv;
                        if (int.TryParse(args[i + 1], out pv)) poolStat = pv;
                    }
                    else if (string.Equals(args[i], "--pooldump", StringComparison.OrdinalIgnoreCase))
                        poolDump = true;
                    else if (string.Equals(args[i], "--coins", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        long cv;
                        if (long.TryParse(args[i + 1], out cv)) forceCoins = cv;
                    }
                    else if (string.Equals(args[i], "--testcmp", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int cv;
                        if (int.TryParse(args[i + 1], out cv)) testCmp = cv;
                    }
                    else if (string.Equals(args[i], "--quizdist", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int dv;
                        if (int.TryParse(args[i + 1], out dv)) quizDist = dv;
                    }
                    else if (string.Equals(args[i], "--testquiz", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        // 负值 = 故意答错（验证答错反馈路径）
                        int qv;
                        if (int.TryParse(args[i + 1], out qv)) testQuiz = qv;
                    }
                    else if (string.Equals(args[i], "--testtrade", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int tv;
                        if (int.TryParse(args[i + 1], out tv)) testTrade = tv;
                    }
                    else if (string.Equals(args[i], "--soundpack", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        // classic / delta
                        if (string.Equals(args[i + 1], "delta", StringComparison.OrdinalIgnoreCase))
                            Music.Pack = Music.SoundPackKind.Delta;
                        else
                            Music.Pack = Music.SoundPackKind.Classic;
                    }
                    else if (string.Equals(args[i], "--poolkey", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        autoPoolKey = args[i + 1];
                    else if (string.Equals(args[i], "--pool", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int v;
                        if (int.TryParse(args[i + 1], out v)) autoPool = v;
                    }
                    else if (string.Equals(args[i], "--tab", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int t;
                        if (int.TryParse(args[i + 1], out t)) startTab = t;
                    }
                }

                Application.Run(new MainForm(db, dataDir, save, autoDraw, startTab, forceJackpot, autoPool, benchDraw, testVault, testDebug, testTrade, testLimit, testQuiz, quizDist, testCmp, poolDump, forceCoins, autoPoolKey, testCd, drawCount, testOffer, autoSell, autoKeep, poolSwitch, showAbout, poolStat));
            }
            catch (Exception ex)
            {
                // 写日志便于排查（弹窗在大屏缩放或无人值守时会看不全）
                try
                {
                    File.WriteAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"),
                        DateTime.Now + "\r\n" + ex.ToString(),
                        new UTF8Encoding(false));
                }
                catch { }

                MessageBox.Show("启动失败：\r\n\r\n" + ex.Message + "\r\n\r\n" + ex.StackTrace,
                    "CS2 饰品抽奖模拟器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
