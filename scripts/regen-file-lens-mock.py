"""Rebuild wwwroot/filelens-findings.mock.json from a FRESH `rig derive --format tsv` dump.

The mock is line-anchored, so it is only valid for the store it was derived from; the client now refuses to
render it against any other store. Regenerating is therefore part of re-indexing, until the server serves
tiers 1-3 for real and this file disappears.

Column layouts were read off the real dump, not guessed:
  hazard        \t type \t confidence \t subtype \t key \t enclosing \t file \t line \t detail
  amplification \t type \t confidence \t subtype \t key \t enclosing \t file \t line \t iteration \t provider \t operation
  cross_method_amplification \t anchorFile \t anchorLine \t caller \t callee \t iterationKind \t iterationDetail
                             \t keyToken \t argIndex \t iteratedSource \t witnessMethod \t witnessFile
                             \t witnessLine \t witnessProvider \t witnessOperation \t witnessResource
                             \t witnessDepth \t anchorGuards \t ... (dispatch/recursive/keyPath/elementType)
"""

import io, json, os, collections

TSV = r'C:\Users\dkushnir\AppData\Local\Temp\claude\C--Git\17be887c-4e94-4dc1-9a91-4cde34e34734\scratchpad\derive-fresh.tsv'
OUT = r'C:\Git\coderig\src\Rig.Cli\wwwroot\filelens-findings.mock.json'
STORE = '66908ed06925-dirty'
MAX_FILES = 24
MAX_ANCHORS_PER_FILE = 60

# Short display name for a DocID: `M:Ns.Type.Member(args)` -> `Type.Member`.
def short(doc):
    if not doc:
        return ''
    s = doc[2:] if len(doc) > 2 and doc[1] == ':' else doc
    s = s.split('(')[0]
    parts = s.split('.')
    return '.'.join(parts[-2:]) if len(parts) >= 2 else s


def cell(row, i):
    return row[i] if i < len(row) else ''


haz = collections.defaultdict(list)
amp = collections.defaultdict(list)
anch = collections.defaultdict(dict)  # file -> (line, caller, callee) -> row (nearest witness wins)

with io.open(TSV, encoding='utf-8', errors='replace') as fh:
    for raw in fh:
        row = raw.rstrip('\n').rstrip('\r').split('\t')
        kind = row[0]
        if kind == 'hazard' and len(row) >= 9:
            f = cell(row, 6)
            haz[f].append({
                'type': cell(row, 1), 'confidence': cell(row, 2), 'subtype': cell(row, 3),
                'key': cell(row, 4), 'enclosing': short(cell(row, 5)),
                'line': int(cell(row, 7) or 0), 'detail': cell(row, 8),
            })
        elif kind == 'amplification' and len(row) >= 11:
            f = cell(row, 6)
            amp[f].append({
                'type': cell(row, 1), 'confidence': cell(row, 2), 'subtype': cell(row, 3),
                'key': cell(row, 4), 'enclosing': short(cell(row, 5)),
                'line': int(cell(row, 7) or 0), 'iteration': cell(row, 8),
                'provider': cell(row, 9), 'operation': cell(row, 10),
            })
        elif kind == 'cross_method_amplification' and len(row) >= 17:
            f = cell(row, 1)
            try:
                line = int(cell(row, 2) or 0)
                depth = int(cell(row, 16) or 0)
            except ValueError:
                continue
            caller, callee = cell(row, 3), cell(row, 4)
            # AnchorFinding grain: one row per anchor call site, NEAREST witness as the representative.
            k = (line, caller, callee)
            prev = anch[f].get(k)
            if prev is not None and prev['witnessDepth'] <= depth:
                continue
            anch[f][k] = {
                'line': line, 'caller': short(caller), 'callee': short(callee),
                'iterationKind': cell(row, 5), 'iterationDetail': cell(row, 6),
                'key': cell(row, 7), 'iteratedSource': cell(row, 9),
                'witnessMethod': short(cell(row, 10)), 'witnessLine': int(cell(row, 12) or 0),
                'witnessProvider': cell(row, 13), 'witnessOperation': cell(row, 14),
                'witnessResource': cell(row, 15), 'witnessDepth': depth,
            }

files = set(haz) | set(amp) | set(anch)
files = {f for f in files if f and f.lower().endswith('.cs')}
scored = sorted(files, key=lambda f: -(len(haz[f]) * 3 + len(amp[f]) * 2 + len(anch[f])))
chosen = scored[:MAX_FILES]

doc = {
    '_comment': (
        'MOCK dataset - real rows lifted from `rig derive --format tsv` on the MedDBase store ({store}), '
        'reshaped into the per-file findings payload the file lens NEEDS but /api/file-effects does not yet '
        'return. Every finding is LINE-ANCHORED, so it is valid ONLY for that store: the client compares '
        '`store` below against the store being viewed and suppresses all findings on a mismatch rather than '
        'mis-anchoring them. Regenerate with scripts (see the backlog card) after every re-index, or delete '
        'this file once the server ships hazards / amplifications / anchors on /api/file-effects.'
    ).format(store=STORE),
    'store': STORE,
    'files': {},
}
for f in sorted(chosen):
    anchors = sorted(anch[f].values(), key=lambda a: (a['witnessDepth'], a['line']))[:MAX_ANCHORS_PER_FILE]
    doc['files'][f] = {
        'hazards': sorted(haz[f], key=lambda r: r['line']),
        'amplifications': sorted(amp[f], key=lambda r: r['line']),
        'anchors': sorted(anchors, key=lambda r: r['line']),
    }

with io.open(OUT, 'w', encoding='utf-8', newline='\n') as fh:
    json.dump(doc, fh, separators=(',', ':'), ensure_ascii=False)

print('store', STORE)
print('files', len(doc['files']), 'size', os.path.getsize(OUT))
for f, v in doc['files'].items():
    print('  %-42s haz=%-3d amp=%-4d anchors=%d' % (
        os.path.basename(f.replace(chr(92), '/')), len(v['hazards']), len(v['amplifications']), len(v['anchors'])))
