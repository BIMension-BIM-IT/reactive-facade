# -*- coding: utf-8 -*-
# 컷오프(해와 입면의 방위차 한계)를 0~90°로 넓게 바꿔 가며 효과와 비용(닫힌 시간, 난방 손실)을 비교
import io, os, sys, math, glob, time, csv
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
CUTS = [0, 15, 30, 45, 60, 75, 90]

def wrap(d): return (d + 180.0) % 360.0 - 180.0
def full_rule(alt, th):
    a = max(0.0, math.degrees(math.asin(min(1.0, 350.0 * math.cos(math.radians(th)) / 300.0))) - abs(th))
    return a if th >= 0 else -a

if __name__ == "__main__":
    t0 = time.time()
    epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
    sp = Sunpath.from_location(epw.location)
    jobs, meta = [], {}
    for mo in MONTHS:
        prof = lm.mavg_all(mo)
        for h in range(24):
            dn, df = prof[h]
            if dn + df <= 0.5: continue
            s = sp.calculate_sun(mo, 15, h + 0.5)
            if s.altitude <= 0: continue
            for fk, nvec, fn in FACES:
                th = wrap(s.azimuth - FACE_AZ[fk])
                meta[(mo, h, fk)] = (s.altitude, th)
                jobs.append((mo, 15, h, dn, df, fk, nvec, None, "none_ctx@x"))
                jobs.append((mo, 15, h, dn, df, fk, nvec, 0.0, "fix0_ctx@x"))
                if s.altitude >= 5.0 and abs(th) < 90.0:
                    jobs.append((mo, 15, h, dn, df, fk, nvec, full_rule(s.altitude, th), "rule_ctx@x"))
    print("jobs:", len(jobs), flush=True)
    W = {}
    with ProcessPoolExecutor(max_workers=6) as ex:
        for k, (job, w) in enumerate(ex.map(lm.one, jobs, chunksize=8)):
            mo, da, h, dn, df, fk, nvec, phi, tag = job
            W[(mo, h, fk, tag.split("@")[0])] = w
            if k % 200 == 0: print("progress", k, "/", len(jobs), "t=%.0fs" % (time.time() - t0), flush=True)
    rows = [u"cutoff,face,cool_reduction_pct,heat_reduction_pct,closed_hours_cool,closed_hours_heat"]
    def evalcut(face, cutfn):
        out = {}
        for sname, months in (("cool", COOL), ("heat", HEAT)):
            bare = tot = 0.0; ch = 0.0
            for mo in months:
                for h in range(24):
                    if (mo, h, face) not in meta: continue
                    alt, th = meta[(mo, h, face)]
                    f = DIM[mo] / 1000.0
                    bare += W[(mo, h, face, "none_ctx")] * f
                    c = cutfn(mo)
                    closed = alt >= 5.0 and abs(th) < 90.0 and abs(th) <= c and (mo, h, face, "rule_ctx") in W
                    tot += W[(mo, h, face, "rule_ctx" if closed else "fix0_ctx")] * f
                    if closed: ch += DIM[mo]
            out[sname] = (100 * (1 - tot / bare), ch)
        return out
    results = {}
    for c in CUTS + ["ours"]:
        for fk, n_, fn in FACES:
            cutfn = (lambda mo, c=c: rn.cutoff(mo, 15)) if c == "ours" else (lambda mo, c=c: float(c))
            r = evalcut(fk, cutfn)
            results[(c, fk)] = r
            rows.append(u"%s,%s,%.0f,%.0f,%.0f,%.0f" % (c, fk, r["cool"][0], r["heat"][0], r["cool"][1], r["heat"][1]))
    io.open(OUT + u"Cutoff_Sweep.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
    for c in CUTS + ["ours"]:
        print(c, {fk: (round(results[(c, fk)]["cool"][0]), round(results[(c, fk)]["heat"][0]), round(results[(c, fk)]["cool"][1] + results[(c, fk)]["heat"][1])) for fk, _, _ in FACES})
    print("done %.0fs" % (time.time() - t0), flush=True)
