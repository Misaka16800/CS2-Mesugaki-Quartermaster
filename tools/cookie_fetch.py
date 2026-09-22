# -*- coding: utf-8 -*-
"""
通过 CDP 取浏览器 cookie，改由 Python 直接请求。

背景：BUFF 需要登录会话，但页面 JS 上下文在长时间批量执行后容易卡死
（Runtime.evaluate 超时）。改为一次性把 cookie 取出来，
之后用普通 HTTP 客户端带 cookie 请求，稳定得多。

cookie 只在内存中使用，不落盘、不外传。
"""
import json
import os
import sys
import time
import urllib.request
import urllib.error

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "lib"))
from cdp import CDP, WsError   # noqa: E402

PORT = 9222
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/124.0 Safari/537.36")


def get_cookie_header(port=PORT):
    """从浏览器取 buff.163.com 的 cookie，拼成请求头。

    连页面级 target（Network 域在页面 target 上可用），
    Network.getCookies 不需要执行页面 JS，因此页面卡死也能取到。
    """
    c = CDP(port=port, timeout=40)
    try:
        c.connect_page("buff.163.com")
    except WsError:
        # 没有 buff 页面时，退而连接任意页面
        for t in c.list_targets():
            if t.get("type") == "page":
                c.ws = None
                break
        raise SystemExit("找不到 buff.163.com 的页面，请在调试浏览器里打开它")
    c.call("Network.enable")
    res = c.call("Network.getCookies", {"urls": ["https://buff.163.com/"]})
    c.close()
    cookies = res.get("cookies", [])
    if not cookies:
        return None, []
    pairs = ["%s=%s" % (x["name"], x["value"]) for x in cookies]
    return "; ".join(pairs), cookies
    cookies = res.get("cookies", [])
    if not cookies:
        return None, []
    pairs = ["%s=%s" % (x["name"], x["value"]) for x in cookies]
    return "; ".join(pairs), cookies


def http_get(url, cookie_header, timeout=40, extra=None):
    headers = {
        "User-Agent": UA,
        "Referer": "https://buff.163.com/market/csgo",
        "Accept": "application/json, text/plain, */*",
        "X-Requested-With": "XMLHttpRequest",
        "Cookie": cookie_header,
    }
    if extra:
        headers.update(extra)
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def csrf_from(header):
    """从 cookie 串里取 csrf_token 值。"""
    for part in header.split(";"):
        k, _, v = part.strip().partition("=")
        if k == "csrf_token":
            return v
    return None


def main():
    print("从浏览器读取 cookie ...", flush=True)
    header, cookies = get_cookie_header()
    if not header:
        raise SystemExit("没取到 cookie。请确认调试浏览器已打开并访问过 buff.163.com")
    names = [x["name"] for x in cookies]
    print("取到 %d 个 cookie: %s" % (len(cookies), ", ".join(names[:10])), flush=True)
    print()

    # 验证能否直接请求（市场列表需要 csrf 相关 header）
    csrf = csrf_from(header)
    print("csrf_token: %s" % ("有" if csrf else "无"), flush=True)
    extra_variants = [
        ("无额外头", None),
        ("X-CSRF-Token", {"X-CSRF-Token": csrf} if csrf else None),
        ("X-Csrftoken", {"X-Csrftoken": csrf} if csrf else None),
        ("csrf-token", {"csrf-token": csrf} if csrf else None),
    ]
    url = "https://buff.163.com/api/market/goods?game=csgo&page_num=1&page_size=3"
    for label, extra in extra_variants:
        try:
            j = http_get(url, header, extra=extra)
            print("  %-14s -> code=%-16s 条目=%s"
                  % (label, j.get("code"), len((j.get("data") or {}).get("items") or [])), flush=True)
        except Exception as e:
            print("  %-14s -> 失败 %s" % (label, str(e)[:90]), flush=True)
        time.sleep(1.0)
    print()

    # 试成交历史接口
    url2 = ("https://buff.163.com/api/market/goods/price_history/buff/v2"
            "?game=csgo&goods_id=33960&days=7")
    try:
        j2 = http_get(url2, header)
        lines = ((j2.get("data") or {}).get("lines") or [])
        rec = next((x for x in lines if x.get("key") == "sell_price_history"), None)
        pts = rec.get("points") if rec else []
        print("成交历史: code=%s  成交点数=%d" % (j2.get("code"), len(pts or [])), flush=True)
    except Exception as e:
        print("成交历史请求失败:", str(e)[:200], flush=True)


if __name__ == "__main__":
    main()
