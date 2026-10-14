from PIL import Image
for f in ['sword.png','shield.png','fireball.png','crypt-rat.png','mini.png','bed.png','rug.png']:
 im=Image.open('C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research/'+f); print(f,im.size,im.mode,im.getbbox())
