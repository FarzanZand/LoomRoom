import zipfile,xml.etree.ElementTree as ET
f=r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx';z=zipfile.ZipFile(f);ns={'p':'http://schemas.openxmlformats.org/presentationml/2006/main','a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
for i in [1,2,5,6,7,8,9,10,11,12]:
 r=ET.fromstring(z.read(f'ppt/slides/slide{i}.xml')); pics=len(r.findall('.//p:pic',ns)); print('slide',i,'pics',pics)
