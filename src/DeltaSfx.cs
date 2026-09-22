using System;
using System.IO;

namespace Cs2Roulette
{
    /// <summary>
    /// 「三角洲风格」音色包：战术军事 / 工业电子风。
    ///
    /// 完全原创合成（FM 金属敲击 + 失谐锯齿 + 噪声滤波扫频 + 重低音冲击），
    /// 不使用任何来自游戏的音频素材。
    /// 音色取向：冷硬、金属质感、紧张推进、命中时有强冲击感。
    /// </summary>
    internal static class DeltaSfx
    {
        private const int SR = 44100;

        // ---------------------------------------------------------- 基础工具
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

        /// <summary>一阶低通（用于给噪声/锯齿做"滤波扫频"的质感）。</summary>
        private sealed class LowPass
        {
            private double _z;
            public double Process(double x, double cutoffNorm)
            {
                // cutoffNorm: 0..1（1 = 全通）
                double a = Math.Max(0.001, Math.Min(0.999, cutoffNorm));
                _z += a * (x - _z);
                return _z;
            }
        }

        private static byte[] ToWav(double[] s, int volume)
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
            double vol = Math.Max(0, Math.Min(100, volume)) / 100.0;
            for (int i = 0; i < n; i++)
            {
                double v = Math.Tanh(s[i] * norm * 1.15) * vol * 0.95;
                if (v > 1) v = 1; else if (v < -1) v = -1;
                w.Write((short)Math.Round(v * 32767));
            }
            w.Flush();
            return ms.ToArray();
        }

        // ---------------------------------------------------------- 音色元件

        /// <summary>重低音冲击：音高快速下坠的正弦，做主"命中"体。</summary>
        private static void SubHit(double[] buf, int n, double t0, double amp, double f0 = 180, double f1 = 38, double decay = 9.0)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(0.9 * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * decay) * Math.Min(1.0, t / 0.003);
                double f = f1 + (f0 - f1) * Math.Exp(-t * 22.0);
                buf[i0 + i] += Math.Sin(2 * Math.PI * f * t) * env * amp;
            }
        }

        /// <summary>金属敲击：两个不谐和正弦做 FM，短促高频衰减。</summary>
        private static void MetalClang(double[] buf, int n, double t0, double f, double amp, double decay = 26.0)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(0.5 * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * decay) * Math.Min(1.0, t / 0.001);
                // FM：调制比为无理数，产生非谐波金属音
                double mod = Math.Sin(2 * Math.PI * f * 2.76 * t) * 3.4 * Math.Exp(-t * 12.0);
                buf[i0 + i] += Math.Sin(2 * Math.PI * f * t + mod) * env * amp;
                // 加一点高频泛音提亮
                buf[i0 + i] += Math.Sin(2 * Math.PI * f * 4.13 * t) * env * amp * 0.28;
            }
        }

        /// <summary>军鼓式敲击：噪声 + 中频体。</summary>
        private static void SnareHit(double[] buf, int n, double t0, ref uint seed, double amp)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(0.20 * SR);
            var lp = new LowPass();
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 22.0) * Math.Min(1.0, t / 0.001);
                double body = Math.Sin(2 * Math.PI * 190 * t) * 0.4;
                double nz = lp.Process(Noise(ref seed), 0.55);
                buf[i0 + i] += (nz * 0.9 + body) * env * amp;
            }
        }

        /// <summary>踩镲：高通噪声，极短。</summary>
        private static void HatHit(double[] buf, int n, double t0, ref uint seed, double amp)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(0.055 * SR);
            double prev = 0;
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 95.0);
                double nz = Noise(ref seed);
                double hp = nz - prev;      // 简易高通
                prev = nz;
                buf[i0 + i] += hp * env * amp;
            }
        }

        /// <summary>低音脉冲：失谐锯齿，工业感推进。</summary>
        private static void BassPulse(double[] buf, int n, double t0, double dur, double f, double amp)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(dur * SR);
            var lp = new LowPass();
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Min(1.0, t / 0.005) * Math.Exp(-t * 7.0);
                double v = Saw(t, f) * 0.6 + Saw(t * 1.006, f) * 0.4;   // 轻微失谐
                buf[i0 + i] += lp.Process(v, 0.22) * env * amp * 1.6;
            }
        }

        /// <summary>噪声上升扫频（紧张感的核心）。</summary>
        private static void NoiseRiser(double[] buf, int n, double t0, double dur, double amp,
                                       ref uint seed, double from = 0.05, double to = 0.9)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(dur * SR);
            var lp = new LowPass();
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double p = t / dur;
                double cut = from + (to - from) * Math.Pow(p, 1.5);
                double env = Math.Pow(p, 2.0);
                double nz = lp.Process(Noise(ref seed), cut);
                // 叠加一点音高上行的正弦，强化"逼近"感
                double tone = Math.Sin(2 * Math.PI * (220 * Math.Pow(2, p * 3.0)) * t) * 0.35;
                buf[i0 + i] += (nz * 1.5 + tone) * env * amp;
            }
        }

        /// <summary>失谐锯齿和弦刺（军乐式铜管感）。</summary>
        private static void Stab(double[] buf, int n, double t0, double dur, double f, double amp)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(dur * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double atk = Math.Min(1.0, t / 0.004);
                double rel = Math.Min(1.0, (dur - t) / 0.10);
                double env = atk * Math.Max(0, rel);
                double v = Saw(t, f) * 0.5
                         + Saw(t, f * 1.007) * 0.3
                         + Saw(t, f * 0.5) * 0.2;
                buf[i0 + i] += v * env * amp * 0.42;
            }
        }

        /// <summary>下坠音（命中落定时的"定音"）。</summary>
        private static void DownSweep(double[] buf, int n, double t0, double dur, double f0, double f1,
                                      double amp, ref uint seed)
        {
            int i0 = (int)(t0 * SR);
            int len = (int)(dur * SR);
            double phase = 0;
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                double t = i / (double)SR;
                double p = t / dur;
                double f = f0 * Math.Pow(f1 / f0, Math.Pow(p, 0.6));
                phase += 2 * Math.PI * f / SR;
                double env = (1 - p) * Math.Min(1.0, t / 0.01);
                buf[i0 + i] += (Math.Sin(phase) * 0.7 + Noise(ref seed) * 0.25) * env * amp;
            }
        }

        /// <summary>心跳式双脉冲（营造"倒计时"压迫感）。</summary>
        private static void DoubleThump(double[] buf, int n, double t0, double amp)
        {
            SubHit(buf, n, t0, amp * 0.85, 150, 46, 12.0);
            SubHit(buf, n, t0 + 0.16, amp * 0.60, 140, 44, 13.0);
        }

        // ---------------------------------------------------------- 转动音乐（10 秒）
        private static readonly double[][] DeltaScales = {
            // 小调，偏暗：A / C / D / F / G / Bb / B / C# / E / G#
            new double[] { 110.00, 130.81, 146.83, 164.81, 196.00, 220.00, 261.63, 293.66 },
            new double[] { 130.81, 155.56, 174.61, 196.00, 233.08, 261.63, 311.13, 349.23 },
            new double[] { 146.83, 174.61, 196.00, 220.00, 261.63, 293.66, 349.23, 392.00 },
            new double[] { 174.61, 207.65, 233.08, 261.63, 311.13, 349.23, 415.30, 466.16 },
            new double[] { 98.00, 116.54, 130.81, 146.83, 174.61, 196.00, 233.08, 261.63 },
            new double[] { 116.54, 138.59, 155.56, 174.61, 207.65, 233.08, 277.18, 311.13 },
            new double[] { 123.47, 146.83, 164.81, 185.00, 220.00, 246.94, 293.66, 329.63 },
            new double[] { 138.59, 164.81, 185.00, 207.65, 246.94, 277.18, 329.63, 369.99 },
            new double[] { 164.81, 196.00, 220.00, 246.94, 293.66, 329.63, 392.00, 440.00 },
            new double[] { 103.83, 123.47, 138.59, 155.56, 185.00, 207.65, 246.94, 277.18 },
        };
        private static readonly double[] DeltaBpms = { 140.0, 150.0, 145.0, 155.0, 135.0,
                                                       160.0, 148.0, 152.0, 143.0, 157.0 };

        /// <summary>三角洲音色包的转动音乐变体数量。</summary>
        internal const int RollVariantCount = 10;

        // 每首不同的 build / drop 时点（秒），让节奏推进感不一样
        private static readonly double[] BuildStarts = { 6.0, 5.4, 6.4, 5.8, 6.2, 5.2, 6.6, 5.6, 6.8, 5.0 };
        private static readonly double[] DropStarts = { 8.4, 8.0, 8.6, 8.2, 8.5, 7.8, 8.8, 8.1, 8.7, 7.6 };

        internal static byte[] BuildRoll(int variant, int volume)
        {
            double bpm = DeltaBpms[variant % DeltaBpms.Length];
            double[] sc = DeltaScales[variant % DeltaScales.Length];
            double beat = 60.0 / bpm;

            int n = (int)(SR * 10.0);
            var buf = new double[n];
            uint seed = (uint)(4471 + variant * 9127);

            // 起手一声金属撞击，定调
            MetalClang(buf, n, 0.0, sc[0] * 2, 0.30, 18.0);
            SubHit(buf, n, 0.0, 0.55, 200, 40, 7.0);

            int totalBeats = (int)(10.0 / beat);
            int vv = ((variant % RollVariantCount) + RollVariantCount) % RollVariantCount;
            double buildStart = BuildStarts[vv];   // build-up 时点（各变体不同）
            double dropStart = DropStarts[vv];     // drop 时点

            for (int b = 0; b < totalBeats; b++)
            {
                double tb = b * beat;
                if (tb >= 10.0) break;
                bool building = tb >= buildStart;
                bool drop = tb >= dropStart;

                // 底鼓：随进度加密
                SubHit(buf, n, tb, drop ? 0.75 : (building ? 0.62 : 0.5), 185, 40, 8.5);
                if (building && (b % 2 == 1)) SubHit(buf, n, tb + beat * 0.5, 0.4, 170, 38, 9.5);

                // 军鼓 / 金属敲击
                if (b % 4 == 2) SnareHit(buf, n, tb, ref seed, drop ? 0.55 : 0.42);
                if (building) MetalClang(buf, n, tb + beat * 0.5, sc[b % sc.Length] * 4, 0.16, 34.0);
                if (drop) MetalClang(buf, n, tb, sc[(b + 2) % sc.Length] * 5, 0.20, 30.0);

                // 踩镲：build 阶段变密（八分 -> 十六分）
                int hats = building ? 4 : 2;
                for (int h = 0; h < hats; h++)
                    HatHit(buf, n, tb + h * beat / hats, ref seed, building ? 0.13 : 0.09);

                // 低音脉冲：drop 后加厚
                int bassSteps = drop ? 4 : (building ? 4 : 2);
                for (int k = 0; k < bassSteps; k++)
                {
                    double t0 = tb + k * beat / bassSteps;
                    if (t0 >= 10.0) break;
                    int deg = (b * bassSteps + k) % sc.Length;
                    double f = sc[deg] / 2.0;
                    if (drop) f *= 0.5;                     // drop 下沉八度
                    BassPulse(buf, n, t0, beat / bassSteps * 0.9, f, drop ? 0.42 : (building ? 0.34 : 0.26));
                }

                // 和弦刺：只在关键拍
                if (building && b % 4 == 0)
                    Stab(buf, n, tb, beat * 1.6, sc[0] * 2, 0.55);
                if (drop && b % 2 == 0)
                    Stab(buf, n, tb, beat * 0.9, sc[(b / 2) % sc.Length] * 2, 0.60);
            }

            // 心跳压迫：build 阶段
            // 心跳压迫：build 阶段，密度随变体略变
            double thumpGap = 0.24 + (vv % 4) * 0.02;
            for (int k = 0; k < 10; k++)
            {
                double t0 = buildStart + k * thumpGap;
                if (t0 >= dropStart) break;
                DoubleThump(buf, n, t0, 0.26 + k * 0.022);
            }

            // 噪声上升扫频：最后 4 秒长扫 + 最后 1.5 秒急扫
            NoiseRiser(buf, n, buildStart, 10.0 - buildStart, 0.32, ref seed, 0.04, 0.72);
            NoiseRiser(buf, n, dropStart + 0.1, 10.0 - dropStart - 0.1, 0.42, ref seed, 0.25, 0.95);

            // 收尾：drop 顶点一记重击 + 下坠
            SubHit(buf, n, 9.55, 0.95, 220, 34, 6.0);
            MetalClang(buf, n, 9.55, sc[0] * 6, 0.34, 16.0);
            DownSweep(buf, n, 9.55, 0.45, 1800, 180, 0.30, ref seed);

            // 结尾淡出
            int fade = (int)(0.30 * SR);
            for (int i = 0; i < fade; i++)
            {
                int idx = n - 1 - i;
                if (idx < 0) break;
                buf[idx] *= i / (double)fade;
            }
            return ToWav(buf, volume);
        }

        // ---------------------------------------------------------- 中头奖（5 秒）
        internal static byte[] BuildJackpot(int volume)
        {
            double dur = 5.0;
            int n = (int)(SR * dur);
            var buf = new double[n];
            uint seed = 20261u;

            // 0 - 1.0s：上升蓄力（噪声扫 + 心跳加速）
            NoiseRiser(buf, n, 0.0, 1.0, 0.40, ref seed, 0.05, 0.95);
            for (int k = 0; k < 5; k++) DoubleThump(buf, n, 0.10 + k * 0.18, 0.30);

            // 1.0s：主冲击 —— 重低音 + 金属撞击（"命中"的核心）
            SubHit(buf, n, 1.0, 1.0, 260, 32, 5.0);
            MetalClang(buf, n, 1.0, 587.33, 0.42, 11.0);
            MetalClang(buf, n, 1.01, 880.00, 0.34, 13.0);
            SnareHit(buf, n, 1.0, ref seed, 0.5);

            // 1.0 - 2.6s：铜管式和弦刺上行（战术胜利感）
            double[] chord = { 293.66, 369.99, 440.00, 587.33, 739.99, 880.00 };
            for (int k = 0; k < chord.Length; k++)
                Stab(buf, n, 1.05 + k * 0.09, 0.85, chord[k], 0.80);

            // 1.3s 起：两轮确认重击（像"击中要害"）
            for (int k = 0; k < 4; k++)
            {
                double t0 = 1.30 + k * 0.30;
                SubHit(buf, n, t0, 0.70, 200, 38, 8.0);
                MetalClang(buf, n, t0, chord[k % chord.Length] * 3, 0.24, 20.0);
            }

            // 2.0 - 4.4s：高频金属闪烁（战利品感）
            double[] shine = { 1174.66, 1396.91, 1760.00, 2093.00, 2349.32, 2793.83 };
            for (int k = 0; k < 22; k++)
            {
                double t0 = 2.0 + k * 0.105;
                if (t0 >= dur - 0.5) break;
                MetalClang(buf, n, t0, shine[k % shine.Length], 0.13, 30.0);
            }

            // 3.3s：收尾大和弦，长余韵
            double[] tail = { 293.66, 369.99, 440.00, 587.33 };
            foreach (var f in tail)
                Stab(buf, n, 3.3, 1.6, f, 0.72);
            SubHit(buf, n, 3.3, 0.75, 180, 34, 3.2);

            // 5 秒处淡出，避免与下一次播放冲突
            int fade = (int)(0.25 * SR);
            for (int i = 0; i < fade; i++)
            {
                int idx = n - 1 - i;
                if (idx < 0) break;
                buf[idx] *= i / (double)fade;
            }
            return ToWav(buf, volume);
        }

        // ---------------------------------------------------------- 未中头奖（2.5 秒）
        internal static byte[] BuildNormal(int volume)
        {
            double dur = 2.5;
            int n = (int)(SR * dur);
            var buf = new double[n];
            uint seed = 30941u;

            // 落定的一声闷响（不是失败提示音，是"确认"感）
            SubHit(buf, n, 0.0, 0.70, 190, 44, 9.0);
            MetalClang(buf, n, 0.0, 523.25, 0.22, 24.0);
            SnareHit(buf, n, 0.0, ref seed, 0.28);

            // 两句短促上行提示音（干净、克制）
            double[] seq = { 587.33, 880.00 };
            for (int k = 0; k < seq.Length; k++)
            {
                double t0 = 0.13 + k * 0.15;
                Stab(buf, n, t0, 0.42, seq[k], 0.62);
                MetalClang(buf, n, t0, seq[k] * 2, 0.16, 26.0);
            }

            // 轻微下坠收尾，避免悬停感
            DownSweep(buf, n, 0.55, 0.7, 900, 320, 0.16, ref seed);

            int fade = (int)(0.20 * SR);
            for (int i = 0; i < fade; i++)
            {
                int idx = n - 1 - i;
                if (idx < 0) break;
                buf[idx] *= i / (double)fade;
            }
            return ToWav(buf, volume);
        }

        // ---------------------------------------------------------- 滴答（战术感短促点击）
        internal static byte[] BuildTick(int volume)
        {
            int n = (int)(0.032 * SR);
            var b = new double[n];
            uint seed = 8123u;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 150.0) * Math.Min(1.0, t / 0.0008);
                // 金属点击：高频正弦 + 极短噪声
                b[i] = (Math.Sin(2 * Math.PI * 3200 * t) * 0.7 + Noise(ref seed) * 0.3) * env * 0.55;
            }
            return ToWav(b, volume);
        }
    }
}
