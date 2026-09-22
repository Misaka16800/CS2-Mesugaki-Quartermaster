# -*- coding: utf-8 -*-
"""
生成抽奖程序使用的饰品数据（修正版）。

修复的关键问题:
  旧版用「中文名」做图片文件名，而 slug() 只保留 [a-z0-9]，
  中文被全部删掉，导致文件名退化成 "ddpat.png"、".png" 甚至空串，
  大量图片互相覆盖 —— 道具与图片完全对不上。

  现改为用「英文名」生成文件名（英文名唯一且可读），重名用 paint_index 区分，
  从源头保证一一对应。

输出:
  <项目>/data/items.json
  <项目>/images/<english-slug>.png   （扁平英文文件名）
"""
import json
import os
import re
import shutil
import sys
from collections import Counter

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_IMG = os.path.join(HERE, "cs2-skins")        # 源图（中文名，内容正确）
MANIFEST = os.path.join(HERE, "manifest.json")
OUT_DIR = os.path.join(HERE, "data")
IMG_DIR = os.path.join(HERE, "images")

WEARS = [
    ("Factory New",    0.00, 0.07),
    ("Minimal Wear",   0.07, 0.15),
    ("Field-Tested",   0.15, 0.38),
    ("Well-Worn",      0.38, 0.45),
    ("Battle-Scarred", 0.45, 1.00),
]

# 磨损合法性：某些皮肤并非全磨损区间都有。
#   key = 皮肤名关键字（英文名里的小写子串），value = 允许的磨损英文名集合
#   多普勒 / 伽玛多普勒：只有 崭新出厂 与 略有磨损
#   外表生锈          ：只有 久经沙场 与 破损不堪（无略磨及以上、无战痕累累）
WEAR_ALLOW = {
    "doppler":     {"Factory New", "Minimal Wear"},   # 含 Gamma Doppler
    "gamma doppler": {"Factory New", "Minimal Wear"},
    "rust coat":   {"Field-Tested", "Well-Worn"},
}


def allowed_wears(name_en: str):
    """返回该英文皮肤名允许的磨损集合；无限制则返回 None。"""
    low = (name_en or "").lower()
    for key, allowed in WEAR_ALLOW.items():
        if key in low:
            return allowed
    return None


def wear_exists(minf: float, maxf: float, lo: float, hi: float) -> bool:
    """皮肤 float 区间 [minf, maxf] 与磨损区间 [lo, hi] 是否有交集。

    这是判断「某磨损是否真实存在」的根本依据：
    例如 AWP | 二西莫夫 float 为 0.18-1，就绝不可能有崭新出厂与略有磨损。
    """
    return max(minf, lo) < min(maxf, hi)

# 剔除的低价值品质（要求：不要白色与绿色品质）
# 消费级 = Consumer Grade（白）, 工业级 = Industrial Grade（浅蓝绿）
EXCLUDE_RARITIES = {"消费级", "工业级"}


def slug_en(name: str) -> str:
    """由英文饰品名生成安全文件名。

    例: "★ Hand Wraps | Spruce DDPAT" -> "hand-wraps-spruce-ddpat"
        "AK-47 | Redline"            -> "ak-47-redline"
    """
    s = name.lower()
    s = s.replace("★", "").replace("™", "")
    s = s.replace("（★）", "").replace("(", "").replace(")", "")
    s = re.sub(r"[^a-z0-9]+", "-", s)
    s = re.sub(r"-+", "-", s).strip("-")
    return s or "unnamed"


def main():
    if not os.path.exists(MANIFEST):
        raise SystemExit("缺少 manifest.json")
    with open(MANIFEST, encoding="utf-8") as f:
        manifest = json.load(f)
    files = manifest["files"]

    # 中文数据（用于武器名/类别名/稀有度中文），与 manifest 顺序一致
    with open(os.path.join(HERE, "skins-zh.json"), encoding="utf-8") as f:
        zh = json.load(f)
    # 英文数据（用于生成文件名）
    with open(os.path.join(HERE, "skins.json"), encoding="utf-8") as f:
        en = json.load(f)

    if not (len(files) == len(zh) == len(en)):
        raise SystemExit("数据源条数不一致: manifest=%d zh=%d en=%d"
                         % (len(files), len(zh), len(en)))

    # 清空旧的（已被污染的）扁平图片目录
    if os.path.isdir(IMG_DIR):
        shutil.rmtree(IMG_DIR)
    os.makedirs(IMG_DIR, exist_ok=True)
    os.makedirs(OUT_DIR, exist_ok=True)

    # 预统计英文名重复情况，用于决定是否加 paint_index 后缀
    name_counts = Counter(s.get("name", "") for s in en)

    items = []
    used_files = {}
    n = 0
    copied = 0
    skipped_rarity = 0

    for entry, zrec, erec in zip(files, zh, en):
        name_cn = entry["name"]
        rarity = entry.get("rarity") or ""
        img_src_name = entry["file"]          # cs2-skins 里的中文名
        paint_index = entry.get("paint_index")

        if rarity in EXCLUDE_RARITIES:
            skipped_rarity += 1
            continue

        en_name = erec.get("name") or ""
        base = slug_en(en_name)
        # 英文名重复时用 paint_index 区分，保证唯一
        if name_counts.get(en_name, 0) > 1:
            fname = "%s-%s.png" % (base, paint_index or "x")
        else:
            fname = base + ".png"
        if fname in used_files:
            fname = "%s-%s.png" % (base, paint_index or "y")
        k = 2
        while fname in used_files:
            fname = "%s-%s-%d.png" % (base, paint_index or "z", k)
            k += 1
        used_files[fname] = True

        src = os.path.join(SRC_IMG, img_src_name)
        dst = os.path.join(IMG_DIR, fname)
        if os.path.exists(src):
            shutil.copy2(src, dst)
            copied += 1
        else:
            fname = ""            # 源图缺失，避免指向错图

        wobj = zrec.get("weapon") or {}
        wname = wobj.get("name", "") if isinstance(wobj, dict) else str(wobj)
        cobj = zrec.get("category") or {}
        cat_name = cobj.get("name", "") if isinstance(cobj, dict) else str(cobj)

        minf = zrec.get("min_float") or 0.0
        maxf = zrec.get("max_float")
        if maxf is None:
            maxf = 1.0

        # 该皮肤允许的磨损（None 表示全磨损都可用）
        allow = allowed_wears(en_name)

        # 原皮刀（无涂装）：游戏中本就没有磨损区分，BUFF 里也只有一条 wear=无涂装。
        # 只生成 1 条，避免同一件物品被复制成 5 条重复条目。
        is_vanilla = ("★" in en_name and "|" not in en_name)

        for wi, (wear, lo, hi) in enumerate(WEARS):
            if is_vanilla:
                if wi > 0:
                    continue                      # 只保留第一条
            else:
                # 根本判据：皮肤 float 区间与磨损 float 区间必须有交集，
                # 否则该磨损在游戏中并不存在（如 AWP | 二西莫夫没有崭新/略磨）
                if not wear_exists(minf, maxf, lo, hi):
                    continue
                # 额外规则表（多普勒系列、外表生锈等）
                if allow is not None and wear not in allow:
                    continue
            n += 1
            items.append({
                "id": n,
                "paintIndex": paint_index,
                "name": name_cn,
                "nameEn": en_name,
                "displayName": (name_cn if is_vanilla else name_cn + " (" + wear + ")"),
                "weapon": wname,
                "category": cat_name,
                "rarity": rarity,
                "wear": ("无涂装" if is_vanilla else wear),
                "wearIndex": wi,
                "minFloat": minf,
                "maxFloat": maxf,
                "statTrak": False,
                "souvenir": False,
                "image": fname,
                "priceUsd": None,
            })

    out = {
        "schema": 2,
        "note": "image 为 images/ 下的文件名，由英文饰品名生成；重名用 paint_index 区分。",
        "wearLevels": [w[0] for w in WEARS],
        "count": len(items),
        "items": items,
    }
    dest = os.path.join(OUT_DIR, "items.json")
    with open(dest, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"))

    no_img = sum(1 for i in items if not i["image"])
    print("物品数      : %d" % len(items))
    print("跳过白/绿品质: %d" % skipped_rarity)
    print("图片文件数  : %d" % len(os.listdir(IMG_DIR)))
    print("缺图的物品  : %d" % no_img)
    print("数据文件    : %s (%.0f KB)" % (dest, os.path.getsize(dest) / 1024))
    print()
    print("命名样本:")
    for it in items[:6]:
        print("   %-46s -> %s" % (it["displayName"][:44], it["image"]))
    print()
    print("刀具命名样本:")
    knives = [i for i in items if i["category"] == "匕首"]
    for it in knives[:4]:
        print("   %-46s -> %s" % (it["displayName"][:44], it["image"]))


if __name__ == "__main__":
    main()
