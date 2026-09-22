using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;

namespace Cs2Roulette
{
    /// <summary>
    /// 音效模块：波形全部由代码合成（正弦叠加 + 包络），不依赖任何外部音频文件，
    /// 因此不涉及任何第三方音频素材的授权问题。
    /// </summary>
    public static class SoundFx
    {
        private static byte[] _tick;
        private static byte[] _chime;
        private static bool _muted;

        public static bool Muted
        {
            get { return _muted; }
            set { _muted = value; }
        }

        /// <summary>滚动中的滴答声：短促、干净，带一点点噪声增加"颗粒感"。</summary>
        private static byte[] Tick()
        {
            if (_tick != null) return _tick;
            double dur = 0.045;
            int n = (int)(SampleRate * dur);
            var buf = new double[n];
            var rnd = new Random(7);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;
                // 基频 + 一个八度泛音，模拟木质敲击
                double body = Math.Sin(2 * Math.PI * 1760 * t) * 0.75
                            + Math.Sin(2 * Math.PI * 3520 * t) * 0.25;
                body += (rnd.NextDouble() * 2 - 1) * 0.22;   // 噪声瞬态
                double atk = Math.Min(1.0, t / 0.0022);      // 2.2ms 起振，避免爆音
                double dec = Math.Exp(-t * 118.0);
                buf[i] = body * atk * dec * 0.55;
            }
            _tick = ToWav(buf);
            return _tick;
        }

        /// <summary>落定音：两声上行的铃音（C6 与 G6），带泛音余韵。</summary>
        private static byte[] Chime()
        {
            if (_chime != null) return _chime;
            double dur = 1.35;
            int n = (int)(SampleRate * dur);
            var buf = new double[n];

            // 两个音的起始时间与频率（C6 / G6，纯五度上行）
            double[][] notes = {
                new double[] { 0.00, 1046.50 },
                new double[] { 0.14, 1567.98 },
            };
            double[] harm = { 1.0, 0.42, 0.22, 0.12, 0.06, 0.03 }; // 钟形泛音列

            foreach (var note in notes)
            {
                double start = note[0], f = note[1];
                int i0 = (int)(start * SampleRate);
                for (int i = i0; i < n; i++)
                {
                    double t = (i - i0) / (double)SampleRate;
                    double atk = Math.Min(1.0, t / 0.004);
                    double dec = Math.Exp(-t * 3.4);
                    double v = 0;
                    for (int k = 0; k < harm.Length; k++)
                        v += harm[k] * Math.Sin(2 * Math.PI * f * (k + 1) * t);
                    buf[i] += v * atk * dec * 0.20;
                }
            }

            // 轻微立体感：加一个 8ms 延迟的副本
            int d = (int)(0.008 * SampleRate);
            for (int i = n - 1; i >= d; i--)
                buf[i] += buf[i - d] * 0.28;

            _chime = ToWav(buf);
            return _chime;
        }

        private const int SampleRate = 44100;

        /// <summary>把 [-1,1] 的浮点样本包成 16bit 单声道 WAV。</summary>
        private static byte[] ToWav(double[] samples)
        {
            int n = samples.Length;
            int dataLen = n * 2;
            var ms = new MemoryStream(44 + dataLen);
            var w = new BinaryWriter(ms, Encoding.ASCII);
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);                 // PCM 头长度
            w.Write((short)1);           // PCM
            w.Write((short)1);           // 单声道
            w.Write(SampleRate);
            w.Write(SampleRate * 2);     // 字节率
            w.Write((short)2);           // 块对齐
            w.Write((short)16);          // 位深
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataLen);

            // 限幅 + 写入
            double peak = 0;
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
            double norm = peak > 0.99 ? 0.99 / peak : 1.0;
            for (int i = 0; i < n; i++)
            {
                double v = samples[i] * norm;
                if (v > 1) v = 1; else if (v < -1) v = -1;
                w.Write((short)Math.Round(v * 32767));
            }
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>播放（每次新建实例，保证连点也能立刻重新触发）。</summary>
        private static void Play(byte[] wav)
        {
            if (_muted || wav == null) return;
            try
            {
                var sp = new SoundPlayer(new MemoryStream(wav));
                sp.Play();
            }
            catch
            {
                // 声卡不可用等情况下静默降级，不影响功能
            }
        }

        public static void PlayTick() { Play(Tick()); }
        public static void PlayChime() { Play(Chime()); }

        /// <summary>读取 exe 同级的 settings.txt，判断是否静音（供诊断与初始化使用）。</summary>
        public static bool MutedFromFile()
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
                if (!File.Exists(p)) return false;
                string txt = File.ReadAllText(p, Encoding.UTF8);
                return txt.IndexOf("sound=off", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        /// <summary>预热：首次抽取前先把波形算好，避免第一次卡顿。</summary>
        public static void WarmUp()
        {
            try { Tick(); Chime(); } catch { }
        }
    }
}
