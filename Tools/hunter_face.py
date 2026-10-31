# Run from a folder holding hunter_orig.png (the restyle before this pass, from git) and headmap.png/headmapx.png
# (per-texel maps of the head written by an eval: height, normals, lateral position; see the review round 2 notes).
# The hunter body's head, repainted on its own texels (maps from headmap.cs: height, normals, lateral position):
# a short dark crop over the crown, the sides down to the ears and the back to the nape (its edge texels filled so no
# pale specks are left between triangles), the cloth face mask in a darker woven tone with folds, a seam under the eyes
# and a nose ridge, and shaded eye sockets. Grade as the house style: colour varying in 2x2 clusters, dark creases.
import sys
from PIL import Image
import numpy as np
T='C:/Game projects/LoomRoom/Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Hunter_Male_01 (Characters)/SM_Chr_Hunter_Male_01 PolygonDarkFantasy_Mat_01_A.png'
src=np.array(Image.open('hunter_orig.png').convert('RGBA')).astype(float)
m=np.array(Image.open('headmap.png').convert('RGBA')).astype(float)
mx=np.array(Image.open('headmapx.png').convert('RGBA')).astype(float)
head=m[:,:,3]>0; h=m[:,:,0]/255; up=(m[:,:,1]-128)/127; fw=(m[:,:,2]-128)/127; xf=mx[:,:,0]/255; nx=(mx[:,:,1]-128)/127
rgb=src[:,:,:3]; lum=rgb.mean(2)/255
eyes=head&(lum<.1)
eyeH=float(np.percentile(h[eyes],10)) if eyes.sum() else .56   # the eyes' lower edge (the brows above are lighter)
print('eye texels',eyes.sum(),'eye height',round(eyeH,3))
rng=np.random.default_rng(11)
cl=rng.uniform(.86,1.12,(130,130))
out=src.copy()
def paint(mask,base,calm=1.0):
    ys,xs=np.nonzero(mask)
    for y,x in zip(ys,xs):
        k=1+(cl[y//2,x//2]*rng.uniform(.93,1.06)-1)*(calm if calm>0 else .35)
        out[y,x,:3]=np.clip(np.array(base)*k,0,255)
# hair
hair=head&((h>.78)|((h>.42)&(fw<-.3))|((h>.6)&(fw<.45)&(np.abs(nx)>.45)))&~eyes
skinlike=(rgb[:,:,0]>140)&(rgb[:,:,0]>rgb[:,:,2]+15)
for _ in range(2):   # fill the texels between triangles at the crop's edge
    grow=np.zeros_like(hair)
    grow[1:]|=hair[:-1]; grow[:-1]|=hair[1:]; grow[:,1:]|=hair[:,:-1]; grow[:,:-1]|=hair[:,1:]
    hair|=grow&skinlike&~(head&(h<.78)&(fw>.3))
paint(hair,(38,29,25))
dark=hair&(rng.random(hair.shape)<.16); out[dark,:3]*=.7
# face mask (cloth): below the eyes, the front and sides of the jaw
mask=head&(h<eyeH-.015)&(fw>-.35)&~hair
paint(mask,(134,120,104),.0)
ys,xs=np.nonzero(mask)
for y,x in zip(ys,xs):
    f=out[y,x,:3]
    band=(h[y,x]*14+np.sin(xf[y,x]*7)*.25)%1
    if band<.22: f*=.74
    elif band<.32: f*=1.08          # the lit top of each fold          # folds of the cloth
    if abs(xf[y,x]-.5)<.05 and h[y,x]>eyeH-.2 and fw[y,x]>.55: f*=1.12   # the nose under the cloth
    elif .05<=abs(xf[y,x]-.5)<.1 and h[y,x]>eyeH-.2 and fw[y,x]>.4: f*=.84
    if h[y,x]>eyeH-.045: f*=.7   # the seam at its top edge, under the eyes
    out[y,x,:3]=np.clip(f,0,255)
# eye sockets: skin round the eyes darker
near=np.zeros_like(eyes)
for dy in (-2,-1,0,1,2):
    for dx in (-2,-1,0,1,2): near|=np.roll(np.roll(eyes,dy,0),dx,1)
sock=near&~eyes&head&~hair&~mask
out[sock,:3]*=.78
Image.fromarray(out.astype(np.uint8)).save(T)
print('hair',hair.sum(),'mask',mask.sum(),'socket',sock.sum())
