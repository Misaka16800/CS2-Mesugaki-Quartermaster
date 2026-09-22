# -*- coding: utf-8 -*-
"""
把 BUFF 抓取的原始挂单数据匹配到本程序的物品，并生成 data/prices.json。

匹配方式（名称归一化后精确比对，不做模糊猜测）：
  BUFF:  AK-47（纪念品） | 皇后 (略有磨损)   + wear=略有磨损 + quality=纪念品
  本地:  AK-47 | 皇后 (略有磨损)            + wear=略有磨损 + souvenir=True
  做法:  去掉名称中的（...）标记，把磨损统一为中文，再用 (基础名, 磨损, 品质) 作键

币种：BUFF 以人民币计价，需换算成美元。汇率可调（默认 7.0），
      换算后的美元值用于奖池定价（均价 ×120%、回收 ×90%）。

用法:
  python match_prices.py                 # 生成 data/prices.json
  python match_prices.py --rate 7.2      # 指定汇率
  python match_prices.py --report        # 只看统计，不写文件
"""
import json
import os
import re
import sys
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "buff_raw.jsonl")
ITEMS = os.path.join(HERE, "data", "items.json")
OUT = os.path.join(HERE, "data", "prices.json")

WEAR_EN2CN = {
    "Factory New": "崭新出厂",
    "Minimal Wear": "略有磨损",
    "Field-Tested": "久经沙场",
    "Well-Worn": "破损不堪",
    "Battle-Scarred": "战痕累累",
}

# BUFF quality -> 本程序标记
QUALITY_ST = "StatTrak™"
QUALITY_SV = "纪念品"

TAG_RE = re.compile(r"（[^）]*）")
WEAR_TAIL_RE = re.compile(r"\s*\([^)]*\)\s*$")


def base_of(name, wear):
    """取基础名：去掉（...）标记与结尾的磨损括号。"""
    if not name:
        return ""
    s = name
    if wear:
        # 结尾若是 "(磨损)" 则去掉
        s = re.sub(r"\s*\(" + re.escape(wear) + r"\)\s*$", "", s)
    s = WEAR_TAIL_RE.sub("", s)
    s = TAG_RE.sub("", s)
    s = s.replace("｜", "|").replace("|", "|")
    s = re.sub(r"\s+", " ", s).strip()
    return s


def norm(s):
    return re.sub(r"[\s\-_·。．,，]+", "", (s or "")).lower()


def local_key(it):
    return (norm(base_of(it["displayName"], None)), WEAR_EN2CN.get(it["wear"], it["wear"]),
            bool(it.get("statTrak")), bool(it.get("souvenir")))


def buff_key(name, wear, quality):
    st = QUALITY_ST in (quality or "")
    sv = (quality or "") == QUALITY_SV
    return (norm(base_of(name, wear)), wear or "", st, sv)


def parse_price(v):
    try:
        f = float(str(v).replace(",", "").strip())
        return f if f > 0 else None
    except Exception:
        return None


def main():
    argv = sys.argv[1:]
    report_only = "--report" in argv
    rate = 7.0
    if "--rate" in argv:
        rate = float(argv[argv.index("--rate") + 1])

    if not os.path.exists(RAW):
        raise SystemExit("缺少原始数据: %s（请先运行 fetch_all_prices.py）" % RAW)

    # ---- BUFF 数据聚合：同一物品可能有多个磨损档的重复挂单，取最低价 ----
    buff = {}
    n_rows = 0
    for line in open(RAW, encoding="utf-8"):
        try:
            o = json.loads(line)
        except Exception:
            continue
        if not o.get("ok"):
            continue
        for it in o["items"]:
            # 只保留武器/刀/手套类（排除印花、探员、箱子、音乐盒等）
            name = it.get("name") or ""
            if "印花" in name or "探员" in name or "音乐盒" in name or "涂鸦" in name:
                continue
            if not it.get("wear"):
                continue          # 没有磨损字段的不是我们收录的物品
            # 用户要求：纪念品不参与定价（纪念品价格体系与普通版差异过大）
            if (it.get("quality") or "") == QUALITY_SV:
                continue
            if "（纪念品）" in name or "(纪念品)" in name:
                continue
            k = buff_key(name, it["wear"], it.get("quality"))
            if not k[0]:
                continue
            n_rows += 1
            p = parse_price(it.get("ref")) or parse_price(it.get("p"))
            if p is None:
                continue
            if k not in buff or p < buff[k]:
                buff[k] = p

    print("BUFF 有效挂单条目: %d   去重后唯一物品: %d" % (n_rows, len(buff)))

    # ---- 本地物品 ----
    data = json.load(open(ITEMS, encoding="utf-8"))
    items = data["items"]
    lk = [local_key(it) for it in items]
    local_index = defaultdict(list)
    for i, k in enumerate(lk):
        local_index[k].append(i)

    matched = {}
    missed = []
    for i, k in enumerate(lk):
        if k in buff:
            matched[i] = buff[k]
        else:
            missed.append(items[i])

    print("本地物品: %d   匹配到价格: %d (%.1f%%)   未匹配: %d"
          % (len(items), len(matched), len(matched) * 100.0 / max(1, len(items)), len(missed)))

    # ---- 未匹配样本，便于判断是否还能补 ----
    if missed:
        print()
        print("未匹配样本（前 12 条）:")
        for it in missed[:12]:
            print("   %s" % it["displayName"])

    # ---- 覆盖率按类别 ----
    print()
    by_cat = defaultdict(lambda: [0, 0])
    for i, it in enumerate(items):
        c = it["category"] or "?"
        by_cat[c][1] += 1
        if i in matched:
            by_cat[c][0] += 1
    print("分类覆盖率:")
    for c, (m, t) in sorted(by_cat.items(), key=lambda x: -x[1][1]):
        print("   %-10s %5d/%-5d  %.1f%%" % (c, m, t, m * 100.0 / max(1, t)))

    if report_only:
        return

    # ---- 二级回退：同一皮肤缺某个磨损时，用已知磨损按系数推算 ----
    # 冷门磨损（破损不堪 / 战痕累累）经常没有挂单，这是市场现实而非匹配错误。
    WEAR_FACTOR = {
        "崭新出厂": 1.00,
        "略有磨损": 0.78,
        "久经沙场": 0.58,
        "破损不堪": 0.50,
        "战痕累累": 0.45,
    }
    by_skin = defaultdict(dict)      # (基础名, st, sv) -> {磨损: 价格}
    for i, cny in matched.items():
        b = norm(base_of(items[i]["displayName"], None))
        by_skin[(b, bool(items[i].get("statTrak")), bool(items[i].get("souvenir")))][
            WEAR_EN2CN.get(items[i]["wear"], items[i]["wear"])] = cny

    derived = {}
    for i, it in enumerate(items):
        if i in matched:
            continue
        b = norm(base_of(it["displayName"], None))
        known = by_skin.get((b, bool(it.get("statTrak")), bool(it.get("souvenir"))))
        if not known:
            continue
        wear_cn = WEAR_EN2CN.get(it["wear"], it["wear"])
        f = WEAR_FACTOR.get(wear_cn)
        if not f:
            continue
        # 用「已知磨损价 ÷ 该磨损系数」还原基准价，再乘目标磨损系数
        est = [p / WEAR_FACTOR[w] * f for w, p in known.items() if w in WEAR_FACTOR]
        if not est:
            continue
        derived[i] = sum(est) / len(est)

    print()
    print("同皮肤磨损推算补充: %d 条" % len(derived))

    # ---- 三级回退：同稀有度+同磨损的中位数 ----
    med = {}
    buckets = defaultdict(list)
    for i, cny in matched.items():
        buckets[(items[i]["rarity"], items[i]["wear"])].append(cny)
    for k, vals in buckets.items():
        vals.sort()
        med[k] = vals[len(vals) // 2]

    filled = {}
    for i, it in enumerate(items):
        if i in matched or i in derived:
            continue
        m = med.get((it["rarity"], it["wear"]))
        if m:
            filled[i] = m

    print("同稀有度中位数补充: %d 条" % len(filled))
    print("仍无价格(将用程序内估值): %d 条" % (len(items) - len(matched) - len(derived) - len(filled)))

    # ---- 写出 prices.json（键用物品ID，值换算为美元）----
    prices = {}
    for i, cny in matched.items():
        prices[str(items[i]["id"])] = round(cny / rate, 4)
    for src, label in ((derived, "同皮肤磨损推算"), (filled, "同稀有度中位数")):
        for i, cny in src.items():
            prices.setdefault(str(items[i]["id"]), round(cny / rate, 4))

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({
            "source": "BUFF163 市场挂单（sell_reference_price 优先取最低；已排除纪念品）",
            "cnyPerUsd": rate,
            "note": "值为美元；键为物品ID。含三档来源：直接匹配 / 同皮肤磨损推算 / 同稀有度中位数。",
            "count": len(prices),
            "matched": len(matched),
            "derived": len(derived),
            "filledByRarity": len(filled),
            "prices": prices,
        }, f, ensure_ascii=False, indent=1)

    print()
    print("已写出 %s" % OUT)
    print("价格条数 %d（直接匹配 %d + 磨损推算 %d + 稀有度补充 %d），汇率 %.2f CNY/USD"
          % (len(prices), len(matched), len(derived), len(filled), rate))
    print()
    print("抽样（美元）:")
    for i in list(matched.keys())[:6]:
        it = items[i]
        print("   %-44s ￥%-10s -> $%.2f" % (it["displayName"][:42], matched[i], matched[i] / rate))


if __name__ == "__main__":
    main()
