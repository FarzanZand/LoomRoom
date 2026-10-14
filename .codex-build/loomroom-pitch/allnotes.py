import zipfile,xml.etree.ElementTree as ET,html,re
z=zipfile.ZipFile(r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx');ns={'a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
for i in [2,3,4,5,6,7,8,9,10,11,12]:
 b=z.read(f'ppt/notesSlides/notesSlide{i}.xml');r=ET.fromstring(b);print(i,' '.join(x.text or '' for x in r.findall('.//a:t',ns)))
