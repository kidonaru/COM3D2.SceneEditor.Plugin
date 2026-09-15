from pathlib import Path
import sys,json,datetime,re,hashlib,shutil
import xml.etree.ElementTree as E

p=Path(sys.argv[1]);skill=Path(sys.argv[2]);stage=Path(__file__).parent
cfg=json.loads((stage/'beam-sync-kasou.json').read_text(encoding='utf-8'))
names={x['material'] for x in cfg['mappings']};limit=cfg['emission_max']
raw=p.read_bytes();root=E.fromstring(raw);count=0
def edit_layer(m):
    l=E.fromstring(m.group())
    if l.findtext('ClassName')!='ModelMaterialTimelineLayer' or l.findtext('SlotNo')!=str(cfg['slot']):return m.group()
    def edit_transform(tm):
        global count
        t=E.fromstring(tm.group())
        if t.findtext('Name') not in names:return tm.group()
        assert t.findtext('Type')=='ModelMaterial' and len(t.findall('Value'))==49,'材質の形式が違います'
        if float(t.findall('Value')[41].text)<=limit:return tm.group()
        v=list(re.finditer(r'<Value>[^<]*</Value>',tm.group()))[41];count+=1
        return tm.group()[:v.start()]+'<Value>'+format(limit,'.9g')+'</Value>'+tm.group()[v.end():]
    return re.sub(r'<Transform>.*?</Transform>',edit_transform,m.group(),flags=re.S)
out=re.sub(r'<Layer>.*?</Layer>',edit_layer,raw.decode('utf-8'),flags=re.S).encode('utf-8');after=E.fromstring(out)
allowed={id(t.findall('Value')[41]) for l in root.findall('Layer') if l.findtext('ClassName')=='ModelMaterialTimelineLayer' and l.findtext('SlotNo')==str(cfg['slot']) for f in l.findall('Frame') for t in f.findall('Bone/Transform') if t.findtext('Name') in names}
a,b=list(root.iter()),list(after.iter());assert len(a)==len(b),'要素数が変わりました'
for x,y in zip(a,b):
    assert x.tag==y.tag and x.attrib==y.attrib and x.tail==y.tail,'対象外の構造が変わりました'
    if x.text!=y.text:assert id(x) in allowed and float(x.text)>limit and float(y.text)==limit,'対象外の値が変わりました'
manifest=json.loads((stage/'manifest.json').read_text(encoding='utf-8'))
for name,digest in manifest.items():assert hashlib.sha256((skill/name).read_bytes()).hexdigest()==digest,'スキルが更新されたため中止します'
print('検証済み: 上限',limit,'変更キー',count,'対象外差分なし')
if '--apply' in sys.argv:
    assert p.read_bytes()==raw,'XMLが更新されたため中止します'
    stamp=datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f')
    backup=p.with_name(p.name+'.bak_emission_cap_'+stamp)
    with backup.open('xb') as f:f.write(raw)
    assert p.read_bytes()==raw,'XMLが更新されたため中止します'
    p.write_bytes(out);assert p.read_bytes()==out,'保存結果が一致しません'
    for name,digest in manifest.items():
        dest=skill/name
        assert hashlib.sha256(dest.read_bytes()).hexdigest()==digest,'スキルが更新されたため中止します'
        shutil.copyfile(dest,dest.with_name(dest.name+'.bak_cap_'+stamp))
        shutil.copyfile(stage/dest.name,dest)
        assert dest.read_bytes()==(stage/dest.name).read_bytes(),'スキルの保存結果が一致しません'
    print('バックアップ:',backup)
