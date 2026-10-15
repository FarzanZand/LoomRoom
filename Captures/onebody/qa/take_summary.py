import csv, sys, os
# Prints, per take, each animator layer's state/tag changes with their frame (what the take actually did).
root = sys.argv[1]
for a in sys.argv[2:]:
    p = os.path.join(root, 'gt_' + a, 'frames.csv')
    if not os.path.exists(p): print(a, 'missing'); continue
    R = list(csv.DictReader(open(p)))
    cols = [c for c in R[0] if c.endswith('_state') or c.endswith('_tag')]
    out = []
    for c in cols:
        prev = None
        for i, r in enumerate(R):
            if r[c] != prev and r[c] not in ('', '0'):
                if prev is not None: out.append(f"{i}:{c[:-6] if c.endswith('_state') else c}={r[c]}")
                prev = r[c]
            elif r[c] != prev: prev = r[c]
    cam0 = [float(R[0]['cam_p' + k]) for k in 'xyz']; cam1 = [float(R[-1]['cam_p' + k]) for k in 'xyz']
    ev = open(os.path.join(root, 'gt_' + a, 'events.csv')).read().splitlines()[1:]
    print(f"{a}: {len(R)} frames, moved {sum((cam1[i]-cam0[i])**2 for i in range(3))**.5:.2f} m, events {len(ev)}: {[e.split(',')[3] for e in ev][:8]}")
    print('   ', ' '.join(out[:24]))
