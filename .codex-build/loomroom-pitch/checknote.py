import zipfile
f=r'C:\Game projects\LoomRoom\pitch-output\LoomRoom_Publisher_Pitch.pptx'; z=zipfile.ZipFile(f)
for n in ['ppt/notesSlides/notesSlide3.xml','ppt/notesSlides/notesSlide9.xml','ppt/notesSlides/notesSlide12.xml']:
 s=z.read(n).decode('utf8');print(n,s[s.find('<a:t'):s.find('</a:t>')+7])
