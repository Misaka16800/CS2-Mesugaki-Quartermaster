# -*- coding: utf-8 -*-
"""
在已登录的浏览器页面上下文里读取 BUFF 市场数据。

前提：已有一个开启 --remote-debugging-port=9222 的 Edge/Chrome，
      且其中一个标签页停在 buff.163.com。

做法：用 CDP 在页面里执行 fetch('/api/market/goods?...')，
      由浏览器携带真实 header 与 cookie，因此不受服务端对裸 HTTP 客户端的限流影响。
      只读取公开的市场挂单数据，不涉及账号密码或交易操作。
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


def fetch_pages(c, pages, page_size=20, delay=0.55, progress=None):
    """在页面里连续抓取若干页，返回条目列表。"""
    js_tpl = """
(async () => {
  const pages = %s;
  const size = %d;
  const out = [];
  for (const p of pages) {
    try {
      const r = await fetch('/api/market/goods?game=csgo&page_num=' + p + '&page_size=' + size,
                            {credentials: 'include'});
      const j = await r.json();
      if (j.code === 'OK' && j.data && j.data.items) {
        out.push({page: p, ok: true, total: j.data.total_count, items: j.data.items});
      } else {
        out.push({page: p, ok: false, code: j.code});
      }
    } catch (e) {
      out.push({page: p, ok: false, code: 'EXC:' + String(e).slice(0, 80)});
    }
    await new Promise(s => setTimeout(s, %d));
  }
  return JSON.stringify(out);
})()
"""
    js = js_tpl % (json.dumps(pages), page_size, int(delay * 1000))
    raw = c.evaluate(js, await_promise=True, timeout=60 + 2 * len(pages))
    return json.loads(raw)


def main():
    limit_pages = int(sys.argv[1]) if len(sys.argv) > 1 else 5
    c = CDP(port=PORT)
    t = c.connect_page("buff.163.com")
    print("已连接:", t.get("url"))

    got = fetch_pages(c, list(range(1, limit_pages + 1)))
    ok = sum(1 for g in got if g.get("ok"))
    print("成功 %d/%d 页" % (ok, len(got)))
    total = None
    for g in got:
        if g.get("ok"):
            total = g.get("total")
            break
    print("市场总条目数:", total)

    # 显示前几条，确认字段
    for g in got:
        if g.get("ok"):
            for it in g["items"][:5]:
                gi = it.get("goods_info") or {}
                tags = (gi.get("info") or {}).get("tags", {})
                wear = (tags.get("exterior") or {}).get("localized_name", "?")
                print("  %-46s ￥%-10s %s" % (it["name"][:44], it.get("sell_min_price"), wear))
            break

    c.close()


if __name__ == "__main__":
    main()
