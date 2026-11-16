"""Eyes that glow in the old skeleton bodies' sockets, step 2 of 2 (see Tools/socket_eyes.eval.cs).

For each head written to Temp/SocketEyes: a front depth map at 1 mm a pixel, the two pits in the upper face (the eye
sockets: deeper than the skull's closed surface by 3.5 mm or more), and an ember painted into the texels that show
there: full strength within 6 mm of the socket's middle, fading toward its rim. The colour is the creature's own
(COL below). Writes <texture> eyes.png beside each body's texture; nothing else on the texture is touched.

    python Tools/socket_eyes.py
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__))).replace("\\", "/") + "/"
WORK = ROOT + "Temp/SocketEyes/"
COL = {"Crypt Archer": (150, 255, 110), "Crypt Warden": (120, 190, 255), "Crypt Soldier": (255, 170, 70)}
res = 0.001
x0, x1, y0, y1 = -0.12, 0.12, -0.05, 0.30


def depth_map(P):
    W = int((x1 - x0) / res); H = int((y1 - y0) / res)
    depth = np.full((H, W), -1.0)
    for tri in P:
        xs = (tri[:, 0] - x0) / res; ys = (y1 - tri[:, 1]) / res; zs = tri[:, 2]
        minx = max(int(np.floor(xs.min())), 0); maxx = min(int(np.ceil(xs.max())), W - 1)
        miny = max(int(np.floor(ys.min())), 0); maxy = min(int(np.ceil(ys.max())), H - 1)
        if minx > maxx or miny > maxy:
            continue
        gx, gy = np.meshgrid(np.arange(minx, maxx + 1) + .5, np.arange(miny, maxy + 1) + .5)
        d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(d) < 1e-9:
            continue
        a = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / d
        b = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / d
        c = 1 - a - b
        m = (a >= 0) & (b >= 0) & (c >= 0)
        z = a * zs[0] + b * zs[1] + c * zs[2]
        sub = depth[miny:maxy + 1, minx:maxx + 1]
        sub[m] = np.maximum(sub[m], z[m])
    return depth


idx = {l.split(";")[0]: l.strip().split(";") for l in open(WORK + "index.txt") if l.strip()}
for k, col in COL.items():
    T = np.loadtxt(WORK + k + ".txt")
    P = T[:, :9].reshape(-1, 3, 3); UV = T[:, 9:].reshape(-1, 3, 2)
    depth = depth_map(P); H, W = depth.shape
    d=depth.copy(); d[d<-0.5]=d[d>-0.5].min()
    closed=ndimage.grey_closing(d,size=(41,41))
    pit=(closed-d)>0.0035
    yy,xx=np.mgrid[0:H,0:W]; X=x0+(xx+.5)*res; Y=y1-(yy+.5)*res
    face=(depth>-0.5)&(np.abs(X)>0.010)&(np.abs(X)<0.075)
    # the upper face: above the nose's tip. The band is found from the head's own height.
    ys=np.where((depth>-0.5).any(1))[0]; top=y1-ys.min()*res
    band=(Y<top-0.05)&(Y>top-0.14)
    lab,n=ndimage.label(pit&face&band)
    comps=[]
    for j in range(1,n+1):
        m=lab==j; a=m.sum()
        if a<60: continue
        comps.append((a,j,X[m].mean(),Y[m].mean()))
    comps.sort(reverse=True)
    left=[c for c in comps if c[2]<0][:1]; right=[c for c in comps if c[2]>0][:1]
    print(k,"head top %.3f"%top,"pits:",[(c[0],round(c[2],3),round(c[3],3)) for c in left+right])
    mask=np.zeros_like(pit)
    for c in left+right: mask|=(lab==c[1])
    mask=ndimage.binary_erosion(mask,iterations=2)
    centres=[(c[2],c[3]) for c in left+right]
    # texels
    texpath = ROOT + idx[k][3]; tex=Image.open(texpath); S=tex.size[0]
    em=np.zeros((S,S,3),"float32"); hits=0
    for tri,uv in zip(P,UV):
        cx=tri[:,0].mean(); cy=tri[:,1].mean()
        px=int((cx-x0)/res); py=int((y1-cy)/res)
        if not (0<=px<W and 0<=py<H): continue
        # only triangles that touch a pit and are the visible surface there
        us=uv[:,0]*S; vs=(1-uv[:,1])*S
        minu=int(np.floor(us.min())); maxu=int(np.ceil(us.max())); minv=int(np.floor(vs.min())); maxv=int(np.ceil(vs.max()))
        den=(vs[1]-vs[2])*(us[0]-us[2])+(us[2]-us[1])*(vs[0]-vs[2])
        if abs(den)<1e-9: continue
        for tv in range(max(minv,0),min(maxv+1,S)):
            for tu in range(max(minu,0),min(maxu+1,S)):
                a=((vs[1]-vs[2])*(tu+.5-us[2])+(us[2]-us[1])*(tv+.5-vs[2]))/den; b=((vs[2]-vs[0])*(tu+.5-us[2])+(us[0]-us[2])*(tv+.5-vs[2]))/den; c=1-a-b
                if a<-.02 or b<-.02 or c<-.02: continue
                p=a*tri[0]+b*tri[1]+c*tri[2]
                qx=int((p[0]-x0)/res); qy=int((y1-p[1])/res)
                if not (0<=qx<W and 0<=qy<H) or not mask[qy,qx]: continue
                if depth[qy,qx]-p[2]>0.0015: continue      # hidden behind something in front of it
                dist=min(np.hypot(p[0]-c0,p[1]-c1) for c0,c1 in centres)
                w=1.0 if dist<0.006 else (0.55 if dist<0.012 else 0.28)
                em[tv,tu]=np.maximum(em[tv,tu],np.array(col,dtype="float32")*w); hits+=1
    out=Image.fromarray(em.clip(0,255).astype("uint8"))
    name=os.path.splitext(texpath)[0]+" eyes.png"
    out.save(name); print("   texels",int((em.sum(2)>0).sum()),"->",os.path.basename(name),S)
