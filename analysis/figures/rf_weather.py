# -*- coding: utf-8 -*-
import io, csv, collections
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager as fm
for name in ("Malgun Gothic", "맑은 고딕"):
    if any(name == f.name for f in fm.fontManager.ttflist):
        plt.rcParams["font.family"] = name; break
plt.rcParams["axes.unicode_minus"] = False
OUT = "C:/Users/User/Desktop/bim-it/TEAM PJT/1013/"
DIM = {1: 31, 2: 28, 3: 31, 6: 30, 7: 31, 8: 31, 9: 30, 11: 30, 12: 31}
COOL, HEAT = (6, 7, 8, 9), (11, 12, 1, 2, 3)
W = collections.defaultdict(float)   # (mo, class, face, tag) -> sum over hours of W/m2
days = {}
for r in csv.DictReader(io.open(OUT + "Weather_Variability_Hourly.csv", encoding="utf-8-sig")):
    mo = int(r["month"]); cls = r["class"].split("-")[0]
    W[(mo, cls, r["face"], r["tag"])] += float(r["W_m2"]) / 1000.0     # kWh/m2/day (1시간 간격)
    days[(mo, cls)] = int(r["day"])
def wts(mo):
    n = DIM[mo]; k = n // 3
    return {"cloudy": k, "mid": n - 2 * k, "clear": k}
def season(face, months, tag, scen):
    tot = 0.0
    for mo in months:
        if scen == "avg": tot += W[(mo, "avg", face, tag)] * DIM[mo]
        elif scen == "mix": tot += sum(W[(mo, c, face, tag)] * w for c, w in wts(mo).items())
        else: tot += W[(mo, scen, face, tag)] * DIM[mo]
    return tot
SC = [("clear", u"맑은 날만"), ("mix", u"실제 날씨 섞임"), ("avg", u"월평균(지금 방식)"), ("cloudy", u"흐린 날만")]
rows = [u"season,face,scenario,bare,fixed_pct,rule_pct,winter_pct"]
res = {}
for season_name, months in (("cool", COOL), ("heat", HEAT)):
    for f in ("E", "S", "W"):
        for sc, lab in SC:
            b = season(f, months, "none_ctx", sc)
            fx = 100 * (1 - season(f, months, "fix0_ctx", sc) / b)
            ru = 100 * (1 - season(f, months, "rule_ctx", sc) / b)
            wi = 100 * (1 - season(f, months, "win_ctx", sc) / b) if season_name == "heat" else float("nan")
            res[(season_name, f, sc)] = (b, fx, ru, wi)
            rows.append(u"%s,%s,%s,%.1f,%.0f,%.0f,%s" % (season_name, f, sc, b, fx, ru, ("%.0f" % wi) if wi == wi else ""))
io.open("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Weather_Variability_Summary.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
print("%-5s %-2s %-12s %7s %6s %6s %7s" % ("sea", "f", "scenario", "bare", "fix%", "rule%", "winter%"))
for k, v in res.items():
    print("%-5s %-2s %-12s %7.0f %6.0f %6.0f %7s" % (k[0], k[1], k[2], v[0], v[1], v[2], ("%.0f" % v[3]) if v[3] == v[3] else "-"))

import web_style as T
INK, GRAY, BG = T.INK, T.GRAY, T.BG
COLS = {"clear": T.ORANGE, "mix": T.BLUE, "avg": T.BLUE_L, "cloudy": T.GRAY_M}
fig, axes = plt.subplots(1, 2, figsize=(16, 6.6), dpi=105, facecolor=BG)
fig.subplots_adjust(left=0.05, right=0.99, top=0.76, bottom=0.2, wspace=0.14)
panels = [("cool", 2, u"냉방기(6~9월): 반응형(규칙대로) 일사 감소율 %", u"높을수록 좋아요"),
          ("heat", 3, u"난방기(11~3월): 반응형(겨울 모드 켬) 일사 감소율 %", u"낮을수록 좋아요(겨울 햇빛을 덜 잃어요)")]
for ax, (sn, idx, title, sub) in zip(axes, panels):
    ax.set_facecolor(BG)
    for gi, f in enumerate(("E", "S", "W")):
        for si, (sc, lab) in enumerate(SC):
            v = res[(sn, f, sc)][idx]
            x = gi - 0.36 + 0.24 * (si + 0.5)
            ax.bar(x, v, width=0.22, color=COLS[sc], zorder=3, label=lab if gi == 0 else None)
            ax.text(x, v + 1, u"%.0f" % v, ha="center", va="bottom", fontsize=8.6, color=INK)
    ax.set_xticks(range(3)); ax.set_xticklabels([u"동면", u"남면", u"서면"], fontsize=12, color=INK)
    ax.set_title(title, fontsize=12.5, fontweight="bold", color=INK, loc="left", pad=22)
    ax.text(0, 1.02, sub, transform=ax.transAxes, fontsize=10, color=GRAY)
    ax.yaxis.grid(True, color=T.GRID, zorder=0); ax.tick_params(axis="y", labelsize=9, colors=GRAY, length=0); ax.tick_params(axis="x", length=0)
    for s_ in ("top", "right", "left"): ax.spines[s_].set_visible(False)
    ax.set_ylim(0, 100)
axes[0].legend(frameon=False, fontsize=9.5, loc="upper left", ncol=2)
fig.text(0.05, 0.965, u"날씨가 달라도 결론이 같을까? 맑은 날 · 흐린 날 · 실제 날씨 섞임 비교 (1784 9층)", fontsize=18, fontweight="bold", color=INK, va="top")
fig.text(0.05, 0.915, u"월별로 날을 맑은/보통/흐린 3그룹으로 나눠 대표일을 Radiance로 계산했어요. '실제 날씨 섞임'은 세 그룹을 날 수만큼 합친 값이에요.", fontsize=10.5, color=GRAY, va="top")
fig.text(0.05, 0.10, u"방법: Radiance 3D 날(폭 300·간격 350·반사율 0.5), 주변 건물 325개, 서울(성남) TMY 시간별 직달·산란 일사. 층별 그림자 보정은 넣지 않은 규칙 각도(컷오프 날짜 보간). 월평균은 같은 방식으로 만든 비교용 값이에요.", fontsize=8.8, color=GRAY, va="top")
for _t in list(fig.texts): _t.remove()
fig.subplots_adjust(top=0.86, bottom=0.12)
fig.savefig("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Weather_Variability.png", facecolor=BG)
print("saved")
