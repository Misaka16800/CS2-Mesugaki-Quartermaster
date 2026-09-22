# -*- coding: utf-8 -*-
"""探测 BUFF 接口的参数上限与排序支持（需已登录，否则会被限流）。"""
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
from cdp import CDP   # noqa: E402

PORT = 9222

CASES = [
    ("size20",      "page_num=1&page_size=20"),
    ("size50",      "page_num=1&page_size=50"),
    ("size80",      "page_num=1&page_size=80"),
    ("size200",     "page_num=1&page_size=200"),
    ("sort_price",  "page_num=1&page_size=20&sort_by=price"),
    ("sort_desc",   "page_num=1&page_size=20&sort_by=price.desc"),
    ("orderby",     "page_num=1&page_size=20&order_by=price&order=desc"),
    ("minprice",    "page_num=1&page_size=20&min_price=1000"),
    ("category_k",  "page_num=1&page_size=20&category=knife"),
    ("search_red",  "page_num=1&page_size=20&search=" + "%E7%BA%A2%E7%BA%BF"),
]


def main():
    c = CDP(port=PORT)
    t = c.connect_page("buff.163.com")
    print("已连接:", t.get("url"))
    print()

    js_tpl = """
(async () => {
  const cases = %s;
  const out = [];
  for (const [tag, qs] of cases) {
    try {
      const r = await fetch('/api/market/goods?game=csgo&' + qs, {credentials:'include'});
      const j = await r.json();
      const d = j.data || {};
      const items = d.items || [];
      out.push({tag: tag, code: j.code, n: items.length, total: d.total_count,
                first: items.length ? (items[0].name + ' = ' + items[0].sell_min_price) : null});
    } catch (e) { out.push({tag: tag, code: 'EXC', err: String(e).slice(0,70)}); }
    await new Promise(s => setTimeout(s, 1600));
  }
  return JSON.stringify(out);
})()
"""
    js = js_tpl % json.dumps(CASES)
    res = json.loads(c.evaluate(js, await_promise=True, timeout=180))
    print("%-14s %-16s %-5s %-8s %s" % ("参数", "code", "n", "total", "首条"))
    print("-" * 96)
    for r in res:
        print("%-14s %-16s %-5s %-8s %s" % (
            r.get("tag"), r.get("code") or r.get("err", ""), r.get("n", ""),
            r.get("total", ""), (r.get("first") or "")[:44]))
    c.close()


if __name__ == "__main__":
    main()
