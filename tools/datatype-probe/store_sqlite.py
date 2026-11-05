#!/usr/bin/env python3
# Loads the live datatype probe results (results/*.jsonl) and the workbook's documented status
# (crossref.csv, written by summarize.py) into crossref.db. Rebuilt from scratch on each run; the
# JSON-lines files stay the source of truth.
import csv, glob, json, os, sqlite3, sys

S = os.path.dirname(os.path.abspath(__file__))
DB = os.path.join(S, 'crossref.db')

SCHEMA = """
DROP VIEW IF EXISTS accepted_declarations;
DROP VIEW IF EXISTS disagreements;
DROP TABLE IF EXISTS probe_result;
DROP TABLE IF EXISTS workbook_status;
DROP TABLE IF EXISTS run_info;
DROP TABLE IF EXISTS fidelity_types;

CREATE TABLE run_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);

-- The integration type-fidelity list: one row per fidelity type and database. status is 'declared' (the server
-- accepted the declaration), 'widened' (no native type; the next signed type the library binds), or
-- 'unsupported' (reason is the server's refusal).
CREATE TABLE fidelity_types (
    semantic_type   TEXT NOT NULL,
    column_name     TEXT NOT NULL,
    clr_type        TEXT NOT NULL,
    db_type         TEXT NOT NULL,
    database        TEXT NOT NULL,
    status          TEXT NOT NULL CHECK (status IN ('declared', 'widened', 'unsupported')),
    declaration     TEXT,
    provider_type   TEXT,
    reason          TEXT,
    PRIMARY KEY (column_name, database)
);

-- One row per candidate declaration tried. 'stage' says where it failed ('create' or 'select');
-- NULL when accepted. not_probed marks a semantic type with no candidate list.
CREATE TABLE probe_result (
    database        TEXT NOT NULL,
    semantic_type   TEXT NOT NULL,
    declaration     TEXT,
    accepted        INTEGER NOT NULL CHECK (accepted IN (0, 1)),
    not_probed      INTEGER NOT NULL DEFAULT 0 CHECK (not_probed IN (0, 1)),
    stage           TEXT,
    provider_type   TEXT,
    clr_type        TEXT,
    error           TEXT,
    PRIMARY KEY (database, semantic_type, declaration)
);

-- The workbook's documented status per (semantic type, database), for comparison only.
CREATE TABLE workbook_status (
    database        TEXT NOT NULL,
    semantic_type   TEXT NOT NULL,
    status          TEXT,
    spelling        TEXT,
    PRIMARY KEY (database, semantic_type)
);

CREATE VIEW accepted_declarations AS
    SELECT database, semantic_type, declaration, provider_type, clr_type
    FROM probe_result WHERE accepted = 1;

-- Cells where the live run and the workbook disagree, with the reason.
CREATE VIEW disagreements AS
    SELECT p.database, p.semantic_type, w.status AS workbook_status, w.spelling AS workbook_spelling,
           CASE
             WHEN p.accepted = 1 AND w.status = 'X' THEN 'probe accepts, workbook says X'
             WHEN p.accepted = 1 AND w.status = '?' THEN 'workbook unverified, probe resolves'
             WHEN p.accepted = 0 AND w.status = 'N' THEN 'workbook says N, no candidate accepted'
           END AS disagreement
    FROM (SELECT database, semantic_type, MAX(accepted) AS accepted
          FROM probe_result WHERE not_probed = 0 GROUP BY database, semantic_type) p
    JOIN workbook_status w USING (database, semantic_type)
    WHERE (p.accepted = 1 AND w.status IN ('X', '?'))
       OR (p.accepted = 0 AND w.status = 'N');
"""


def main():
    if os.path.exists(DB):
        os.remove(DB)
    con = sqlite3.connect(DB)
    con.executescript(SCHEMA)

    probe_rows = []
    container_errors = {}
    for path in sorted(glob.glob(os.path.join(S, 'results', '*.jsonl'))):
        for line in open(path, encoding='utf-8'):
            r = json.loads(line)
            if 'containerError' in r:
                container_errors[r['db']] = r['containerError']
                continue
            if r.get('notProbed'):
                probe_rows.append((r['db'], r['type'], None, 0, 1, None, None, None, None))
                continue
            probe_rows.append((
                r['db'], r['type'], r['decl'], 1 if r['accepted'] else 0, 0,
                r.get('stage'), r.get('providerType'), r.get('clrType'), r.get('error'),
            ))
    con.executemany('INSERT OR REPLACE INTO probe_result VALUES (?,?,?,?,?,?,?,?,?)', probe_rows)

    with open(os.path.join(S, 'crossref.csv'), encoding='utf-8') as f:
        reader = csv.DictReader(f)
        wb_rows = [(r['database'], r['semantic_type'], r['workbook_status'], r['workbook_spelling'])
                   for r in reader]
    # crossref.csv has one row per (type, database): take the first, since the status is the same.
    seen = set()
    unique = []
    for row in wb_rows:
        key = (row[0], row[1])
        if key not in seen:
            seen.add(key)
            unique.append(row)
    con.executemany('INSERT INTO workbook_status VALUES (?,?,?,?)', unique)

    fidelity_path = os.path.join(S, 'fidelity_types.csv')
    if os.path.exists(fidelity_path):
        with open(fidelity_path, encoding='utf-8', newline='') as f:
            rows = list(csv.DictReader(f))
        con.executemany('INSERT INTO fidelity_types VALUES (?,?,?,?,?,?,?,?,?)',
                        [(r['semantic_type'], r['column'], r['clr_type'], r['db_type'], r['database'],
                          r['status'], r['declaration'] or None, r['provider_type'] or None,
                          r['reason'] or None) for r in rows])

    con.executemany('INSERT INTO run_info VALUES (?,?)', [
        ('probe_rows', str(len(probe_rows))),
        ('container_errors', json.dumps(container_errors)),
    ])
    con.commit()

    n_acc = con.execute('SELECT COUNT(*) FROM probe_result WHERE accepted = 1').fetchone()[0]
    n_dis = con.execute('SELECT COUNT(*) FROM disagreements').fetchone()[0]
    print(f'wrote {DB}: {len(probe_rows)} probe rows, {n_acc} accepted, {n_dis} disagreements')
    con.close()


if __name__ == '__main__':
    sys.exit(main())
