import zipfile,xml.etree.ElementTree as ET
f=r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx';z=zipfile.ZipFile(f);ns={'a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
for i in [1,2,3,4,5,6,7,8,9,10,11,12]:
 r=ET.fromstring(z.read(f'ppt/slides/slide{i}.xml')); print('\n',i)
 for t in r.findall('.//a:t',ns):
  if t.text and any(c in t.text for c in ['�','�']):print(repr(t.text))
