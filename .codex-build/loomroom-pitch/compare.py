import zipfile,xml.etree.ElementTree as ET
for f in [r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx',r'C:\Game projects\LoomRoom\pitch-output\LoomRoom_Publisher_Pitch.pptx']:
 print('\nFILE',f)
 z=zipfile.ZipFile(f); ns={'a':'http://schemas.openxmlformats.org/drawingml/2006/main','p':'http://schemas.openxmlformats.org/presentationml/2006/main'}
 slides=sorted([x for x in z.namelist() if x.startswith('ppt/slides/slide') and x.endswith('.xml') and '/_rels/' not in x], key=lambda x:int(x.split('slide')[2].split('.')[0]))
 for x in slides:
  root=ET.fromstring(z.read(x)); texts=[t.text or '' for t in root.findall('.//a:t',ns)]; print('\n'+x+': '+' | '.join(texts))
