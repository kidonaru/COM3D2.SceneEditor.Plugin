import pathlib, json, re, math, hashlib, datetime, sys
import xml.etree.ElementTree as E

p = pathlib.Path(sys.argv[1])
raw = p.read_bytes()
digest = hashlib.sha256(raw).hexdigest()
r = E.fromstring(raw)
beams = json.loads(pathlib.Path(__file__).with_name('laser_emission_measurements.json').read_text(encoding='utf-8'))
models = {t.findtext('Name'): t for l in r.findall('Layer') if l.findtext('ClassName') == 'ModelTimelineLayer' for f in l.findall('Frame') for t in f.findall('Bone/Transform')}
for b in beams:
    name = b['name'].replace('kdnr_midnight_beam_light_i_', 'kdnr_midnight_beam_light_i_.menu', 1)
    v = [float(x.text) for x in models[name].findall('Value')]
    assert all(abs(x-y)<0.00001 for x,y in zip(v[:10], b['position']+b['rotation']+b['scale'])), 'ゲームとXMLのモデル配置が一致しません'
layers = [l for l in r.findall('Layer') if l.findtext('ClassName') == 'StageLaserTimelineLayer' and l.findtext('SlotNo') == '0']
assert len(layers)==1, '対象レイヤーが一意ではありません'
targets = {}
mapping = {}
for f in layers[0].findall('Frame'):
    for t in f.findall('Bone/Transform'):
        if t.findtext('Type') != 'StageLaserController': continue
        name=t.findtext('Name'); pos=[float(x.text) for x in t.findall('Value')[:3]]
        if name not in mapping:
            distances=sorted((math.dist(pos,b['emission']), b['name'], b) for b in beams)
            # 中央上方の等距離候補は登録名順で決定する。
            best=min((d for d in distances if abs(d[0]-distances[0][0])<0.00001),key=lambda d:d[1])
            mapping[name]=best[2]
        targets[(f.findtext('FrameNo'), name)] = [format(x,'.9g') for x in mapping[name]['emission']]
assert len(targets)==128 and len(mapping)==6, '対象キー数が想定と違います'
text=raw.decode('utf-8')
count=0
def layer_replace(m):
    el=E.fromstring(m.group())
    if el.findtext('ClassName')!='StageLaserTimelineLayer' or el.findtext('SlotNo')!='0': return m.group()
    def frame_replace(fm):
        frame=E.fromstring(fm.group()).findtext('FrameNo')
        def transform_replace(tm):
            global count
            t=E.fromstring(tm.group()); key=(frame,t.findtext('Name'))
            if t.findtext('Type')!='StageLaserController': return tm.group()
            values=targets[key]; count+=1
            head,tail=tm.group().split('<Value>',1)
            block='<Value>'+tail
            i=iter(values)
            return head+re.sub(r'<Value>[^<]*</Value>',lambda vm:'<Value>'+next(i)+'</Value>',block,count=3)
        return re.sub(r'<Transform>.*?</Transform>',transform_replace,fm.group(),flags=re.S)
    return re.sub(r'<Frame>.*?</Frame>',frame_replace,m.group(),flags=re.S)
out=re.sub(r'<Layer>.*?</Layer>',layer_replace,text,flags=re.S).encode('utf-8')
assert count==len(targets), '置換件数が一致しません'
after=E.fromstring(out)
original_nodes=list(r.iter()); new_nodes=list(after.iter())
assert len(original_nodes)==len(new_nodes), 'XML構造が変わりました'
allowed=set()
for f in layers[0].findall('Frame'):
    for t in f.findall('Bone/Transform'):
        if t.findtext('Type')=='StageLaserController': allowed.update(id(v) for v in t.findall('Value')[:3])
changes=0
for a,b in zip(original_nodes,new_nodes):
    assert a.tag==b.tag and a.attrib==b.attrib and a.tail==b.tail, '対象外の構造変更があります'
    if a.text!=b.text:
        assert id(a) in allowed, '対象外の値が変わりました'
        changes+=1
for name,b in mapping.items():print(name,'=>',b['name'],b['emission'])
print('検証済み:',count,'キー、',changes,'座標値')
if '--apply' in sys.argv:
    assert hashlib.sha256(p.read_bytes()).hexdigest()==digest, '原本が変更されたため中止します'
    backup=p.with_name(p.name+'.bak_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    with backup.open('xb') as f:f.write(raw)
    assert p.read_bytes()==raw, '原本が変更されたため中止します'
    p.write_bytes(out)
    assert p.read_bytes()==out, '保存結果が一致しません'
    E.parse(p)
    print('バックアップ:',backup)
