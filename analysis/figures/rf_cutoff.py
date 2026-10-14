# -*- coding: utf-8 -*-
import io, csv, collections
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager as fm
import web_style as T
for name in ("Malgun Gothic", "맑은 고딕"):
    if any(name == f.name for f in fm.fontManager.ttflist):
        plt.rcParams["font.family"] = name; break
plt.rcParams["axes.unicode_minus"] = False
OUT = "C:/Users/User/Desktop/bim-it/TEAM PJT/1013/"
D = collections.defaultdict(dict)
for r in csv.DictReader(io.open(OUT + "Cutoff_Sweep.csv", encoding="utf-8-sig")):
    D[r["face"]][r["cutoff"]] = (float(r["cool_reduction_pct"]), float(r["heat_reduction_pct"]), float(r["closed_hours_cool"]) + float(r["closed_hours_heat"]))
CUTS = [0, 15, 30, 45, 60, 75, 90]
COL = {"E": T.BLUE_L, "S": T.BLUE, "W": T.ORANGE}
NAME = {"E": u"동면", "S": u"남면", "W": u"서면"}
fig, axes = plt.subplots(1, 3, figsize=(18, 6.4), dpi=105, facecolor=T.BG)
fig.subplots_adjust(left=0.05, right=0.99, top=0.76, bottom=0.2, wspace=0.2)
panels = [(0, u"냉방기 일사 감소율 (%)", u"높을수록 좋아요"), (1, u"난방기 일사 감소율 (%) · 규칙대로 닫을 때", u"낮을수록 좋아요(겨울 햇빛 손실)"), (2, u"연간 닫힌 시간 (시간)", u"적을수록 좋아요(채광·조망·구동 마모)")]
for ax, (idx, title, sub) in zip(axes, panels):
    ax.set_facecolor(T.BG)
    for f in ("E", "S", "W"):
        ys = [D[f][str(c)][idx] for c in CUTS]
        ax.plot(CUTS, ys, color=COL[f], lw=2.4, marker="o", ms=5, zorder=3, label=NAME[f])
        o = D[f]["ours"][idx]
        ax.axhline(o, color=COL[f], lw=1.2, ls=(0, (4, 3)), zorder=2)
        ax.text(91, o, u"우리 %.0f" % o, color=COL[f], fontsize=8.6, va="center", ha="left")
    ax.set_xticks(CUTS); ax.set_xlim(-3, 108)
    ax.set_xlabel(u"컷오프(해와 입면의 방위차 한계, °)", fontsize=9.5, color=T.GRAY)
    ax.set_title(title, fontsize=12.5, fontweight="bold", color=T.INK, loc="left", pad=22)
    ax.text(0, 1.02, sub, transform=ax.transAxes, fontsize=10, color=T.GRAY)
    ax.yaxis.grid(True, color=T.GRID, zorder=0); ax.tick_params(colors=T.GRAY, length=0, labelsize=9)
    for s_ in ("top", "right", "left"): ax.spines[s_].set_visible(False)
axes[0].legend(frameon=False, fontsize=10, loc="lower right")
fig.text(0.05, 0.965, u"컷오프를 0~90°로 바꾸면? 우리 계절별 값은 효과가 가장 큰 구간에 닫힌 시간은 절반", fontsize=18, fontweight="bold", color=T.INK, va="top")
fig.text(0.05, 0.915, u"점선은 우리 계절별 컷오프(춘분 30·하지 60·추분 45·동지 30, 날짜 보간). 냉방 효과는 컷오프 45° 부근에서 거의 다 얻어지고, 더 키우면 닫힌 시간만 늘어요.", fontsize=10.5, color=T.GRAY, va="top")
fig.text(0.05, 0.10, u"방법: Radiance 3D 날(폭 300·간격 350·반사율 0.5), 주변 건물 325개, 월평균 날씨, 냉방기 6~9월·난방기 11~3월, 1784 9층 기준. 층별 그림자 보정과 겨울 모드는 넣지 않은 규칙 각도예요(겨울 모드를 켜면 난방 손실은 17~34%로 줄어요).", fontsize=8.8, color=T.GRAY, va="top")
for _t in list(fig.texts): _t.remove()
fig.subplots_adjust(top=0.86, bottom=0.12)
fig.savefig("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Cutoff_Sweep.png", facecolor=T.BG)
print("saved")
