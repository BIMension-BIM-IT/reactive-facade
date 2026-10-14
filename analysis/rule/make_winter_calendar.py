# -*- coding: utf-8 -*-
# 겨울 모드 날짜표(성남): 7일 평균 외기온도가 12°C 미만이면 겨울, 14°C를 넘으면 겨울 끝, 그 사이는 이전 상태 유지
import io, datetime, glob
import numpy as np
from ladybug.epw import EPW
epw = EPW(glob.glob(r"C:\Users\User\Desktop\bim-it\TEAM PJT\EPW\*.epw")[0])
t = np.array(epw.dry_bulb_temperature.values)
dm = t.reshape(365, 24).mean(axis=1)
# 7일 이동평균(해당 날짜를 가운데로, 연말·연초는 이어 붙여서)
ext = np.concatenate([dm[-3:], dm, dm[:3]])
ma = np.array([ext[i:i + 7].mean() for i in range(365)])
ON, OFF = 12.0, 14.0
state = np.zeros(365, dtype=int)
cur = 1 if ma[0] < ON else 0
for rep in range(2):                        # 두 바퀴 돌려서 연초 상태를 맞춰요
    for d in range(365):
        if ma[d] < ON: cur = 1
        elif ma[d] > OFF: cur = 0
        state[d] = cur
base = datetime.date(2026, 1, 1)
rows = [u"month,day,winter,temp7_c"]
for d in range(365):
    dt = base + datetime.timedelta(days=d)
    rows.append(u"%d,%d,%d,%.1f" % (dt.month, dt.day, state[d], ma[d]))
io.open("C:/Users/User/Desktop/bim-it/TEAM PJT/1012/Unity_Package/StreamingAssets/winter_calendar.csv", "w", encoding="utf-8-sig", newline="").write(u"\r\n".join(rows) + u"\r\n")
# 구간 요약
segs = []; s = None
for d in range(365):
    if state[d] and s is None: s = d
    if not state[d] and s is not None: segs.append((s, d - 1)); s = None
if s is not None: segs.append((s, 364))
f = lambda i: (base + datetime.timedelta(days=int(i))).strftime("%m/%d")
print("겨울 구간:", [(f(a), f(b)) for a, b in segs], "총 %d일" % int(state.sum()))
for md in ((3, 21), (6, 21), (9, 23), (12, 21), (11, 15), (3, 15)):
    i = (datetime.date(2026, md[0], md[1]) - base).days
    print("  %d/%d: 7일 평균 %.1f°C -> %s" % (md[0], md[1], ma[i], u"겨울" if state[i] else u"겨울 아님"))
