from pathlib import Path
import sys,re,datetime,hashlib,collections
import xml.etree.ElementTree as E

p=Path(sys.argv[1]);value=sys.argv[2];expected=sys.argv[3]
raw=p.read_bytes();assert hashlib.sha256(raw).hexdigest()==expected,'原本が更新されたため中止します'
root=E.fromstring(raw);indices={'StageLaser':20,'StageLaserController':22};counts=collections.Counter();allowed=set()
for l in root.findall('Layer'):
    if l.findtext('ClassName')!='StageLaserTimelineLayer':continue
    for f in l.findall('Frame'):
        for t in f.findall('Bone/Transform'):
            kind=t.findtext('Type');assert kind in indices,'未対応のレーザー型です'
            allowed.add(id(t.findall('Value')[indices[kind]]))
def edit_layer(m):
    if E.fromstring(m.group()).findtext('ClassName')!='StageLaserTimelineLayer':return m.group()
    def edit_transform(tm):
        t=E.fromstring(tm.group());kind=t.findtext('Type');idx=indices[kind]
        v=t.findall('Value');assert len(v)=={'StageLaser':24,'StageLaserController':37}[kind],'値数が一致しません'
        match=list(re.finditer(r'<Value>[^<]*</Value>',tm.group()))[idx];counts[kind]+=1
        return tm.group()[:match.start()]+'<Value>'+value+'</Value>'+tm.group()[match.end():]
    return re.sub(r'<Transform>.*?</Transform>',edit_transform,m.group(),flags=re.S)
out=re.sub(r'<Layer>.*?</Layer>',edit_layer,raw.decode('utf-8'),flags=re.S).encode('utf-8')
after=E.fromstring(out);a,b=list(root.iter()),list(after.iter());assert len(a)==len(b),'要素数が変わりました'
for x,y in zip(a,b):
    assert x.tag==y.tag and x.attrib==y.attrib and x.tail==y.tail,'対象外の構造が変わりました'
    if id(x) in allowed:assert y.text==value,'オフセット範囲が一致しません'
    else:assert x.text==y.text,'対象外の値が変わりました'
assert sum(counts.values())==len(allowed),'変更件数が一致しません'
print('検証済み:',dict(counts),'オフセット範囲',value,'対象外差分なし')
if '--apply' in sys.argv and out!=raw:
    assert p.read_bytes()==raw,'原本が更新されたため中止します'
    backup=p.with_name(p.name+'.bak_laser_offset_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    with backup.open('xb') as f:f.write(raw)
    assert p.read_bytes()==raw,'原本が更新されたため中止します'
    p.write_bytes(out);assert p.read_bytes()==out,'保存結果が一致しません'
    E.parse(p);print('バックアップ:',backup)
