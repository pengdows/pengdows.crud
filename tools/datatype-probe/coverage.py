# Coverage of every probe semantic type against every always-on database: is there a CLR type and a DbType for
# it (decided in docs/datatype-clr-mapping-proposal.md), a sample value in the fidelity spec, and what each
# database does with it (declared / widened / unsupported, with the server's refusal).
import csv, json, glob, os, sqlite3
S = os.path.dirname(os.path.abspath(__file__))

# semantic type -> (CLR type, DbType name, or None when the type has no column form)
MAP = {
 'signed 8-bit': ('sbyte','SByte'), 'signed 16-bit': ('short','Int16'), 'signed 32-bit': ('int','Int32'),
 'signed 64-bit': ('long','Int64'), 'signed 128-bit': ('Int128','Object'), 'unsigned 8-bit': ('byte','Byte'),
 'unsigned 16-bit': ('ushort','UInt16'), 'unsigned 32-bit': ('uint','UInt32'), 'unsigned 64-bit': ('ulong','UInt64'),
 'unsigned 128-bit': ('UInt128','Object'), 'arbitrary-size integer': ('BigInteger','Object'),
 'auto-increment integer': (None,None), 'fixed decimal': ('decimal','Decimal'),
 'decimal floating point': ('decimal','Decimal'), 'binary float32': ('float','Single'),
 'binary float64': ('double','Double'), 'money / currency': ('decimal','Decimal'),
 'bit / bitstring': ('BitArray','Object'), 'boolean': ('bool','Boolean'), 'fixed character': ('string','String'),
 'varying character': ('string','String'), 'unbounded text': ('string','String'),
 'national / Unicode character': ('string','String'), 'long text LOB': ('string','String'),
 'enumeration': ('string','String'), 'set-valued enum': ('string','String'), 'fixed binary': ('byte[]','Binary'),
 'varying binary': ('byte[]','Binary'), 'binary LOB': ('byte[]','Binary'), 'legacy long binary': ('byte[]','Binary'),
 'rowversion / timestamp token': ('byte[]','Binary'), 'calendar date': ('DateOnly','Date'),
 'time without zone': ('TimeOnly','Time'), 'time with zone': ('DateTimeOffset','DateTimeOffset'),
 'timestamp without zone': ('DateTime','DateTime2'), 'timestamp with zone': ('DateTimeOffset','DateTimeOffset'),
 'local-zone timestamp': ('DateTime','DateTime'), 'second-precision datetime': ('DateTime','DateTime'),
 'year-only': ('short','Int16'), 'year-month interval': ('IntervalYearMonth','Object'),
 'day-time interval': ('IntervalDaySecond','Object'), 'generic interval': ('PostgreSqlInterval','Object'),
 'UUID / GUID': ('Guid','Guid'), 'row identifier / ROWID': ('string','String'),
 'object identifier / OID': ('uint','UInt32'), 'transaction ID': ('ulong','UInt64'),
 'WAL / log position': ('string','String'), 'JSON text / JSON value': ('string','String'),
 'native binary JSON': ('string','String'), 'XML': ('string','String'), 'BSON': ('byte[]','Binary'),
 'variant / dynamic value': ('object','Object'), 'IP address': ('IPAddress','Object'),
 'IP network / CIDR': ('Cidr','Object'), 'MAC address': ('MacAddress','Object'),
 'point geometry': ('Geometry','Object'), 'line / linestring geometry': ('Geometry','Object'),
 'polygon geometry': ('Geometry','Object'), 'general geometry': ('Geometry','Object'),
 'geography / geodetic': ('Geography','Object'), 'SQL array': ('int[]','Object'),
 'fixed-length array': ('int[]','Object'), 'list / multiset / set collection': ('List<int>','Object'),
 'map / dictionary': ('Dictionary<string,object>','Object'), 'struct / composite / row': ('object','Object'),
 'tagged union / variant': ('object','Object'), 'range': ('Range<int>','Object'),
 'multirange': ('Range<int>[]','Object'), 'user-defined enum / domain': ('string','String'),
 'user-defined scalar / object': ('object','Object'), 'full-text search vector / query': ('string','String'),
 'vector embedding': ('float[]','Object'), 'hierarchical path': ('HierarchyId','Object'),
 'opaque / special database type': ('object','Object'), 'external-file reference': ('string','String'),
 'spatial raster / other extensions': ('object','Object'), 'cursor / table variable': (None,None),
 'specialized security label': ('string','String'),
}
# the fidelity spec's current samples (TypeFidelityRows.cs is generated from gen_fidelity.py SPEC)
spec_src = open(os.path.join(S, 'gen_fidelity.py'), encoding='utf-8').read()
in_spec = {s for s in MAP if f'"{s}"' in spec_src}

dbs = ['PostgreSql','SqlServer','Oracle','Firebird','CockroachDb','MariaDb','MySql','Sqlite','DuckDB','YugabyteDb',
       'TiDb','SingleStore','SybaseASE','Spanner','FlatFile']
c = sqlite3.connect(os.path.join(S, 'crossref.db'))
status = {}
for r in c.execute("select semantic_type, database, status, declaration from fidelity_types"):
    status[(r[0], r[1])] = (r[2], r[3])
ok = []
rows = []
for sem in MAP:
    clr, dbt = MAP[sem]
    row = [sem, clr or '', dbt or '', 'yes' if sem in in_spec else 'no']
    gaps = []
    for db in dbs:
        st = status.get((sem, db))
        if st is None:
            row.append('not in fidelity_types'); gaps.append(db)
        else:
            row.append(st[0] + (f':{st[1]}' if st[1] else ''))
            if st[0] == 'unsupported': gaps.append(db)
    row.append(str(len(gaps)))
    rows.append(row)
with open(os.path.join(S, 'fidelity_coverage.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f); w.writerow(['semantic_type','clr_type','db_type','in_fidelity_spec'] + dbs + ['databases_without_column'])
    w.writerows(rows)
no_clr = [s for s in MAP if MAP[s][0] is None]
not_in_spec = [s for s in MAP if s not in in_spec and MAP[s][0] is not None]
print('semantic types:', len(MAP))
print('with no CLR/DbType (not a column type):', no_clr)
print('with a CLR and DbType but no fidelity sample yet:', len(not_in_spec))
print('in the fidelity spec now:', len(in_spec))
