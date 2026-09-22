# -*- coding: utf-8 -*-
"""探查 BUFF 接口里与「成交」相关的字段与筛选参数。"""
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "lib"))
from cdp import CDP   # noqa: E402

PORT = 9222


def main():
    c = CDP(port=PORT)
    t = c.connect_page("buff.163.com")
    print("已连接:", t.get("url"))
    print()

    # 1) 完整结构，找出所有含 price / transact / sell 的字段
    js1 = """
(async () => {
  const r = await fetch('/api/market/goods?game=csgo&page_num=1&page_size=1', {credentials:'include'});
  const j = await r.json();
  if (j.code !== 'OK' || !j.data || !j.data.items) return JSON.stringify({code: j.code});
  const it = j.data.items[0];
  const flat = {};
  for (const k of Object.keys(it)) {
    const v = it[k];
    if (v === null || typeof v !== 'object') flat[k] = v;
  }
  const gi = it.goods_info || {};
  return JSON.stringify({top: flat, goods_info_keys: Object.keys(gi),
                         goods_info_simple: Object.fromEntries(
                           Object.entries(gi).filter(([k,v]) => v===null || typeof v!=='object'))});
})()
"""
    print("=== 字段清单 ===")
    print(c.evaluate(js1, await_promise=True, timeout=45))
    print()

    # 2) 试探排序/筛选参数：能否按成交量、能否筛 7 天内成交
    cases = [
        ("baseline",        "page_num=1&page_size=20"),
        ("sort_transacted", "page_num=1&page_size=20&sort_by=transacted_num.desc"),
        ("sort_sellnum",    "page_num=1&page_size=20&sort_by=sell_num.desc"),
        ("sort_price_asc",  "page_num=1&page_size=20&sort_by=price.asc"),
        ("transacted_gt0",  "page_num=1&page_size=20&transacted_num=1"),
        ("has_transacted",  "page_num=1&page_size=20&has_transacted=true"),
        ("days7",           "page_num=1&page_size=20&transacted_days=7"),
        ("recent7",         "page_num=1&page_size=20&recent_days=7"),
        ("sold7",           "page_num=1&page_size=20&sold_within=7"),
    ]
    js2 = """
(async () => {
  const cases = %s;
  const out = [];
  for (const [tag, qs] of cases) {
    try {
      const r = await fetch('/api/market/goods?game=csgo&' + qs, {credentials:'include'});
      const j = await r.json();
      const items = (j.data && j.data.items) || [];
      out.push({tag: tag, code: j.code, n: items.length, total: j.data ? j.data.total_count : null,
                first: items.length ? items[0].name : null,
                t0: items.length ? items[0].transacted_num : null,
                s0: items.length ? items[0].sell_num : null});
    } catch (e) { out.push({tag: tag, code: 'EXC', err: String(e).slice(0,60)}); }
    await new Promise(s => setTimeout(s, 1300));
  }
  return JSON.stringify(out);
})()
""" % json.dumps(cases)
    res = json.loads(c.evaluate(js2, await_promise=True, timeout=180))
    print("=== 参数探测 ===")
    print("%-18s %-16s %-5s %-8s %-12s %-8s %s" % ("参数", "code", "n", "total", "transacted", "sell_num", "首条"))
    print("-" * 110)
    for r in res:
        print("%-18s %-16s %-5s %-8s %-12s %-8s %s" % (
            r.get("tag"), r.get("code") or r.get("err", ""), r.get("n", ""), r.get("total", ""),
            r.get("t0", ""), r.get("s0", ""), (r.get("first") or "")[:30]))

    # 3) 成交记录接口试探
    print()
    print("=== 成交记录接口试探 ===")
    js3 = """
(async () => {
  const urls = [
    ['bill',        '/api/market/goods/bill?game=csgo&goods_id=1&page_num=1&page_size=5'],
    ['history',     '/api/market/goods/price_history?game=csgo&goods_id=1'],
    ['sell_orders', '/api/market/goods/sell_order?game=csgo&goods_id=1&page_num=1&page_size=5'],
  ];
  const out = [];
  for (const [tag, u] of urls) {
    try {
      const r = await fetch(u, {credentials:'include'});
      const j = await r.json();
      out.push({tag: tag, http: r.status, code: j.code, keys: j.data ? Object.keys(j.data).slice(0,10) : null});
    } catch (e) { out.push({tag: tag, err: String(e).slice(0,60)}); }
    await new Promise(s => setTimeout(s, 900));
  }
  return JSON.stringify(out);
})()
"""
    print(c.evaluate(js3, await_promise=True, timeout=90))
    c.close()


if __name__ == "__main__":
    main()
