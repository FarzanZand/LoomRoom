from PIL import Image
import struct
im=Image.open('Assets/Game/Art/Synty Pixel/Props/Menu figure/Menu figure painted.png')
print('png',im.getpixel((64,64)),im.getpixel((64,10)),im.getpixel((10,64)))
# create 1x2 view sheet
im.resize((128,128)).save('C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research/mini.png')
