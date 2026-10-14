# -*- coding: utf-8 -*-
# 11월·3월 수직 루버 Radiance (시공 전 / 고정 0° / 해와 나란히 / 규칙대로[주변 건물 그림자 없이 규칙 각도])
import io, math, time, glob
from concurrent.futures import ProcessPoolExecutor
import louver_radiance_monthly as lm
from ladybug.sunpath import Sunpath
from ladybug.location import Location
from ladybug.epw import EPW

D = "C:/Users/User/Desktop/bim-it/TEAM PJT/1008/"
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
def rule(alt, az, faz, cut):
    if faz == 0.0 or alt < 5.0: return 0.0
    th = wrap(az - faz)
    if abs(th) > cut: return 0.0
    a = max(0.0, math.degrees(math.asin(min(1.0, 350.0 * math.cos(math.radians(th)) / 300.0))) - abs(th))
    return a if th >= 0 else -a

if __name__ == "__main__":
    t0 = time.time()
    epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
    sp = Sunpath.from_location(epw.location)
    FACE_AZ = {"E": 90.0, "S": 180.0, "W": 270.0, "N": 0.0}
    jobs = []
    for mo in (3, 11):
        prof = lm.mavg_all(mo)
        cut = cutoff(mo, 15)
        for h in range(24):
            dn, df = prof[h]
            if dn + df <= 0.5: continue
            import datetime
            s = sp.calculate_sun(mo, 15, h + 0.5)
            alt, az = s.altitude, s.azimuth
            if alt <= 0: continue
            for fk, n, fn in lm.FAC:
                faz = FACE_AZ[fk]
                dth = wrap(az - faz)
                par = -dth if abs(dth) < 90.0 else 0.0
                jobs.append((mo, 15, h, dn, df, fk, n, None, "none_ctx"))
                jobs.append((mo, 15, h, dn, df, fk, n, 0.0, "fix0_ctx"))
                jobs.append((mo, 15, h, dn, df, fk, n, par, "par_ctx"))
                jobs.append((mo, 15, h, dn, df, fk, n, rule(alt, az, faz, cut), "floor_ctx"))
    print("jobs:", len(jobs), flush=True)
    rows = [u"month,face,tag,hour,W_m2"]
    with ProcessPoolExecutor(max_workers=5) as ex:
        for k, (job, w) in enumerate(ex.map(lm.one, jobs, chunksize=8)):
            mo, da, h, dn, df, fk, n, phi, tag = job
            rows.append(u"%d,%s,%s,%d,%.3f" % (mo, fk, tag, h, w))
            if k % 80 == 0: print("progress", k, "/", len(jobs), "t=%.0fs" % (time.time() - t0), flush=True)
    io.open(D + u"Solar_Result_Hourly_Radiance_NovMar.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
    print("saved; total %.0fs" % (time.time() - t0), flush=True)
