# -*- coding: utf-8 -*-
"""英文模式的端到端验证：同一份合成数据跑中/英两遍，对比报告与图表。"""
import io
import json
import os
import subprocess
import sys

import numpy as np
import rasterio
from rasterio.transform import from_origin

PY = sys.executable
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENGINE = os.path.join(ROOT, "Engine", "camarkov_engine.py")
WORK = os.path.join(os.environ["TEMP"], "sgcam_en")
if not os.path.isdir(WORK):
    os.makedirs(WORK)

H, W = 80, 100
NODATA = 0


def write(name, arr):
    path = os.path.join(WORK, name)
    with rasterio.open(path, "w", driver="GTiff", height=H, width=W, count=1,
                       dtype="uint8", nodata=NODATA, transform=from_origin(500000, 3500000, 30, 30)) as ds:
        ds.write(arr.astype("uint8"), 1)
    return path


a1 = np.ones((H, W), np.uint8)
a1[:40, :] = 2
a2 = a1.copy()
a2[20:35, 30:60] = 3
a3 = a2.copy()
a3[30:45, 35:70] = 1

p1 = write("lulc_2010.tif", a1)
p2 = write("lulc_2015.tif", a2)
p3 = write("lulc_2020.tif", a3)

NAMES = ["NoData", "Cropland", "Forest", "Water"]


def build_job(lang, tag):
    job = {
        "mode": "predict",
        "lang": lang,
        "periods": [
            {"path": p1, "band": 1, "year": 2010},
            {"path": p2, "band": 1, "year": 2015},
            {"path": p3, "band": 1, "year": 2020},
        ],
        "class_count": 4,
        "class_names": NAMES,
        "nodata": NODATA,
        "transition_source": "last_two_average",
        "steps": 1,
        "neighborhood_size": 5,
        "suitability_mode": "none",
        "weights": {"suitability": 0.6, "neighborhood": 0.4},
        "recompute_neighborhood": True,
        "validation": {"enabled": True, "same_period": False},
        "outputs": {
            "raster": os.path.join(WORK, "pred_%s.tif" % tag),
            "report": os.path.join(WORK, "report_%s.txt" % tag),
            "metrics": os.path.join(WORK, "metrics_%s.json" % tag),
            "fig_dir": os.path.join(WORK, "figs_%s" % tag),
        },
        "write_compare_image": True,
        "write_hotspot_image": True,
    }
    path = os.path.join(WORK, "job_%s.json" % tag)
    with io.open(path, "w", encoding="utf-8") as f:
        f.write(json.dumps(job, ensure_ascii=False))
    return path


def run(job_path, tag):
    r = subprocess.run([PY, ENGINE, "--job", job_path], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    print("[%s] exit=%d" % (tag, r.returncode))
    if r.returncode != 0:
        print(r.stdout[-2000:])
        print(r.stderr[-2000:])
    return r.returncode


ok = True
for lang, tag in (("zh", "zh"), ("en", "en")):
    jp = build_job(lang, tag)
    if run(jp, tag) != 0:
        ok = False

# ---- 检查报告 ----
zh = io.open(os.path.join(WORK, "report_zh.txt"), encoding="utf-8").read()
en = io.open(os.path.join(WORK, "report_en.txt"), encoding="utf-8").read()


def has_cjk(text):
    return any('\u4e00' <= c <= '\u9fff' for c in text)


print("\n=== 报告检查 ===")
print("中文报告含中文:", has_cjk(zh))
print("英文报告含中文:", has_cjk(en), "(应为 False)")
if has_cjk(en):
    ok = False
    for line in en.splitlines():
        if has_cjk(line):
            print("  残留:", line)

print("\n=== 英文报告全文 ===")
print(en)

# ---- 检查图表标题 ----
print("=== 图表文件 ===")
for f in sorted(os.listdir(os.path.join(WORK, "figs_en"))):
    print("  ", f)

# ---- 指标一致 ----
mz = json.load(io.open(os.path.join(WORK, "metrics_zh.json"), encoding="utf-8"))
me = json.load(io.open(os.path.join(WORK, "metrics_en.json"), encoding="utf-8"))
same = (mz["matrix_budget"] == me["matrix_budget"]
        and abs(mz["accuracy"]["oa"] - me["accuracy"]["oa"]) < 1e-12)
print("\n中英两版预算矩阵与 OA 一致:", same)
if not same:
    ok = False

print("\n结论:", "通过" if ok else "失败")
sys.exit(0 if ok else 1)
