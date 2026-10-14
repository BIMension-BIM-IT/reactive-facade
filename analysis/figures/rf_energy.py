# -*- coding: utf-8 -*-
import io, json, csv
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager as fm
for name in ("Malgun Gothic", "맑은 고딕"):
    if any(name == f.name for f in fm.fontManager.ttflist):
        plt.rcParams["font.family"] = name; break
plt.rcParams["axes.unicode_minus"] = False
R = "C:/Users/User/Desktop/bim-it/TEAM PJT/1012/"
ep = json.load(io.open(R + "EnergyPlus/ep_blind_results_v9.json", encoding="utf-8"))
est = {r["case"]: r for r in csv.DictReader(io.open(R + "Energy_Winter_Strategies_v9.csv", encoding="utf-8-sig"))}
cases = [("bare", "bare", u"시공 전"), ("fixed0", "fixed", u"외부 고정 0°"), ("reactive", "rule", u"반응형\n겨울 모드 끔"), ("reactive_winter_v9", "C", u"반응형\n겨울 모드 켬")]
COP_C, EFF, COP_H = 3.5, 0.9, 3.0
def vals(src, k, mode):
    if src == "ep":
        c, h = ep[k]["cool_kWh_m2"], ep[k]["heat_kWh_m2"]
    else:
        c, h = float(est[k]["cool_kWh_m2"]), float(est[k]["heat_mid"])
    return {"load": c + h, "boiler": c / COP_C + h / EFF, "hp": c / COP_C + h / COP_H}[mode]
INK, GRAY, BG = "#0A1317", "#5D6C7B", "#FFFFFF"
fig, axes = plt.subplots(1, 3, figsize=(19, 7.4), dpi=105, facecolor=BG, gridspec_kw={"wspace": 0.2})
fig.subplots_adjust(left=0.045, right=0.99, top=0.76, bottom=0.25)
rows = [u"case,kind,load,boiler,heatpump"]
for ax, (mode, title, ymax) in zip(axes, [("load", u"냉난방 열 부하", 125), ("boiler", u"에너지: 난방 가스 보일러(효율 0.9)", 70), ("hp", u"에너지: 난방 열펌프(COP 3.0)", 40)]):
    ax.set_facecolor(BG)
    for i, (ek, sk, lab) in enumerate(cases):
        b0 = vals("ep", "bare", mode)
        for j, (src, key, col, nm) in enumerate((("est", sk, "#9AA6B2", u"추정(민감도 + Radiance)"), ("ep", ek, "#0064E0", u"EnergyPlus 직접 계산(블라인드 모델)"))):
            v = vals(src, key, mode)
            x = i + (j - 0.5) * 0.38
            ax.bar(x, v, width=0.34, color=col, zorder=3, label=nm if i == 0 else None)
            ax.text(x, v + ymax * 0.012, u"%.1f" % v + (u"\n(%+.0f%%)" % (100 * (v / vals(src, "bare" if src == "ep" else "bare", mode) - 1)) if ek != "bare" else u""), ha="center", va="bottom", fontsize=8.6, color=INK)
    ax.set_xticks(range(4)); ax.set_xticklabels([l for _, _, l in cases], fontsize=9.4, color=INK)
    ax.set_title(title, fontsize=12.5, fontweight="bold", color=INK, loc="left", pad=10)
    ax.yaxis.grid(True, color="#DEE3E9", zorder=0); ax.tick_params(axis="y", labelsize=9, colors=GRAY, length=0); ax.tick_params(axis="x", length=0)
    for s_ in ("top", "right", "left"): ax.spines[s_].set_visible(False)
    ax.set_ylim(0, ymax); ax.set_ylabel(u"kWh/㎡·년", fontsize=9.5, color=GRAY)
axes[0].legend(frameon=False, fontsize=9.5, loc="upper right")
for ek, sk, lab in cases:
    rows.append(u"%s,estimate,%.1f,%.1f,%.1f" % (ek, vals("est", sk, "load"), vals("est", sk, "boiler"), vals("est", sk, "hp")))
    rows.append(u"%s,energyplus,%.1f,%.1f,%.1f" % (ek, vals("ep", ek, "load"), vals("ep", ek, "boiler"), vals("ep", ek, "hp")))
io.open("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Energy_Compare.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
fig.text(0.045, 0.965, u"에너지: EnergyPlus 직접 계산과 추정 비교 (서남쪽 모서리 방)", fontsize=19, fontweight="bold", color=INK, va="top")
fig.text(0.045, 0.912, u"두 방법 모두 같은 결론이에요: 겨울 모드를 켜면 시공 전보다 열 부하가 줄고, 열펌프에서는 확실히 줄고 가스 보일러에서는 비슷해요. 겨울에도 규칙대로 닫으면 보일러에서 크게 불리해요.", fontsize=11, color=GRAY, va="top")
fig.text(0.045, 0.872, u"EnergyPlus 블라인드 모델은 산란광 차단을 Radiance보다 적게 계산해서 날의 효과(냉방 감소, 난방 손실)가 모두 작게 나와요. 그래서 막대 크기보다 순서와 경향을 보는 게 맞아요.", fontsize=11, color=GRAY, va="top")
foot = (u"방법: EnergyPlus 25.2(Honeybee), 서울(성남) TMY 연간, 서남쪽 모서리 방(15.75×13.8 m, 통창), 로이 복층(U 1.9, SHGC 0.40, 가정), 창 바깥 수직 블라인드(폭 300·간격 350·반사율 0.5)의 각도를 시간별로 지정.\n"
        u"반응형 규칙 각도는 9층 주변 건물 그림자 반영 스케줄이고, 겨울 모드 켬은 11~3월에 해와 나란히 + 눈부심 시각만 0°(혼합 규칙)예요. 냉방 COP 3.5, 난방 보일러 0.9 / 열펌프 3.0은 가정값이에요.\n"
        u"3D 날 모델의 연간 계산은 시간이 너무 오래 걸려서(사이징 단계에서 멈춤) 블라인드 모델로 대신했어요. 추정은 EnergyPlus 시공 전 모델의 민감도에 Radiance 일사 감소율을 곱한 값이에요.")
fig.text(0.045, 0.18, foot, fontsize=8.8, color=GRAY, va="top", linespacing=1.6)
for _t in list(fig.texts): _t.remove()
fig.subplots_adjust(top=0.86, bottom=0.12)
fig.savefig("C:/Users/User/Desktop/bim-it/TEAM PJT/1013/Report_Figures/Energy_Compare.png", facecolor=BG)
print(open(R + "Energy_EnergyPlus_vs_Estimate_v9.csv", encoding="utf-8-sig").read())
