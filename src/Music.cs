using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;

namespace Cs2Roulette
{
    /// <summary>
    /// 音乐与音效，全部由代码合成波形（不内嵌任何外部音频素材）。
    ///
    /// 播放方式说明：
    ///   原先用 SoundPlayer.Play() 播放 10 秒转动音乐，实测经常听不到——该 API 从流
    ///   异步播放时依赖托管对象的存活，长音频容易中途被回收而静音。
    ///   现改用 winmm.dll 的 PlaySound(SND_MEMORY | SND_ASYNC)：
    ///   直接播放内存中的 WAV，由系统持有缓冲区，不受 GC 影响。
    /// </summary>
    public static class Music
    {
        private const int SR = 44100;
        public const double RollSeconds = 10.0;      // 转动音乐与动画时长一致

        // ---- winmm 播放 ----
        private const uint SND_ASYNC = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;
        private const uint SND_MEMORY = 0x0004;
        private const uint SND_NOSTOP = 0x0010;

        [DllImport("winmm.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

        private static byte[][] _rolls;              // 转动音乐（数量见 RollVariantCount）
        private static byte[] _tick;
        private static byte[] _winNormal;
        private static byte[] _winJackpot;
        private static readonly Random _rnd = new Random();

        public static bool Muted;
        public static int Volume = 70;               // 0..100

        /// <summary>
        /// 音色包选择。
        ///   Classic —— 原电子舞曲风（默认）
        ///   Delta   —— 三角洲风格：战术军事 / 工业电子（FM 金属敲击 + 重低音冲击）
        /// 两者均为本程序原创合成，不含任何第三方音频素材。
        /// </summary>
        public enum SoundPackKind { Classic, Delta }

        public static SoundPackKind Pack = SoundPackKind.Classic;

        /// <summary>最近一次转动音乐是否成功启动（供诊断）。</summary>
        public static bool LastRollStarted;
        public static int LastRollIndex = -1;

        // ============================================================ 基础波形
        private static double Saw(double t, double f)
        {
            double x = (t * f) % 1.0;
            return 2.0 * x - 1.0;
        }

        private static double Square(double t, double f)
        {
            return ((t * f) % 1.0) < 0.5 ? 1.0 : -1.0;
        }

        private static double Noise(ref uint seed)
        {
            seed = seed * 1664525u + 1013904223u;
            return (seed >> 9) / (double)(1 << 23) - 1.0;
        }

        private static byte[] ToWav(double[] s)
        {
            int n = s.Length, dataLen = n * 2;
            var ms = new MemoryStream(44 + dataLen);
            var w = new BinaryWriter(ms, System.Text.Encoding.ASCII);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)1);
            w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);

            double peak = 0;
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(s[i]));
            double norm = peak > 0.97 ? 0.97 / peak : 1.0;
            double vol = Math.Max(0, Math.Min(100, Volume)) / 100.0;
            for (int i = 0; i < n; i++)
            {
                double v = s[i] * norm * vol;
                if (v > 1) v = 1; else if (v < -1) v = -1;
                w.Write((short)Math.Round(v * 32767));
            }
            w.Flush();
            return ms.ToArray();
        }

        // ============================================================ 鼓组与音色
        private static void Kick(double[] buf, int n, double t0)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(0.17 * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 25.0);
                double f = 155.0 * Math.Exp(-t * 30.0) + 46.0;
                buf[i0 + i] += Math.Sin(2 * Math.PI * f * t) * env * 0.95;
            }
        }

        private static void Snare(double[] buf, int n, double t0, ref uint seed, double amp = 0.55)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(0.14 * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 28.0);
                double body = Math.Sin(2 * Math.PI * 205 * t) * 0.35;
                buf[i0 + i] += (Noise(ref seed) * 0.78 + body) * env * amp;
            }
        }

        private static void Hat(double[] buf, int n, double t0, ref uint seed, double amp = 0.15)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(0.05 * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                buf[i0 + i] += Noise(ref seed) * Math.Exp(-t * 85.0) * amp;
            }
        }

        private static void Bass(double[] buf, int n, double t0, double dur, double f, double amp)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(dur * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Min(1.0, t / 0.004) * Math.Exp(-t * 5.5);
                buf[i0 + i] += Saw(t, f) * env * amp;
            }
        }

        private static void Arp(double[] buf, int n, double t0, double dur, double f, double amp)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(dur * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Min(1.0, t / 0.003) * Math.Exp(-t * 16.0);
                buf[i0 + i] += Square(t, f) * env * amp;
            }
        }

        private static void Sweep(double[] buf, int n, double t0, double dur, double f0, double f1,
                                  double amp, ref uint seed)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(dur * SR);
            double phase = 0;
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double p = t / dur;
                double f = f0 * Math.Pow(f1 / f0, p);
                phase += 2 * Math.PI * f / SR;
                double env = Math.Pow(p, 1.4);
                buf[i0 + i] += (Math.Sin(phase) * 0.55 + Noise(ref seed) * 0.3 * p) * env * amp;
            }
        }

        private static void Bell(double[] buf, int n, double t0, double f, double amp,
                                 double decay, double attack = 0.006)
        {
            int i0 = (int)(t0 * SR);
            if (i0 >= n) return;
            int len = n - i0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                double env = Math.Min(1.0, t / attack) * Math.Exp(-t * decay);
                double v = Math.Sin(2 * Math.PI * f * t) * 0.55
                         + Math.Sin(2 * Math.PI * f * 2 * t) * 0.24
                         + Math.Sin(2 * Math.PI * f * 3 * t) * 0.11
                         + Math.Sin(2 * Math.PI * f * 4 * t) * 0.05;
                buf[i0 + i] += v * env * amp;
            }
        }

        // ============================================================ 转动音乐
        // ---- 10 种变体参数（调式 / 速度 / 骨架）----
        // 底鼓 16 步骨架：一个小节 16 个十六分音符位置，'x' = 踩底鼓
        private static readonly string[] KickPatterns = {
            "x...x...x...x...",   // 四踩（标准）
            "x.....x.x...x...",   // 反拍
            "x..x..x...x.x...",   // 切分
            "x...x..xx...x...",   // 加花
            "x.x...x.x...x.x.",   // 双踩
        };
        // 军鼓落点（十六分位置索引，0-15）
        private static readonly int[][] SnareSteps = {
            new[] { 4, 12 },
            new[] { 4, 10, 12 },
            new[] { 4, 12, 14 },
            new[] { 6, 12 },
            new[] { 4, 8, 12 },
        };
        // 踩镲：每拍分几个
        private static readonly int[] HatRates = { 2, 2, 4, 3, 2 };
        // 贝斯节奏型（十六分位置 + 音级偏移）
        private static readonly int[][] BassPats = {
            new[] { 0, 0, 4, 4, 5, 5, 3, 2 },
            new[] { 0, 0, 0, 3, 5, 5, 4, 2 },
            new[] { 0, 4, 0, 4, 2, 5, 2, 5 },
            new[] { 0, 0, 2, 2, 4, 4, 6, 6 },
            new[] { 0, 5, 0, 3, 0, 5, 4, 2 },
        };
        // 琶音：每拍分几个
        private static readonly int[] ArpRates = { 4, 4, 8, 3, 6 };

        private static readonly double[][] Scales = {
            // D 小调
            new double[] { 146.83, 164.81, 174.61, 196.00, 220.00, 233.08, 261.63, 293.66 },
            // A 小调
            new double[] { 110.00, 123.47, 130.81, 146.83, 164.81, 174.61, 196.00, 220.00 },
            // E 小调
            new double[] { 164.81, 185.00, 196.00, 220.00, 246.94, 261.63, 293.66, 329.63 },
            // G 小调
            new double[] { 98.00, 110.00, 116.54, 130.81, 146.83, 155.56, 174.61, 196.00 },
            // C 小调
            new double[] { 130.81, 155.56, 174.61, 196.00, 233.08, 261.63, 311.13, 349.23 },
            // F 小调
            new double[] { 174.61, 196.00, 207.65, 233.08, 261.63, 277.18, 311.13, 349.23 },
            // B 小调
            new double[] { 123.47, 138.59, 146.83, 164.81, 185.00, 196.00, 220.00, 246.94 },
            // C# 小调
            new double[] { 138.59, 155.56, 164.81, 185.00, 207.65, 220.00, 246.94, 277.18 },
            // G# 小调
            new double[] { 103.83, 116.54, 123.47, 138.59, 155.56, 164.81, 185.00, 207.65 },
            // Bb 小调
            new double[] { 116.54, 130.81, 138.59, 155.56, 174.61, 185.00, 207.65, 233.08 },
        };
        private static readonly double[] Bpms = { 150.0, 160.0, 140.0, 155.0, 145.0,
                                                  165.0, 148.0, 158.0, 142.0, 152.0 };

        /// <summary>转动音乐变体数量（每首调式、速度、鼓点骨架都不同）。</summary>
        internal const int RollVariantCount = 10;

        private static byte[] BuildRoll(int variant)
        {
            int v = ((variant % RollVariantCount) + RollVariantCount) % RollVariantCount;
            double bpm = Bpms[v];
            double[] scale = Scales[v];
            string kickPat = KickPatterns[v % KickPatterns.Length];
            int[] snareSteps = SnareSteps[v % SnareSteps.Length];
            int hatRate = HatRates[v % HatRates.Length];
            int[] bassPat = BassPats[v % BassPats.Length];
            int arpRate = ArpRates[v % ArpRates.Length];

            double beat = 60.0 / bpm;
            double step16 = beat / 4.0;

            int n = (int)(SR * RollSeconds);
            var buf = new double[n];
            uint seed = (uint)(9871 + v * 7919);

            int totalSteps = (int)(RollSeconds / step16);
            double lastQuarter = RollSeconds * 0.75;

            for (int s = 0; s < totalSteps; s++)
            {
                double ts = s * step16;
                if (ts >= RollSeconds) break;
                int inBar = s % 16;
                bool nearEnd = ts >= lastQuarter;

                // 底鼓：按骨架踩
                if (kickPat[inBar] == 'x') Kick(buf, n, ts);

                // 军鼓：按落点
                for (int k = 0; k < snareSteps.Length; k++)
                    if (snareSteps[k] == inBar) { Snare(buf, n, ts, ref seed); break; }

                // 踩镲：按密度
                if (inBar % (16 / hatRate) == 0) Hat(buf, n, ts, ref seed, 0.13);

                // 贝斯：每 16 步取 2 个位置
                if (s % 2 == 0)
                {
                    int bi = (s / 2) % bassPat.Length;
                    double dur = step16 * (nearEnd ? 1.4 : 1.9);
                    Bass(buf, n, ts, dur, scale[bassPat[bi] % scale.Length] / 2.0,
                         nearEnd ? 0.28 : 0.24);
                }

                // 琶音：按密度，末段升八度并加密
                int stepPerBeat = arpRate * 4;
                if (s % Math.Max(1, 16 / stepPerBeat) == 0)
                {
                    int idx = s % 8;
                    double f = scale[idx] * 2.0 * (nearEnd ? 1.5 : 1.0);
                    Arp(buf, n, ts, step16 * 1.6, Math.Min(f, 2600), nearEnd ? 0.14 : 0.11);
                }
            }

            // 末段整体加密：加一层十六分踏板音
            for (int s = 0; s < totalSteps; s++)
            {
                double ts = s * step16;
                if (ts < lastQuarter || ts >= RollSeconds) continue;
                if (s % 2 == 0)
                    Arp(buf, n, ts, step16 * 0.9, Math.Min(scale[s % 8] * 3.0, 3200), 0.075);
            }

            Sweep(buf, n, RollSeconds - 3.0, 3.0, 200, 3000, 0.22, ref seed);
            Sweep(buf, n, RollSeconds - 1.2, 1.2, 800, 5200, 0.16, ref seed);

            int fade = (int)(0.35 * SR);
            for (int i = 0; i < fade; i++)
            {
                int idx = n - 1 - i;
                if (idx < 0) break;
                buf[idx] *= i / (double)fade;
            }

            for (int i = 0; i < n; i++) buf[i] = Math.Tanh(buf[i] * 1.22) * 0.86;
            return ToWav(buf);
        }

        // ============================================================ 短音效
        private static byte[] BuildTick()
        {
            int n = (int)(0.038 * SR);
            var b = new double[n];
            uint seed = 999u;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 130.0);
                b[i] = (Square(t, 2400) * 0.6 + Noise(ref seed) * 0.4)
                       * Math.Min(1.0, t / 0.0015) * env * 0.5;
            }
            return ToWav(b);
        }

        // ============================================================ 出货音乐
        private static byte[] BuildWinNormal()
        {
            double dur = 2.2;
            int n = (int)(SR * dur);
            var buf = new double[n];

            double[] seq = { 587.33, 880.00, 1174.66 };
            for (int k = 0; k < seq.Length; k++)
                Bell(buf, n, k * 0.09, seq[k], 0.24, 3.0);

            for (int k = 0; k < 2; k++)
            {
                double t0 = k * 0.16;
                int i0 = (int)(t0 * SR);
                int len = (int)(0.3 * SR);
                for (int i = 0; i < len && i0 + i < n; i++)
                {
                    double t = i / (double)SR;
                    double env = Math.Exp(-t * 9.0);
                    double f = 120.0 * Math.Exp(-t * 7.0) + 62.0;
                    buf[i0 + i] += Math.Sin(2 * Math.PI * f * t) * env * 0.42;
                }
            }
            for (int i = 0; i < n; i++) buf[i] = Math.Tanh(buf[i] * 1.1) * 0.9;
            return ToWav(buf);
        }

        private static byte[] BuildWinJackpot()
        {
            double dur = 5.0;
            int n = (int)(SR * dur);
            var buf = new double[n];
            uint seed = 777u;

            Sweep(buf, n, 0.0, 0.9, 300, 4200, 0.30, ref seed);

            double[] chord = { 293.66, 369.99, 440.00, 587.33, 739.99, 880.00 };
            for (int rep = 0; rep < 2; rep++)
            {
                double startAt = 0.85 + rep * 0.55;
                for (int k = 0; k < chord.Length; k++)
                    Bell(buf, n, startAt + k * 0.075, chord[k], 0.26 - rep * 0.06, 1.9 + rep * 0.6);
            }

            for (int k = 0; k < 4; k++)
            {
                double t0 = 0.85 + k * 0.28;
                int i0 = (int)(t0 * SR);
                int len = (int)(0.35 * SR);
                for (int i = 0; i < len && i0 + i < n; i++)
                {
                    double t = i / (double)SR;
                    double env = Math.Exp(-t * 7.5);
                    double f = 135.0 * Math.Exp(-t * 6.0) + 58.0;
                    buf[i0 + i] += Math.Sin(2 * Math.PI * f * t) * env * 0.52;
                }
            }

            double[] sparkle = { 1174.66, 1396.91, 1760.00, 2093.00, 2349.32, 2793.83, 3520.00 };
            for (int k = 0; k < 26; k++)
            {
                double t0 = 1.6 + k * 0.105;
                if (t0 >= dur) break;
                double f = sparkle[k % sparkle.Length] * (1 + (k / 7) * 0.02);
                Bell(buf, n, t0, f, 0.16, 9.0, 0.003);
            }

            double[] tail = { 293.66, 369.99, 440.00, 587.33 };
            foreach (var f in tail)
                Bell(buf, n, 3.4, f, 0.16, 1.1, 0.01);

            for (int i = 0; i < n; i++) buf[i] = Math.Tanh(buf[i] * 1.15) * 0.9;
            return ToWav(buf);
        }

        // ============================================================ 播放
        public static void WarmUp()
        {
            try
            {
                int vol = Volume;
                if (Pack == SoundPackKind.Delta)
                {
                    if (_rolls == null || _builtPack != SoundPackKind.Delta)
                    {
                        var r = new byte[DeltaSfx.RollVariantCount][];
                        for (int i = 0; i < DeltaSfx.RollVariantCount; i++) r[i] = DeltaSfx.BuildRoll(i, vol);
                        _rolls = r;
                        _tick = DeltaSfx.BuildTick(vol);
                        _winNormal = DeltaSfx.BuildNormal(vol);
                        _winJackpot = DeltaSfx.BuildJackpot(vol);
                        _builtPack = SoundPackKind.Delta;
                    }
                }
                else
                {
                    if (_rolls == null || _builtPack != SoundPackKind.Classic)
                    {
                        var r = new byte[RollVariantCount][];
                        for (int i = 0; i < RollVariantCount; i++) r[i] = BuildRoll(i);
                        _rolls = r;
                        _tick = BuildTick();
                        _winNormal = BuildWinNormal();
                        _winJackpot = BuildWinJackpot();
                        _builtPack = SoundPackKind.Classic;
                    }
                }
            }
            catch { }
        }

        /// <summary>已生成的音色包（切换后需要重建）。</summary>
        private static SoundPackKind _builtPack = SoundPackKind.Classic;

        /// <summary>
        /// 播放一段内存 WAV（异步）。
        /// 把数据复制到非托管内存后交给 PlaySound，避免托管缓冲区在播放中途被回收；
        /// 播放结束后由后台任务释放该内存。
        /// </summary>
        private static bool PlayMem(byte[] wav)
        {
            if (wav == null || wav.Length < 44) return false;
            if (Muted) return false;
            try
            {
                IntPtr ptr = Marshal.AllocHGlobal(wav.Length);
                Marshal.Copy(wav, 0, ptr, wav.Length);

                bool ok = PlaySound(ptr, IntPtr.Zero,
                                    SND_ASYNC | SND_MEMORY | SND_NODEFAULT);

                // WAV 时长 + 余量后再释放
                int ms = WavDurationMs(wav) + 3000;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    System.Threading.Thread.Sleep(Math.Max(2000, ms));
                    try { Marshal.FreeHGlobal(ptr); } catch { }
                });
                return ok;
            }
            catch { return false; }
        }

        /// <summary>从 WAV 头算出时长（毫秒）。</summary>
        private static int WavDurationMs(byte[] wav)
        {
            try
            {
                int sr = BitConverter.ToInt32(wav, 24);
                short ch = BitConverter.ToInt16(wav, 22);
                short bits = BitConverter.ToInt16(wav, 34);
                int dataLen = BitConverter.ToInt32(wav, 40);
                int bytesPerSec = sr * ch * (bits / 8);
                if (bytesPerSec <= 0) return 3000;
                return (int)(dataLen * 1000L / bytesPerSec);
            }
            catch { return 3000; }
        }

        /// <summary>停止当前播放。</summary>
        public static void StopAll()
        {
            try { PlaySound(IntPtr.Zero, IntPtr.Zero, SND_ASYNC); } catch { }
            LastRollStarted = false;
        }

        /// <summary>播放一首转动音乐（4 首随机）。</summary>
        public static int StartRollMusic()
        {
            WarmUp();
            StopAll();
            LastRollStarted = false;
            LastRollIndex = -1;

            int count = (_rolls != null && _rolls.Length > 0) ? _rolls.Length : 0;
            if (count == 0) return -1;
            int idx = _rnd.Next(count);
            LastRollIndex = idx;
            LastRollStarted = PlayMem(_rolls[idx]);
            return idx;
        }

        public static void PlayWin(bool jackpot)
        {
            WarmUp();
            StopAll();
            PlayMem(jackpot ? _winJackpot : _winNormal);
        }

        public static void PlayTick()
        {
            // 滴答很短，直接播；不保留引用（播完即弃）
            if (_tick == null) WarmUp();
            PlayMem(_tick);
        }
    }
}
