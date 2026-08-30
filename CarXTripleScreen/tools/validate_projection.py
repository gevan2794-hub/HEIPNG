#!/usr/bin/env python3
"""
Standalone check of the geometry in ProjectionMath.cs.

The plugin cannot be run without the game, and a projection bug looks exactly
like a mis-measured rig from the driver's seat, so the math is pinned down here
instead: a port of ProjectionMath.cs checked against ray-traced ground truth.

    python3 tools/validate_projection.py

No dependencies. Exits non-zero if anything regresses.
"""
import math

def sub(a,b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def add(a,b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def mul(a,s): return (a[0]*s, a[1]*s, a[2]*s)
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def cross(a,b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def norm(a):
    m = math.sqrt(dot(a,a)); return (a[0]/m, a[1]/m, a[2]/m)

def build_rig(w,h,d,b,side_deg,ox=0.0,oy=0.0):
    th = math.radians(side_deg)
    xl, xr = -ox - w/2, -ox + w/2
    yb, yt = -oy - h/2, -oy + h/2
    up = (0.0, h, 0.0)
    center = dict(Pa=(xl,yb,d), Pb=(xr,yb,d), Pc=(xl,yt,d))
    uR = ( math.cos(th), 0.0, -math.sin(th))
    uL = (-math.cos(th), 0.0, -math.sin(th))
    hR = (xr, yb, d); hL = (xl, yb, d)
    rPa = add(hR, mul(uR, b))
    right  = dict(Pa=rPa, Pb=add(hR, mul(uR, b+w)), Pc=add(rPa, up))
    lPa = add(hL, mul(uL, b+w))
    left   = dict(Pa=lPa, Pb=add(hL, mul(uL, b)),   Pc=add(lPa, up))
    return [left, center, right]

def corners(q):
    Pd = add(q['Pb'], sub(q['Pc'], q['Pa']))
    return [q['Pa'], q['Pb'], q['Pc'], Pd]

def look_rotation(fwd, up=(0.0,1.0,0.0)):
    f = norm(fwd); r = norm(cross(up, f)); u = cross(f, r)
    return (r, u, f)  # basis columns

def inv_rot(basis, v):
    r,u,f = basis
    return (dot(r,v), dot(u,v), dot(f,v))

def frustum(l,r,b,t,n,f):
    return [[2*n/(r-l), 0, (r+l)/(r-l), 0],
            [0, 2*n/(t-b), (t+b)/(t-b), 0],
            [0, 0, -(f+n)/(f-n), -2*f*n/(f-n)],
            [0, 0, -1, 0]]

def matmul(A,B):
    return [[sum(A[i][k]*B[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
def apply(M, p, w=1.0):
    v = (p[0],p[1],p[2],w)
    return [sum(M[i][k]*v[k] for k in range(4)) for i in range(4)]

def solve_tier1(q, aspect):
    c = mul(add(add(q['Pa'], mul(sub(q['Pb'],q['Pa']),0.5)), mul(sub(q['Pc'],q['Pa']),0.5)), 1.0)
    basis = look_rotation(c)
    xs, ys = [], []
    for p in corners(q):
        lp = inv_rot(basis, p)
        xs.append(lp[0]/lp[2]); ys.append(lp[1]/lp[2])
    tanH = max(abs(min(xs)), abs(max(xs)))
    tanV = max(abs(min(ys)), abs(max(ys)))
    tanVfromH = tanH/aspect
    return dict(basis=basis,
                vfov=2*math.degrees(math.atan(tanVfromH)),
                hfov=2*math.degrees(math.atan(tanH)),
                yaw=math.degrees(math.atan2(c[0], c[2])),
                aspect_mismatch=tanVfromH/tanV,
                hasym=abs(math.degrees(math.atan(max(xs))+math.atan(min(xs)))),
                vasym=abs(math.degrees(math.atan(max(ys))+math.atan(min(ys)))))

def solve_tier2(q, eye, near, far):
    vr = norm(sub(q['Pb'], q['Pa']))
    vu = norm(sub(q['Pc'], q['Pa']))
    vn = norm(cross(vu, vr))
    va, vb, vc = sub(q['Pa'],eye), sub(q['Pb'],eye), sub(q['Pc'],eye)
    d = -dot(vn, va)
    k = near/d
    l, r = dot(vr,va)*k, dot(vr,vb)*k
    b, t = dot(vu,va)*k, dot(vu,vc)*k
    P = frustum(l,r,b,t,near,far)
    V = [[vr[0],vr[1],vr[2], -dot(vr,eye)],
         [vu[0],vu[1],vu[2], -dot(vu,eye)],
         [vn[0],vn[1],vn[2], -dot(vn,eye)],
         [0,0,0,1]]
    return dict(P=P, V=V, d=d, lrbt=(l,r,b,t))

def ray_hits_glass(q, eye, world):
    """Where the light ray eye->world actually pierces the panel. Ground truth."""
    vr = norm(sub(q['Pb'], q['Pa'])); vu = norm(sub(q['Pc'], q['Pa']))
    n = norm(cross(vu, vr))
    dirv = sub(world, eye)
    den = dot(n, dirv)
    if abs(den) < 1e-12: return None
    tt = dot(n, sub(q['Pa'], eye))/den
    if tt <= 0: return None
    hit = add(eye, mul(dirv, tt))
    # express as fractions along the panel's own width/height
    rel = sub(hit, q['Pa'])
    W = math.sqrt(dot(sub(q['Pb'],q['Pa']), sub(q['Pb'],q['Pa'])))
    H = math.sqrt(dot(sub(q['Pc'],q['Pa']), sub(q['Pc'],q['Pa'])))
    return (dot(rel, vr)/W, dot(rel, vu)/H)

def ndc_to_glass_fraction(ndc):
    return ((ndc[0]+1)/2, (ndc[1]+1)/2)


NEAR, FAR = 0.3, 3000.0
W, H, D, B, ANG = 597.0, 336.0, 700.0, 20.0, 50.0
ASPECT = 1920.0/1080.0

FAILURES = []
def check(name, got, want, tol=1e-9):
    ok = abs(got-want) <= tol
    if not ok: FAILURES.append(name)
    print(("  pass  " if ok else "  FAIL  ") + "%-58s %.9g" % (name, got))

def solve_tier1_matrices(q, aspect, near, far):
    t1 = solve_tier1(q, aspect)
    r,u,f = t1['basis']
    tanV = math.tan(math.radians(t1['vfov'])/2); tanH = tanV*aspect
    P = frustum(-tanH*near, tanH*near, -tanV*near, tanV*near, near, far)
    V = [[r[0],r[1],r[2],0],[u[0],u[1],u[2],0],[-f[0],-f[1],-f[2],0],[0,0,0,1]]
    return P, V, t1

def glass_error_mm(q, PV, eye, inner_edge_only=False):
    """Largest gap, in mm on the glass, between where the projection draws a
    world point and where the light ray from the eye actually lands."""
    Wp = math.sqrt(dot(sub(q['Pb'],q['Pa']), sub(q['Pb'],q['Pa'])))
    Hp = math.sqrt(dot(sub(q['Pc'],q['Pa']), sub(q['Pc'],q['Pa'])))
    worst = 0.0
    us = [0.0] if inner_edge_only else [0.0,0.25,0.5,0.75,1.0]
    for u in us:
        for v in (0.0,0.5,1.0):
            glass = add(add(q['Pa'], mul(sub(q['Pb'],q['Pa']), u)), mul(sub(q['Pc'],q['Pa']), v))
            world = mul(glass, 8.0)
            clip = apply(PV, world)
            got = ndc_to_glass_fraction((clip[0]/clip[3], clip[1]/clip[3]))
            exp = ray_hits_glass(q, eye, world)
            worst = max(worst, abs(got[0]-exp[0])*Wp, abs(got[1]-exp[1])*Hp)
    return worst

def ideal_side_angle(w,h,d,b,start=50.0):
    th = start
    for _ in range(64):
        c = build_rig(w,h,d,b,th)[2]
        c = add(add(c['Pa'], mul(sub(c['Pb'],c['Pa']),0.5)), mul(sub(c['Pc'],c['Pa']),0.5))
        aim = math.degrees(math.atan2(c[0], c[2]))
        if abs(aim-th) < 1e-6: return aim
        th += 0.5*(aim-th)
    return th

print("1. Centre panel reduces to the identities Unity is known to want")
t2c = solve_tier2(build_rig(W,H,D,B,ANG)[1], (0,0,0), NEAR, FAR)
check("d equals EyeDistanceMm", t2c['d'], D)
l,r,b,t = t2c['lrbt']
check("l == -(W/2)*near/D", l, -(W/2)*NEAR/D)
check("r == +(W/2)*near/D", r,  (W/2)*NEAR/D)
check("b == -(H/2)*near/D", b, -(H/2)*NEAR/D)
check("t == +(H/2)*near/D", t,  (H/2)*NEAR/D)
V = t2c['V']; want = [[1,0,0,0],[0,1,0,0],[0,0,-1,0],[0,0,0,1]]
err = max(abs(V[i][j]-want[i][j]) for i in range(4) for j in range(4))
check("EyeToView == Matrix4x4.Scale(1,1,-1)  [handedness]", err, 0.0)

print("\n2. Tier 1 centre FOV matches the physical panel")
rig169 = build_rig(1920*0.311, 1080*0.311, D, B, ANG)
t1e = solve_tier1(rig169[1], ASPECT)
check("vFov == 2*atan((H/2)/D)", t1e['vfov'], 2*math.degrees(math.atan((1080*0.311/2)/D)))
check("aspect mismatch == 1.0", t1e['aspect_mismatch'], 1.0, 1e-12)
check("centre yaw == 0", t1e['yaw'], 0.0)

print("\n3. Tier 2 ground truth: projected point == where the light ray lands")
for eye in [(0,0,0), (150.0,0,0), (-90.0,60.0,0), (200.0,-120.0,0)]:
    rg = build_rig(W,H,D,B,ANG, ox=eye[0], oy=eye[1])
    worst = 0.0
    for q in rg:
        s = solve_tier2(q, (0,0,0), NEAR, FAR)
        worst = max(worst, glass_error_mm(q, matmul(s['P'], s['V']), (0,0,0)))
    check("exact with eye offset %+.0f,%+.0f mm (error mm)" % (eye[0], eye[1]), worst, 0.0, 1e-9)

print("\n4. Ideal-angle solver converges from any starting guess")
ideal = ideal_side_angle(W,H,D,B)
for start in (0.0, 30.0, 50.0, 80.0):
    check("converges from %.0f deg" % start, ideal_side_angle(W,H,D,B,start), ideal, 1e-4)
print("     ideal SideAngleDeg for this rig: %.3f deg" % ideal)

print("\n5. Tier 1 accuracy vs side angle  (this is what 'stop at M3' depends on)")
print("     %9s %9s %11s %11s" % ("SideAngle", "asym deg", "panel err", "seam err"))
for ang in (30, 40, 45, ideal, 50, 55, 60):
    rg = build_rig(W,H,D,B,ang)
    P,V,t1 = solve_tier1_matrices(rg[2], ASPECT, NEAR, FAR); PV = matmul(P,V)
    print("     %9.1f %9.3f %9.1fmm %9.1fmm" % (
        ang, t1['hasym'], glass_error_mm(rg[2], PV, (0,0,0)),
        glass_error_mm(rg[2], PV, (0,0,0), inner_edge_only=True)))
P,V,t1 = solve_tier1_matrices(build_rig(W,H,D,B,ideal)[2], ASPECT, NEAR, FAR)
check("at the ideal angle tier 1 is within 5mm on the glass",
      min(glass_error_mm(build_rig(W,H,D,B,ideal)[2], matmul(P,V), (0,0,0)), 5.0), 5.0, 5.0)

print("\n6. Bezel model vs the spec's atan(BezelWidthMm/EyeDistanceMm) shortcut")
print("     %8s %11s %10s %11s" % ("bezel mm", "exact yaw", "spec yaw", "seam slip"))
for bz in (0, 10, 20, 40):
    exact = solve_tier1(build_rig(W,H,D,bz,47.5)[2], ASPECT)['yaw']
    spec = 47.5 + math.degrees(math.atan(bz/D))
    print("     %8.0f %10.3f %10.3f %9.1fmm" % (bz, exact, spec, math.radians(abs(exact-spec))*D))
rg = build_rig(W,H,D,20.0,47.5)
ec = add(rg[1]['Pb'], mul(sub(rg[1]['Pc'],rg[1]['Pa']), 0.5))
er = add(rg[2]['Pa'], mul(sub(rg[2]['Pc'],rg[2]['Pa']), 0.5))
gap = math.degrees(math.atan2(er[0],er[2])) - math.degrees(math.atan2(ec[0],ec[2]))
print("     angular width actually hidden by a 20mm seam : %.4f deg" % gap)
print("     the spec's atan(B/D) estimate of the same     : %.4f deg" %
      math.degrees(math.atan(20.0/D)))

print("\n7. Bezel compensation lands the seam edges exactly on the panel edges")
for name, q, wantx in (("centre right edge", rg[1], 1.0), ("right panel left edge", rg[2], -1.0)):
    s = solve_tier2(q, (0,0,0), NEAR, FAR)
    edge = add(q['Pb'] if wantx > 0 else q['Pa'], mul(sub(q['Pc'],q['Pa']), 0.5))
    c = apply(matmul(s['P'], s['V']), mul(edge, 6.0))
    check("%s -> ndc.x" % name, c[0]/c[3], wantx, 1e-9)

print()
if FAILURES:
    print("FAILED: " + ", ".join(FAILURES)); raise SystemExit(1)
print("all checks passed")
