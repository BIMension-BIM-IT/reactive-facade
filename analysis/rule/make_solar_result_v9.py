# -*- coding: utf-8 -*-
# 웹이 받는 solar_result JSON v9: 겨울 모드 끔 = 연중 규칙대로 / 켬 = 난방이 필요한 달(11~3월)에 혼합 규칙(해와 나란히 + 눈부심 구간 0°)
import io, csv, json, math, collections, glob
from ladybug.sunpath import Sunpath
from ladybug.epw import EPW

D = "C:/Users/User/Desktop/bim-it/TEAM PJT/1008/"
SC = "C:/Users/User/AppData/Local/Temp/claude/C--Users-User-Desktop-bim-it/728f4bb3-c9cf-4681-8370-f1c71c2fd85e/scratchpad/"
R = "C:/Users/User/Desktop/bim-it/TEAM PJT/1012/"
DIM = {1: 31, 2: 28, 3: 31, 6: 30, 7: 31, 8: 31, 9: 30, 11: 30, 12: 31}
COOL, HEAT = (6, 7, 8, 9), (11, 12, 1, 2, 3)
FACES = ["E", "S", "W", "N"]
FAZ = {"E": 90.0, "S": 180.0, "W": 270.0, "N": 0.0}
ALT_THR, GLARE_DTH, CAP = 20.0, 60.0, 75.0
CUTS = [30.0, 60.0, 45.0, 30.0]
CUM = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
def wrap(d): return (d + 180.0) % 360.0 - 180.0
def cutoff(mo, da):
    n = CUM[mo - 1] + da
    anchor = [80, 172, 266, 355, 80 + 365]; val = CUTS + [CUTS[0]]
    if n < anchor[0]: n += 365
    for i in range(4):
        if anchor[i] <= n <= anchor[i + 1]:
            return val[i] + (val[i + 1] - val[i]) * (n - anchor[i]) / float(anchor[i + 1] - anchor[i])
    return CUTS[3]

# ---- Radiance 시간별 창면 일사 (월별 15일)
W = collections.defaultdict(float)
for fn in ("Solar_Result_Hourly_Radiance.csv", "Solar_Result_Hourly_Radiance_NovMar.csv"):
    for r in csv.DictReader(io.open(D + fn, encoding="utf-8-sig")):
        W[(int(r["month"]), r["face"], r["tag"], int(r["hour"]))] = float(r["W_m2"])
hours = collections.defaultdict(list)
for (mo, f, tag, h) in list(W):
    if tag == "none_ctx": hours[(mo, f)].append(h)

epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
sp = Sunpath.from_location(epw.location)
def sun_at(mo, hh):
    s = sp.calculate_sun(mo, 15, hh)
    return s.altitude, s.azimuth

def pick_hybrid(mo, f, h):
    a, az = sun_at(mo, h + 0.5)
    dth = wrap(az - FAZ[f])
    if abs(dth) > CAP: return "fix0_ctx"
    if a < ALT_THR and abs(dth) <= GLARE_DTH: return "fix0_ctx"
    return "par_ctx"
def season(face, months, picker):
    tot = 0.0
    for mo in months:
        for h in hours[(mo, face)]:
            tot += W[(mo, face, picker(mo, face, h), h)] * DIM[mo] / 1000.0
    return tot
const = lambda tag: (lambda mo, f, h: tag)

# ---- 닫힘 시간 · 날 회전량 (월별 15일, 10분 간격, 9층)
sched = collections.defaultdict(list)
for r in csv.DictReader(io.open(D + "Floor9_Monthly_Schedule.csv", encoding="utf-8-sig")):
    mo = int(r["day"].split("/")[0]); h, m = r["time"].split(":")
    sched[(mo, r["face"])].append((int(h) * 60 + int(m), r["final_state"] == "closed", float(r["final_angle"]) if r["final_state"] == "closed" else 0.0))

def rule_series(mo, face):
    """주변 건물 그림자 없이 규칙만으로 만든 10분 간격 (분, 닫힘, 각도)"""
    out = []
    cut = cutoff(mo, 15)
    for mins in range(5 * 60, 20 * 60 + 1, 10):
        a, az = sun_at(mo, mins / 60.0)
        if a < 5.0 or face == "N":
            out.append((mins, False, 0.0)); continue
        th = wrap(az - FAZ[face])
        if abs(th) > cut:
            out.append((mins, False, 0.0)); continue
        v = max(0.0, math.degrees(math.asin(min(1.0, 350.0 * math.cos(math.radians(th)) / 300.0))) - abs(th))
        out.append((mins, True, v if th >= 0 else -v))
    return out
def hybrid_series(mo, face):
    out = []
    for mins in range(5 * 60, 20 * 60 + 1, 10):
        a, az = sun_at(mo, mins / 60.0)
        ang = 0.0
        if a > 0.0 and face != "N":
            th = wrap(az - FAZ[face])
            if abs(th) <= CAP and not (a < ALT_THR and abs(th) <= GLARE_DTH):
                ang = -th
        out.append((mins, False, ang))
    return out
def stats(series, unwrap):
    hrs = sum(1 for t, c, a in series if c) / 6.0
    cur = None; rot = 0.0
    for t, c, a in series:
        if cur is None: cur = a
        else:
            d = ((a - cur + 90.0) % 180.0) - 90.0 if unwrap else (a - cur)
            rot += abs(d); cur += d if unwrap else (a - cur)
    return hrs, rot

def closed_and_rotation(face, winter_on):
    hours_c = rot_t = 0.0
    for mo in sorted(DIM):
        if mo in HEAT and winter_on:
            h_, r_ = stats(hybrid_series(mo, face), False)      # 겨울 모드: 닫힘 없음, 해 방향을 따라 회전
        elif (mo, face) in sched:
            h_, r_ = stats(sched[(mo, face)], True)             # 그림자 반영한 9층 스케줄
        else:
            h_, r_ = stats(rule_series(mo, face), True)         # 3월·11월: 그림자 없이 규칙만
        hours_c += h_ * DIM[mo]; rot_t += r_ * DIM[mo]
    return hours_c, rot_t

def build(winter_on):
    facs = []
    for f in FACES:
        bare_c, bare_h = season(f, COOL, const("none_ctx")), season(f, HEAT, const("none_ctx"))
        fix_c, fix_h = season(f, COOL, const("fix0_ctx")), season(f, HEAT, const("fix0_ctx"))
        rea_c = season(f, COOL, const("floor_ctx"))
        rea_h = season(f, HEAT, pick_hybrid if winter_on else const("floor_ctx"))
        if f == "N":
            rea_c = fix_c
            rea_h = fix_h
        hrs, rot = closed_and_rotation(f, winter_on)
        facs.append({"id": f,
            "cooling_season": {"bare_kwh_m2_yr": round(bare_c, 1), "internal_reactive_kwh_m2_yr": round(bare_c, 1), "external_fixed_kwh_m2_yr": round(fix_c, 1), "external_reactive_kwh_m2_yr": round(rea_c, 1)},
            "heating_season": {"bare_kwh_m2_yr": round(bare_h, 1), "internal_reactive_kwh_m2_yr": round(bare_h, 1), "external_fixed_kwh_m2_yr": round(fix_h, 1), "external_reactive_kwh_m2_yr": round(rea_h, 1)},
            "closed_hours_yr": round(hrs, 0), "blade_rotation_deg_yr": round(rot, 0)})
    return {"meta": {"weather_source": "KOR_KG_Seoul-Seongnam.AP.471110_TMYx (month-average hourly)", "shading_included": True, "timestep_min": 60, "version": "radiance-9F-0.2",
                     "cooling_months": "6-9", "heating_months": "11-3",
                     "winter_mode": "on: heating months use hybrid rule (parallel to sun; 0 deg when sun altitude < %d deg and azimuth diff <= %d deg, or azimuth diff > %d deg); off: reactive rule all year" % (ALT_THR, GLARE_DTH, CAP)},
            "facades": facs}

out = {}
for name, on in (("winter_on", True), ("winter_off", False)):
    res = build(on)
    io.open(R + "solar_result_%s.json" % name, "w", encoding="utf-8").write(json.dumps(res, ensure_ascii=False, indent=2))
    out[name] = res
rows = ["variant,face,season,bare,external_fixed_0deg,external_reactive,fixed_reduction_pct,reactive_reduction_pct,closed_hours_yr,blade_rotation_deg_yr"]
for name, res in out.items():
    for fc in res["facades"]:
        for sn in ("cooling_season", "heating_season"):
            s = fc[sn]
            rows.append("%s,%s,%s,%.1f,%.1f,%.1f,%.0f,%.0f,%.0f,%.0f" % (name, fc["id"], sn, s["bare_kwh_m2_yr"], s["external_fixed_kwh_m2_yr"], s["external_reactive_kwh_m2_yr"],
                        100 * (s["bare_kwh_m2_yr"] - s["external_fixed_kwh_m2_yr"]) / s["bare_kwh_m2_yr"], 100 * (s["bare_kwh_m2_yr"] - s["external_reactive_kwh_m2_yr"]) / s["bare_kwh_m2_yr"], fc["closed_hours_yr"], fc["blade_rotation_deg_yr"]))
io.open(R + "solar_result_summary.csv", "w", encoding="utf-8-sig", newline="").write("\r\n".join(rows) + "\r\n")
for name in ("winter_on", "winter_off"):
    print("==", name)
    for fc in out[name]["facades"]:
        c, h = fc["cooling_season"], fc["heating_season"]
        print(fc["id"], "cool bare %.0f fixed %.0f react %.0f | heat bare %.0f fixed %.0f react %.0f | closed %.0f h, rot %.0f deg" % (
            c["bare_kwh_m2_yr"], c["external_fixed_kwh_m2_yr"], c["external_reactive_kwh_m2_yr"], h["bare_kwh_m2_yr"], h["external_fixed_kwh_m2_yr"], h["external_reactive_kwh_m2_yr"], fc["closed_hours_yr"], fc["blade_rotation_deg_yr"]))
