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
DIM = {1: 31, 2: 28, 3: 31, 6: 30, 7: 31, 8: 31, 9: 30, 11: 30, 12: 31}
COOL, HEAT = (6, 7, 8, 9), (11, 12, 1, 2, 3)
W = {}
SH = {}
for r in csv.DictReader(io.open(OUT + "Floor_Radiance_Hourly.csv", encoding="utf-8-sig")):
    k = (int(r["floor"]), int(r["month"]), r["face"], r["tag"], int(r["hour"]))
    W[k] = float(r["W_m2"]); SH[(k[0], k[1], k[2], k[4])] = float(r["shaded_frac"])
floors = sorted(set(k[0] for k in W))
def hours(fl, mo, fk): return sorted(h for (f, m, x, t, h) in W if f == fl and m == mo and x == fk and t == "none_ctx")
def season(fl, fk, months, kind, suffix=""):
    tot = 0.0; ch = 0.0
    for mo in months:
        for h in hours(fl, mo, fk):
            d = DIM[mo] / 1000.0
            if kind == "bare": tot += W[(fl, mo, fk, "none%s_ctx" % suffix, h)] * d
            elif kind == "fix": tot += W[(fl, mo, fk, "fix0%s_ctx" % suffix, h)] * d
            elif kind == "rule":
                key = (fl, mo, fk, "rule%s_ctx" % suffix, h)
                if key in W: tot += W[key] * d; ch += DIM[mo]
                else: tot += W[(fl, mo, fk, "fix0%s_ctx" % suffix, h)] * d
            elif kind == "win": tot += W[(fl, mo, fk, "win%s_ctx" % suffix, h)] * d
    return tot, ch
rows = [u"floor,face,cool_bare_kWh_m2,cool_saved_kWh_m2,cool_reduction_pct,closed_hours_cool,heat_loss_rule_pct,heat_loss_winter_pct"]
R = {}
for fl in floors:
    for fk in ("E", "S", "W"):
        cb, _ = season(fl, fk, COOL, "bare"); cr, ch = season(fl, fk, COOL, "rule")
        hb, _ = season(fl, fk, HEAT, "bare"); hr, _ = season(fl, fk, HEAT, "rule"); hw, _ = season(fl, fk, HEAT, "win")
        tot = sh = 0
        for mo in COOL:
            for h in hours(fl, mo, fk):
                if SH.get((fl, mo, fk, h), 1.0) < 1.0 or True:
                    tot += 1; sh += 1 if SH.get((fl, mo, fk, h), 0.0) >= 0.5 else 0
        R[(fl, fk)] = (cb, cb - cr, 100 * (1 - cr / cb), ch, 100 * (1 - hr / hb), 100 * (1 - hw / hb), 100.0 * sh / max(tot, 1))
        rows.append(u"%d,%s,%.1f,%.1f,%.0f,%.0f,%.0f,%.0f" % ((fl, fk) + R[(fl, fk)][:6]))
io.open("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Floor_Effect_Summary.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
for fk in ("E", "S", "W"):
    print(fk, [(fl, "bare %.0f saved %.0f (%.0f%%) closed %.0fh heat-rule %.0f%% heat-win %.0f%%" % R[(fl, fk)][:6]) for fl in floors])
# 보호 유리
gf = sorted(set(k[0] for k in W if k[3].startswith("noneg")))
grows = [u"floor,face,cool_saved_no_glass,cool_saved_with_glass_vs_bare,cool_saved_with_glass_vs_glass_bare,louver_only_reduction_pct_with_glass"]
for fl in gf:
    for fk in ("E", "S", "W"):
        cb, _ = season(fl, fk, COOL, "bare"); cr, _ = season(fl, fk, COOL, "rule")
        cbg, _ = season(fl, fk, COOL, "bare", "g"); crg, _ = season(fl, fk, COOL, "rule", "g")
        grows.append(u"%d,%s,%.1f,%.1f,%.1f,%.0f" % (fl, fk, cb - cr, cb - crg, cbg - crg, 100 * (1 - crg / cbg)))
        print("glass", fl, fk, "no-glass 절감 %.0f | 유리+루버 절감(맨창 대비) %.0f | 유리 대비 추가 절감 %.0f | 유리 있을때 루버 효과 %.0f%% (유리 없을때 %.0f%%)" % (cb - cr, cb - crg, cbg - crg, 100 * (1 - crg / cbg), 100 * (1 - cr / cb)))
io.open("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Protective_Glass_Radiance.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(grows) + u"\r\n")
# 그림
COL = {"E": T.BLUE_L, "S": T.BLUE, "W": T.ORANGE}; NAME = {"E": u"동면", "S": u"남면", "W": u"서면"}
fig, axes = plt.subplots(1, 3, figsize=(18, 6.4), dpi=105, facecolor=T.BG)
fig.subplots_adjust(left=0.05, right=0.99, top=0.76, bottom=0.2, wspace=0.2)
panels = [(1, u"냉방기 창 일사 절감량 (kWh/㎡)", u"높을수록 효과가 커요"), (3, u"냉방기 닫힌 시간 (시간)", u"적을수록 구동 마모가 적어요"), (5, u"난방기 일사 손실, 겨울 모드 켬 (%)", u"낮을수록 좋아요")]
for ax, (idx, title, sub) in zip(axes, panels):
    ax.set_facecolor(T.BG)
    for fk in ("E", "S", "W"):
        ys = [R[(fl, fk)][idx] for fl in floors]
        ax.plot(floors, ys, color=COL[fk], lw=2.4, marker="o", ms=5, label=NAME[fk], zorder=3)
    ax.set_xticks(floors); ax.set_xlabel(u"층", fontsize=9.5, color=T.GRAY)
    ax.set_title(title, fontsize=12.5, fontweight="bold", color=T.INK, loc="left", pad=22)
    ax.text(0, 1.02, sub, transform=ax.transAxes, fontsize=10, color=T.GRAY)
    ax.yaxis.grid(True, color=T.GRID, zorder=0); ax.tick_params(colors=T.GRAY, length=0, labelsize=9)
    for s_ in ("top", "right", "left"): ax.spines[s_].set_visible(False)
axes[0].legend(frameon=False, fontsize=10, loc="best")
fig.text(0.05, 0.965, u"몇 층부터 다는 게 효율적일까? 층별 효과 (1784, 수직 루버 규칙대로)", fontsize=18, fontweight="bold", color=T.INK, va="top")
fig.text(0.05, 0.915, u"주변 건물 325개 그림자를 층 높이마다 다시 적용한 Radiance 계산이에요. 저층은 그림자 때문에 해가 덜 닿아서 날이 닫힐 일이 줄어요.", fontsize=10.5, color=T.GRAY, va="top")
fig.text(0.05, 0.10, u"방법: Radiance 3D 날(폭 300·간격 350·반사율 0.5), 월평균 날씨, 냉방기 6~9월·난방기 11~3월, 면 중앙 1㎡. 해가 막히는 층·시각은 규칙에 따라 날을 열어 둠(절반 이상 막히면 열림). 3·5·9·15·20·25층 계산, 사이는 이어 그린 선이에요.", fontsize=8.8, color=T.GRAY, va="top")
for _t in list(fig.texts): _t.remove()
fig.subplots_adjust(top=0.86, bottom=0.12)
fig.savefig("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Floor_Effect.png", facecolor=T.BG)
print("saved")
