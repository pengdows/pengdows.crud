import sys, zipfile, re
import xml.etree.ElementTree as ET
NS = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main',
      'r': 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'}
def col_idx(ref):
    letters = re.match(r'[A-Z]+', ref).group(0)
    n = 0
    for ch in letters: n = n*26 + (ord(ch)-64)
    return n-1
path = sys.argv[1]
z = zipfile.ZipFile(path)
ss = []
if 'xl/sharedStrings.xml' in z.namelist():
    root = ET.fromstring(z.read('xl/sharedStrings.xml'))
    for si in root.findall('m:si', NS):
        ss.append(''.join(t.text or '' for t in si.iter('{%s}t' % NS['m'])))
wb = ET.fromstring(z.read('xl/workbook.xml'))
rels = ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))
rmap = {r.get('Id'): r.get('Target') for r in rels}
for sh in wb.find('m:sheets', NS):
    name = sh.get('name')
    rid = sh.get('{%s}id' % NS['r'])
    target = rmap[rid].lstrip('/')
    if not target.startswith('xl/'): target = 'xl/' + target
    print(f'=== SHEET: {name} ({target})')
    root = ET.fromstring(z.read(target))
    for row in root.iter('{%s}row' % NS['m']):
        cells = {}
        for c in row.findall('m:c', NS):
            t = c.get('t'); v = c.find('m:v', NS)
            if t == 's' and v is not None: val = ss[int(v.text)]
            elif t == 'inlineStr':
                val = ''.join(x.text or '' for x in c.iter('{%s}t' % NS['m']))
            else: val = v.text if v is not None else ''
            cells[col_idx(c.get('r'))] = val
        if cells:
            mx = max(cells)
            print(' | '.join(cells.get(i, '') for i in range(mx+1)))
