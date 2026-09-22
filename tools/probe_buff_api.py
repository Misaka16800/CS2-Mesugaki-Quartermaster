# -*- coding: utf-8 -*-
"""探查 BUFF 市场接口的可排序/可筛选参数，以及单条数据的完整结构。"""
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

# 依次尝试不同的排序/筛选参数，看哪个既被接受又能改变结果
VARIANTS = [
    ("baseline",        "/api/market/goods?game=csgo&page_num=1&page_size=20"),
    ("sort_price_desc", "/api/market/goods?game=csgo&page_num=1&page_size=20&sort_by=price.desc"),
    ("sort_price_asc",  "/api/market/goods?game=csgo&page_num=1&page_size=20&sort_by=price.asc"),
    ("order_desc",      "/api/market/goods?game=csgo&page_num=1&page_size=20&order=desc&sort_by=price"),
    ("with_search",     "/api/market/goods?game=csgo&page_num=1&page_size=20&search=%E7%BA%A2%E7%BA%BF"),
]


def main():
    c = CDP(port=PORT)
    t = c.connect_page("buff.163.com")
    print("已连接:", t.get("url"))
    print()

    js_tpl = """
(async () => {
  const urls = %s;
  const out = [];
  for (const [tag, u] of urls) {
    try {
      const r = await fetch(u, {credentials: 'include'});
      const j = await r.json();
      const d = j.data || {};
      const items = d.items || [];
      out.push({tag: tag, http: r.status, code: j.code, n: items.length,
                total: d.total_count,
                first: items.length ? items[0].name + ' = ' + items[0].sell_min_price : null,
                firstPrice: items.length ? parseFloat(items[0].sell_min_price || '0') : null});
    } catch (e) { out.push({tag: tag, err: String(e).slice(0,90)}); }
    await new Promise(s => setTimeout(s, 900));
  }
  return JSON.stringify(out);
})()
"""
    js = js_tpl % json.dumps([[v[0], v[1]] for v in VARIANTS])
    res = json.loads(c.evaluate(js, await_promise=True, timeout=90))
    print("=== 参数探测 ===")
    for r in res:
        if r.get("err"):
            print("  %-16s ERR %s" % (r["tag"], r["err"]))
        else:
            print("  %-16s http=%s code=%-14s n=%-3s total=%-6s 首条: %s"
                  % (r["tag"], r.get("http"), r.get("code"), r.get("n"), r.get("total"), r.get("first")))

    print()
    print("=== 单条完整结构（取第一条） ===")
    js2 = """
(async () => {
  const r = await fetch('/api/market/goods?game=csgo&page_num=1&page_size=1', {credentials:'include'});
  const j = await r.json();
  return JSON.stringify(j.data.items[0], null, 1);
})()
"""
    detail = c.evaluate(js2, await_promise=True, timeout=45)
    print(detail[:2500] if detail else "(空)")
    c.close()


if __name__ == "__main__":
    main()
