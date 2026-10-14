# -*- coding: utf-8 -*-
# 직달 일사로 인한 조도(총조도 - 하늘만 켠 조도)가 1,000 lx 이상인 면적 비율 (ASE와 비슷한 대체 지표)
import io, json
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager as fm

for name in ("Malgun Gothic", "맑은 고딕"):
    if any(name == f.name for f in fm.fontManager.ttflist):
        plt.rcParams["font.family"] = name
        break
plt.rcParams["axes.unicode_minus"] = False
S = "C:/Users/User/Desktop/bim-it/TEAM PJT/1012/"
R = "C:/Users/User/Desktop/bim-it/TEAM PJT/1012/"
RX, RY, ST = 15.75, 13.8, 0.25
NX, NY = int(RX / ST), int(RY / ST)
xs = (np.arange(NX) + 0.5) * ST; ys = (np.arange(NY) + 0.5) * ST
X, Y = np.meshgrid(xs, ys, indexing="ij")
dist = np.minimum(X, Y)
tot = json.load(open(S + "Interior_Renders_Corner_default/grids.json"))
sky = json.load(open(S + "Interior_Renders_Corner_default_sky/grids.json"))
CASES = [("none", u"시공 전"), ("fix0", u"고정 0°"), ("react", u"반응형")]
DAYK = {"Summer": u"하지", "Winter": u"동지"}
HOURS = [10, 12, 15]
res = {}
rows = [u"day,hour,case,sun1000_window_zone_pct,sun1000_room_pct,sun_max_lux,sun_mean_lux"]
for day in ("Summer", "Winter"):
    for h in HOURS:
        for tag, lab in CASES:
            k = "%s_%02d_%s" % (day, h, tag)
            a = np.array(tot[k]["lux"]).reshape(NX, NY); b = np.array(sky[k]["lux"]).reshape(NX, NY)
            sun = np.maximum(a - b, 0.0)
            w = 100.0 * float((sun[dist < 4.5] >= 1000.0).mean())
            r = 100.0 * float((sun >= 1000.0).mean())
            res[(day, h, tag)] = (w, r, float(sun.max()), float(sun.mean()))
            rows.append(u"%s,%d,%s,%.0f,%.0f,%.0f,%.0f" % (day, h, tag, w, r, sun.max(), sun.mean()))
io.open("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Interior_DirectSun.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")

INK, GRAY, BG = "#0A1317", "#5D6C7B", "#FFFFFF"
COL = {"none": "#9AA6B2", "fix0": "#0064E0", "react": "#F2B544"}
fig, axes = plt.subplots(1, 2, figsize=(15, 6.4), dpi=105, facecolor=BG)
fig.subplots_adjust(left=0.06, right=0.99, top=0.76, bottom=0.2, wspace=0.14)
for ax, day in zip(axes, ("Summer", "Winter")):
    ax.set_facecolor(BG)
    for gi, h in enumerate(HOURS):
        for si, (tag, lab) in enumerate(CASES):
            v = res[(day, h, tag)][1]
            x = gi - 0.3 + si * 0.3
            ax.bar(x, v, width=0.27, color=COL[tag], zorder=3, label=lab if gi == 0 else None)
            ax.text(x, v + 1, u"%.0f%%" % v, ha="center", va="bottom", fontsize=9, color=INK)
    ax.set_xticks(range(3)); ax.set_xticklabels([u"%02d:30" % h for h in HOURS], fontsize=11, color=INK)
    ax.set_ylim(0, 100); ax.yaxis.grid(True, color="#DEE3E9", zorder=0)
    ax.tick_params(axis="y", labelsize=9, colors=GRAY, length=0); ax.tick_params(axis="x", length=0)
    for s in ("top", "right", "left"): ax.spines[s].set_visible(False)
    ax.set_title(DAYK[day], fontsize=13.5, fontweight="bold", color=INK, loc="left", pad=8)
    ax.set_ylabel(u"직달 일사 조도 1,000 lx 이상 면적 (%)", fontsize=9.5, color=GRAY)
axes[0].legend(frameon=False, fontsize=10, loc="upper right")
fig.text(0.06, 0.965, u"모서리 방: 직달 햇빛이 직접 닿는 면적 (눈부심 위험 지표)", fontsize=18, fontweight="bold", color=INK, va="top")
fig.text(0.06, 0.915, u"ASE(직사 일사 1,000 lx 이상 면적)를 대표일 시각으로 근사했어요. 직달 조도 = 햇빛을 켠 조도 - 하늘만 켠 조도.", fontsize=10.5, color=GRAY, va="top")
fig.text(0.06, 0.875, u"연 250시간 노출 면적 10% 이하 같은 연간 기준은 계산하지 않았어요(연간 계산 필요). 아래는 대표일 시각 하나의 값이에요.", fontsize=10.5, color=GRAY, va="top")
fig.text(0.06, 0.09, u"Radiance, 월평균 날씨, 작업면 0.75 m, 마감 천장 0.80·벽 0.60·바닥 0.20, 통창·층고 4.2 m, 방 15.75×13.8 m, 전체 방 면적 기준(왼쪽 벽 서면, 아래쪽 벽 남면).", fontsize=9, color=GRAY, va="top")
for _t in list(fig.texts): _t.remove()
fig.subplots_adjust(top=0.86, bottom=0.12)
fig.savefig("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Interior_DirectSun.png", facecolor=BG)
for day in ("Summer", "Winter"):
    for h in HOURS:
        print(day, h, {t: tuple(round(x) for x in res[(day, h, t)][:2]) for t, _ in CASES})
