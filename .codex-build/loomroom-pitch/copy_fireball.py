from PIL import Image,ImageFilter,ImageEnhance
from pathlib import Path
p=Path('Assets/Game/Combat/Spells/_Pack/Textures/Fireball.png'); im=Image.open(p); print(im.size,im.mode,im.getbbox()); im.thumbnail((256,256)); im.save('C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research/fireball.png')
