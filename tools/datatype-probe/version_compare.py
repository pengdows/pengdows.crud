import json, glob, os, collections, sqlite3, csv
S = os.path.dirname(os.path.abspath(__file__))
# version label per run: baseline = the default (latest) image in results/
runs = collections.defaultdict(dict)   # (db, sem) -> {version: set(decl)}
def add(db, version, path):
    for line in open(path, encoding='utf-8'):
        r = json.loads(line)
        if 'containerError' in r or r.get('notProbed'): continue
        key = (r['db'], r['type'])
        runs[key].setdefault(version, set())
        if r.get('accepted'): runs[key][version].add(r['decl'])
for p in glob.glob(os.path.join(S, 'results', '*.jsonl')):
    db = os.path.basename(p)[:-6]
    add(db, 'latest', p)
for p in glob.glob(os.path.join(S, 'results-versions', '*.jsonl')):
    db, tag = os.path.basename(p)[:-6].split('__', 1)
    add(db, tag, p)

# a type is version-dependent in a database when some version accepts a declaration another version refuses
rows = []
for (db, sem), versions in sorted(runs.items()):
    if len(versions) < 2: continue
    per_version_accepts = {v: len(s) > 0 for v, s in versions.items()}
    decls_all = set().union(*versions.values())
    dependent = [d for d in decls_all if len({d in s for s in versions.values()}) > 1]
    if dependent:
        rows.append((db, sem, '; '.join(sorted(dependent))[:200], ', '.join(f'{v}:{len(s)}' for v, s in sorted(versions.items()))))
with open(os.path.join(S, 'version_dependence.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f); w.writerow(['database', 'semantic_type', 'version_dependent_declarations', 'accepted_per_version'])
    w.writerows(rows)
print('version-dependent (database, type) pairs:', len(rows))
c = collections.Counter(r[0] for r in rows)
print(dict(c))
