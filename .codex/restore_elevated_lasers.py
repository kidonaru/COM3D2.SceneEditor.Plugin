from pathlib import Path
import sys, re, datetime
import xml.etree.ElementTree as E

p=Path(sys.argv[1])
source=p.with_name(p.name+'.bak_20260908_003744_406063')
raw=p.read_bytes()
old=E.fromstring(source.read_bytes())
current=E.fromstring(raw)
names={'StageLaserController (4)', 'StageLaserController (5)'}
def targets(root):
    return {(f.findtext('FrameNo'),t.findtext('Name')):t for l in root.findall('Layer') if l.findtext('ClassName')=='StageLaserTimelineLayer' and l.findtext('SlotNo')=='0' for f in l.findall('Frame') for t in f.findall('Bone/Transform') if t.findtext('Type')=='StageLaserController' and t.findtext('Name') in names}
before=targets(old)
now=targets(current)
assert before.keys()==now.keys(), '復元対象のキー構成が変わっています'
assert len(before)==24, '対象キー数が一致しません'
count=0
def layer_replace(m):
    l=E.fromstring(m.group())
    if l.findtext('ClassName')!='StageLaserTimelineLayer' or l.findtext('SlotNo')!='0':return m.group()
    def frame_replace(fm):
        frame=E.fromstring(fm.group()).findtext('FrameNo')
        def transform_replace(tm):
            global count
            t=E.fromstring(tm.group()); key=(frame,t.findtext('Name'))
            if key not in before or t.findtext('Type')!='StageLaserController':return tm.group()
            values=iter(v.text for v in before[key].findall('Value')[:3])
            count+=1
            return re.sub(r'<Value>[^<]*</Value>',lambda v:'<Value>'+next(values)+'</Value>',tm.group(),count=3)
        return re.sub(r'<Transform>.*?</Transform>',transform_replace,fm.group(),flags=re.S)
    return re.sub(r'<Frame>.*?</Frame>',frame_replace,m.group(),flags=re.S)
out=re.sub(r'<Layer>.*?</Layer>',layer_replace,raw.decode('utf-8'),flags=re.S).encode('utf-8')
after=E.fromstring(out)
assert count==24, '置換件数が一致しません'
for key,t in targets(after).items():
    assert [v.text for v in t.findall('Value')[:3]]==[v.text for v in before[key].findall('Value')[:3]], '元の座標と一致しません'
allowed={id(v) for t in now.values() for v in t.findall('Value')[:3]}
a=list(current.iter()); b=list(after.iter())
assert len(a)==len(b), '要素数が変わりました'
for x,y in zip(a,b):
    assert x.tag==y.tag and x.attrib==y.attrib and x.tail==y.tail, '対象外の構造が変わりました'
    assert x.text==y.text or id(x) in allowed, '対象外の値が変わりました'
print('検証済み: コントローラー(4)(5)の24キーを元のXYZへ復元。対象外差分なし。')
if '--apply' in sys.argv:
    assert p.read_bytes()==raw, '原本が変更されたため中止します'
    backup=p.with_name(p.name+'.bak_before_restore_height_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    with backup.open('xb') as f:f.write(raw)
    assert p.read_bytes()==raw, '原本が変更されたため中止します'
    p.write_bytes(out)
    assert p.read_bytes()==out, '保存結果が一致しません'
    E.parse(p)
    print('バックアップ:',backup.name)
