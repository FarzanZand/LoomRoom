import zipfile,re
z=zipfile.ZipFile(r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx')
for n in ['ppt/slides/slide1.xml','ppt/slides/slide2.xml','ppt/slides/slide7.xml','ppt/slides/slide12.xml']:
 print('\n',n)
 s=z.read(n).decode('utf8');
 for m in re.finditer(r'<a:t>(.*?)</a:t>',s):
  t=m.group(1)
  if any(x in t for x in ['?','?','�']): print(t)
