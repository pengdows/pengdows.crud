import re, sqlite3, csv, os
S = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(S, '..', '..', 'docs', 'datatype-unified-crossref.md')

# unified id -> probe semantic type (None = no probe equivalent)
MAP = {
 'B01':'signed 8-bit','B02':'unsigned 8-bit','B03':'signed 16-bit','B04':'unsigned 16-bit',
 'B06':'signed 32-bit','B07':'unsigned 32-bit','B08':'signed 64-bit','B09':'unsigned 64-bit',
 'B10':'signed 128-bit','B11':'unsigned 128-bit','B12':'arbitrary-size integer','B13':'auto-increment integer',
 'B14':None,'B15':'fixed decimal','B16':'decimal floating point','B17':'money / currency',
 'B18':'binary float32','B19':'binary float64','B20':'boolean','B21':'fixed character',
 'B22':'varying character','B23':'unbounded text','B24':'national / Unicode character',
 'B25':'national / Unicode character','B26':'national / Unicode character','B31':None,
 'B32':'fixed binary','B33':'varying binary','B34':'binary LOB','B37':'bit / bitstring',
 'B38':'calendar date','B39':'time without zone','B40':'time with zone','B41':'timestamp without zone',
 'B42':'timestamp with zone','B44':'year-month interval','B45':'day-time interval','B46':'year-only',
 'B47':'rowversion / timestamp token','B48':'UUID / GUID','B51':'IP address','B52':'MAC address',
 'B53':'JSON text / JSON value','B54':'native binary JSON','B55':'BSON','B56':'XML','B57':'variant / dynamic value',
 'B58':'SQL array','B62':'enumeration','B63':'set-valued enum','B64':'range','B65':'multirange',
 'B66':'user-defined enum / domain','B67':'opaque / special database type','B68':'point geometry',
 'B69':'geography / geodetic','B71':'vector embedding','B72':'full-text search vector / query',
 'B73':'hierarchical path','B74':'object identifier / OID','A26':'set-valued enum',
 'A30':'legacy long binary','A38':'second-precision datetime','A42':'year-month interval',
 'A45':'object identifier / OID','A46':'transaction ID','A47':'WAL / log position','A53':'IP address',
 'A54':'IP network / CIDR','A56':'point geometry','A57':'line / linestring geometry','A58':'polygon geometry',
 'A62':'fixed-length array','A63':'list / multiset / set collection','A74':'opaque / special database type',
 'A76':'spatial raster / other extensions','A77':'cursor / table variable',
}
PROBE_DB = {'PG':'PostgreSql','MSSQL':'SqlServer','ORA':'Oracle','FB':'Firebird','CRDB':'CockroachDb',
 'MDB':'MariaDb','MYSQL':'MySql','SQLITE':'Sqlite','DUCK':'DuckDB','YB':'YugabyteDb','TIDB':'TiDb',
 'SNOW':None,'AUR-M':None,'AUR-P':None,'DB2':None,'FLAT':'FlatFile','SS':'SingleStore','ASE':'SybaseASE',
 'SPAN':'Spanner','IDS':None,'HANA':None,'IB':None,'ACE':None}

rows = []
header = None
for line in open(SRC, encoding='utf-8'):
    if line.startswith('| ID |'):
        header = [c.strip() for c in line.strip().strip('|').split('|')]
        continue
    if header and re.match(r'^\| [AB]\d\d \|', line):
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        uid, cat, concept = cells[0], cells[1], cells[2]
        for abbr, status in zip(header[3:], cells[3:]):
            code = re.sub(r'[`\[\]]', '', status).strip()
            rows.append((uid, cat, concept, abbr, PROBE_DB.get(abbr), code[:1] if code else '?', MAP.get(uid)))
    elif line.startswith('## ') or line.startswith('### Databases') :
        if not line.startswith('### Databases'): header = None
        else: header = None
        # header is re-set on the next table header line
print('unified cells parsed:', len(rows))

con = sqlite3.connect(os.path.join(S, 'crossref.db'))
con.executescript('''
DROP TABLE IF EXISTS unified_status;
CREATE TABLE unified_status (
  unified_id TEXT NOT NULL, category TEXT, concept TEXT, db_abbrev TEXT NOT NULL,
  probe_database TEXT, status TEXT NOT NULL, probe_semantic_type TEXT,
  PRIMARY KEY (unified_id, db_abbrev));
''')
con.executemany('INSERT INTO unified_status VALUES (?,?,?,?,?,?,?)', rows)
con.commit()
print(con.execute('select status,count(*) from unified_status group by 1').fetchall())
