# -*- coding: utf-8 -*-
"""
通用 JS 执行器：把 .js 文件内容放到已登录的 BUFF 页面里执行并打印结果。

用法:
  python runjs.py <js文件>            # 执行并打印返回值
  python runjs.py <js文件> --raw      # 原样打印（不尝试 JSON 格式化）

这样可以把 JS 写在文件里，避免 PowerShell / Python 多层转义互相干扰。
"""
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "lib"))
from cdp import CDP, WsError   # noqa: E402

PORT = 9222


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    path = sys.argv[1]
    if not os.path.isabs(path):
        path = os.path.join(HERE, path)
    with open(path, encoding="utf-8") as f:
        js = f.read()

    raw = "--raw" in sys.argv
    out_path = None
    if "--out" in sys.argv:
        out_path = sys.argv[sys.argv.index("--out") + 1]

    c = CDP(port=PORT)
    t = c.connect_page("buff.163.com")
    print("已连接:", t.get("url"), flush=True)
    try:
        val = c.evaluate(js, await_promise=True, timeout=180)
    except WsError as e:
        print("执行失败:", str(e)[:600])
        c.close()
        sys.exit(1)
    c.close()

    if raw or not isinstance(val, str):
        text = val if isinstance(val, str) else json.dumps(val, ensure_ascii=False, indent=1)
    else:
        try:
            text = json.dumps(json.loads(val), ensure_ascii=False, indent=1)
        except ValueError:
            text = val

    if out_path:
        # 直接以 UTF-8 写文件，避免 PowerShell 重定向产生 UTF-16
        if not os.path.isabs(out_path):
            out_path = os.path.join(HERE, out_path)
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(text)
        print("已写入:", out_path, "(%d 字节)" % os.path.getsize(out_path))
    else:
        print(text)


if __name__ == "__main__":
    main()
