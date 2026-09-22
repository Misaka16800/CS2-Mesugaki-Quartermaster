# -*- coding: utf-8 -*-
"""
用「成交记录」价格重建 data/prices.json，并把 7 天内无成交的物品踢出奖池。

数据来源:
  buff_raw.jsonl  —— 市场列表（含 goods_id / 名称 / 磨损 / 挂单价）
  buff_tx.jsonl   —— 每个 goods_id 的 7 天成交记录（sell_price_history）

处理规则:
  1. 抓了成交数据的物品
     - points 非空 -> 用成交价（中位数，抗异常单）换算美元
     - points 为空 -> 7 天内无成交 -> 标记剔除（excluded=true）
  2. 未抓成交数据的物品（价格低于阈值）-> 保留挂单价，不判定剔除
  3. 纪念品一律排除

输出:
  data/prices.json   —— 物品ID -> 美元价（含剔除名单）
  data/excluded.json —— 被踢出奖池的物品ID列表（7 天无成交）

用法:
  python match_transactions.py --rate 7.0
  python match_transactions.py --report      # 只看统计
"""
import json
import os
import re
import sys
import statistics
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
LIST_RAW = os.path.join(HERE, "buff_raw.jsonl")
TX_RAW = os.path.join(HERE, "buff_tx.jsonl")
ITEMS = os.path.join(HERE, "data", "items.json")
OUT = os.path.join(HERE, "data", "prices.json")
EXCLUDED = os.path.join(HERE, "data", "excluded.json")

WEAR_EN2CN = {
    "Factory New": "崭新出厂",
    "Minimal Wear": "略有磨损",
    "Field-Tested": "久经沙场",
    "Well-Worn": "破损不堪",
    "Battle-Scarred": "战痕累累",
}

TAG_RE = re.compile(r"（[^）]*）")
WEAR_TAIL_RE = re.compile(r"\s*\([^)]*\)\s*$")


def base_of(name, wear):
    if not name:
        return ""
    s = name
    if wear:
        s = re.sub(r"\s*\(" + re.escape(wear) + r"\)\s*$", "", s)
    s = WEAR_TAIL_RE.sub("", s)
    s = TAG_RE.sub("", s)
    s = s.replace("｜", "|")
    return re.sub(r"\s+", " ", s).strip()


def norm(s):
    return re.sub(r"[\s\-_·。．,，]+", "", (s or "")).lower()


def key_of(base, wear, is_st, is_sv):
    return (norm(base), wear or "", bool(is_st), bool(is_sv))


def local_key(it):
    base = base_of(it["displayName"], None)
    return key_of(base, WEAR_EN2CN.get(it["wear"], it["wear"]),
                  it.get("statTrak"), it.get("souvenir"))


def main():
    argv = sys.argv[1:]
    report_only = "--report" in argv
    rate = float(argv[argv.index("--rate") + 1]) if "--rate" in argv else 7.0

    for p in (LIST_RAW, ITEMS):
        if not os.path.exists(p):
            raise SystemExit("缺少文件: %s" % p)

    # ---------- 1) 市场列表：key -> gid（纪念品排除） ----------
    gid_of = {}
    price_of = {}
    for line in open(LIST_RAW, encoding="utf-8"):
        try:
            o = json.loads(line)
        except Exception:
            continue
        if not o.get("ok"):
            continue
        for it in o["items"]:
            nm = it.get("name") or ""
            if not it.get("wear"):
                continue
            if "（纪念品）" in nm or (it.get("quality") or "") == "纪念品":
                continue
            if "印花" in nm or "探员" in nm or "音乐盒" in nm or "涂鸦" in nm:
                continue
            base = base_of(nm, it["wear"])
            st = "StatTrak" in nm
            k = key_of(base, it["wear"], st, False)
            gid = it.get("gid")
            if gid and k not in gid_of:
                gid_of[k] = gid
            try:
                p = float(it.get("ref") or it.get("p") or 0)
            except Exception:
                p = 0
            if p > 0:
                price_of[k] = min(price_of.get(k, p), p)

    print("市场可匹配物品(去纪念品): %d" % len(gid_of))

    # ---------- 2) 成交记录：gid -> 成交价中位数 / 是否有成交 ----------
    tx_by_gid = {}
    if os.path.exists(TX_RAW):
        for line in open(TX_RAW, encoding="utf-8"):
            try:
                o = json.loads(line)
            except Exception:
                continue
            if not o.get("ok"):
                continue
            pts = o.get("pts") or []
            prices = [float(p[1]) for p in pts if isinstance(p, (list, tuple)) and len(p) >= 2 and p[1]]
            tx_by_gid[o["gid"]] = prices
    print("有成交记录数据的物品: %d" % len(tx_by_gid))

    # ---------- 3) 匹配到本地物品 ----------
    data = json.load(open(ITEMS, encoding="utf-8"))
    items = data["items"]

    gid_to_key = {v: k for k, v in gid_of.items()}

    matched_tx = {}        # 本地下标 -> 成交价(人民币)
    matched_list = {}      # 本地下标 -> 挂单价(人民币)
    no_trade = set()       # 本地下标 -> 7天无成交，剔除
    gid_to_local = defaultdict(list)
    for i, it in enumerate(items):
        k = local_key(it)
        g = gid_of.get(k)
        if g:
            gid_to_local[g].append(i)

    for g, idxs in gid_to_local.items():
        prices = tx_by_gid.get(g)
        if prices is None:
            # 没抓这个物品的成交数据 -> 用挂单价
            for i in idxs:
                k = local_key(items[i])
                cny = price_of.get(k)
                if cny:
                    matched_list[i] = cny
            continue
        if prices:
            med = statistics.median(prices)
            for i in idxs:
                matched_tx[i] = med
        else:
            # 抓了数据但 7 天零成交 -> 剔除
            for i in idxs:
                no_trade.add(i)

    print()
    print("用成交价: %d 件" % len(matched_tx))
    print("用挂单价(未抓成交): %d 件" % len(matched_list))
    print("7 天无成交、踢出池子: %d 件" % len(no_trade))
    print("无任何价格: %d 件" % (len(items) - len(matched_tx) - len(matched_list) - len(no_trade)))

    if matched_tx and matched_list:
        print()
        print("成交价 vs 挂单价 抽样对比:")
        cnt = 0
        for i in list(matched_tx.keys())[:400]:
            if i in matched_list:
                t, l = matched_tx[i], matched_list[i]
                print("   %-42s 成交 ￥%-9.2f 挂单 ￥%-9.2f  差 %+.1f%%"
                      % (items[i]["displayName"][:40], t, l, (t - l) / l * 100))
                cnt += 1
                if cnt >= 8:
                    break

    if report_only:
        pass  # 报告模式下也要继续算回退，便于查看最终覆盖率

    # ---------- 4) 回退填充：保证每件物品都有价格 ----------
    WEAR_FACTOR = {
        "崭新出厂": 1.00, "略有磨损": 0.78, "久经沙场": 0.58,
        "破损不堪": 0.50, "战痕累累": 0.45,
    }

    # 4a) 同皮肤已知磨损 -> 按系数推算
    by_skin = defaultdict(dict)
    for src in (matched_tx, matched_list):
        for i, cny in src.items():
            it = items[i]
            b = norm(base_of(it["displayName"], None))
            st, sv = bool(it.get("statTrak")), bool(it.get("souvenir"))
            by_skin[(b, st, sv)][WEAR_EN2CN.get(it["wear"], it["wear"])] = cny

    derived = {}
    for i, it in enumerate(items):
        if i in matched_tx or i in matched_list or i in no_trade:
            continue
        b = norm(base_of(it["displayName"], None))
        known = by_skin.get((b, bool(it.get("statTrak")), bool(it.get("souvenir"))))
        if not known:
            continue
        wear_cn = WEAR_EN2CN.get(it["wear"], it["wear"])
        f = WEAR_FACTOR.get(wear_cn)
        if not f:
            continue
        est = [p / WEAR_FACTOR[w] * f for w, p in known.items() if w in WEAR_FACTOR]
        if est:
            derived[i] = sum(est) / len(est)

    # 4b) 同稀有度 + 同磨损的中位数兜底
    buckets = defaultdict(list)
    for src in (matched_tx, matched_list):
        for i, cny in src.items():
            buckets[(items[i]["rarity"], items[i]["wear"])].append(cny)
    med = {}
    for k, vals in buckets.items():
        vals.sort()
        med[k] = vals[len(vals) // 2]

    filled = {}
    for i, it in enumerate(items):
        if i in matched_tx or i in matched_list or i in no_trade or i in derived:
            continue
        m = med.get((it["rarity"], it["wear"]))
        if m:
            filled[i] = m

    total_no_price = len(items) - len(matched_tx) - len(matched_list) - len(no_trade) \
        - len(derived) - len(filled)
    print()
    print("回退填充：同皮肤磨损推算 %d 件，同稀有度中位数 %d 件，仍无价格 %d 件"
          % (len(derived), len(filled), total_no_price))

    if report_only:
        return

    # ---------- 5) 写 prices.json ----------
    prices = {}
    for i, cny in matched_tx.items():
        prices[str(items[i]["id"])] = round(cny / rate, 4)
    for src in (matched_list, derived, filled):
        for i, cny in src.items():
            prices.setdefault(str(items[i]["id"]), round(cny / rate, 4))

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({
            "source": "BUFF163：优先用 7 天成交记录中位数，其次挂单价；已排除纪念品",
            "cnyPerUsd": rate,
            "count": len(prices),
            "fromTransaction": len(matched_tx),
            "fromListing": len(matched_list),
            "derivedByWear": len(derived),
            "filledByRarity": len(filled),
            "excludedNoTrade": len(no_trade),
            "prices": prices,
        }, f, ensure_ascii=False, indent=1)
    print()
    print("已写出 %s（%d 条，汇率 %.2f）" % (OUT, len(prices), rate))
    print("  成交价 %d · 挂单价 %d · 磨损推算 %d · 稀有度兜底 %d"
          % (len(matched_tx), len(matched_list), len(derived), len(filled)))

    with open(EXCLUDED, "w", encoding="utf-8") as f:
        json.dump({
            "reason": "BUFF 7 天内无成交记录",
            "count": len(no_trade),
            "ids": sorted(items[i]["id"] for i in no_trade),
        }, f, ensure_ascii=False, indent=1)
    print("已写出 %s（剔除 %d 件）" % (EXCLUDED, len(no_trade)))


def cmd_coverage():
    """统计我们库中高价物品的成交数据覆盖进度（抓取过程中可用）。"""
    gid_of = {}
    price_of = {}
    for line in open(LIST_RAW, encoding="utf-8"):
        try:
            o = json.loads(line)
        except Exception:
            continue
        if not o.get("ok"):
            continue
        for it in o["items"]:
            nm = it.get("name") or ""
            if not it.get("wear"):
                continue
            if "（纪念品）" in nm or (it.get("quality") or "") == "纪念品":
                continue
            if any(x in nm for x in ("印花", "探员", "音乐盒", "涂鸦")):
                continue
            k = key_of(base_of(nm, it["wear"]), it["wear"], "StatTrak" in nm, False)
            if it.get("gid") and k not in gid_of:
                gid_of[k] = it["gid"]
            try:
                p = float(it.get("ref") or it.get("p") or 0)
            except Exception:
                p = 0
            if p > 0:
                price_of[k] = min(price_of.get(k, p), p)

    done = set()
    if os.path.exists(TX_RAW):
        for line in open(TX_RAW, encoding="utf-8"):
            try:
                o = json.loads(line)
            except Exception:
                continue
            if o.get("ok"):
                done.add(o["gid"])

    data = json.load(open(ITEMS, encoding="utf-8"))
    items = data["items"]

    hi_total = hi_done = 0
    for it in items:
        k = local_key(it)
        g = gid_of.get(k)
        if not g:
            continue
        if price_of.get(k, 0) < 1000:
            continue
        hi_total += 1
        if g in done:
            hi_done += 1

    print("库中 ￥1000+ 且有市场对应的物品: %d 件" % hi_total)
    print("已抓成交数据:                  %d 件 (%.0f%%)"
          % (hi_done, hi_done * 100.0 / max(1, hi_total)))
    print("已抓成交记录总数:              %d 条" % len(done))
    print()
    print("市场待抓物品总数(￥1000+): 见抓取日志")


if __name__ == "__main__":
    argv = sys.argv[1:]
    if "--coverage" in argv:
        cmd_coverage()
        sys.exit(0)
    main()
