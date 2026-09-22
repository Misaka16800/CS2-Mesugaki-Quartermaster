# -*- coding: utf-8 -*-
"""
抓取 BUFF 的「成交记录」价格历史（不是最低挂单价）。

接口: /api/market/goods/price_history/buff/v2?game=csgo&goods_id=<gid>&days=7
返回: data.lines[] 中 key 为 'sell_price_history' 的项即「成交记录」，
      points 为 [时间戳, 成交价] 数组（人民币）。

需求对应:
  - 成交价 -> 取 points 的均值/中位数
  - 7 天内无成交 -> points 为空，该物品踢出随机池

用法:
  python fetch_transactions.py --list-only      # 只重抓市场列表（拿 goods_id）
  python fetch_transactions.py --pages 50       # 试跑 50 个物品
  python fetch_transactions.py --all            # 全量（约 2 小时，可断点续抓）
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
LIST_RAW = os.path.join(HERE, "buff_raw.jsonl")
TX_RAW = os.path.join(HERE, "buff_tx.jsonl")
PAGE_SIZE = 80
LIST_DELAY = 1500          # 列表间隔放宽，避免触发限流导致 WebSocket 超时
TX_DELAY = 650


# ---------------------------------------------------------------- 市场列表
def fetch_list_pages(c, pages):
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
    } catch (e) { out.push({page: p, ok: false, code: 'EXC:' + String(e).slice(0,60)}); }
    await new Promise(s => setTimeout(s, %d));
  }
  return JSON.stringify(out);
})()
"""
    js = js_tpl % (json.dumps(pages), PAGE_SIZE, LIST_DELAY)
    return json.loads(c.evaluate(js, await_promise=True, timeout=120 + 3 * len(pages)))


def cmd_list(c, want):
    # 只把成功页算作已完成：失败页会在下次运行时自动重试
    done = set()
    if os.path.exists(LIST_RAW):
        for line in open(LIST_RAW, encoding="utf-8"):
            try:
                o = json.loads(line)
            except Exception:
                continue
            if o.get("ok") and o.get("items"):
                done.add(o["page"])

    total = None
    for p in fetch_list_pages(c, [1]):
        if p.get("ok"):
            total = p["total"]
    total_pages = (total + PAGE_SIZE - 1) // PAGE_SIZE if total else 1
    print("市场条目 %s -> %d 页，已抓 %d 页" % (total, total_pages, len(done)), flush=True)

    todo = [p for p in range(1, total_pages + 1) if p not in done][:want]
    print("本次抓 %d 页" % len(todo), flush=True)

    BATCH = 5
    t0 = time.time()
    consec_fail = 0
    for i in range(0, len(todo), BATCH):
        batch = todo[i:i + BATCH]
        try:
            # 单批超时给足：每页 LIST_DELAY + 余量
            res = fetch_list_pages(c, batch)
            consec_fail = 0
        except WsError as e:
            consec_fail += 1
            wait = min(60, 5 * consec_fail)
            print("  批次异常(%d 连败)，%ds 后重连: %s" % (consec_fail, wait, str(e)[:90]), flush=True)
            time.sleep(wait)
            try:
                c.close()
            except Exception:
                pass
            c2 = CDP(port=PORT)
            try:
                c2.connect_page("buff.163.com")
                c.ws = c2.ws
                c._id = c2._id
            except Exception as e2:
                print("    重连失败:", str(e2)[:90], flush=True)
            continue
        except Exception as e:
            print("  批次其他异常:", str(e)[:120], flush=True)
            time.sleep(5)
            continue
        with open(LIST_RAW, "a", encoding="utf-8") as f:
            for p in res:
                f.write(json.dumps(
                    {"page": p["page"], "ok": True,
                     "items": [slim(x) for x in p["items"]]} if p.get("ok")
                    else {"page": p["page"], "ok": False, "code": p.get("code")},
                    ensure_ascii=False) + "\n")
        n = i + len(batch)
        print("  列表 %d/%d 页  %.0fs" % (n, len(todo), time.time() - t0), flush=True)


def slim(it):
    gi = it.get("goods_info") or {}
    tags = (gi.get("info") or {}).get("tags", {}) or {}
    return {
        "gid": it.get("id"),
        "name": it.get("name"),
        "p": it.get("sell_min_price"),
        "ref": it.get("sell_reference_price"),
        "buy": it.get("buy_max_price"),
        "steam": gi.get("steam_price"),
        "wear": (tags.get("exterior") or {}).get("localized_name"),
        "quality": (tags.get("quality") or {}).get("localized_name"),
        "sellNum": it.get("sell_num"),
        "buyNum": it.get("buy_num"),
        "transacted": it.get("transacted_num"),
        "hist": it.get("has_buff_price_history"),
    }


# ---------------------------------------------------------------- 成交历史
def fetch_tx_batch(c, gids):
    """批量取每个 goods_id 的 7 天成交记录。JS 内部对单项做重试，避免一项失败毁掉整批。"""
    js_tpl = """
(async () => {
  const gids = %s;
  const out = [];
  for (const gid of gids) {
    let rec = null;
    for (let attempt = 0; attempt < 3; attempt++) {
      try {
        const u = '/api/market/goods/price_history/buff/v2?game=csgo&goods_id=' + gid + '&days=7';
        const r = await fetch(u, {credentials: 'include'});
        const j = await r.json();
        if (j.code !== 'OK' || !j.data) {
          rec = {gid: gid, ok: false, code: j.code};
        } else {
          const lines = j.data.lines || [];
          const p = lines.find(x => x.key === 'sell_price_history');
          const pts = p ? (p.points || []) : [];
          rec = {gid: gid, ok: true, currency: j.data.currency, n: pts.length, pts: pts};
        }
        break;                       // 成功即跳出重试
      } catch (e) {
        rec = {gid: gid, ok: false, code: 'EXC:' + String(e).slice(0,50)};
        await new Promise(s => setTimeout(s, 1200 * (attempt + 1)));
      }
    }
    out.push(rec);
    await new Promise(s => setTimeout(s, %d));
  }
  return JSON.stringify(out);
})()
"""
    js = js_tpl % (json.dumps(gids), TX_DELAY)
    return json.loads(c.evaluate(js, await_promise=True, timeout=120 + 6 * len(gids)))


def cmd_tx(c, want, min_cny=0.0):
    # 读取市场列表，建立 名称+磨损 -> gid 映射；排除纪念品
    if not os.path.exists(LIST_RAW):
        raise SystemExit("缺少市场列表数据，请先运行: python fetch_transactions.py --list-only")

    seen = {}
    for line in open(LIST_RAW, encoding="utf-8"):
        try:
            o = json.loads(line)
        except Exception:
            continue
        if not o.get("ok"):
            continue
        for it in o["items"]:
            if not it.get("wear"):
                continue
            nm = it.get("name") or ""
            if "（纪念品）" in nm or (it.get("quality") or "") == "纪念品":
                continue
            if "印花" in nm or "探员" in nm or "音乐盒" in nm or "涂鸦" in nm:
                continue
            key = (nm, it["wear"])
            if key not in seen:
                seen[key] = it["gid"]

    print("市场唯一物品(含磨损): %d" % len(seen), flush=True)

    # 已抓过的 gid
    done = set()
    if os.path.exists(TX_RAW):
        for line in open(TX_RAW, encoding="utf-8"):
            try:
                o = json.loads(line)
                if o.get("ok"):
                    done.add(o["gid"])
            except Exception:
                pass
    print("已抓成交历史: %d" % len(done), flush=True)

    # 按价格从高到低优先（池子分级最需要贵价物品的成交数据）
    def price_of(gid):
        for line in open(LIST_RAW, encoding="utf-8"):
            pass
        return 0

    gids = []
    with open(LIST_RAW, encoding="utf-8") as f:
        rows = []
        for line in f:
            try:
                o = json.loads(line)
            except Exception:
                continue
            if o.get("ok"):
                rows.extend(o["items"])
    best = {}
    for it in rows:
        g = it.get("gid")
        if not g:
            continue
        try:
            p = float(it.get("ref") or it.get("p") or 0)
        except Exception:
            p = 0
        if g not in best or p > best[g]:
            best[g] = p
    # 只取与本地池子相关的物品；可按价格下限筛选（只抓贵价物品的成交记录）
    for key, g in seen.items():
        if g in done:
            continue
        if min_cny > 0 and best.get(g, 0) < min_cny:
            continue
        gids.append(g)
    gids.sort(key=lambda g: -best.get(g, 0))
    gids = gids[:want]
    print("价格下限 ￥%.0f，本次计划抓 %d 个物品的成交记录" % (min_cny, len(gids)), flush=True)

    BATCH = 5
    t0 = time.time()
    ok = empty = fail = 0
    consec_fail = 0
    i = 0
    while i < len(gids):
        batch = gids[i:i + BATCH]
        try:
            res = fetch_tx_batch(c, batch)
            consec_fail = 0
        except WsError as e:
            consec_fail += 1
            wait = min(120, 10 * consec_fail)
            print("  批次异常(%d 连败)，%ds 后重连: %s" % (consec_fail, wait, str(e)[:90]), flush=True)
            time.sleep(wait)
            # 重新建立 CDP 连接
            try:
                c.close()
            except Exception:
                pass
            try:
                c2 = CDP(port=PORT, timeout=60)
                c2.connect_page("buff.163.com")
                c.ws = c2.ws
                c._id = c2._id
                print("    重连成功", flush=True)
            except Exception as e2:
                print("    重连失败:", str(e2)[:90], flush=True)
            continue
        except Exception as e:
            print("  批次其他异常:", str(e)[:120], flush=True)
            time.sleep(8)
            continue

        with open(TX_RAW, "a", encoding="utf-8") as f:
            for r in res:
                f.write(json.dumps(r, ensure_ascii=False) + "\n")
                if not r.get("ok"):
                    fail += 1
                elif r.get("n", 0) > 0:
                    ok += 1
                else:
                    empty += 1
        i += BATCH
        n = min(i, len(gids))
        el = time.time() - t0
        rate = n / el if el > 0 else 0
        eta = (len(gids) - n) / rate if rate > 0 else 0
        print("  成交 %d/%d   有成交 %d / 无成交 %d / 失败 %d   剩余约 %.0fs"
              % (n, len(gids), ok, empty, fail, eta), flush=True)

    print()
    print("完成。成交数据: %s (%.1f MB)" % (TX_RAW, os.path.getsize(TX_RAW) / 1024 / 1024))


def main():
    argv = sys.argv[1:]
    list_only = "--list-only" in argv
    do_all = "--all" in argv
    want = int(argv[argv.index("--pages") + 1]) if "--pages" in argv else (10 ** 9 if do_all else 50)
    # --min-cny 只抓价格不低于该值(人民币)的物品成交记录；默认 7000 元 ≈ $1000
    min_cny = float(argv[argv.index("--min-cny") + 1]) if "--min-cny" in argv else 0.0

    c = CDP(port=PORT)
    t = c.connect_page("buff.163.com")
    print("已连接:", t.get("url"), flush=True)

    if list_only:
        cmd_list(c, 10 ** 9)
    else:
        # 列表若为空或页数不足，先补列表
        need_list = True
        if os.path.exists(LIST_RAW):
            pages = set()
            with open(LIST_RAW, encoding="utf-8") as f:
                for line in f:
                    try:
                        o = json.loads(line)
                    except Exception:
                        continue
                    if o.get("ok"):
                        pages.add(o.get("page"))
            # 442 页为市场全量；少一页也算不完整（缺页会导致物品缺 gid）
            if len(pages) >= 442:
                need_list = False
                print("市场列表已完整(%d 页)，跳过" % len(pages), flush=True)
            else:
                print("市场列表仅 %d 页，需要补抓" % len(pages), flush=True)
        if need_list:
            cmd_list(c, 10 ** 9)
        cmd_tx(c, want, min_cny)

    c.close()


if __name__ == "__main__":
    main()
