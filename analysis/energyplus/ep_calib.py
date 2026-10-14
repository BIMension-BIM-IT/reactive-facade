# -*- coding: utf-8 -*-
# EnergyPlus 블라인드 모델의 날 폭 · 반사율을 조정해서, 창 일사 감소율이 Radiance와 같아지게 맞춰요(보정).
import io, json, math, itertools, sys, os
import ep_blind as b
import ep_cases as c
import ep_v9 as v

DIM = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
COOL, HEAT = (6, 7, 8, 9), (11, 12, 1, 2, 3)
# Radiance 목표(시공 전 대비 창 일사 감소율 %, solar_result): face -> (냉방기 고정, 냉방기 규칙, 난방기 고정, 난방기 규칙, 난방기 겨울모드)
TARGET = {"S": (48, 65, 40, 78, 17), "W": (32, 70, 56, 62, 34)}
WIN = {"S": "CornerRoom_Back_Glz0", "W": "CornerRoom_Left_Glz0"}

def month_sum(vals, months):
    return sum(vals[mo - 1] for mo in months)

def season_sol(out, face, months):
    key = [k for k in out["sol_m"] if k.upper() == WIN[face].upper()]
    return month_sum(out["sol_m"][key[0]], months)

def blind_text(width, refl, av, fixed_open=False):
    t = b.blind_idf(c.FW, av)
    t = t.replace(" 0.35, !- separation", " %.3f, !- separation" % width)
    t = t.replace(" 0.0, 0.5, 0.5, !- beam tx, front refl, back refl", " 0.0, %.2f, %.2f, !- beam tx, front refl, back refl" % (refl, refl))
    t = t.replace(" 0.0, 0.5, 0.5, !- diffuse tx, front, back", " 0.0, %.2f, %.2f, !- diffuse tx, front, back" % (refl, refl))
    return t

def run_all(width, refl, tag):
    idf = b.base_idf()
    res = {}
    res["bare"] = b.run_text(idf, "k_bare")
    avs = {"fixed0": {f: c.angle_vals(f, -1, fixed=True) for f in ("S", "W")},
           "rule": {f: c.angle_vals(f, -1) for f in ("S", "W")},
           "winter": {f: v.av_v9(f) for f in ("S", "W")}}
    for k, av in avs.items():
        res[k] = b.run_text(idf + "\n" + blind_text(width, refl, av), "k_%s_%s" % (k, tag))
    return res

def reductions(res):
    out = {}
    for f in ("S", "W"):
        bc, bh = season_sol(res["bare"], f, COOL), season_sol(res["bare"], f, HEAT)
        red = lambda k, m, base: 100.0 * (1 - season_sol(res[k], f, m) / base)
        out[f] = (red("fixed0", COOL, bc), red("rule", COOL, bc), red("fixed0", HEAT, bh), red("rule", HEAT, bh), red("winter", HEAT, bh))
    return out

def err(red):
    return sum((red[f][i] - TARGET[f][i]) ** 2 for f in ("S", "W") for i in range(5))

if __name__ == "__main__":
    best = None
    grid = [(w, r) for w in (0.35, 0.32, 0.30) for r in (0.4, 0.3, 0.2)]
    for w, r in grid:
        res = run_all(w, r, "s%d_r%d" % (round(w * 100), round(r * 100)))
        red = reductions(res)
        e = err(red)
        print("sep=%.2f r=%.1f err=%.0f  S %s  W %s" % (w, r, e, [round(x) for x in red["S"]], [round(x) for x in red["W"]]), flush=True)
        if best is None or e < best[0]: best = (e, w, r, red, res)
    e, w, r, red, res = best
    print("BEST sep=%.2f r=%.1f err=%.0f" % (w, r, e))
    print("target S %s W %s" % (TARGET["S"], TARGET["W"]))
    json.dump({"width": w, "refl": r, "err": e}, io.open(b.BASE + "ep_calib_best.json", "w"))
    out = {}
    for k in ("bare", "fixed0", "rule", "winter"):
        x = res[k]
        out[k] = {"cool_kWh_m2": x["cool"] / b.AREA, "heat_kWh_m2": x["heat"] / b.AREA}
        print(k, "냉방 %.1f 난방 %.1f 합 %.1f 보일러 %.1f 열펌프 %.1f" % (out[k]["cool_kWh_m2"], out[k]["heat_kWh_m2"], out[k]["cool_kWh_m2"] + out[k]["heat_kWh_m2"],
              out[k]["cool_kWh_m2"] / 3.5 + out[k]["heat_kWh_m2"] / 0.9, out[k]["cool_kWh_m2"] / 3.5 + out[k]["heat_kWh_m2"] / 3.0))
    json.dump(out, io.open(b.BASE + "ep_calib_results.json", "w", encoding="utf-8"), ensure_ascii=False, indent=2)
