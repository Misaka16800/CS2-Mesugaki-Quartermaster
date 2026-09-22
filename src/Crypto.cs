using System;
using System.IO;
using System.Text;

namespace Cs2Roulette
{
    /// <summary>
    /// 数据文件加解密 + 分享水印。
    ///
    /// 设计取舍：
    ///   - 只加密 data 下的数据文件（items/prices/excluded/quiz），不动图片。
    ///     图片本身是 PNG，加密后解码要额外开销，且体积大，收益低。
    ///   - 格式：明文前 4 字节为魔数 CKXE，其余为 XOR 加密内容。
    ///   - 读取时自动识别：有魔数就解密，没有就按明文读。
    ///     => 兼容未加密的旧数据，开发时不用每次都加密。
    ///   - 密钥由分享标识派生，因此该标识也随数据一起嵌入，删不掉。
    /// </summary>
    public static class Crypto
    {
        /// <summary>分享标识（同时用于派生密钥）。</summary>
        public const string Marker = "HFUT2026_CKX免费分享";

        static readonly byte[] Magic = { (byte)'C', (byte)'K', (byte)'X', (byte)'E' };

        static byte[] _pad;

        /// <summary>由标识派生密钥流（FNV-1a 扩散）。</summary>
        static byte[] Pad()
        {
            if (_pad != null) return _pad;
            byte[] seed = Encoding.UTF8.GetBytes(Marker + "|cs2-mesugaki-quartermaster|v1");
            var k = new byte[32];
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < k.Length; i++)
                {
                    h ^= seed[i % seed.Length];
                    h *= 16777619u;
                    h ^= (uint)(i * 2654435761u);
                    k[i] = (byte)(h >> 13);
                }
            }
            _pad = k;
            return _pad;
        }

        static byte[] Xor(byte[] data, int offset)
        {
            byte[] k = Pad();
            var outb = new byte[data.Length - offset];
            for (int i = 0; i < outb.Length; i++)
                outb[i] = (byte)(data[offset + i] ^ k[i % k.Length]);
            return outb;
        }

        /// <summary>加密字节（前面加魔数）。</summary>
        public static byte[] Encrypt(byte[] plain)
        {
            var res = new byte[plain.Length + Magic.Length];
            Buffer.BlockCopy(Magic, 0, res, 0, Magic.Length);
            byte[] body = Xor(plain, 0);
            Buffer.BlockCopy(body, 0, res, Magic.Length, body.Length);
            return res;
        }

        /// <summary>文件是否已加密。</summary>
        public static bool IsEncrypted(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var fs = File.OpenRead(path))
                {
                    if (fs.Length < Magic.Length) return false;
                    for (int i = 0; i < Magic.Length; i++)
                        if (fs.ReadByte() != Magic[i]) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 读数据文件。自动识别加密与否；返回 UTF-8 文本。
        /// 读不到返回 null（调用方自行兜底）。
        /// </summary>
        public static string ReadText(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length >= Magic.Length)
                {
                    bool enc = true;
                    for (int i = 0; i < Magic.Length; i++)
                        if (raw[i] != Magic[i]) { enc = false; break; }
                    if (enc)
                        return new UTF8Encoding(false).GetString(Xor(raw, Magic.Length));
                }
                return new UTF8Encoding(false).GetString(raw);
            }
            catch { return null; }
        }
    }
}
