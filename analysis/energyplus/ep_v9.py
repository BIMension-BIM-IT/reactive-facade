# -*- coding: utf-8 -*-
# EnergyPlus 블라인드 모델(간단 모델)을 v9 겨울 모드(11~3월, 혼합 규칙)로 다시 돌려요.
import io, json, math, glob
import ep_blind as b
import ep_cases as c
from ladybug.epw import EPW
from ladybug.sunpath import Sunpath

SIGN = -1
FAZ = {"S": 180.0, "W": 270.0}
ALT_THR, GLARE, CAP = 20.0, 60.0, 75.0
HEAT = (11, 12, 1, 2, 3)
epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
sp = Sunpath.from_location(epw.location)
def wrap(d): return (d + 180.0) % 360.0 - 180.0

def hybrid_vals(face, mo):
    """겨울 모드 켬: 해와 나란히(-theta), 눈부심 시각은 0°. 규칙의 닫힘 각도와 같은 부호 약속(90 + SIGN*각도)을 써요"""
    vals = []
    for h in range(24):
        s = sp.calculate_sun(mo, 15, h + 0.5)
        a = 90.0
        if s.altitude > 0:
            th = wrap(s.azimuth - FAZ[face])
            if abs(th) <= CAP and not (s.altitude < ALT_THR and abs(th) <= GLARE):
                a = max(1.0, min(179.0, 90.0 + SIGN * (-th)))
        vals.append(a)
    return vals

def av_v9(face):
    base = c.angle_vals(face, SIGN)                      # 규칙대로(연중)
    for mo in HEAT:
        base[mo] = hybrid_vals(face, mo)
    return base

if __name__ == "__main__":
    res = {}
    old = json.load(io.open(b.BASE + "ep_blind_results.json", encoding="utf-8"))
    for k in ("bare", "fixed0", "reactive"):
        res[k] = old[k]
    idf = b.base_idf()
    av = {f: av_v9(f) for f in ("S", "W")}
    r = b.run_text(idf + "\n" + b.blind_idf(c.FW, av), "c_reactive_winter_v9")
    res["reactive_winter_v9"] = {"cool_kWh": r["cool"], "heat_kWh": r["heat"], "cool_kWh_m2": r["cool"] / b.AREA, "heat_kWh_m2": r["heat"] / b.AREA}
    json.dump(res, io.open(b.BASE + "ep_blind_results_v9.json", "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    bare = res["bare"]
    for k, v in res.items():
        tot = v["cool_kWh_m2"] + v["heat_kWh_m2"]
        bt = bare["cool_kWh_m2"] + bare["heat_kWh_m2"]
        print("%-20s 냉방 %.1f 난방 %.1f 합 %.1f (%+.0f%%)  보일러 %.1f 열펌프 %.1f" % (k, v["cool_kWh_m2"], v["heat_kWh_m2"], tot, 100 * (tot / bt - 1),
              v["cool_kWh_m2"] / 3.5 + v["heat_kWh_m2"] / 0.9, v["cool_kWh_m2"] / 3.5 + v["heat_kWh_m2"] / 3.0))
