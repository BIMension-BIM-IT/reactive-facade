# -*- coding: utf-8 -*-
# 수평 루버 겨울 모드(혼합 규칙)의 Radiance 검증: 난방기(11~3월) 창면 일사를 시공 전 / 고정 0° / 규칙(닫힘) / 겨울 모드(해와 나란히 + 눈부심 0°)로 비교
import io, os, math, glob, csv, time, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from concurrent.futures import ProcessPoolExecutor
import horiz_radiance as H
from ladybug.epw import EPW
from ladybug.sunpath import Sunpath

D = H.D
OUT = H.OUT
MONTHS = (11, 12, 1, 2, 3)
DIM = {1: 31, 2: 28, 3: 31, 11: 30, 12: 31}
ALT_THR, GLARE, CAP = 20.0, 60.0, 75.0


def winter_tilt(alt, azd):
    """LouverRule.WinterHorizontal와 같은 식: 바깥 끝이 아래로 + (해와 나란히 = -beta)"""
    if alt <= 0 or abs(azd) > CAP:
        return 0.0
    if alt < ALT_THR and abs(azd) <= GLARE:
        return 0.0
    beta = math.degrees(math.atan2(math.tan(math.radians(alt)), math.cos(math.radians(azd))))
    return -min(90.0, beta)


if __name__ == "__main__":
    t0 = time.time()
    epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
    dni = list(epw.direct_normal_radiation.values); dhi = list(epw.diffuse_horizontal_radiation.values)
    sp = Sunpath.from_location(epw.location)
    jobs = []
    meta = {}
    for mo in MONTHS:
        prof = H.mavg_all(mo, dni, dhi)
        for h in range(24):
            dn, df = prof[h]
            if dn + df <= 0.5:
                continue
            s = sp.calculate_sun(mo, 15, h + 0.5)
            alt, azs = s.altitude, s.azimuth
            if alt <= 0:
                continue
            for fk, n in H.FAC:
                if fk == "N":
                    continue
                azd = H.wrap180(azs - H.FACE_AZ[fk])
                jobs.append((mo, 15, h, dn, df, fk, n, 0.0, "fix0h_ctx"))
                wt = winter_tilt(alt, azd)
                jobs.append((mo, 15, h, dn, df, fk, n, wt, "winterh_ctx"))
                meta[(mo, fk, h)] = (alt, azs, azd, wt)
                if alt >= 5.0 and abs(azd) < 90.0:
                    beta, tilt = H.rule_angle(alt, azd)
                    jobs.append((mo, 15, h, dn, df, fk, n, tilt, "closedh_ctx"))
    print("jobs:", len(jobs), flush=True)
    res = {}
    with ProcessPoolExecutor(max_workers=6) as ex:
        for k, (job, w) in enumerate(ex.map(H.one, jobs, chunksize=6)):
            mo, da, h, dn, df, fk, n, tilt, tag = job
            res[(mo, fk, tag, h)] = w
            if k % 100 == 0:
                print("progress", k, "/", len(jobs), "t=%.0fs" % (time.time() - t0), flush=True)
    rows = [u"month,face,tag,hour,W_m2"]
    for (mo, fk, tag, h), w in sorted(res.items()):
        rows.append(u"%d,%s,%s,%d,%.3f" % (mo, fk, tag, h, w))
    io.open(OUT + u"Horizontal_Winter_Radiance.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")

    # 시공 전(none_ctx): 수직 계산 때 쓴 같은 창면 값
    bare = {}
    for fn in ("Solar_Result_Hourly_Radiance.csv", "Solar_Result_Hourly_Radiance_NovMar.csv"):
        for r in csv.DictReader(io.open(D + fn, encoding="utf-8-sig")):
            if r["tag"] == "none_ctx":
                bare[(int(r["month"]), r["face"], int(r["hour"]))] = float(r["W_m2"])
    out = [u"face,bare_kWh_m2,fixed0_kWh_m2,rule_closed_kWh_m2,winter_kWh_m2,fixed_reduction_pct,rule_reduction_pct,winter_reduction_pct"]
    print("\n난방기(11~3월) 수평 루버, 9층 창면 일사(kWh/㎡). 감소율 = 시공 전 대비 줄어든 비율")
    for fk in ("E", "S", "W"):
        tot = {"bare": 0.0, "fix0h_ctx": 0.0, "closedh_ctx": 0.0, "winterh_ctx": 0.0}
        for mo in MONTHS:
            for h in range(24):
                if (mo, fk, h) not in meta:
                    continue
                f = DIM[mo] / 1000.0
                tot["bare"] += bare.get((mo, fk, h), 0.0) * f
                for tag in ("fix0h_ctx", "winterh_ctx"):
                    tot[tag] += res[(mo, fk, tag, h)] * f
                tot["closedh_ctx"] += res.get((mo, fk, "closedh_ctx", h), res[(mo, fk, "fix0h_ctx", h)]) * f
        b = tot["bare"]
        pr = lambda v: 100.0 * (b - v) / b
        print("%s: bare %.0f | 고정 %.0f (%.0f%%) | 규칙(닫힘) %.0f (%.0f%%) | 겨울 모드 %.0f (%.0f%%)" % (
            fk, b, tot["fix0h_ctx"], pr(tot["fix0h_ctx"]), tot["closedh_ctx"], pr(tot["closedh_ctx"]), tot["winterh_ctx"], pr(tot["winterh_ctx"])))
        out.append(u"%s,%.1f,%.1f,%.1f,%.1f,%.0f,%.0f,%.0f" % (fk, b, tot["fix0h_ctx"], tot["closedh_ctx"], tot["winterh_ctx"], pr(tot["fix0h_ctx"]), pr(tot["closedh_ctx"]), pr(tot["winterh_ctx"])))
    io.open(OUT + u"Horizontal_Winter_Summary.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(out) + u"\r\n")
    print("total %.0fs" % (time.time() - t0))
