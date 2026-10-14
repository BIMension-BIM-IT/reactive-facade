# -*- coding: utf-8 -*-
import io, json
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager as fm

for name in ("Malgun Gothic", "맑은 고딕"):
    if any(name == f.name for f in fm.fontManager.ttflist):
        plt.rcParams["font.family"] = name
        break
plt.rcParams["axes.unicode_minus"] = False
D = r"C:\Users\User\Desktop\bim-it\TEAM PJT\1012" + "\\"
off = json.load(io.open(D + "solar_result_winter_off.json", encoding="utf-8"))
on = json.load(io.open(D + "solar_result_winter_on.json", encoding="utf-8"))
ORDER = ["S", "E", "W", "N"]
NAME = {"S": u"남면", "E": u"동면", "W": u"서면", "N": u"북면"}
get = lambda res: {f["id"]: f for f in res["facades"]}
fo, fn = get(off), get(on)
INK, GRAY, BG = "#0A1317", "#5D6C7B", "#FFFFFF"
C_BARE, C_FIX, C_REA, C_WIN = "#9AA6B2", "#0064E0", "#F2B544", "#5AAEFF"

fig, axes = plt.subplots(1, 2, figsize=(16, 7.8), dpi=110, facecolor=BG, gridspec_kw={"wspace": 0.12})
fig.subplots_adjust(left=0.05, right=0.985, top=0.80, bottom=0.22)

def panel(ax, season, title, series, note):
    ax.set_facecolor(BG)
    n = len(series)
    w = 0.8 / n
    for gi, f in enumerate(ORDER):
        for si, (lab, col, src) in enumerate(series):
            v = src[f][season][lab_key[lab]] if False else None
    return

# 값 꺼내기
def val(src, f, season, key):
    return src[f][season][key]

KEY = {"bare": "bare_kwh_m2_yr", "fix": "external_fixed_kwh_m2_yr", "rea": "external_reactive_kwh_m2_yr"}

def draw(ax, season, title, with_winter):
    ax.set_facecolor(BG)
    series = [(u"시공 전", C_BARE, fo, "bare"), (u"외부 고정 0°", C_FIX, fo, "fix"), (u"외부 반응형(규칙대로)", C_REA, fo, "rea")]
    if with_winter:
        series.append((u"외부 반응형(겨울 모드: 혼합 규칙)", C_WIN, fn, "rea"))
    n = len(series)
    w = 0.82 / n
    top = 0
    for gi, f in enumerate(ORDER):
        bare = val(fo, f, season, KEY["bare"])
        for si, (lab, col, src, k) in enumerate(series):
            v = val(src, f, season, KEY[k])
            x = gi - 0.41 + w * (si + 0.5)
            ax.bar(x, v, width=w * 0.92, color=col, label=lab if gi == 0 else None, zorder=3)
            top = max(top, v)
            txt = u"%.0f" % v
            if k != "bare" and bare > 0:
                txt += u"\n(%d%%)" % round(-100.0 * (bare - v) / bare) if False else u"\n(-%d%%)" % round(100.0 * (bare - v) / bare)
            ax.text(x, v + 4, txt, ha="center", va="bottom", fontsize=8.4, color=INK, linespacing=1.1)
    ax.set_xticks(range(len(ORDER)))
    ax.set_xticklabels([NAME[f] for f in ORDER], fontsize=12, color=INK)
    ax.set_ylim(0, top * 1.38)
    ax.yaxis.grid(True, color="#DEE3E9", zorder=0)
    ax.tick_params(axis="y", labelsize=9, colors=GRAY, length=0)
    ax.tick_params(axis="x", length=0)
    for s in ("top", "right", "left"):
        ax.spines[s].set_visible(False)
    ax.spines["bottom"].set_color("#DEE3E9")
    ax.set_title(title, fontsize=13.5, fontweight="bold", color=INK, loc="left", pad=10)
    ax.set_ylabel(u"창 일사량 (kWh/㎡, 계절 합계)", fontsize=9.5, color=GRAY)

draw(axes[0], "cooling_season", u"냉방기 (6~9월): 해를 막을수록 좋아요", False)
draw(axes[1], "heating_season", u"난방기 (11~3월): 해를 받을수록 좋아요", True)
axes[0].legend(loc="upper left", frameon=False, fontsize=9.5, labelcolor=INK)
axes[1].legend(loc="upper right", frameon=False, fontsize=9.5, labelcolor=INK)

fig.text(0.05, 0.955, u"네이버 1784 외피 반응형 루버: 외부 방식별 창 일사량 비교 (9층, 방향별)", fontsize=18, fontweight="bold", color=INK, va="top")
fig.text(0.05, 0.905, u"막대 위 %는 시공 전 대비 줄어든 비율이에요. 냉방기는 많이 줄수록, 난방기는 덜 줄수록(또는 겨울 모드로 되찾을수록) 좋아요.", fontsize=10.5, color=GRAY, va="top")
fig.text(0.05, 0.868, u"북면은 직달 일사가 거의 없어 날이 항상 열려 있어서 고정과 반응형이 같아요.", fontsize=10.5, color=GRAY, va="top")
fig.text(0.05, 0.105, u"방법: Radiance 3D 날(폭 300·간격 350·반사율 0.5, 두께 없음), 창 뒤 벽 반사율 0.3, 주변 건물 325개 그림자 반영, 서울(성남) TMY 월평균 시간별 일사량, 월별 대표일(15일)×그달 일수로 계절 합산.\n"
         u"9층 기준 면 중앙 한 구역의 값이에요. 날 회전: 대표일 컷오프(춘분 30°·하지 60°·추분 45°·동지 30°)를 날짜로 이어 계산, 해 고도 5° 미만은 열림.", fontsize=8.8, color=GRAY, va="top")
fig.text(0.05, 0.04, u"겨울 모드 켬: 난방이 필요한 달(11~3월)에 해와 나란히 눕히고 눈부심 시각만 0°(혼합 규칙). 끔: 겨울에도 규칙대로 닫음. 외부 고정형은 날 0° 고정이에요.", fontsize=8.8, color=GRAY, va="top")
for _t in list(fig.texts): _t.remove()
fig.subplots_adjust(top=0.86, bottom=0.12)
fig.savefig("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/1784_Comparison.png", facecolor=BG)
print("saved")
for f in ORDER:
    for s in ("cooling_season", "heating_season"):
        b = val(fo, f, s, KEY["bare"]); x = val(fo, f, s, KEY["fix"]); r = val(fo, f, s, KEY["rea"]); w = val(fn, f, s, KEY["rea"])
        print(f, s, "bare %.0f fix %.0f (%.0f%%) rea %.0f (%.0f%%) winter %.0f (%.0f%%)" % (b, x, 100 * (b - x) / b, r, 100 * (b - r) / b, w, 100 * (b - w) / b))
