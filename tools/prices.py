# -*- coding: utf-8 -*-
"""
生成价格文件模板与导入工具。

背景: 程序不内置任何价格抓取（各平台接口均需登录或已失效）。
价格通过本目录下的 prices.json 提供；未提供价格的物品，
程序会按「稀有度基准价 × 磨损系数」估算，并在界面标注「估价」。

prices.json 格式（三种键任选，程序都会匹配）:
{
  "prices": {
    "6297": 1248.00,                                  // 物品 ID（推荐，最精确）
    "M4A4 | 咆哮 (略有磨损)": 1248.00,                  // 中文展示名
    "M4A4 | 咆哮 (Minimal Wear)": 1248.00             // 英文展示名
  }
}

用法:
  python prices.py template            # 导出全部物品清单为 CSV, 方便填价
  python prices.py template --limit 200
  python prices.py import prices.csv   # 从填写好的 CSV 生成 prices.json
  python prices.py stats               # 查看当前价格覆盖情况
"""
import csv
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "data")
ITEMS = os.path.join(DATA, "items.json")
PRICES = os.path.join(DATA, "prices.json")

WEAR_CN = {
    "Factory New": "崭新出厂",
    "Minimal Wear": "略有磨损",
    "Field-Tested": "久经沙场",
    "Well-Worn": "破损不堪",
    "Battle-Scarred": "战痕累累",
}


def load_items():
    with open(ITEMS, encoding="utf-8") as f:
        return json.load(f)


def cmd_template(argv):
    limit = None
    if "--limit" in argv:
        limit = int(argv[argv.index("--limit") + 1])
    d = load_items()
    items = d["items"]
    if limit:
        # 优先导出高价稀有物品，便于先填重要的
        items = sorted(items, key=lambda x: (x["rarity"], x["wearIndex"]))[:limit]

    out = os.path.join(HERE, "prices_template.csv")
    with open(out, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["物品ID", "展示名", "武器", "类别", "稀有度", "磨损", "磨损档", "价格USD"])
        for it in items:
            w.writerow([
                it["id"], it["displayName"], it["weapon"], it["category"],
                it["rarity"], WEAR_CN.get(it["wear"], it["wear"]), it["wearIndex"], "",
            ])
    print("已导出模板: %s" % out)
    print("共 %d 行。填写「价格USD」列后运行: python prices.py import prices_template.csv" % len(items))


def cmd_import(argv):
    if not argv:
        raise SystemExit("用法: python prices.py import <csv文件>")
    src = argv[0]
    if not os.path.isabs(src):
        src = os.path.join(HERE, src)
    prices = {}
    skipped = 0
    with open(src, encoding="utf-8-sig", newline="") as f:
        r = csv.DictReader(f)
        for row in r:
            raw = (row.get("价格USD") or "").strip().replace("$", "").replace(",", "")
            if not raw:
                skipped += 1
                continue
            try:
                v = float(raw)
            except ValueError:
                skipped += 1
                continue
            if v <= 0:
                skipped += 1
                continue
            prices[str(row["物品ID"]).strip()] = round(v, 2)

    dest = PRICES
    existing = {}
    if os.path.exists(dest):
        try:
            with open(dest, encoding="utf-8") as f:
                existing = json.load(f).get("prices", {})
        except Exception:
            existing = {}
    existing.update(prices)
    with open(dest, "w", encoding="utf-8") as f:
        json.dump({
            "note": "键可用物品ID / 中文展示名 / 英文展示名；未列出的物品按稀有度估值。",
            "prices": existing,
        }, f, ensure_ascii=False, indent=1)

    print("已写入 %s" % dest)
    print("本次导入 %d 条, 跳过空行/无效 %d 条, 累计 %d 条" % (len(prices), skipped, len(existing)))


def cmd_stats(argv):
    d = load_items()
    items = d["items"]
    total = len(items)
    have = set()
    if os.path.exists(PRICES):
        with open(PRICES, encoding="utf-8") as f:
            have = set(json.load(f).get("prices", {}).keys())
    by_id = sum(1 for it in items if str(it["id"]) in have)
    by_name = sum(1 for it in items if it["displayName"] in have)
    covered = max(by_id, by_name)
    print("物品总数      : %d" % total)
    print("prices.json 键数: %d" % len(have))
    print("能匹配上的物品 : %d  (%.1f%%)" % (covered, covered * 100.0 / total))
    print("仍按估值       : %d" % (total - covered))


if __name__ == "__main__":
    argv = sys.argv[1:]
    if not argv:
        print(__doc__)
    elif argv[0] == "template":
        cmd_template(argv[1:])
    elif argv[0] == "import":
        cmd_import(argv[1:])
    elif argv[0] == "stats":
        cmd_stats(argv[1:])
    else:
        print(__doc__)
