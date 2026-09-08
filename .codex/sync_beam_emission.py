from pathlib import Path
import sys, re, math, bisect, collections, datetime
import xml.etree.ElementTree as E

p=Path(sys.argv[1]); raw=p.read_bytes(); root=E.fromstring(raw)
fps=float(root.findtext('FrameRate')); end=int(root.findtext('MaxFrameNo'))
assert root.findtext('IsLoopAnm')=='false', 'ループ設定には未対応です'
delay=root.findtext('SingleFrameType') in ('Delay','Advance')
def layer(r,name):
    a=[l for l in r.findall('Layer') if l.findtext('ClassName')==name and l.findtext('SlotNo')=='0']
    assert len(a)==1, '対象レイヤーが一意ではありません'
    return a[0]
laser=layer(root,'StageLaserTimelineLayer'); material=layer(root,'ModelMaterialTimelineLayer')
tracks=collections.defaultdict(list)
for f in laser.findall('Frame'):
    for t in f.findall('Bone/Transform'):
        tracks[t.findtext('Name')].append((int(f.findtext('FrameNo')),t))
for rows in tracks.values(): rows.sort(key=lambda x:x[0])

class Curve:
    def __init__(self,rows,index):
        self.rows=rows; self.frames=[x[0] for x in rows]; self.index=index
        self.values=[[float(v.text) for v in t.findall('Value')] for _,t in rows]
        self.tangents=[]
        for k,(frame,t) in enumerate(rows):
            previous=rows[k-1][0] if k else -1
            following=rows[k+1][0] if k+1<len(rows) else end+1
            x=self.values[k][index]
            x0=self.values[k-1][index] if k else x
            x2=self.values[k+1][index] if k+1<len(rows) else x
            if delay and frame-previous==1:x0=x
            if delay and following-frame==1:x2=x
            dx0=x-x0;dx1=x2-x
            if dx0==0 and dx1!=0:dx0=math.copysign(.01,dx1)
            elif dx1==0 and dx0!=0:dx1=math.copysign(.01,dx0)
            base0=dx0*fps/(frame-previous);base1=dx1*fps/(following-frame)
            pair=[]
            for side,base in [('In',base0),('Out',base1)]:
                data=t.findall(side+'Tangents/Value')
                norm=float(data[index].text) if data else 0.
                smooth=(int(t.findtext(side+'SmoothBit','0'))>>index)&1
                if smooth and base0!=0 and base1!=0:norm=((x2-x0)*fps/(following-previous))/base
                pair.append(norm*base)
            self.tangents.append(pair)
    def state(self,frame):
        k=max(0,bisect.bisect_right(self.frames,frame)-1)
        return self.values[k]
    def at(self,frame):
        k=max(0,bisect.bisect_right(self.frames,frame)-1)
        v=self.values[k][self.index]
        if k+1==len(self.frames) or frame==self.frames[k]:return v
        width=self.frames[k+1]-self.frames[k]; u=(frame-self.frames[k])/width
        return (2*u**3-3*u*u+1)*v+(u**3-2*u*u+u)*width/fps*self.tangents[k][1]+(-2*u**3+3*u*u)*self.values[k+1][self.index]+(u**3-u*u)*width/fps*self.tangents[k+1][0]

# 光源位置を合わせた床面の4コントローラーに対応するモデル。
mapping={0:11,1:15,2:8,3:18}
existing={(int(f.findtext('FrameNo')),t.findtext('Name')):t for f in material.findall('Frame') for t in f.findall('Bone/Transform')}
reference_name=next(name for frame,name in existing if frame==1419 and '.menu (8)/' in name)
off=float(existing[(1412,reference_name)].findall('Value')[41].text)
on=float(existing[(1419,reference_name)].findall('Value')[41].text)
assert off==0 and on==1, '参考キーの明るさが変わっています'
planned={}; samples={}; stats=[]
for ci,mi in mapping.items():
    name=next(name for frame,name in existing if frame==0 and f'.menu ({mi})/' in name)
    controller=Curve(tracks[f'StageLaserController ({ci})'],15)
    children=[Curve(rows,13) for n,rows in tracks.items() if n.startswith(f'StageLaser ({ci},')]
    assert children, '子レーザーがありません'
    data=[]
    for frame in range(end+1):
        c=controller.state(frame); light=[]
        for child in children:
            state=child.state(frame)
            visible=c[14] if c[29]!=0 else state[12]
            intensity=controller.at(frame) if c[28]!=0 else child.at(frame)
            light.append(max(0,intensity) if visible!=0 else 0)
        data.append(float(format(off+(on-off)*max(light),'.9g')))
    frames={0,end}
    # 変化区間は毎フレームを記録し、一定区間は両端だけ残す。
    for f in range(1,end+1):
        if data[f]!=data[f-1]:frames.update((f-1,f))
    frames.update(f for f,n in existing if n==name)
    for frame in sorted(frames):
        if (frame,name) in existing and frame!=0:
            assert abs(float(existing[(frame,name)].findall('Value')[41].text)-data[frame])<1e-7, '既存の参考キーと算出値が一致しません'
        planned[(frame,name)]=format(data[frame],'.9g')
    samples[name]=data
    stats.append((mi,len(frames),min(data),max(data)))

text=raw.decode('utf-8'); newline='\r\n' if '\r\n' in text else '\n'
layer_blocks=list(re.finditer(r'<Layer>.*?</Layer>',text,re.S))
match=next(m for m in layer_blocks if E.fromstring(m.group()).findtext('ClassName')=='ModelMaterialTimelineLayer' and E.fromstring(m.group()).findtext('SlotNo')=='0')
block=match.group(); frame_blocks={int(E.fromstring(m.group()).findtext('FrameNo')):m.group() for m in re.finditer(r'<Frame>.*?</Frame>',block,re.S)}
templates={}
for bone in re.findall(r'<Bone>.*?</Bone>',frame_blocks[0],re.S):
    name=E.fromstring(bone).findtext('Transform/Name')
    if name in samples:templates[name]=bone
def emission_replace(bone,value):
    hits=list(re.finditer(r'<Value>[^<]*</Value>',bone))
    assert len(E.fromstring(bone).findall('Transform/Value'))==49, '材質値数が一致しません'
    m=hits[41];return bone[:m.start()]+'<Value>'+value+'</Value>'+bone[m.end():]
def make_bone(name,value):
    bone=emission_replace(templates[name],value)
    # 材質レイヤーの代表補間を線形にし、中間フレームでの過剰発光を防ぐ。
    for side in ['In','Out']:
        bone=re.sub(r'<'+side+r'Tangents>.*?</'+side+r'Tangents>\s*','',bone,flags=re.S)
        tag=side+'SmoothBit';m=re.search('<'+tag+r'>(\d+)</'+tag+'>',bone)
        assert m, '補間フラグがありません'
        bone=bone[:m.start(1)]+str(int(m.group(1))&~1)+bone[m.end(1):]
    tangents=''
    for side in ['In','Out']:
        tangents+=newline+'          <'+side+'Tangents>'+''.join(newline+'            <Value>'+('1' if i==0 else '0')+'</Value>' for i in range(49))+newline+'          </'+side+'Tangents>'
    return bone.replace('</Transform>',tangents+newline+'        </Transform>')
new_count=0
for frame in sorted({f for f,n in planned}):
    fb=frame_blocks.get(frame,'<Frame>'+newline+'      <FrameNo>'+str(frame)+'</FrameNo>'+newline+'    </Frame>')
    for (f,name),value in sorted(planned.items()):
        if f!=frame:continue
        if (f,name) in existing:
            def replace_existing(m):
                return emission_replace(m.group(),value) if E.fromstring(m.group()).findtext('Transform/Name')==name else m.group()
            fb=re.sub(r'<Bone>.*?</Bone>',replace_existing,fb,flags=re.S)
        else:
            fb=fb.replace('</Frame>','  '+make_bone(name,value)+newline+'    </Frame>');new_count+=1
    frame_blocks[frame]=fb
fm=list(re.finditer(r'<Frame>.*?</Frame>',block,re.S))
newblock=block[:fm[0].start()]+(newline+'    ').join(frame_blocks[f] for f in sorted(frame_blocks))+block[fm[-1].end():]
out=(text[:match.start()]+newblock+text[match.end():]).encode('utf-8')
after=E.fromstring(out)
def canonical(e):return (e.tag,tuple(sorted(e.attrib.items())),(e.text or '').strip(),tuple(canonical(c) for c in e))
assert [canonical(x) for x in root if x is not material]==[canonical(x) for x in after if x is not layer(after,'ModelMaterialTimelineLayer')], '材質以外に差分があります'
actual={(int(f.findtext('FrameNo')),t.findtext('Name')):t for f in layer(after,'ModelMaterialTimelineLayer').findall('Frame') for t in f.findall('Bone/Transform')}
assert len(actual)==len(existing)+new_count, 'キーが重複しています'
for key,t in existing.items():
    a=E.fromstring(E.tostring(t));b=E.fromstring(E.tostring(actual[key]))
    if key in planned:a.findall('Value')[41].text=b.findall('Value')[41].text
    assert canonical(a)==canonical(b), '既存キーの対象外値が変わりました'
for key,value in planned.items():assert actual[key].findall('Value')[41].text==value, '発光値が一致しません'
for f in [1411,1412,1419]:assert canonical(existing[(f,reference_name)])==canonical(actual[(f,reference_name)]), '参考キーが変わりました'
for key,t in actual.items():
    if key not in existing:
        a=[v.text for v in t.findall('Value')];b=[v.text for v in existing[(0,key[1])].findall('Value')]
        assert len(a)==49 and a[:41]+a[42:]==b[:41]+b[42:], '追加キーの発光以外の値が変わっています'
print('モデル番号・最終キー数・最小/最大EmissionValue:',stats)
print('追加キー数:',new_count,'参考3キー保持・材質以外の差分なし・既存キー保持: 検証済み')
if '--apply' in sys.argv:
    assert p.read_bytes()==raw, '原本が変更されたため中止します'
    backup=p.with_name(p.name+'.bak_before_emission_sync_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    with backup.open('xb') as f:f.write(raw)
    assert p.read_bytes()==raw, '原本が変更されたため中止します'
    p.write_bytes(out);assert p.read_bytes()==out, '保存結果が一致しません'
    E.parse(p);print('バックアップ:',backup.name)
