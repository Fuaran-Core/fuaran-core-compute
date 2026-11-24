"""Every figure DECISIONS.md D89 and 2026-11-24-i7-8650u-phase-730.md quote, recomputed from the captures beside
this script. Python 3 standard library only:

    python benchmarks/results/2026-11-24-i7-8650u-phase-730/figures.py

A capture is the harness's printed table: one row per case, `| case | median | least | greatest | checksum |`, in
nanoseconds per call. The trees are named by commit: `general-*` is the base tree (db12822), `specialised-*`,
`pgo-final-*`, `nopgo-*` and `notier-*` the final tree (73f765d), `pgo-base-*` the base tree again, `aggr-*` the final
tree with the general fold marked AggressiveOptimization (not kept), and `interim-*` the two intermediate trees.
"""

import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROW = re.compile(r'\| ((?:product|specialised|function): .*?) \| ([\d.]+) \| ([\d.]+) \| ([\d.]+) \| (\w+) \|')

PAIRS = [
    ('rotor * rotor', 'product: rotor * rotor (geometric)', 'specialised: rotor * rotor'),
    ('motor * motor', 'product: motor * motor (geometric)', 'specialised: motor * motor'),
    ('translator * rotor', 'product: translator * rotor (geometric)', 'specialised: translator * rotor'),
    ('rotor * point', 'product: rotor * point (sandwich, first)', 'specialised: rotor * point (sandwich, first)'),
    ('(rotor point) * reverse rotor', 'product: (rotor point) * reverse rotor (sandwich, second)',
     'specialised: (rotor point) * reverse rotor (sandwich, second)'),
    ('translator * point', 'product: translator * point (sandwich, first)',
     'specialised: translator * point (sandwich, first)'),
    ('(translator point) * reverse translator', 'product: (translator point) * reverse translator (sandwich, second)',
     'specialised: (translator point) * reverse translator (sandwich, second)'),
    ('motor * point', 'product: motor * point (sandwich, first)', 'specialised: motor * point (sandwich, first)'),
    ('(motor point) * reverse motor', 'product: (motor point) * reverse motor (sandwich, second)',
     'specialised: (motor point) * reverse motor (sandwich, second)'),
]

FUNCTIONS = ['Rotor.compose', 'Motor.compose', 'Motor.ofRotorTranslator', 'Rotor.applyToPoint',
             'Translator.applyToPoint', 'Motor.applyToPoint', 'Motor.applyToSphere', 'Sphere.power']


def load(name):
    rows = {}
    with open(os.path.join(HERE, name + '.txt'), encoding='utf-8') as f:
        for line in f:
            m = ROW.match(line)
            if m:
                rows[m[1]] = (float(m[2]), float(m[3]), float(m[4]), m[5])
    return rows


def runs(prefix, n):
    return [(f'{prefix}-{i}', load(f'{prefix}-{i}')) for i in range(1, n + 1)]


def middle(xs):
    return sorted(xs)[len(xs) // 2]


MARGIN = 2.5
base = {h: runs('general-' + h, 3) for h in ('dotnet', 'node')}
final = {h: runs('specialised-' + h, 3) for h in ('dotnet', 'node')}
# Every committed run of the final tree in its default configuration: the three designated runs and, on .NET, the
# four runs interleaved with the base tree's.
pooled = {'dotnet': final['dotnet'] + runs('pgo-final', 4), 'node': final['node']}
other = {'nopgo': runs('nopgo', 2), 'notier': runs('notier', 1), 'aggr': runs('aggr', 3),
         'pgo-base': runs('pgo-base', 4)}

out = []
p = out.append

p('## The baseline (the general product before any specialisation): medians, spread within a run and across runs')
for h in ('dotnet', 'node'):
    across = []
    within = []
    for k in base[h][0][1]:
        meds = [r[k][0] for _, r in base[h]]
        p(f'{h} | {k} | {", ".join(f"{x:.0f}" for x in meds)} | '
          f'within {max((r[k][2] - r[k][1]) / r[k][0] for _, r in base[h]):.2f}')
        across.append((max(meds) / min(meds), k))
        within.append(max((r[k][2] - r[k][1]) / r[k][0] for _, r in base[h]))
    p(f'{h} | greatest across-run factor {max(across)[0]:.2f} ({max(across)[1]}) | greatest within-run spread '
      f'{max(within):.2f}')
p('')

p('## The rule, on the designated runs: least general median over greatest specialised median, same runs')
for h in ('dotnet', 'node'):
    p(f'### {h}')
    for name, g, s in PAIRS:
        gm = [r[g][0] for _, r in final[h]]
        sm = [r[s][0] for _, r in final[h]]
        ratio = min(gm) / max(sm)
        p(f'| {name} | {", ".join(f"{x:.0f}" for x in gm)} | {", ".join(f"{x:.0f}" for x in sm)} | {ratio:.2f} | '
          f'{"clears" if ratio >= MARGIN else "BELOW"} |')

p('')
p('## Against the baseline runs\' general medians (the pairs the baseline measured)')
for h in ('dotnet', 'node'):
    least = None
    for name, g, s in PAIRS:
        if g not in base[h][0][1]:
            p(f'{h} | {name} | not in the baseline')
            continue
        bg = min(r[g][0] for _, r in base[h])
        sm = max(r[s][0] for _, r in final[h])
        p(f'{h} | {name} | {bg:.0f} / {sm:.0f} = {bg / sm:.2f}')
        least = min(least or 1e9, bg / sm)
    p(f'{h} | least {least:.2f}')

p('')
p('## Pooled over every committed default-configuration run of the final tree')
for h in ('dotnet', 'node'):
    p(f'### {h}: {", ".join(n for n, _ in pooled[h])}')
    for name, g, s in PAIRS:
        gm = [r[g][0] for _, r in pooled[h]]
        sm = [r[s][0] for _, r in pooled[h]]
        ratio = min(gm) / max(sm)
        p(f'| {name} | general {min(gm):.0f} to {max(gm):.0f} | specialised {min(sm):.0f} to {max(sm):.0f} | '
          f'{ratio:.2f} | {"clears" if ratio >= MARGIN else "BELOW"} |')

p('')
p('## Same-process ratio, general median over specialised median in one run: least over the pooled runs')
for h in ('dotnet', 'node'):
    for name, g, s in PAIRS:
        rs = [(r[g][0] / r[s][0], n) for n, r in pooled[h]]
        lo = min(rs)
        p(f'{h} | {name} | least {lo[0]:.2f} ({lo[1]}) | greatest {max(rs)[0]:.2f}')

p('')
p('## The specialised fold per run (.NET, pooled runs)')
for name, g, s in PAIRS:
    p(f'{name} | ' + ', '.join(f'{n}: {r[s][0]:.0f}' for n, r in pooled['dotnet']))

p('')
p('## The general product\'s spread across the final tree\'s runs (greatest over least median)')
for h in ('dotnet', 'node'):
    for label, rs in (('designated', final[h]), ('pooled', pooled[h])):
        worst = max(((max(r[g][0] for _, r in rs) / min(r[g][0] for _, r in rs)), name) for name, g, _ in PAIRS)
        p(f'{h} | {label} | {worst[1]} {worst[0]:.2f}')
mm = [r['product: motor * motor (geometric)'][0] for _, r in final['dotnet']]
p(f'dotnet | motor * motor designated general medians {", ".join(f"{x:.0f}" for x in mm)}')

p('')
p('## The typed functions: middle of three medians, base over final, and every median')
for h in ('dotnet', 'node'):
    for f in FUNCTIONS:
        k = 'function: ' + f
        b = [r[k][0] for _, r in base[h]]
        a = [r[k][0] for _, r in final[h]]
        p(f'{h} | {f} | before {", ".join(f"{x:.0f}" for x in b)} | after {", ".join(f"{x:.0f}" for x in a)} | '
          f'{middle(b) / middle(a):.2f}')

p('')
p('## Functions no pair serves, every committed .NET run of each tree')
for f in ('Motor.applyToSphere', 'Sphere.power'):
    k = 'function: ' + f
    p(f'{f} | base: ' + ', '.join(f'{n}: {r[k][0]:.0f}' for n, r in base['dotnet'] + other['pgo-base']))
    p(f'{f} | final: ' + ', '.join(f'{n}: {r[k][0]:.0f}' for n, r in pooled['dotnet']))
    p(f'{f} | final, PGO or tiering off: ' + ', '.join(f'{n}: {r[k][0]:.0f}' for n, r in other['nopgo'] + other['notier']))

p('')
p('## The general product on point * sphere, every committed .NET run')
k = 'product: point * sphere (geometric)'
for label, rs in (('base', base['dotnet'] + other['pgo-base']), ('final', pooled['dotnet']),
                  ('final, PGO or tiering off', other['nopgo'] + other['notier']), ('aggr', other['aggr'])):
    p(f'{label} | ' + ', '.join(f'{n}: {r[k][0]:.0f}' for n, r in rs))

p('')
p('## Checksums: each specialised arm against its general arm, and every row against the baseline')
for h in ('dotnet', 'node'):
    ok = all(r[g][3] == r[s][3] for _, r in pooled[h] for _, g, s in PAIRS)
    b0 = base[h][0][1]
    same = all(r[k][3] == b0[k][3] for _, r in pooled[h] for k in b0)
    p(f'{h} | specialised = general in every pooled run: {ok} | every row = baseline: {same}')

print('\n'.join(out))
