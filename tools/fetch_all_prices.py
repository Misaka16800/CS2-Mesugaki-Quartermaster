# -*- coding: utf-8 -*-
"""
通过 CDP 在已登录的浏览器里，把 BUFF 市场挂单数据完整抓下来。

- 使用 sort_by=price.desc，先拿到高价值饰品（奖池分级最需要的部分）
- page_size=80（接口硬上限），35285 条约 442 页
- 逐页写入 JSONL，支持断点续抓
- 只读取公开挂单价格，不涉及账号密码与任何交易操作

用法:
  python fetch_all_prices.py              # 默认抓前 60 页（试跑）
  python fetch_all_prices.py --all        # 抓完全部
  python fetch_all_prices.py --pages 100  # 指定页数
"""
import json
import os
import sys
import time

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "lib"))
from cdp import CDP, WsError   # noqa: E402

PORT = 9222
PAGE_SIZE = 80
RAW = os.path.join(HERE, "buff_raw.jsonl")
DELAY_MS = 700          # 每页之间的间隔，做个体面的抓取者


def load_done():
    """已抓好的页码集合。"""
    done = set()
    if not os.path.exists(RAW):
        return done
    with open(RAW, encoding="utf-8") as f:
        for line in f:
            try:
                o = json.loads(line)
                if o.get("ok"):
                    done.add(o["page"])
            except Exception:
                pass
    return done


def fetch_batch(c, pages):
    js_tpl = """
(async () => {
  const pages = %s;
  const out = [];
  for (const p of pages) {
    try {
      const u = '/api/market/goods?game=csgo&page_num=' + p +
                '&page_size=%d&sort_by=price.desc';
      const r = await fetch(u, {credentials: 'include'});
      const j = await r.json();
      if (j.code === 'OK' && j.data && j.data.items) {
        out.push({page: p, ok: true, total: j.data.total_count, items: j.data.items});
      } else {
        out.push({page: p, ok: false, code: j.code});
      }
    } catch (e) {
      out.push({page: p, ok: false, code: 'EXC:' + String(e).slice(0, 60)});
    }
    await new Promise(s => setTimeout(s, %d));
  }
  return JSON.stringify(out);
})()
"""
    js = js_tpl % (json.dumps(pages), PAGE_SIZE, DELAY_MS)
    raw = c.evaluate(js, await_promise=True, timeout=120 + 3 * len(pages))
    return json.loads(raw)


def slim(it):
    """只保留需要字段，压缩体积。"""
    gi = it.get("goods_info") or {}
    tags = (gi.get("info") or {}).get("tags", {}) or {}
    wear = (tags.get("exterior") or {}).get("localized_name")
    quality = (tags.get("quality") or {}).get("localized_name")
    return {
        "gid": it.get("id"),                       # goods_id，成交接口必需
        "name": it.get("name"),
        "hash": it.get("market_hash_name"),
        "p": it.get("sell_min_price"),
        "ref": it.get("sell_reference_price"),
        "buy": it.get("buy_max_price"),
        "steam": gi.get("steam_price"),
        "wear": wear,
        "quality": quality,
        "sellNum": it.get("sell_num"),
        "buyNum": it.get("buy_num"),
        "transacted": it.get("transacted_num"),
        "hist": it.get("has_buff_price_history"),
    }


def main():
    argv = sys.argv[1:]
    do_all = "--all" in argv
    if "--pages" in argv:
        want = int(argv[argv.index("--pages") + 1])
    elif do_all:
        want = 10000
    else:
        want = 60

    c = CDP(port=PORT)
    t = c.connect_page("buff.163.com")
    print("已连接:", t.get("url"), flush=True)

    done = load_done()
    print("已抓页数: %d" % len(done), flush=True)

    # 先问总页数
    probe = fetch_batch(c, [1])
    total = None
    for p in probe:
        if p.get("ok"):
            total = p.get("total")
            if 1 not in done:
                with open(RAW, "a", encoding="utf-8") as f:
                    f.write(json.dumps({"page": 1, "ok": True,
                                        "items": [slim(x) for x in p["items"]]},
                                       ensure_ascii=False) + "\n")
                done.add(1)
    total_pages = (total + PAGE_SIZE - 1) // PAGE_SIZE if total else None
    print("市场条目 %s -> 共 %s 页" % (total, total_pages), flush=True)

    todo = [p for p in range(1, (total_pages or want) + 1) if p not in done][:want]
    print("本次计划抓 %d 页" % len(todo), flush=True)

    BATCH = 8
    got_items = 0
    t0 = time.time()
    for i in range(0, len(todo), BATCH):
        batch = todo[i:i + BATCH]
        try:
            res = fetch_batch(c, batch)
        except WsError as e:
            print("  批次失败(将重连): %s" % str(e)[:120], flush=True)
            time.sleep(3)
            try:
                c.connect_page("buff.163.com")
            except Exception:
                pass
            continue

        with open(RAW, "a", encoding="utf-8") as f:
            for p in res:
                if p.get("ok"):
                    f.write(json.dumps({"page": p["page"], "ok": True,
                                        "items": [slim(x) for x in p["items"]]},
                                       ensure_ascii=False) + "\n")
                    got_items += len(p["items"])
                else:
                    f.write(json.dumps({"page": p["page"], "ok": False,
                                        "code": p.get("code")}, ensure_ascii=False) + "\n")

        el = time.time() - t0
        n_done = i + len(batch)
        rate = n_done / el if el > 0 else 0
        eta = (len(todo) - n_done) / rate if rate > 0 else 0
        print("  进度 %d/%d 页   累计 %d 条   用时 %.0fs   剩余约 %.0fs"
              % (n_done, len(todo), got_items, el, eta), flush=True)

    print()
    print("完成。原始数据: %s (%.1f MB)" % (RAW, os.path.getsize(RAW) / 1024 / 1024))
    c.close()


if __name__ == "__main__":
    main()
