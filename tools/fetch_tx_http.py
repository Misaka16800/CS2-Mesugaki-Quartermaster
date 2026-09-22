# -*- coding: utf-8 -*-
"""
抓取 BUFF 7 天成交记录 —— 基于 cookie + 直连 HTTP（不依赖页面 JS 上下文）。

为什么不用页面 fetch：
  长时间批量在页面上下文里执行 JS，一旦页面重载/卡死，Runtime.evaluate 会超时。
  而把 cookie 一次性取出后直连 HTTP，稳定性高得多。

已验证：
  - /api/market/goods/price_history/buff/v2 直连可用（无需签名）
  - /api/market/goods（市场列表）需要请求签名，直连返回 Action Forbidden，
    因此市场列表仍由 fetch_transactions.py 走页面抓取。

用法:
  python fetch_tx_http.py                # 抓价格 > ￥1000 的物品
  python fetch_tx_http.py --min-cny 0    # 全量
  python fetch_tx_http.py --workers 4    # 并发数（默认 3）
"""
import json
import os
import re
import sys
import threading
import time
import urllib.request
import urllib.error
from concurrent.futures import ThreadPoolExecutor

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "lib"))
from cdp import CDP, WsError   # noqa: E402

PORT = 9222
LIST_RAW = os.path.join(HERE, "buff_raw.jsonl")
TX_RAW = os.path.join(HERE, "buff_tx.jsonl")
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/124.0 Safari/537.36")

LOCK = threading.Lock()


# ---------------------------------------------------------------- cookie
def get_cookie_header():
    c = CDP(port=PORT, timeout=40)
    c.connect_page("buff.163.com")
    c.call("Network.enable")
    res = c.call("Network.getCookies", {"urls": ["https://buff.163.com/"]})
    c.close()
    cookies = res.get("cookies", [])
    if not cookies:
        return None
    return "; ".join("%s=%s" % (x["name"], x["value"]) for x in cookies)


def http_json(url, cookie, timeout=35, retries=3):
    last = None
    for a in range(retries):
        try:
            req = urllib.request.Request(url, headers={
                "User-Agent": UA,
                "Referer": "https://buff.163.com/market/csgo",
                "Accept": "application/json, text/plain, */*",
                "Cookie": cookie,
            })
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8"))
        except Exception as e:
            last = e
            time.sleep(1.5 * (a + 1))
    raise last


# ---------------------------------------------------------------- 目标列表
def load_targets(min_cny):
    """从市场列表里取 名称+磨损 去重后的 goods_id（排除纪念品）。"""
    if not os.path.exists(LIST_RAW):
        raise SystemExit("缺少市场列表: %s（先运行 fetch_transactions.py --list-only）" % LIST_RAW)

    gid_price = {}
    for line in open(LIST_RAW, encoding="utf-8"):
        try:
            o = json.loads(line)
        except Exception:
            continue
        if not o.get("ok") or not o.get("items"):
            continue
        for it in o["items"]:
            nm = it.get("name") or ""
            if not it.get("wear") or not it.get("gid"):
                continue
            if "（纪念品）" in nm or (it.get("quality") or "") == "纪念品":
                continue
            if any(x in nm for x in ("印花", "探员", "音乐盒", "涂鸦")):
                continue
            try:
                p = float(it.get("ref") or it.get("p") or 0)
            except Exception:
                p = 0
            g = it["gid"]
            if g not in gid_price or p > gid_price[g]:
                gid_price[g] = p

    done = set()
    if os.path.exists(TX_RAW):
        for line in open(TX_RAW, encoding="utf-8"):
            try:
                o = json.loads(line)
                if o.get("ok"):
                    done.add(o["gid"])
            except Exception:
                pass

    todo = [(g, p) for g, p in gid_price.items() if g not in done and p >= min_cny]
    todo.sort(key=lambda x: -x[1])
    return todo, len(gid_price), len(done)


# ---------------------------------------------------------------- 抓取
def fetch_one(gid, cookie):
    url = ("https://buff.163.com/api/market/goods/price_history/buff/v2"
           "?game=csgo&goods_id=%d&days=7" % gid)
    try:
        j = http_json(url, cookie)
    except Exception as e:
        return {"gid": gid, "ok": False, "code": "EXC:" + str(e)[:60]}
    if j.get("code") != "OK" or not j.get("data"):
        return {"gid": gid, "ok": False, "code": j.get("code")}
    lines = j["data"].get("lines") or []
    rec = next((x for x in lines if x.get("key") == "sell_price_history"), None)
    pts = (rec or {}).get("points") or []
    return {"gid": gid, "ok": True, "currency": j["data"].get("currency"),
            "n": len(pts), "pts": pts}


def main():
    argv = sys.argv[1:]
    min_cny = float(argv[argv.index("--min-cny") + 1]) if "--min-cny" in argv else 1000.0
    workers = int(argv[argv.index("--workers") + 1]) if "--workers" in argv else 3

    cookie = get_cookie_header()
    if not cookie:
        raise SystemExit("取不到 cookie，请确认调试浏览器打开了 buff.163.com")
    print("cookie 获取成功", flush=True)

    todo, total_gid, done_cnt = load_targets(min_cny)
    print("市场物品 %d 个，已抓成交 %d 个" % (total_gid, done_cnt), flush=True)
    print("价格下限 ￥%.0f，本次待抓 %d 个（并发 %d）" % (min_cny, len(todo), workers), flush=True)
    print(flush=True)

    t0 = time.time()
    ok = empty = fail = 0
    n = 0
    with ThreadPoolExecutor(max_workers=workers) as ex:
        futs = {ex.submit(fetch_one, g, cookie): g for g, _ in todo}
        for fut in futs:
            r = fut.result()
            with LOCK:
                with open(TX_RAW, "a", encoding="utf-8") as f:
                    f.write(json.dumps(r, ensure_ascii=False) + "\n")
                if not r.get("ok"):
                    fail += 1
                elif r.get("n", 0) > 0:
                    ok += 1
                else:
                    empty += 1
                n += 1
                if n % 50 == 0 or n == len(todo):
                    el = time.time() - t0
                    rate = n / el if el > 0 else 0
                    eta = (len(todo) - n) / rate if rate > 0 else 0
                    print("  %d/%d  有成交 %d / 无成交 %d / 失败 %d   %.1f 个/秒  剩余 %.0f 秒"
                          % (n, len(todo), ok, empty, fail, rate, eta), flush=True)
                # 温和限速
                if n % 20 == 0:
                    time.sleep(0.4)

    print()
    print("完成：有成交 %d，7天无成交 %d，失败 %d" % (ok, empty, fail), flush=True)
    print("成交数据: %s (%.1f MB)" % (TX_RAW, os.path.getsize(TX_RAW) / 1024 / 1024), flush=True)


if __name__ == "__main__":
    main()
