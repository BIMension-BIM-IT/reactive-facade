# -*- coding: utf-8 -*-
# 웹 담당 1단계 구현 확인용: 해 위치와 루버 각도의 정답표 (같은 값이 나오는지 비교하세요)
import csv, io, math
D = "C:/Users/User/Desktop/bim-it/TEAM PJT/1008/"
OUT = "C:/Users/User/Desktop/bim-it/TEAM PJT/1012/Unity_Package/"
DAYS = {u"춘분": (3, 21), u"하지": (6, 21), u"추분": (9, 23), u"동지": (12, 21)}
PICK = {u"춘분": ["09:00", "12:00", "15:00"], u"하지": ["08:00", "10:00", "12:00", "14:00", "16:00", "18:00"], u"추분": ["10:00", "13:00", "16:00"], u"동지": ["09:00", "11:00", "12:00", "14:00", "15:00"]}
FACES = [(u"동", 90.0), (u"남", 180.0), (u"서", 270.0), (u"북", 0.0), (u"남동(135°)", 135.0), (u"남서(225°)", 225.0)]
def wrap(d): return (d + 180.0) % 360.0 - 180.0
def rule(alt, az, faz, cut, w=300.0, s=350.0):
    if abs(wrap(faz - 0.0)) <= 45.0 or alt < 5.0:
        return False, 0.0
    th = wrap(az - faz)
    if abs(th) > cut:
        return False, 0.0
    a = max(0.0, math.degrees(math.asin(min(1.0, s * math.cos(math.radians(th)) / w))) - abs(th))
    return True, (a if th >= 0 else -a)
rows = list(csv.DictReader(io.open(D + "sun_louver_v3_s350.csv", encoding="utf-8-sig")))
out = [u"month,day,time(KST),lat,lon,sun_altitude_deg,sun_azimuth_deg,cutoff_deg,facade,facade_azimuth_deg,state,angle_deg"]
n = 0
for r in rows:
    nm, tm = r[u"대표일"], r[u"시각"]
    if tm not in PICK.get(nm, []):
        continue
    mo, da = DAYS[nm]
    alt, az, cut = float(r[u"고도"]), float(r[u"방위"]), float(r[u"컷오프"])
    for fn, faz in FACES:
        closed, ang = rule(alt, az, faz, cut)
        out.append(u"%d,%d,%s,37.35888,127.10518,%.3f,%.3f,%.2f,%s,%.0f,%s,%.2f" % (mo, da, tm, alt, az, cut, fn, faz, u"닫힘" if closed else u"열림", ang))
        n += 1
text = u"\r\n".join(out) + u"\r\n"
io.open(OUT + "LouverRule_TestVectors.csv", "w", encoding="utf-8-sig", newline="").write(text)
print("rows", n)
