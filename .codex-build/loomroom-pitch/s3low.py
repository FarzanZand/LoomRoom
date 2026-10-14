import zipfile,xml.etree.ElementTree as ET
z=zipfile.ZipFile(r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx');ns={'p':'http://schemas.openxmlformats.org/presentationml/2006/main','a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
r=ET.fromstring(z.read('ppt/slides/slide3.xml'))
for sh in r.findall('.//p:sp',ns):
 tx=' '.join(t.text or '' for t in sh.findall('.//a:t',ns));xf=sh.find('./p:spPr/a:xfrm',ns)
 if xf is None or not tx.strip():continue
 o=xf.find('a:off',ns);e=xf.find('a:ext',ns);x,y,w,h=[int(v)/914400 for v in(o.get('x'),o.get('y'),e.get('cx'),e.get('cy'))]
 if y>5.8: print(tx,[round(q,2) for q in (x,y,w,h)])
