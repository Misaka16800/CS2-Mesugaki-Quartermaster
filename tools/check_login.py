# -*- coding: utf-8 -*-
"""检测调试浏览器里的 BUFF 登录状态，并验证接口可用性。"""
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


def main():
    wait = "--wait" in sys.argv
    deadline = time.time() + (600 if wait else 0)

    while True:
        try:
            c = CDP(port=PORT)
            # 优先找已打开的 buff 页面
            t = None
            for cand in c.list_targets():
                if cand.get("type") == "page" and "buff.163.com" in cand.get("url", ""):
                    t = cand
                    break
            if t is None:
                print("没有 buff.163.com 的标签页，请在该窗口打开 https://buff.163.com/market/csgo")
                if not wait:
                    return
                time.sleep(5)
                continue

            c.ws = None
            c.connect_page("buff.163.com")
            url = c.evaluate("location.href")
            print("页面:", url)

            # 登录状态：页面上是否出现「登录/注册」
            txt = c.evaluate("document.body ? document.body.innerText.slice(0,1500) : ''") or ""
            need_login = "登录/注册" in txt or "登录" in txt[:400]

            # 直接试接口
            js = """
(async () => {
  const r = await fetch('/api/market/goods?game=csgo&page_num=1&page_size=3', {credentials:'include'});
  const j = await r.json();
  return JSON.stringify({code: j.code, n: (j.data && j.data.items) ? j.data.items.length : 0,
                         total: j.data ? j.data.total_count : null,
                         sample: (j.data && j.data.items) ? j.data.items[0].name : null});
})()
"""
            api = json.loads(c.evaluate(js, await_promise=True, timeout=40))
            c.close()

            print("页面显示登录入口:", need_login)
            print("接口返回:", json.dumps(api, ensure_ascii=False))
            if api.get("code") == "OK":
                print()
                print(">>> 接口可用，可以开始抓取。")
                return
            print()
            print(">>> 接口被限流或未登录。请在浏览器窗口里登录 BUFF 后重试。")
        except WsError as e:
            print("连接/执行失败:", str(e)[:150])
        except Exception as e:
            print("异常:", str(e)[:150])

        if not wait or time.time() > deadline:
            return
        print("  10 秒后重试 ...")
        time.sleep(10)


if __name__ == "__main__":
    main()
