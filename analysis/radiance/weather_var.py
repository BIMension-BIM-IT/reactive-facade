# -*- coding: utf-8 -*-
# 날씨 다양성: 월평균 날씨 대신 실제 날(맑은/보통/흐린)로 계산해서 월평균 방식과 비교
import io, os, sys, math, glob, time, csv, datetime
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from concurrent.futures import ProcessPoolExecutor
import louver_radiance_monthly as lm
import radiance_nov_mar as rn
from ladybug.sunpath import Sunpath
from ladybug.epw import EPW

OUT = "C:/Users/User/Desktop/bim-it/TEAM PJT/1013/"
COOL, HEAT = (6, 7, 8, 9), (11, 12, 1, 2, 3)
MONTHS = COOL + HEAT
DIM = {1: 31, 2: 28, 3: 31, 6: 30, 7: 31, 8: 31, 9: 30, 11: 30, 12: 31}
FACE_AZ = {"E": 90.0, "S": 180.0, "W": 270.0}
FACES = [f for f in lm.FAC if f[0] in FACE_AZ]
ALT_THR, GLARE, CAP = 20.0, 60.0, 75.0

def wrap(d): return (d + 180.0) % 360.0 - 180.0

if __name__ == "__main__":
    t0 = time.time()
    epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
    dni = list(epw.direct_normal_radiation.values); dhi = list(epw.diffuse_horizontal_radiation.values)
    sp = Sunpath.from_location(epw.location)
    classes = {}   # (mo) -> list of (class, day, weight, profile)
    for mo in MONTHS:
        n = DIM[mo]; base = lm.OFF[mo - 1] * 24
        days = []
        for d in range(1, n + 1):
            prof = [(dni[base + (d - 1) * 24 + h], dhi[base + (d - 1) * 24 + h]) for h in range(24)]
            days.append((sum(a + b for a, b in prof), d, prof))
        days.sort()
        k = n // 3
        groups = {"cloudy": days[:k], "mid": days[k:n - k], "clear": days[n - k:]}
        lst = []
        for name, g in groups.items():
            rep = g[len(g) // 2]
            lst.append((name, rep[1], len(g), rep[2]))
        lst.append(("avg", 15, n, lm.mavg_all(mo)))
        classes[mo] = lst
    jobs = []
    for mo in MONTHS:
        for cname, day, w, prof in classes[mo]:
            cut = rn.cutoff(mo, day)
            for h in range(24):
                dn, df = prof[h]
                if dn + df <= 0.5: continue
                s = sp.calculate_sun(mo, day, h + 0.5)
                if s.altitude <= 0: continue
                for fk, nvec, fn in FACES:
                    faz = FACE_AZ[fk]
                    dth = wrap(s.azimuth - faz)
                    tag_id = "%s-%d" % (cname, day)
                    jobs.append((mo, day, h, dn, df, fk, nvec, None, "none_ctx@" + tag_id))
                    jobs.append((mo, day, h, dn, df, fk, nvec, 0.0, "fix0_ctx@" + tag_id))
                    jobs.append((mo, day, h, dn, df, fk, nvec, rn.rule(s.altitude, s.azimuth, faz, cut), "rule_ctx@" + tag_id))
                    if mo in HEAT:
                        a = 0.0
                        if abs(dth) <= CAP and not (s.altitude < ALT_THR and abs(dth) <= GLARE):
                            a = -dth
                        jobs.append((mo, day, h, dn, df, fk, nvec, a, "win_ctx@" + tag_id))
    print("jobs:", len(jobs), flush=True)
    rows = [u"month,class,day,face,tag,hour,W_m2"]
    res = {}
    with ProcessPoolExecutor(max_workers=6) as ex:
        for k, (job, w) in enumerate(ex.map(lm.one, jobs, chunksize=8)):
            mo, da, h, dn, df, fk, nvec, phi, tag = job
            t, cid = tag.split("@")
            res[(mo, cid, fk, t, h)] = w
            rows.append(u"%d,%s,%d,%s,%s,%d,%.3f" % (mo, cid, da, fk, t, h, w))
            if k % 200 == 0: print("progress", k, "/", len(jobs), "t=%.0fs" % (time.time() - t0), flush=True)
    io.open(OUT + u"Weather_Variability_Hourly.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
    print("saved hourly; total %.0fs" % (time.time() - t0), flush=True)
