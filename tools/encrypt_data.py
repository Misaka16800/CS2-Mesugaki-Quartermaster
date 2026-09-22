# -*- coding: utf-8 -*-
"""
加密 data 下的数据文件（items/prices/excluded/quiz）。

格式：魔数 CKXE + XOR 密文，与 src/Crypto.cs 一致。
密钥由 "HFUT2026_CKX免费分享" 派生 —— 水印和数据绑在一起，删不掉。

图片不加密（PNG 本身无需保护，且体积大、解密有开销）。
"""
import io
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

MARK = "HFUT2026_CKX免费分享"
MAGIC = b"CKXE"
TARGETS = ["items.json", "prices.json", "excluded.json", "quiz.txt"]


def pad():
    seed = (MARK + "|cs2-mesugaki-quartermaster|v1").encode("utf-8")
    k = bytearray(32)
    h = 2166136261
    for i in range(32):
        h ^= seed[i % len(seed)]
        h = (h * 16777619) & 0xFFFFFFFF
        h ^= (i * 2654435761) & 0xFFFFFFFF
        k[i] = (h >> 13) & 0xFF
    return bytes(k)


def encrypt(data: bytes) -> bytes:
    k = pad()
    body = bytes(b ^ k[i % len(k)] for i, b in enumerate(data))
    return MAGIC + body


def is_enc(data: bytes) -> bool:
    return len(data) >= 4 and data[:4] == MAGIC


def main():
    base = sys.argv[1] if len(sys.argv) > 1 else r"D:\DSH_workspace\cs2-roulette\release\data"
    print("目标目录: %s" % base)
    print()
    for name in TARGETS:
        p = os.path.join(base, name)
        if not os.path.exists(p):
            print("  !! 缺少 %s" % name)
            continue
        raw = open(p, "rb").read()
        if is_enc(raw):
            print("  %-16s 已加密，跳过" % name)
            continue
        # 备份一份明文（开发用）
        bak = p + ".plain"
        if not os.path.exists(bak):
            open(bak, "wb").write(raw)
        enc = encrypt(raw)
        open(p, "wb").write(enc)
        print("  %-16s %8d B -> %8d B  已加密" % (name, len(raw), len(enc)))
    print()
    print("完成（明文备份为 *.plain，分发前请删除）")


if __name__ == "__main__":
    main()
