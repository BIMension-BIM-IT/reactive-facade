# -*- coding: utf-8 -*-
# EnergyPlus 외부 수직 블라인드(슬랫 각도 스케줄)로 루버를 직접 모델링
import io, os, re, csv, json, sys, time, collections
from ladybug.sql import SQLiteResult
from honeybee_energy.run import run_idf

BASE = "C:/Users/User/Desktop/bim-it/TEAM PJT/1012/EnergyPlus/"
D = "C:/Users/User/Desktop/bim-it/TEAM PJT/1008/"
EPW = "C:/Users/User/Desktop/bim-it/TEAM PJT/EPW/KOR_KG_Seoul-Seongnam.AP.471110_TMYx.epw"
DIM = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
AREA = 15.75 * 13.8

def base_idf():
    return io.open(BASE + "bare/in.idf", encoding="utf-8").read()

def window_names(idf):
    names = re.findall(r"FenestrationSurface:Detailed,\s*\n\s*([^,]+),", idf)
    return names

def window_dirs(idf, names):
    """창 이름 -> 'S' 또는 'W' (표면 이름의 방향이 아니라 좌표로 판별)"""
    out = {}
    for n in names:
        m = re.search(r"FenestrationSurface:Detailed,\s*\n\s*%s,.*?\n(?:.*?\n){0,12}?\s*([-\d.]+),\s*([-\d.]+),\s*([-\d.]+),\s*!- Vertex 1" % re.escape(n), idf, re.S)
        out[n] = None
    return out

def compact(name, month_vals, ltype="Any Number"):
    """month_vals: {month: [24 values]} -> Schedule:Compact"""
    s = "Schedule:Compact,\n %s, !- name\n %s, !- type limits\n" % (name, ltype)
    for m in range(1, 13):
        s += " Through: %d/%d,\n For: AllDays,\n" % (m, DIM[m - 1])
        vals = month_vals[m]
        for h in range(24):
            last = (m == 12 and h == 23)
            s += " Until: %02d:00, %.3f%s\n" % (h + 1, vals[h], ";" if last else ",")
    return s

def blind_idf(face_windows, angle_vals, open_angle=0.0):
    txt = "ScheduleTypeLimits,\n Any Number;\n\n"
    txt += "WindowMaterial:Blind,\n LouverBlade, !- name\n Vertical, !- orientation\n 0.30, !- width\n 0.35, !- separation\n 0.001, !- thickness\n 0.0, !- angle\n 160, !- conductivity\n"
    txt += " 0.0, 0.5, 0.5, !- beam tx, front refl, back refl\n 0.0, 0.5, 0.5, !- diffuse tx, front, back\n 0.0, 0.5, 0.5, !- vis beam tx, front, back\n 0.0, 0.5, 0.5, !- vis diffuse tx, front, back\n"
    txt += " 0.0, 0.9, 0.9, !- ir tx, front emis, back emis\n 0.3, 1.0, 1.0, 1.0, 1.0, !- distance, top, bottom, left, right\n 0, 180; !- min, max angle\n\n"
    txt += "Schedule:Constant, AlwaysOn, , 1;\n\n"
    for face, wins in face_windows.items():
        sname = "SlatAngle_%s" % face
        txt += compact(sname, angle_vals[face]) + "\n"
        txt += "WindowShadingControl,\n Louver_%s, !- name\n CornerRoom, !- zone\n 1, !- sequence\n ExteriorBlind, !- type\n , !- construction with shading\n OnIfScheduleAllows, !- control type\n AlwaysOn, !- schedule\n , !- setpoint\n Yes, !- is scheduled\n No, !- glare\n LouverBlade, !- material\n ScheduledSlatAngle, !- slat control\n %s, !- slat schedule\n , !- setpoint 2\n , !- daylighting\n Sequential, !- multiple surface control\n" % (face, sname)
        txt += ",\n".join(" " + w for w in wins) + ";\n\n"
    return txt

def run_text(idf, tag, epw=None, extra=''):
    idf = idf + extra
    folder = BASE + "blind_" + tag
    os.makedirs(folder, exist_ok=True)
    path = folder + "/in.idf"
    io.open(path, "w", encoding="utf-8").write(idf)
    sql, zsz, rdd, html, err = run_idf(path, epw or EPW, silent=True)
    r = SQLiteResult(sql)
    def tot(name, months=None):
        o = 0.0
        for c in r.data_collections_by_output_name(name):
            o += sum(v for v, d in zip(c.values, c.datetimes) if months is None or (d if isinstance(d, int) else d.month) in months)
        return o
    def per_window(name, months=None):
        res = []
        for c in r.data_collections_by_output_name(name):
            res.append(sum(v for v, d in zip(c.values, c.datetimes) if months is None or (d if isinstance(d, int) else d.month) in months))
        return res
    sol_m = {}
    for c_ in r.data_collections_by_output_name("Surface Window Transmitted Solar Radiation Energy"):
        sol_m[c_.header.metadata.get("Surface", "")] = list(c_.values)
    inc = {}
    try:
        for c_ in r.data_collections_by_output_name("Surface Outside Face Incident Solar Radiation Rate per Area"):
            inc[c_.header.metadata.get("Surface", "")] = list(c_.values)
    except Exception:
        pass
    out = {"inc": inc, "sol_m": sol_m, "cool": tot("Zone Ideal Loads Supply Air Total Cooling Energy"), "heat": tot("Zone Ideal Loads Supply Air Total Heating Energy"),
           "sol_win": per_window("Surface Window Transmitted Solar Radiation Energy"),
           "sol_cool": per_window("Surface Window Transmitted Solar Radiation Energy", (6, 7, 8, 9)),
           "sol_heat": per_window("Surface Window Transmitted Solar Radiation Energy", (12, 1, 2))}
    return out

if __name__ == "__main__":
    idf = base_idf()
    names = window_names(idf)
    print("windows:", names)
