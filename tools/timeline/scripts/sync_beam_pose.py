"""対応表に従いレーザーXZをモデル原点へ合わせ、モデルのヨー角を同期する。"""
import argparse,datetime,hashlib,json,math,re,sys
from pathlib import Path
import sync_beam_materials as s

def multiply(a,b):
    x,y,z,w=a;X,Y,Z,W=b
    return (w*X+x*W+y*Z-z*Y,w*Y-x*Z+y*W+z*X,w*Z+x*Y-y*X+z*W,w*W-x*X-y*Y-z*Z)

def euler(x,y,z):
    # UnityのEulerはZ、X、Yの順で適用される。
    x,y,z=[math.radians(v)/2 for v in (x,y,z)]
    return multiply(multiply((0,math.sin(y),0,math.cos(y)),(math.sin(x),0,0,math.cos(x))),(0,0,math.sin(z),math.cos(z)))

def forward(q):
    x,y,z,w=q
    return (2*(x*z+w*y),2*(y*z-w*x),1-2*(x*x+y*y))

def replace_values(block,changes):
    # 直下Valueの連続部分だけを変更し、タンジェントには入らない。
    t=s.parse(block);t=t if t.tag=='Transform' else t.find('Transform')
    count=len(t.findall('Value'));start=block.index('<Value>')
    matches=list(re.finditer(r'<Value>[^<]*</Value>',block[start:]))[:count]
    for i,v in sorted(changes.items(),reverse=True):
        m=matches[i];block=block[:start+m.start()]+'<Value>'+v+'</Value>'+block[start+m.end():]
    return block

def synchronize(raw,cfg,reduce_keys=True,angle_tolerance=.05):
    root=s.parse(raw);s.require(root.attrib.get('version')=='33' and root.findtext('IsLoopAnm')=='false','非ループのversion 33のみ対応しています')
    slot=cfg.get('slot',0);end=int(root.findtext('MaxFrameNo'));fps=float(root.findtext('FrameRate'));delay=root.findtext('SingleFrameType') in ('Delay','Advance')
    laser=s.layer(root,'StageLaserTimelineLayer',slot);model=s.layer(root,'ModelTimelineLayer',slot)
    lt,mt=s.tracks(laser),s.tracks(model);positions={};planned={};summary=[];series_by_name={}
    for entry in cfg['mappings']:
        cn=entry['controller'];mn=entry['material'].split('/',1)[0]
        s.require(cn in lt and mn in mt,'対応するコントローラーまたはモデルがありません')
        s.require(cn not in positions,'コントローラーが重複しています')
        vv=[s.values(t) for _,t in mt[mn]];original=vv[0]
        s.require(mt[mn][0][0]==0 and len(original)==12,'モデルの先頭キー形式が違います')
        s.require(all(all(v[i]==original[i] for i in [0,1,2,7,8,9,10,11]) for v in vv),'モデル位置・スケール・表示のアニメーションがあるため中止します')
        s.require(all(abs(v[3])<1e-7 and abs(v[5])<1e-7 for v in vv),'Y軸以外のモデル回転があるため中止します')
        positions[cn]={0:mt[mn][0][1].findall('Value')[0].text,2:mt[mn][0][1].findall('Value')[2].text}
        track=s.Track(lt[cn],end,fps,delay)
        s.require(all(len(v)==37 and v[26]==1 for v in track.data),'一括回転が有効なコントローラーのみ対応しています')
        series=[];prev=original[3:7];last_yaw=math.atan2(forward(prev)[0],forward(prev)[2])
        for frame in range(end+1):
            rotation=[track.hermite(frame,i) for i in (3,4,5)]
            center=[(track.hermite(frame,i)+track.hermite(frame,i+3))/2 for i in (31,32,33)]
            direction=forward(multiply(euler(*rotation),euler(*center)))
            # 真上・真下では方位が定まらないため、直前のY軸回転を保つ。
            if math.hypot(direction[0],direction[2])>1e-8:last_yaw=math.atan2(direction[0],direction[2])
            q=(0.,math.sin(last_yaw/2),0.,math.cos(last_yaw/2))
            if sum(a*b for a,b in zip(q,prev))<0:q=tuple(-v for v in q)
            q=tuple(float(s.fmt(v)) for v in q);prev=q;series.append(tuple(s.fmt(v) for v in q))
            check=forward(q)
            if math.hypot(direction[0],direction[2])>1e-8:
                target=math.atan2(direction[0],direction[2]);actual=math.atan2(check[0],check[2]);err=abs(math.atan2(math.sin(target-actual),math.cos(target-actual)))
                s.require(err<1e-7,'モデルの水平方向が一致しません')
        series_by_name[mn]=[tuple(float(v) for v in q) for q in series]
        frames={f for f,_ in mt[mn]}|{0,end}
        for f in range(1,end+1):
            if series[f]!=series[f-1]:frames.update((f-1,f))
        for f in frames:planned[(f,mn)]={i:v for i,v in zip(range(3,7),series[f])}
        summary.append({'controller':cn,'model':mn,'xz':[original[0],original[2]],'model_keys':len(frames)})
    text=raw.decode('utf-8');nl='\r\n' if '\r\n' in text else '\n';count=0;added=0
    def edit_layer(lm):
        nonlocal count,added
        l=s.parse(lm.group());name=l.findtext('ClassName')
        if l.findtext('SlotNo')!=str(slot):return lm.group()
        if name=='StageLaserTimelineLayer':
            def edit_transform(tm):
                nonlocal count
                t=s.parse(tm.group());n=t.findtext('Name')
                if n not in positions:return tm.group()
                s.require(t.findtext('Type')=='StageLaserController','コントローラー型が違います');count+=1
                return replace_values(tm.group(),positions[n])
            return re.sub(r'<Transform>.*?</Transform>',edit_transform,lm.group(),flags=re.S)
        if name!='ModelTimelineLayer':return lm.group()
        block=lm.group();matches=list(re.finditer(r'<Frame>.*?</Frame>',block,re.S));blocks={int(s.parse(m.group()).findtext('FrameNo')):m.group() for m in matches}
        s.require(len(blocks)==len(matches) and list(blocks)==sorted(blocks),'モデルフレームが重複または非昇順です')
        templates={s.parse(b).findtext('Transform/Name'):b for b in re.findall(r'<Bone>.*?</Bone>',blocks[0],re.S)}
        for (f,n),changes in sorted(planned.items()):
            fb=blocks.get(f,'<Frame>'+nl+'      <FrameNo>'+str(f)+'</FrameNo>'+nl+'    </Frame>')
            existing=any(t.findtext('Name')==n for t in s.parse(fb).findall('Bone/Transform'))
            if existing:
                fb=re.sub(r'<Bone>.*?</Bone>',lambda m:replace_values(m.group(),changes) if s.parse(m.group()).findtext('Transform/Name')==n else m.group(),fb,flags=re.S)
            else:
                fb=fb.replace('</Frame>','  '+replace_values(templates[n],changes)+nl+'    </Frame>');added+=1
            blocks[f]=fb
        pending=sorted(set(blocks)-{int(s.parse(m.group()).findtext('FrameNo')) for m in matches});pieces=[];cursor=0
        for m in matches:
            f=int(s.parse(m.group()).findtext('FrameNo'));pieces.append(block[cursor:m.start()])
            while pending and pending[0]<f:pieces.append(blocks[pending.pop(0)]+nl+'    ')
            pieces.append(blocks[f]);cursor=m.end()
        pieces.extend(nl+'    '+blocks[f] for f in pending);pieces.append(block[cursor:]);return ''.join(pieces)
    out=re.sub(r'<Layer>.*?</Layer>',edit_layer,text,flags=re.S).encode('utf-8');after=s.parse(out)
    for l in root.findall('Layer'):
        al=s.layer(after,l.findtext('ClassName'),l.findtext('SlotNo'))
        if l not in (laser,model):s.require(s.canonical(l)==s.canonical(al),'対象外レイヤーに差分があります');continue
        before=s.tracks(l);new=s.tracks(al)
        for n,rows in before.items():
            after_rows=dict(new[n])
            for f,t in rows:
                a,b=s.parse(s.E.tostring(t)),s.parse(s.E.tostring(after_rows[f]))
                changes=positions.get(n,{}) if l is laser else planned.get((f,n),{})
                for i,v in changes.items():a.findall('Value')[i].text=v
                s.require(s.canonical(a)==s.canonical(b),'既存キーの対象外に差分があります')
        if l is model:
            for n,rows in new.items():
                oldframes=dict(before[n])
                for f,t in rows:
                    if f in oldframes:continue
                    v=s.values(t);base=s.values(before[n][0][1]);s.require(len(v)==12 and all(v[i]==base[i] for i in [0,1,2,7,8,9,10,11]),'追加モデルキーの対象外値が違います')
    reduction=[]
    if reduce_keys:
        from beam_curves import reduce_pose_keys
        out,reduction=reduce_pose_keys(out,cfg,series_by_name,angle_tolerance)
        final=s.tracks(s.layer(s.parse(out),'ModelTimelineLayer',slot))
        for item in summary:item['model_keys']=len(final[item['model']])
        added=sum(len(set(f for f,_ in final[n])-set(f for f,_ in mt[n])) for n in series_by_name)
        if s.canonical(s.parse(out))==s.canonical(root):out=raw
    return out,{'mapping':summary,'controller_keys':count,'added_model_keys':added,'reduction':reduction,'angle_tolerance':angle_tolerance,'changed':out!=raw,'validated_frames':len(summary)*(end+1)}

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('xml',type=Path);parser.add_argument('--config',type=Path,required=True);parser.add_argument('--apply',action='store_true');parser.add_argument('--sha256')
    parser.add_argument('--no-reduce',action='store_true',help='補間キー生成を無効にする')
    parser.add_argument('--angle-tolerance',type=float,default=.05,help='回転補間の許容誤差（度、既定0.05）')
    args=parser.parse_args()
    try:
        raw=args.xml.read_bytes();digest=hashlib.sha256(raw).hexdigest();out,report=synchronize(raw,json.loads(args.config.read_text(encoding='utf-8-sig')),not args.no_reduce,args.angle_tolerance);report['sha256']=digest
        if args.apply:
            s.require(args.sha256==digest and args.xml.read_bytes()==raw,'原本が更新されたため中止します')
            if out!=raw:
                backup=args.xml.with_name(args.xml.name+'.bak_beam_pose_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
                with backup.open('xb') as f:f.write(raw)
                s.require(args.xml.read_bytes()==raw,'原本が更新されたため中止します');args.xml.write_bytes(out);s.require(args.xml.read_bytes()==out,'保存結果が一致しません');report['backup']=str(backup)
        print(json.dumps(report,ensure_ascii=False,indent=2))
    except (ValueError,KeyError,OSError) as exc:print('同期に失敗しました: '+str(exc),file=sys.stderr);return 1
    return 0

if __name__=='__main__':sys.exit(main())
