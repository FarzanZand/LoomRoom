import csv, sys, os
W,H=1920,1080
qa=r'C:/Game projects/LoomRoom-qa/Temp/FirstPerson'; ob=r'C:/Game projects/LoomRoom-onebody/Temp/FirstPerson'
pts=['hand_R','hand_L','elbow_R','elbow_L','blade_base','blade_tip','shield_centre']
for a in sys.argv[1:]:
    pa=os.path.join(qa,'zq_'+a,'frames.csv'); pb=os.path.join(ob,'zq_'+a,'frames.csv')
    if not (os.path.exists(pa) and os.path.exists(pb)): print(a,'missing', os.path.exists(pa), os.path.exists(pb)); continue
    A=list(csv.DictReader(open(pa))); B=list(csv.DictReader(open(pb)))
    n=min(len(A),len(B))
    st=[c for c in A[0] if c.endswith('_state') or c.endswith('_tag')]
    sd=[i for i in range(n) if any(A[i].get(c)!=B[i].get(c) for c in st)]
    worst=(0,None,None); over=0; tot=0
    for i in range(n):
        for p in pts:
            try: av=[float(A[i][p+'_v'+k]) for k in 'xyz']; bv=[float(B[i][p+'_v'+k]) for k in 'xyz']
            except: continue
            if not all(-0.05<=v[0]<=1.05 and -0.05<=v[1]<=1.05 and v[2]>0 for v in (av,bv)): continue
            d=(((av[0]-bv[0])*W)**2+((av[1]-bv[1])*H)**2)**.5; tot+=1
            if d>1: over+=1
            if d>worst[0]: worst=(round(d,2),i,p)
    ea=open(os.path.join(qa,'zq_'+a,'events.csv')).read().splitlines()[1:]; eb=open(os.path.join(ob,'zq_'+a,'events.csv')).read().splitlines()[1:]
    print(f"{a}: frames {n}, state/tag diffs {len(sd)} first {sd[:5]}, on-screen {tot} >1px {over} worst {worst}, events qa {len(ea)} ob {len(eb)} same {ea==eb}")
    if ea!=eb: print('   qa', ea[:6]); print('   ob', eb[:6])
