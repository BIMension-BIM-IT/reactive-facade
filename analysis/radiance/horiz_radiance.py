# -*- coding: utf-8 -*-
# 수평 루버(날이 가로로 긴 형태) Radiance 계산: 열림(0°) / 규칙 각도(닫힘) 두 가지를 시간별로 계산해 두고, 컷오프는 나중에 바꿔 가며 합산해요.
import io, os, math, glob, csv, subprocess, time, tempfile, sys
from concurrent.futures import ProcessPoolExecutor
from ladybug.epw import EPW

RAD = r"C:\Program Files\ladybug_tools\radiance"
BIN = RAD + "\\bin\\"
D = "C:/Users/User/Desktop/bim-it/TEAM PJT/1008/"
OUT = "C:/Users/User/Desktop/bim-it/TEAM PJT/1012/Horizontal/"
SC = "C:/Users/User/AppData/Local/Temp/claude/C--Users-User-Desktop-bim-it/728f4bb3-c9cf-4681-8370-f1c71c2fd85e/scratchpad/"
WORK = os.path.join(tempfile.gettempdir(), "louver_rad_horiz")
LAT, LON, TZ = 37.35888, 127.10518, 9
OFF = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
MD = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
FAC = [("E", (1, 0)), ("S", (0, -1)), ("W", (-1, 0)), ("N", (0, 1))]
FACE_AZ = {"E": 90.0, "S": 180.0, "W": 270.0, "N": 0.0}
W, S, OFFS, R_BLADE = 0.30, 0.35, 0.45, 0.5
ZSLAT0 = 0.175            # 첫 날 중심 높이 (층 바닥 위), 간격 0.35, 층마다 12줄

env = dict(os.environ); env["RAYPATH"] = RAD + "\\lib"; env["PATH"] = BIN + ";" + env["PATH"]


def wrap180(d):
    return (d + 180.0) % 360.0 - 180.0


def mavg_all(mo, dni, dhi):
    s, n = OFF[mo - 1] * 24, MD[mo - 1]
    return [(sum(dni[s + k * 24 + h] for k in range(n)) / n, sum(dhi[s + k * 24 + h] for k in range(n)) / n) for h in range(24)]


def rule_angle(alt, azdiff):
    """수평 날의 닫는 각도(도). 바깥쪽 끝이 아래로 내려가는 방향이 +.
    프로파일각 beta = atan(tan(고도)/cos(방위차)),  tilt = asin(s*cos(beta)/w) - beta"""
    beta = math.degrees(math.atan2(math.tan(math.radians(alt)), math.cos(math.radians(azdiff))))
    arg = S * math.cos(math.radians(beta)) / W
    tilt = 90.0 if arg >= 1.0 else math.degrees(math.asin(arg)) - beta
    return beta, max(0.0, min(90.0, tilt))


def poly(name, pts):
    s = "blade polygon %s\n0\n0\n%d\n" % (name, 3 * len(pts))
    for p in pts:
        s += "  %.4f %.4f %.4f\n" % p
    return s + "\n"


def wall(n):
    f = (-n[1], n[0])
    d = 0.02
    pts = []
    for tt, z in ((-6.0, -1.0), (6.0, -1.0), (6.0, 5.0), (-6.0, 5.0)):
        pts.append((f[0] * tt - n[0] * d, f[1] * tt - n[1] * d, z))
    s = "void plastic bwall 0 0 5 0.3 0.3 0.3 0 0\n\nbwall polygon w0\n0\n0\n12\n"
    for p in pts:
        s += "  %.4f %.4f %.4f\n" % p
    return s + "\n"


def scene_h(n, tilt_deg):
    """수평 날: 창을 따라 길게(±6 m), 층 높이 4.2 m 안에 12줄(간격 0.35). 위에 이웃 층 날도 같은 패턴으로 이어져요."""
    f = (-n[1], n[0])
    th = math.radians(tilt_deg)
    s = "void plastic blade 0 0 5 %g %g %g 0 0\n\n" % ((R_BLADE,) * 3)
    for k in range(-12, 24):                      # 아래층~위층까지 이어 붙임(측정 높이는 가운데)
        zc = ZSLAT0 + S * k
        cx, cy = n[0] * OFFS, n[1] * OFFS
        # 코드 방향: 바깥으로 cos(tilt), 아래로 sin(tilt)
        ox, oy, oz = n[0] * math.cos(th) * W / 2, n[1] * math.cos(th) * W / 2, -math.sin(th) * W / 2
        L = 6.0
        p0 = (cx - ox - f[0] * L, cy - oy - f[1] * L, zc - oz)
        p1 = (cx - ox + f[0] * L, cy - oy + f[1] * L, zc - oz)
        p2 = (cx + ox + f[0] * L, cy + oy + f[1] * L, zc + oz)
        p3 = (cx + ox - f[0] * L, cy + oy - f[1] * L, zc + oz)
        s += poly("h%d" % (k + 100), [p0, p1, p2, p3])
    return s


def run(cmd, inp=None, cwd=None):
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=cwd, env=env)
    o, e = p.communicate(inp)
    if p.returncode != 0:
        raise RuntimeError(e.decode("utf-8", "replace")[:300])
    return o


def one(job):
    mo, da, hour, dn, df, fk, n, tilt, tag = job
    wd = os.path.join(WORK, "j_%d_%d_%s_%s_%d" % (mo, hour, fk, tag, os.getpid()))
    os.makedirs(wd, exist_ok=True)
    sky = run([BIN + "gendaylit.exe", str(mo), str(da), "%.2f" % (hour + 0.5), "-a", str(LAT), "-o", str(-LON), "-m", str(-15 * TZ),
               "-W", "%.1f" % dn, "%.1f" % df, "-O", "1"], cwd=wd)
    sc = sky + ("\nskyfunc glow sky_glow 0 0 4 1 1 1 0\nsky_glow source sky 0 0 4 0 0 1 180\n"
                "skyfunc glow ground_glow 0 0 4 1 1 1 0\nground_glow source ground 0 0 4 0 0 -1 180\n\n").encode()
    sc += wall(n).encode()
    sc += scene_h(n, tilt).encode()
    open(os.path.join(wd, "s.rad"), "wb").write(sc)
    octr = run([BIN + "oconv.exe", "s.rad", SC + "ctx_%s.rad" % fk], cwd=wd)
    open(os.path.join(wd, "s.oct"), "wb").write(octr)
    f = (-n[1], n[0])
    pts = ""
    for i in range(5):                          # 옆으로 5곳 x 높이 10곳(날 간격 한 칸을 고르게)
        for j in range(10):
            t = -0.5 + (i + 0.5) / 5.0
            z = 1.575 + (j + 0.5) * S / 10.0
            pts += "%.3f %.3f %.3f %.3f %.3f 0\n" % (f[0] * t, f[1] * t, z, n[0], n[1])
    out = run([BIN + "rtrace.exe", "-h", "-I", "-ab", "2", "-ad", "512", "-as", "256", "-aa", "0.2", "-ar", "128", "-lw", "1e-3", "s.oct"],
              inp=pts.encode(), cwd=wd)
    vals = [float(l.split()[0]) for l in out.decode().strip().splitlines()]
    return (job, sum(vals) / len(vals))


if __name__ == "__main__":
    t0 = time.time()
    os.makedirs(OUT, exist_ok=True)
    epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
    dni = list(epw.direct_normal_radiation.values); dhi = list(epw.diffuse_horizontal_radiation.values)
    sun = {}
    for r in csv.DictReader(io.open(SC + "sun_louver_months_s350.csv", encoding="utf-8-sig")):
        sun[(r[u"대표일"], r[u"시각"])] = (float(r[u"고도"]), float(r[u"방위"]))
    lit = {}
    for r in csv.DictReader(io.open(D + "Floor9_Monthly_Schedule.csv", encoding="utf-8-sig")):
        lit[(r["day"], r["time"], r["face"])] = float(r["sunlit_frac"])
    jobs, meta = [], []
    for mo in (1, 2, 6, 7, 8, 9, 12):
        key = "%d/15" % mo
        prof = mavg_all(mo, dni, dhi)
        for h in range(24):
            dn, df = prof[h]
            tm = u"%02d:30" % h
            if (dn + df) <= 0.5 or (key, tm) not in sun:
                continue
            alt, azs = sun[(key, tm)]
            for fk, n in FAC:
                jobs.append((mo, 15, h, dn, df, fk, n, 0.0, "fix0h_ctx"))
                if fk == "N":
                    continue
                azd = wrap180(azs - FACE_AZ[fk])
                if alt >= 5.0 and abs(azd) < 90.0:
                    beta, tilt = rule_angle(alt, azd)
                    meta.append((mo, fk, h, alt, azs, azd, beta, tilt, lit.get((key, tm, fk), 1.0)))
                    jobs.append((mo, 15, h, dn, df, fk, n, tilt, "closedh_ctx"))
    print("jobs:", len(jobs), flush=True)
    rows = [u"month,face,tag,hour,W_m2"]
    with ProcessPoolExecutor(max_workers=6) as ex:
        for k, (job, w) in enumerate(ex.map(one, jobs, chunksize=6)):
            mo, da, h, dn, df, fk, n, tilt, tag = job
            rows.append(u"%d,%s,%s,%d,%.3f" % (mo, fk, tag, h, w))
            if k % 50 == 0:
                print("progress", k, "/", len(jobs), "t=%.0fs" % (time.time() - t0), flush=True)
    io.open(OUT + u"Horizontal_Hourly_Radiance.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
    mrows = [u"month,face,hour,altitude,sun_azimuth,azimuth_diff,profile_angle,tilt_deg,sunlit_frac"]
    for m in meta:
        mrows.append(u"%d,%s,%d,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f" % m)
    io.open(OUT + u"Horizontal_Rule_Angles.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(mrows) + u"\r\n")
    print("saved; total %.0fs" % (time.time() - t0), flush=True)
