# -*- coding: utf-8 -*-
# 층별 Radiance (수직 루버, 규칙대로 / 겨울 모드 / 고정 0° / 시공 전) + 보호 유리 유무.
# 층 f의 창은 9층 창 위치에 두고, 주변 건물(ctx)을 (f-9)*4.2 m 만큼 아래로 옮겨서 층 높이를 표현해요.
import io, os, sys, math, glob, time, csv, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from concurrent.futures import ProcessPoolExecutor
import louver_radiance_monthly as lm
import radiance_nov_mar as rn
from ladybug.sunpath import Sunpath
from ladybug.epw import EPW

SC = os.path.dirname(os.path.abspath(__file__)) + os.sep
OUT = "C:/Users/User/Desktop/bim-it/TEAM PJT/1013/"
CTXF = os.path.join(lm.tempfile.gettempdir(), "louver_ctx_floor") + os.sep
FH = 4.2
COOL, HEAT = (6, 7, 8, 9), (11, 12, 1, 2, 3)
MONTHS = COOL + HEAT
DIM = {1: 31, 2: 28, 3: 31, 6: 30, 7: 31, 8: 31, 9: 30, 11: 30, 12: 31}
FACE_AZ = {"E": 90.0, "S": 180.0, "W": 270.0}
FACES = [f for f in lm.FAC if f[0] in FACE_AZ]
ALT_THR, GLARE, CAP = 20.0, 60.0, 75.0
GLASS_T = 0.958          # 보호 유리 가시광 투과율 0.88에 해당하는 Radiance 투과도
GLASS_OFF = 0.70         # 창에서 바깥으로 0.7 m (1784 삼중 외피)

def wrap(d): return (d + 180.0) % 360.0 - 180.0
def full_rule(th):
    a = max(0.0, math.degrees(math.asin(min(1.0, 350.0 * math.cos(math.radians(th)) / 300.0))) - abs(th))
    return a if th >= 0 else -a

def ctx_path(fk, floor):
    p = CTXF + "ctx_%s_f%d.rad" % (fk, floor)
    if not os.path.exists(p):
        os.makedirs(CTXF, exist_ok=True)
        dz = (floor - 9) * FH
        out = lm.run([lm.BIN + "xform.exe", "-t", "0", "0", "%.3f" % (-dz), SC + "ctx_%s.rad" % fk])
        open(p, "wb").write(out)
    return p

def glass_text(n):
    f = (-n[1], n[0])
    pts = []
    for tt, z in ((-30.0, -20.0), (30.0, -20.0), (30.0, 30.0), (-30.0, 30.0)):
        pts.append((f[0] * tt + n[0] * GLASS_OFF, f[1] * tt + n[1] * GLASS_OFF, z))
    s = "void glass pglass 0 0 3 %.3f %.3f %.3f\n\npglass polygon g0\n0\n0\n12\n" % ((GLASS_T,) * 3)
    for p in pts:
        s += "  %.4f %.4f %.4f\n" % p
    return s + "\n"

def meas_points(n):
    f = (-n[1], n[0])
    pts = []
    for i in range(10):
        for z in (1.6, 2.0, 2.4):
            t = -0.5 + (i + 0.5) / 10.0
            pts.append((f[0] * t, f[1] * t, z))
    return pts

def shaded_fraction(fk, n, floor, alt, az):
    """해 방향으로 쏜 선이 주변 건물에 막히는 비율"""
    d = (math.sin(math.radians(az)) * math.cos(math.radians(alt)), math.cos(math.radians(az)) * math.cos(math.radians(alt)), math.sin(math.radians(alt)))
    wd = os.path.join(CTXF, "sh_%s_%d_%d" % (fk, floor, os.getpid())); os.makedirs(wd, exist_ok=True)
    octf = os.path.join(wd, "c.oct")
    if not os.path.exists(octf):
        open(octf, "wb").write(lm.run([lm.BIN + "oconv.exe", ctx_path(fk, floor)], cwd=wd))
    inp = "".join("%.3f %.3f %.3f %.5f %.5f %.5f\n" % (p[0] - n[0] * 0.02, p[1] - n[1] * 0.02, p[2], d[0], d[1], d[2]) for p in meas_points(n))
    out = lm.run([lm.BIN + "rtrace.exe", "-h", "-w", "-oL", "c.oct"], inp=inp.encode(), cwd=wd).decode().split()
    return sum(1 for v in out if float(v) < 1e9) / float(len(out))

def one_floor(job):
    mo, da, hour, dn, df, fk, n, phi, tag, floor, glass = job
    wd = os.path.join(lm.WORK, "fj_%d_%d_%s_%s_%d_%d" % (mo, hour, fk, tag, floor, os.getpid()))
    os.makedirs(wd, exist_ok=True)
    sky = lm.run([lm.BIN + "gendaylit.exe", str(mo), str(da), "%.2f" % (hour + 0.5), "-a", str(lm.LAT), "-o", str(-lm.LON), "-m", str(-15 * lm.TZ),
                  "-W", "%.1f" % dn, "%.1f" % df, "-O", "1"], cwd=wd)
    sc = sky + ("\nskyfunc glow sky_glow 0 0 4 1 1 1 0\nsky_glow source sky 0 0 4 0 0 1 180\n"
                "skyfunc glow ground_glow 0 0 4 1 1 1 0\nground_glow source ground 0 0 4 0 0 -1 180\n\n").encode()
    sc += lm.wall(n).encode()
    if phi is not None:
        sc += lm.scene(n, phi).encode()
    if glass:
        sc += glass_text(n).encode()
    open(os.path.join(wd, "s.rad"), "wb").write(sc)
    octr = lm.run([lm.BIN + "oconv.exe", "s.rad", ctx_path(fk, floor)], cwd=wd)
    open(os.path.join(wd, "s.oct"), "wb").write(octr)
    f = (-n[1], n[0])
    pts = ""
    for p in meas_points(n):
        pts += "%.3f %.3f %.3f %.3f %.3f 0\n" % (p[0], p[1], p[2], n[0], n[1])
    out = lm.run([lm.BIN + "rtrace.exe", "-h", "-I", "-ab", "2", "-ad", "512", "-as", "256", "-aa", "0.2", "-ar", "128", "-lw", "1e-3", "s.oct"],
                 inp=pts.encode(), cwd=wd)
    vals = [float(l.split()[0]) for l in out.decode().strip().splitlines()]
    return (job, sum(vals) / len(vals))

if __name__ == "__main__":
    floors = [int(x) for x in sys.argv[1].split(",")]
    glass_floors = [int(x) for x in sys.argv[2].split(",")] if len(sys.argv) > 2 and sys.argv[2] else []
    t0 = time.time()
    epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
    sp = Sunpath.from_location(epw.location)
    jobs = []
    shade = {}
    for fl in floors:
        for mo in MONTHS:
            prof = lm.mavg_all(mo)
            cut = rn.cutoff(mo, 15)
            for h in range(24):
                dn, df = prof[h]
                if dn + df <= 0.5: continue
                s = sp.calculate_sun(mo, 15, h + 0.5)
                if s.altitude <= 0: continue
                for fk, nvec, fn in FACES:
                    faz = FACE_AZ[fk]; th = wrap(s.azimuth - faz)
                    sf = shaded_fraction(fk, nvec, fl, s.altitude, s.azimuth) if (abs(th) < 90 and s.altitude >= 5) else 1.0
                    shade[(fl, mo, h, fk)] = sf
                    closed = s.altitude >= 5.0 and abs(th) < 90.0 and abs(th) <= cut and sf < 0.5
                    ang_rule = full_rule(th) if closed else 0.0
                    wa = 0.0
                    if s.altitude > 0 and abs(th) <= CAP and not (s.altitude < ALT_THR and abs(th) <= GLARE):
                        wa = -th
                    for g in ([False] + ([True] if fl in glass_floors else [])):
                        sfx = "g" if g else ""
                        jobs.append((mo, 15, h, dn, df, fk, nvec, None, "none%s_ctx" % sfx, fl, g))
                        jobs.append((mo, 15, h, dn, df, fk, nvec, 0.0, "fix0%s_ctx" % sfx, fl, g))
                        if closed: jobs.append((mo, 15, h, dn, df, fk, nvec, ang_rule, "rule%s_ctx" % sfx, fl, g))
                        if mo in HEAT: jobs.append((mo, 15, h, dn, df, fk, nvec, wa, "win%s_ctx" % sfx, fl, g))
    print("jobs:", len(jobs), flush=True)
    rows = [u"floor,month,face,tag,hour,W_m2,shaded_frac"]
    with ProcessPoolExecutor(max_workers=6) as ex:
        for k, (job, w) in enumerate(ex.map(one_floor, jobs, chunksize=8)):
            mo, da, h, dn, df, fk, nvec, phi, tag, fl, g = job
            rows.append(u"%d,%d,%s,%s,%d,%.3f,%.2f" % (fl, mo, fk, tag, h, w, shade.get((fl, mo, h, fk), 1.0)))
            if k % 300 == 0: print("progress", k, "/", len(jobs), "t=%.0fs" % (time.time() - t0), flush=True)
    io.open(OUT + u"Floor_Radiance_Hourly.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
    print("saved; total %.0fs" % (time.time() - t0), flush=True)
