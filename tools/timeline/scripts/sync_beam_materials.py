"""ステージレーザーの色・明るさをモデル材質へ同期する。既定は検証のみ。"""
import argparse, bisect, collections, datetime, hashlib, json, math, re, sys
from pathlib import Path
import xml.etree.ElementTree as E

RGB = (21,22,23)
EMISSION = 41
COLOR_INDICES = set(range(1,17)) | set(range(21,37))

def require(ok, message):
    if not ok: raise ValueError(message)

def parse(data):
    return E.fromstring(data, parser=E.XMLParser(target=E.TreeBuilder(insert_comments=True,insert_pis=True)))

def canonical(e):
    return (e.tag, sorted(e.attrib.items()), (e.text or '').strip(), [canonical(c) for c in e])

def values(t):
    result = [float(v.text) for v in t.findall('Value')]
    require(all(math.isfinite(v) for v in result), '有限でない値があります')
    return result

def layer(root, name, slot):
    found = [l for l in root.findall('Layer') if l.findtext('ClassName')==name and l.findtext('SlotNo')==str(slot)]
    require(len(found)==1, '対象レイヤーが一意ではありません: '+name)
    return found[0]

def tracks(l):
    result = collections.defaultdict(list)
    for f in l.findall('Frame'):
        for t in f.findall('Bone/Transform'):
            result[t.findtext('Name')].append((int(f.findtext('FrameNo')),t))
    for name, rows in result.items():
        rows.sort(key=lambda x:x[0])
        require(len({f for f,_ in rows})==len(rows), 'キーが重複しています: '+name)
    return result

class Track:
    def __init__(self,rows,end,fps,delay):
        self.rows=rows; self.frames=[f for f,_ in rows]; self.data=[values(t) for _,t in rows]
        self.end=end; self.fps=fps; self.delay=delay

    def segment(self,frame):
        k=max(0,bisect.bisect_right(self.frames,frame)-1); j=min(k+1,len(self.frames)-1)
        u=(frame-self.frames[k])/(self.frames[j]-self.frames[k]) if k!=j else 0
        return k,j,max(0.,min(1.,u))

    def state(self,frame): return self.data[self.segment(frame)[0]]

    def linear(self,frame,indices):
        k,j,u=self.segment(frame)
        return [self.data[k][i]*(1-u)+self.data[j][i]*u for i in indices]

    def tangent(self,k,index,side):
        frame,t=self.rows[k]; previous=self.frames[k-1] if k else -1
        following=self.frames[k+1] if k+1<len(self.frames) else self.end+1
        x=self.data[k][index]; x0=self.data[k-1][index] if k else x
        x2=self.data[k+1][index] if k+1<len(self.frames) else x
        if self.delay and frame-previous==1: x0=x
        if self.delay and following-frame==1: x2=x
        dx0,dx1=x-x0,x2-x
        if dx0==0 and dx1!=0: dx0=math.copysign(.01,dx1)
        elif dx1==0 and dx0!=0: dx1=math.copysign(.01,dx0)
        base0=dx0*self.fps/(frame-previous); base1=dx1*self.fps/(following-frame)
        base=base0 if side=='In' else base1
        ts=t.findall(side+'Tangents/Value'); norm=float(ts[index].text) if len(ts)>index else 0.
        if (int(t.findtext(side+'SmoothBit','0'))>>index)&1 and base0!=0 and base1!=0:
            norm=(x2-x0)*self.fps/(following-previous)/base
        return norm,norm*base

    def hermite(self,frame,index):
        k,j,u=self.segment(frame)
        if k==j or u==0: return self.data[k][index]
        dt=(self.frames[j]-self.frames[k])/self.fps
        return ((2*u**3-3*u*u+1)*self.data[k][index]+(u**3-2*u*u+u)*dt*self.tangent(k,index,'Out')[1]
                +(-2*u**3+3*u*u)*self.data[j][index]+(u**3-u*u)*dt*self.tangent(j,index,'In')[1])

    def material(self,frame):
        k,j,u=self.segment(frame)
        # 数値はeasingの曲線、色は区間進行率で線形補間する。
        h=(u**3-2*u*u+u)*self.tangent(k,0,'Out')[0]+(-2*u**3+3*u*u)+(u**3-u*u)*self.tangent(j,0,'In')[0]
        h=max(0.,min(1.,h))
        return [x*(1-(u if i in COLOR_INDICES else h))+y*(u if i in COLOR_INDICES else h)
                for i,(x,y) in enumerate(zip(self.data[k],self.data[j]))]

def blend_color(core,edge,mode):
    if mode=='core': return core[:3]
    if mode=='edge': return edge[:3]
    a,b=max(0.,core[3]),max(0.,edge[3])
    return [(core[i]*a+edge[i]*b)/(a+b) for i in range(3)] if a+b else core[:3]

def evaluate(controller,children,frame,mode):
    c=controller.state(frame); lights=[]
    for child in children:
        state=child.state(frame); visible=c[14] if c[29]!=0 else state[12]
        intensity=controller.hermite(frame,15) if c[28]!=0 else child.hermite(frame,13)
        source,offset=(controller,6) if c[27]!=0 else (child,4)
        rgba=source.linear(frame,range(offset,offset+8))
        lights.append((max(0.,intensity) if visible else 0.,blend_color(rgba[:4],rgba[4:],mode)))
    intensity=max(w for w,_ in lights); weight=sum(w for w,_ in lights)
    color=([sum(w*rgb[i] for w,rgb in lights)/weight for i in range(3)] if weight else
           [sum(rgb[i] for _,rgb in lights)/len(lights) for i in range(3)])
    return intensity,color

def fmt(v): return format(v,'.9g')

def replace_values(bone,updates):
    t=parse(bone).find('Transform')
    require(t is not None and len(t.findall('Value'))==49, 'モデル材質の値数が49ではありません')
    start=bone.index('<Value>'); matches=list(re.finditer(r'<Value>[^<]*</Value>',bone[start:]))[:49]
    require(len(matches)==49, '材質値の位置を特定できません')
    for i in sorted(updates,reverse=True):
        m=matches[i]; bone=bone[:start+m.start()]+'<Value>'+updates[i]+'</Value>'+bone[start+m.end():]
    return bone

def linear_bone(bone,nl):
    # 補間設定の変更は追加キーのみ。既存キーの補間情報は維持する。
    for side in ('In','Out'):
        bone=re.sub(r'<'+side+r'Tangents>.*?</'+side+r'Tangents>\s*','',bone,flags=re.S)
        tag=side+'SmoothBit'; m=re.search('<'+tag+r'>(\d+)</'+tag+'>',bone)
        require(m is not None, '補間フラグがありません')
        bone=bone[:m.start(1)]+str(int(m.group(1))&~1)+bone[m.end(1):]
    extra=''
    for side in ('In','Out'):
        extra+=nl+'          <'+side+'Tangents>'
        extra+=''.join(nl+'            <Value>'+('1' if i==0 else '0')+'</Value>' for i in range(49))
        extra+=nl+'          </'+side+'Tangents>'
    return bone.replace('</Transform>',extra+nl+'        </Transform>')


def reduce_material_keys(raw, config, tolerance):
    from beam_curves import reduce_material_keys as reduce_curves
    return reduce_curves(raw, config, tolerance)

def synchronize(raw,config,channels,reduce_keys=True,reduction_tolerance=1e-6):
    root=parse(raw)
    require(root.attrib.get('version')=='33', '対応するXMLはversion 33です')
    require(root.findtext('IsLoopAnm')=='false', 'ループアニメーションには対応していません')
    require(root.findtext('SingleFrameType') in ('Delay','Advance','None'), '未対応の1フレーム補間設定です')
    require(config.get('schema_version')==1, '設定ファイルの版が違います')
    end,fps=int(root.findtext('MaxFrameNo')),float(root.findtext('FrameRate'))
    require(0<=end<=100000 and fps>0, 'フレーム範囲またはFPSが不正です')
    slot=config.get('slot',0); laser=layer(root,'StageLaserTimelineLayer',slot); material=layer(root,'ModelMaterialTimelineLayer',slot)
    lt,mt=tracks(laser),tracks(material); delay=root.findtext('SingleFrameType') in ('Delay','Advance')
    mode=config.get('color_source','alpha-weighted')
    require(mode in ('core','edge','alpha-weighted'), '色の取得方法が不正です')
    require(config.get('mappings'), '同期先がありません')
    names=[m['material'] for m in config['mappings']]
    require(len(names)==len(set(names)), '同期先の材質が重複しています')
    existing={(f,n):t for n,rows in mt.items() for f,t in rows}
    planned={}; data_by_name={}; summary=[]
    changed_indices=set(RGB if 'color' in channels else ())|({EMISSION} if 'emission' in channels else set())
    for mapping in config['mappings']:
        name,cn=mapping['material'],mapping['controller']
        require(name in mt and cn in lt, '指定された材質またはコントローラーがありません: '+name)
        require(mt[name][0][0]==0 and lt[cn][0][0]==0, '先頭フレーム0が必要です')
        match=re.fullmatch(r'StageLaserController \((\d+)\)',cn)
        require(match is not None, 'コントローラー名が不正です')
        child_rows=[rows for n,rows in lt.items() if n.startswith('StageLaser ('+match.group(1)+',')]
        require(child_rows and all(rows[0][0]==0 for rows in child_rows), '子レーザーの先頭キーがありません')
        require(all(t.findtext('Type')=='StageLaserController' and len(values(t))==37 for _,t in lt[cn]), 'コントローラー形式が違います')
        require(all(t.findtext('Type')=='StageLaser' and len(values(t))==24 for rows in child_rows for _,t in rows), 'レーザー形式が違います')
        require(all(t.findtext('Type')=='ModelMaterial' and len(values(t))==49 for _,t in mt[name]), '材質形式が違います')
        ctrl=Track(lt[cn],end,fps,delay); children=[Track(rows,end,fps,delay) for rows in child_rows]; mat=Track(mt[name],end,fps,delay)
        # 発光関連以外は静止材質のみ扱い、追加キーで他のアニメーションを変形しない。
        static_indices=set(range(49))-set(RGB)-{EMISSION}
        require(all(all(v[i]==mat.data[0][i] for i in static_indices) for v in mat.data), '発光以外の材質アニメーションがあるため中止します: '+name)
        require(all(v[0]==0 for v in mat.data), '非ゼロの材質easing値には対応していません')
        series=[]
        for frame in range(end+1):
            v=mat.material(frame); intensity,color=evaluate(ctrl,children,frame,mode)
            if 'emission' in channels:
                v[EMISSION]=intensity*float(config.get('emission_scale',1))
                if 'emission_max' in config:
                    limit=float(config['emission_max'])
                    require(math.isfinite(limit) and limit>=0, '発光上限は有限の非負値で指定してください')
                    v[EMISSION]=min(v[EMISSION],limit)
            if 'color' in channels:
                for i,value in zip(RGB,color): v[i]=value
            series.append(tuple(fmt(value) for value in v))
        frames={f for f,_ in mt[name]}|{0,end}
        for frame in range(1,end+1):
            if series[frame]!=series[frame-1]: frames.update((frame-1,frame))
        for frame in sorted(frames): planned[(frame,name)]=series[frame]
        data_by_name[name]=series; summary.append({'controller':cn,'material':name,'keys':len(frames)})
    text=raw.decode('utf-8'); require('<!DOCTYPE' not in text, 'DOCTYPEを含むXMLには対応していません')
    nl='\r\n' if '\r\n' in text else '\n'
    lm=[m for m in re.finditer(r'<Layer>.*?</Layer>',text,re.S) if parse(m.group()).findtext('ClassName')=='ModelMaterialTimelineLayer' and parse(m.group()).findtext('SlotNo')==str(slot)]
    require(len(lm)==1, '材質レイヤーの保存位置を特定できません')
    lm=lm[0]; block=lm.group(); frame_matches=list(re.finditer(r'<Frame>.*?</Frame>',block,re.S))
    frame_blocks={int(parse(m.group()).findtext('FrameNo')):m.group() for m in frame_matches}
    require(len(frame_matches)==len(frame_blocks), '材質フレームが重複しています')
    require(list(frame_blocks)==sorted(frame_blocks), '材質フレームが昇順ではありません')
    templates={parse(b).findtext('Transform/Name'):b for b in re.findall(r'<Bone>.*?</Bone>',frame_blocks[0],re.S)}
    additions=0; by_frame=collections.defaultdict(list)
    for (f,n),v in planned.items(): by_frame[f].append((n,v))
    for frame,entries in by_frame.items():
        fb=frame_blocks.get(frame,'<Frame>'+nl+'      <FrameNo>'+str(frame)+'</FrameNo>'+nl+'    </Frame>')
        for name,v in entries:
            if (frame,name) in existing:
                def update(m):
                    return replace_values(m.group(),{i:v[i] for i in changed_indices}) if parse(m.group()).findtext('Transform/Name')==name else m.group()
                fb=re.sub(r'<Bone>.*?</Bone>',update,fb,flags=re.S)
            else:
                bone=replace_values(templates[name],dict(enumerate(v)))
                fb=fb.replace('</Frame>','  '+linear_bone(bone,nl)+nl+'    </Frame>'); additions+=1
        frame_blocks[frame]=fb
    # 既存フレーム間のコメント・未知要素・空白をそのまま残す。
    pending=sorted(set(frame_blocks)-{int(parse(m.group()).findtext('FrameNo')) for m in frame_matches})
    pieces=[]; cursor=0
    for m in frame_matches:
        f=int(parse(m.group()).findtext('FrameNo')); pieces.append(block[cursor:m.start()])
        while pending and pending[0]<f: pieces.append(frame_blocks[pending.pop(0)]+nl+'    ')
        pieces.append(frame_blocks[f]); cursor=m.end()
    pieces.extend(nl+'    '+frame_blocks[f] for f in pending); pieces.append(block[cursor:])
    out=(text[:lm.start()]+''.join(pieces)+text[lm.end():]).encode('utf-8')
    after=parse(out); am=layer(after,'ModelMaterialTimelineLayer',slot)
    require([canonical(l) for l in root if l is not material]==[canonical(l) for l in after if l is not am], '材質レイヤー以外に差分があります')
    actual={(f,n):t for n,rows in tracks(am).items() for f,t in rows}
    require(len(actual)==len(existing)+additions, 'キー数が一致しません')
    for key,t in existing.items():
        a,b=parse(E.tostring(t)),parse(E.tostring(actual[key]))
        if key in planned:
            for i in changed_indices: a.findall('Value')[i].text=b.findall('Value')[i].text
        require(canonical(a)==canonical(b), '既存キーの同期対象外が変更されました')
    for key,v in planned.items():
        av=actual[key].findall('Value'); check=changed_indices if key in existing else set(range(49))
        require(all(abs(float(av[i].text)-float(v[i]))<1e-7 for i in check), '保存候補の値が一致しません')
    for name,series in data_by_name.items():
        frames=sorted(f for f,n in planned if n==name)
        for left,right in zip(frames,frames[1:]):
            if right-left>1: require(all(series[f]==series[left] for f in range(left,right+1)), '省略区間の値が変化しています')
    report={'mappings':summary,'added_keys':additions,'validated_integer_frames':len(names)*(end+1),'color_source':mode,'channels':sorted(channels)}
    if reduce_keys:
        out,reduction=reduce_material_keys(out,config,reduction_tolerance); report.update(reduction)
        final_tracks=tracks(layer(parse(out),'ModelMaterialTimelineLayer',slot))
        for item in summary: item['keys']=len(final_tracks[item['material']])
        final_keys={(f,n) for n,rows in final_tracks.items() for f,_ in rows}
        report['generated_keys']=additions
        report['removed_sample_keys']=report['removed_keys']
        report['added_keys']=len(final_keys-set(existing))
        report['removed_keys']=len(set(existing)-final_keys)
        # 再同期で一時生成したキーの空白だけが残る場合、原本のバイト列を再利用する。
        if canonical(parse(out))==canonical(root): out=raw
    report['changed']=out!=raw
    return out,report

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('xml',type=Path,help='対象タイムラインXML')
    parser.add_argument('--config',type=Path,required=True,help='同期先と色取得方法のJSON')
    parser.add_argument('--channels',choices=['both','color','emission'],default='both',help='同期項目。colorは既存EmissionValueを維持')
    parser.add_argument('--no-reduce',action='store_true',help='補間キー削減を無効にする')
    parser.add_argument('--reduction-tolerance',type=float,default=1e-6,help='全材質成分の補間許容誤差（既定1e-6、最大1e-4）')
    parser.add_argument('--apply',action='store_true',help='検証後にバックアップを作成して上書き')
    parser.add_argument('--sha256',help='検証時の原本SHA256。反映時に必須')
    args=parser.parse_args()
    try:
        raw=args.xml.read_bytes(); digest=hashlib.sha256(raw).hexdigest()
        config=json.loads(args.config.read_text(encoding='utf-8-sig'))
        channels={'color','emission'} if args.channels=='both' else {args.channels}
        out,report=synchronize(raw,config,channels,not args.no_reduce,args.reduction_tolerance); report['sha256']=digest
        if args.apply:
            require(args.sha256==digest, '検証時のSHA256と一致しないため反映を中止します')
            require(args.xml.read_bytes()==raw, '原本が更新されたため反映を中止します')
            if out!=raw:
                backup=args.xml.with_name(args.xml.name+'.bak_beam_sync_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
                with backup.open('xb') as f: f.write(raw)
                require(args.xml.read_bytes()==raw, 'バックアップ中に原本が更新されました')
                args.xml.write_bytes(out); require(args.xml.read_bytes()==out, '保存後の内容が一致しません')
                parse(args.xml.read_bytes()); report['backup']=str(backup)
        print(json.dumps(report,ensure_ascii=False,indent=2))
    except (ValueError,KeyError,OSError,E.ParseError) as exc:
        print('同期に失敗しました: '+str(exc),file=sys.stderr); return 1
    return 0

if __name__=='__main__': sys.exit(main())
