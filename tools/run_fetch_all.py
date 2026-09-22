# -*- coding: utf-8 -*-
"""
串行总控：先把市场列表补全（含 goods_id），再抓全部物品的 7 天成交记录。

两个阶段串行执行，避免并发请求互相抢会话触发限流。
可随时中断，重跑会自动跳过已完成部分（断点续抓）。

用法:
  python run_fetch_all.py                 # 全量（约 2 小时）
  python run_fetch_all.py --tx-limit 200  # 只抓 200 个物品的成交记录
"""
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PY = sys.executable


def run(args, log):
    print(">>> %s" % " ".join(args), flush=True)
    with open(log, "a", encoding="utf-8") as f:
        f.write("\n===== %s =====\n" % time.strftime("%Y-%m-%d %H:%M:%S"))
        f.flush()
        p = subprocess.Popen([PY, "-u"] + args, cwd=HERE,
                             stdout=f, stderr=subprocess.STDOUT)
        p.wait()
    return p.returncode


def count_lines(path):
    if not os.path.exists(path):
        return 0
    n = 0
    with open(path, encoding="utf-8") as f:
        for _ in f:
            n += 1
    return n


def main():
    argv = sys.argv[1:]
    tx_limit = 10 ** 9
    if "--tx-limit" in argv:
        tx_limit = int(argv[argv.index("--tx-limit") + 1])

    log = os.path.join(HERE, "tmp", "fetch_all.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)

    print("阶段 1/2：补全市场列表", flush=True)
    for attempt in range(1, 4):
        rc = run(["fetch_transactions.py", "--list-only"], log)
        pages = count_lines(os.path.join(HERE, "buff_raw.jsonl"))
        print("  列表页数: %d (rc=%d)" % (pages, rc), flush=True)
        if pages >= 442:
            break
        time.sleep(5)

    print()
    print("阶段 2/2：抓取 7 天成交记录", flush=True)
    rc = run(["fetch_transactions.py", "--pages", str(tx_limit)], log)
    n = count_lines(os.path.join(HERE, "buff_tx.jsonl"))
    print("  成交记录条数: %d (rc=%d)" % (n, rc), flush=True)
    print()
    print("完成。日志: %s" % log, flush=True)


if __name__ == "__main__":
    main()
