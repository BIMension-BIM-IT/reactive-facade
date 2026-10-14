# -*- coding: utf-8 -*-
# 검증: 고정 루버를 3D 날로 세워서 Ladybug(RadiationStudy, Radiance 천공 행렬)가 직접 계산한 값 vs 내 수식 값
import io, math, glob, csv, time
from ladybug.epw import EPW
from ladybug_geometry.geometry3d import Point3D, Vector3D, Face3D, Mesh3D
from ladybug_radiance.skymatrix import SkyMatrix
from ladybug_radiance.study.radiation import RadiationStudy

t0 = time.time()
D = r"C:\Users\User\Desktop\bim-it\TEAM PJT\1008" + "\\"
EPW_PATH = glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0]
OFF = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
MD = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
DAYS = [(u"Spring", 3, 21), (u"Summer", 6, 21), (u"Autumn", 9, 23), (u"Winter", 12, 21)]
FAC = [("E", (1, 0)), ("S", (0, -1)), ("W", (-1, 0))]
ANGLES = [0, 45, -45]
W, S, OFFS = 0.30, 0.35, 0.45

epw = EPW(EPW_PATH)
dni = list(epw.direct_normal_radiation.values)
dhi = list(epw.diffuse_horizontal_radiation.values)


def mavg(mo, h):
    s, n = OFF[mo - 1] * 24, MD[mo - 1]
    return (sum(dni[s + k * 24 + h] for k in range(n)) / n, sum(dhi[s + k * 24 + h] for k in range(n)) / n)


def test_mesh(n, f):
    # 1m x 1m 창 조각, 높이 1.5~2.5 m 중앙, 바깥 방향 n
    c = Point3D(0, 0, 2.0)
    a = c - f * 0.5 - Vector3D(0, 0, 0.5)
    pts = [a, a + f * 1.0, a + f * 1.0 + Vector3D(0, 0, 1.0), a + Vector3D(0, 0, 1.0)]
    return Mesh3D.from_face_vertices([Face3D(pts)])


def blades(n, f, phi_deg, span=3.0):
    phi = math.radians(phi_deg)
    b = n * math.cos(phi) + f * math.sin(phi)       # 날 방향 (바깥에서 보아 +는 오른쪽으로)
    out = []
    k = int(span / S) + 1
    for i in range(-k, k + 1):
        c = n * OFFS + f * (i * S)
        p1 = Point3D(c.x, c.y, 0.1) + b * (W / 2)
        p2 = Point3D(c.x, c.y, 0.1) - b * (W / 2)
        p3 = Point3D(c.x, c.y, 4.1) - b * (W / 2)
        p4 = Point3D(c.x, c.y, 4.1) + b * (W / 2)
        out.append(Face3D([p1, p2, p3, p4]))
    return out


mine = {}
for r in csv.DictReader(io.open(D + "Fixed_Louver_Window_Irradiance.csv", encoding="utf-8-sig")):
    mine[(r["day"], r["face"], int(r["fixed_angle_deg"]))] = (float(r["total_kWh_m2"]), float(r["no_louver_kWh_m2"]))

rows = [u"day,face,fixed_angle_deg,ladybug_with_blades_kWh_m2,my_formula_kWh_m2,no_louver_kWh_m2,diff_pct_vs_my_formula"]
log = []
for nm, mo, da in DAYS:
    i0 = (OFF[mo - 1] + da - 1) * 24
    hoys = [i0 + h for h in range(24)]
    c_dni = epw.direct_normal_radiation.duplicate(); c_dhi = epw.diffuse_horizontal_radiation.duplicate()
    vn, vd = list(dni), list(dhi)
    for h in range(24):
        vn[i0 + h], vd[i0 + h] = mavg(mo, h)
    c_dni.values = vn; c_dhi.values = vd
    sky = SkyMatrix.from_components(epw.location, c_dni, c_dhi, hoys, high_density=True, ground_reflectance=0.2)
    for fk, (nx, ny) in FAC:
        n = Vector3D(nx, ny, 0)
        f = Vector3D(-ny, nx, 0)
        m = test_mesh(n, f)
        none_lb = float(RadiationStudy(sky, m, [], offset_distance=0.0).radiation_values[0])
        for a in ANGLES:
            ctx = blades(n, f, a)
            lb = float(RadiationStudy(sky, m, ctx, offset_distance=0.0).radiation_values[0])
            my, my_none = mine[(nm, fk, a)]
            rows.append(u"%s,%s,%d,%.3f,%.3f,%.3f,%.1f" % (nm, fk, a, lb, my, none_lb, 100.0 * (lb - my) / my))
            log.append((nm, fk, a, lb, my, none_lb))
        print(u"done %s %s  t=%.0fs" % (nm, fk, time.time() - t0), flush=True)
io.open(D + u"Fixed_Louver_LB_Check.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
print(u"day face angle | LB(blades 3D) | my formula | no louver (LB, high-density sky) | diff%")
for nm, fk, a, lb, my, nn in log:
    print(u"%-7s %s %+3d | %.2f | %.2f | %.2f | %+.0f%%" % (nm, fk, a, lb, my, nn, 100.0 * (lb - my) / my))
