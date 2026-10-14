# -*- coding: utf-8 -*-
# 외부 창 앞 루버: Radiance(gendaylit -O 1 + rtrace -I)로 날 반사까지 포함한 일사량 (W/m2 -> kWh/m2/day)
import io, os, math, glob, csv, subprocess, time, tempfile
from concurrent.futures import ProcessPoolExecutor
from ladybug.epw import EPW

RAD = r"C:\Program Files\ladybug_tools\radiance"
BIN = RAD + "\\bin\\"
D = r"C:\Users\User\Desktop\bim-it\TEAM PJT\1008" + "\\"
WORK = os.path.join(tempfile.gettempdir(), "louver_rad_ext")
LAT, LON, TZ = 37.35888, 127.10518, 9
OFF = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
MD = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
DAYS = [("Spring", u"\ucd98\ubd84", 3, 21), ("Summer", u"\ud558\uc9c0", 6, 21), ("Autumn", u"\ucd94\ubd84", 9, 23), ("Winter", u"\ub3d9\uc9c0", 12, 21)]
FAC = [("E", (1, 0), u"\ub3d9\uba74"), ("S", (0, -1), u"\ub0a8\uba74"), ("W", (-1, 0), u"\uc11c\uba74"), ("N", (0, 1), u"\ubd81\uba74")]
LEN = {"E": 55.3, "W": 55.3, "S": 63.8, "N": 63.8}
AREA = dict((k, v * 4.2 * 0.45) for k, v in LEN.items())
W, S, OFFS, R_BLADE = 0.30, 0.35, 0.45, 0.5
DIR30 = {"E": 30, "S": -30, "W": -30, "N": 30}     # Ladybug 검증에서 정한 면별 방향
DIR45 = {"E": 45, "S": -45, "W": -45, "N": 45}

env = dict(os.environ); env["RAYPATH"] = RAD + "\\lib"; env["PATH"] = BIN + ";" + env["PATH"]


def mavg_all(mo):
    epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
    dni = list(epw.direct_normal_radiation.values); dhi = list(epw.diffuse_horizontal_radiation.values)
    s, n = OFF[mo - 1] * 24, MD[mo - 1]
    return [(sum(dni[s + k * 24 + h] for k in range(n)) / n, sum(dhi[s + k * 24 + h] for k in range(n)) / n) for h in range(24)]


def poly(name, pts):
    s = "blade polygon %s\n0\n0\n%d\n" % (name, 3 * len(pts))
    for p in pts:
        s += "  %.4f %.4f %.4f\n" % p
    return s + "\n"



def wall(n):
    f = (-n[1], n[0])
    d = 0.02                                   # 측정 면 바로 뒤 (건물 벽, 반사율 0.3)
    pts = []
    for tt, z in ((-6.0, -1.0), (6.0, -1.0), (6.0, 5.0), (-6.0, 5.0)):
        pts.append((f[0] * tt - n[0] * d, f[1] * tt - n[1] * d, z))
    s = "void plastic bwall 0 0 5 0.3 0.3 0.3 0 0\n\nbwall polygon w0\n0\n0\n12\n"
    for p in pts:
        s += "  %.4f %.4f %.4f\n" % p
    return s + "\n"

def pane(n):
    f = (-n[1], n[0])
    d = 0.005
    t = 0.6
    s = "void glass glz 0 0 3 %.3f %.3f %.3f\n\nglz polygon pane\n0\n0\n12\n" % (t, t, t)
    for tt, z in ((-6.0, 0.0), (6.0, 0.0), (6.0, 4.2), (-6.0, 4.2)):
        s += "  %.4f %.4f %.4f\n" % (f[0] * tt + n[0] * d, f[1] * tt + n[1] * d, z)
    return s + "\n"


def scene(n, phi_deg):
    f = (-n[1], n[0])
    phi = math.radians(phi_deg)
    b = (n[0] * math.cos(phi) + f[0] * math.sin(phi), n[1] * math.cos(phi) + f[1] * math.sin(phi))
    s = "void plastic blade 0 0 5 %g %g %g 0 0\n\n" % ((R_BLADE,) * 3)
    k = int(3.0 / S) + 1
    for i in range(-k, k + 1):
        cx, cy = n[0] * OFFS + f[0] * i * S, n[1] * OFFS + f[1] * i * S
        s += poly("b%d" % i, [(cx + b[0] * W / 2, cy + b[1] * W / 2, 0.1), (cx - b[0] * W / 2, cy - b[1] * W / 2, 0.1),
                              (cx - b[0] * W / 2, cy - b[1] * W / 2, 4.1), (cx + b[0] * W / 2, cy + b[1] * W / 2, 4.1)])
    return s


def run(cmd, inp=None, cwd=None):
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=cwd, env=env)
    o, e = p.communicate(inp)
    if p.returncode != 0:
        raise RuntimeError(e.decode("utf-8", "replace")[:300])
    return o


def one(job):
    mo, da, hour, dn, df, fk, n, phi, tag = job
    wd = os.path.join(WORK, "j_%d_%d_%s_%s_%d" % (mo, hour, fk, tag, os.getpid()))
    if not os.path.isdir(wd):
        os.makedirs(wd)
    sky = run([BIN + "gendaylit.exe", str(mo), str(da), "%.2f" % (hour + 0.5), "-a", str(LAT), "-o", str(-LON), "-m", str(-15 * TZ),
               "-W", "%.1f" % dn, "%.1f" % df, "-O", "1"], cwd=wd)
    sc = sky + ("\nskyfunc glow sky_glow 0 0 4 1 1 1 0\nsky_glow source sky 0 0 4 0 0 1 180\n"
                "skyfunc glow ground_glow 0 0 4 1 1 1 0\nground_glow source ground 0 0 4 0 0 -1 180\n\n").encode()
    sc += wall(n).encode()
    sc += pane(n).encode()
    if phi is not None:
        sc += scene(n, phi).encode()
    open(os.path.join(wd, "s.rad"), "wb").write(sc)
    octr = run([BIN + "oconv.exe", "s.rad", r"C:\Users\User\AppData\Local\Temp\claude\C--Users-User-Desktop-bim-it\728f4bb3-c9cf-4681-8370-f1c71c2fd85e\scratchpad\emptyctx_%s.rad" % fk], cwd=wd)
    open(os.path.join(wd, "s.oct"), "wb").write(octr)
    f = (-n[1], n[0])
    pts = ""
    for i in range(10):
        for z in (1.6, 2.0, 2.4):
            t = -0.5 + (i + 0.5) / 10.0
            pts += "%.3f %.3f %.3f %.3f %.3f 0\n" % (f[0] * t, f[1] * t, z, n[0], n[1])
    out = run([BIN + "rtrace.exe", "-h", "-I", "-ab", "2", "-ad", "512", "-as", "256", "-aa", "0.2", "-ar", "128", "-lw", "1e-3", "s.oct"],
              inp=pts.encode(), cwd=wd)
    vals = [float(l.split()[0]) for l in out.decode().strip().splitlines()]
    return (job, sum(vals) / len(vals))      # W/m2 (평균)


if __name__ == "__main__":
    t0 = time.time()
    react = {}
    for r in csv.DictReader(io.open(D + "sun_louver_v3_s350.csv", encoding="utf-8-sig")):
        react[(r[u"\ub300\ud45c\uc77c"], r[u"\uc2dc\uac01"])] = dict((fk, float(r[fn + u"_\uac01\ub3c4"])) for fk, _, fn in FAC)
    DIR45 = {"E": 45, "S": -45, "W": -45, "N": -45}
    FLOOR = 9
    sched = {}
    for r in csv.DictReader(io.open(D + "Floor_Face_Shadow_Schedule.csv", encoding="utf-8-sig")):
        if int(r["floor"]) == FLOOR:
            sched[(r["day"], r["time"], r["face"])] = (float(r["rule_angle"]) if r["rule_state"] == "closed" else 0.0,
                                                        float(r["final_angle"]) if r["final_state"] == "closed" else 0.0)
    jobs = []
    for nm, kn, mo, da in DAYS:
        prof = mavg_all(mo)
        for h in range(24):
            dn, df = prof[h]
            key = (kn, u"%02d:30" % h)
            if (dn + df) <= 0.5 or (key[0], key[1], "E") not in sched:
                continue
            for fk, n, fn in FAC:
                a_rule, a_floor = sched[(kn, key[1], fk)]
                jobs.append((mo, da, h, dn, df, fk, n, None, "none_ctx"))
                jobs.append((mo, da, h, dn, df, fk, n, DIR45[fk], "fix45_ctx"))
                jobs.append((mo, da, h, dn, df, fk, n, a_rule, "rule_ctx"))
                jobs.append((mo, da, h, dn, df, fk, n, a_floor, "floor_ctx"))
    print("jobs:", len(jobs), flush=True)
    rows = [u"day,face,tag,hour,W_m2"]
    with ProcessPoolExecutor(max_workers=7) as ex:
        for k, (job, w) in enumerate(ex.map(one, jobs, chunksize=6)):
            mo, da, h, dn, df, fk, n, phi, tag = job
            nm = [d[0] for d in DAYS if d[2] == mo][0]
            rows.append(u"%s,%s,%s,%d,%.3f" % (nm, fk, tag, h, w))
            if k % 100 == 0:
                print("progress", k, "/", len(jobs), "t=%.0fs" % (time.time() - t0), flush=True)
    io.open(D + u"Alternatives_9F_Shadow_Hourly_Radiance.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
    print("saved hourly", len(rows) - 1, "total time %.0fs" % (time.time() - t0))
