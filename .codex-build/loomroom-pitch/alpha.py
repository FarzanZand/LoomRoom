from PIL import Image
f='C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research/mini.png'
im=Image.open(f).convert('RGBA'); print(im.size,im.getpixel((64,64)),im.getpixel((64,10))); im.save(f)
