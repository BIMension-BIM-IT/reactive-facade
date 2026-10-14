# -*- coding: utf-8 -*-
import io, csv
import ep_blind as b
rows = list(csv.DictReader(io.open(b.D + "Floor9_Monthly_Schedule.csv", encoding="utf-8-sig")))
sched = {}
for r in rows:
    mo = int(r["day"].split("/")[0]); h, m = r["time"].split(":")
    sched[(mo, r["face"], int(h), int(m))] = (r["final_state"], float(r["final_angle"]))
def angle_vals(face, sign, winter_mode=False, fixed=False):
    out = {}
    for mo in range(1, 13):
        vals = []
        for h in range(24):
            st = sched.get((mo, face, h, 30))
            a = 90.0
            if not fixed and st and st[0] == "closed" and not (winter_mode and mo in (12, 1, 2)):
                a = max(1.0, min(179.0, 90.0 + sign * st[1]))
            vals.append(a)
        out[mo] = vals
    return out
FW = {"S": ["CornerRoom_Back_Glz0"], "W": ["CornerRoom_Left_Glz0"]}
def run_case(case, sign=+1):
    idf = b.base_idf()
    if case == "bare":
        return b.run_text(idf, "c_bare")
    av = {f: angle_vals(f, sign, winter_mode=(case == "reactive_winter"), fixed=(case == "fixed0")) for f in ("S", "W")}
    return b.run_text(idf + "\n" + b.blind_idf(FW, av), "c_%s_%+d" % (case, sign))
if __name__ == "__main__":
    bare = run_case("bare")
    print("bare  cool S/W", [round(x) for x in bare["sol_cool"]], "heat", [round(x) for x in bare["sol_heat"]], "| loads cool %.0f heat %.0f" % (bare["cool"], bare["heat"]))
    for case in ("fixed0", "reactive", "reactive_winter"):
        for sign in ((+1, -1) if case != "fixed0" else (+1,)):
            r = run_case(case, sign)
            red = lambda k: [round(100 * (1 - a / c)) for a, c in zip(r[k], bare[k])]
            print("%-16s sign %+d  window solar reduction %%  cool(S,W) %s  heat(S,W) %s | loads cool %.0f heat %.0f" % (case, sign, red("sol_cool"), red("sol_heat"), r["cool"], r["heat"]))
