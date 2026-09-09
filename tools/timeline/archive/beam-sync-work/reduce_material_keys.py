import argparse, bisect, copy, hashlib, json, re, uuid
from pathlib import Path
from datetime import datetime
import xml.etree.ElementTree as E

def require(ok, message):
    if not ok: raise ValueError(message)

def canonical(t):
    return t.tag, tuple(sorted(t.attrib.items())), (t.text or '').strip(), tuple(canonical(x) for x in t)

def main():
    p = argparse.ArgumentParser()
    p.add_argument('path', type=Path)
    p.add_argument('--name', required=True)
    p.add_argument('--apply', action='store_true')
    p.add_argument('--sha256')
    args = p.parse_args()
    raw = args.path.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    r = E.fromstring(raw)
    require(r.get('version') == '33', '未対応のXML版です')
    layers = [l for l in r.findall('Layer') if l.findtext('ClassName') == 'ModelMaterialTimelineLayer' and l.findtext('SlotNo') == '0']
    require(len(layers) == 1, '対象レイヤーを特定できません')
    rows = sorted((int(f.findtext('FrameNo')), t) for f in layers[0].findall('Frame') for t in f.findall('Bone/Transform') if t.findtext('Name') == args.name)
    require(len(rows)>2 and rows[0][0]==0 and len({f for f,t in rows})==len(rows), '対象キーが不正です')
    values = [[float(v.text) for v in t.findall('Value')] for f,t in rows]
    require(all(len(v)==49 and v[0]==0 for v in values), '未対応の材質値です')
    for f,t in rows[1:]:
        for side in ('In','Out'):
            require(int(t.findtext(side+'SmoothBit'))&1==0 and t.findtext(side+'Tangents/Value')=='1', '線形補間ではありません')
    channels = [c for c in range(49) if len({v[c] for v in values})>1]
    epsilon = 1e-7
    n = len(rows)
    d, prev = [n+1]*n, [-1]*n
    d[1]=2
    # 初区間と両端を保持し、許容誤差内で最小個数になる経路を求める。
    for i in range(1,n-1):
        lo,hi=[-float('inf')]*len(channels),[float('inf')]*len(channels)
        for j in range(i+1,n):
            dt=rows[j][0]-rows[i][0]
            slopes=[(values[j][c]-values[i][c])/dt for c in channels]
            if all(lo[c]-1e-15<=s<=hi[c]+1e-15 for c,s in enumerate(slopes)) and d[j]>d[i]+1:
                d[j],prev[j]=d[i]+1,i
            for c,s in enumerate(slopes):
                lo[c]=max(lo[c],s-epsilon/dt)
                hi[c]=min(hi[c],s+epsilon/dt)
            if any(a>b for a,b in zip(lo,hi)): break
    keep=[]
    k=n-1
    while k>=1:
        keep.append(k)
        k=prev[k]
    keep=[0]+list(reversed(keep))
    require(len(keep)==d[-1], '経路の復元に失敗しました')
    def runtime_evaluator(indices):
        fs=[rows[i][0] for i in indices]
        st,ed=fs[:-1],fs[1:]
        for i in range(len(st)-1):
            if fs[i]+1==fs[i+1]:
                if i>1: ed[i-1]=ed[i]
                st[i]=ed[i]
        def evaluate(frame):
            k=max(0,min(len(st)-1,bisect.bisect_right(st,frame+0.001)-1))
            u=max(0.,min(1.,(frame-st[k])/(ed[k]-st[k]))) if ed[k]!=st[k] else 0.
            a,b=indices[k],indices[k+1]
            ot=float(rows[a][1].findtext('OutTangents/Value','0'))
            it=float(rows[b][1].findtext('InTangents/Value','0'))
            h=max(0.,min(1.,(u**3-2*u*u+u)*ot+(-2*u**3+3*u*u)+(u**3-u*u)*it))
            return [values[a][c]*(1-h)+values[b][c]*h for c in (21,22,23,41)]
        return evaluate
    def visible_error(a,b):
        # 消灯中の発光色は見た目に寄与しない。発光値自体は常に照合する。
        return max([abs(a[3]-b[3])]+[abs(a[c]-b[c]) for c in range(3) if a[3]!=0 or b[3]!=0])
    sample_count=int(r.findtext('MaxFrameNo'))*4+1
    original_runtime=runtime_evaluator(list(range(n)))
    reference=[original_runtime(s/4) for s in range(sample_count)]
    frames=[f for f,t in rows]
    required=set(keep)
    for iteration in range(n):
        current=runtime_evaluator(keep)
        bad=[s for s in range(sample_count) if visible_error(reference[s],current(s/4))>epsilon]
        if not bad: break
        expanded=set(keep)
        for s in bad:
            i=max(0,bisect.bisect_right(frames,s/4)-1)
            expanded.update(range(max(0,i-2),min(n,i+3)))
        require(len(expanded)>len(keep),'再生時の誤差を解消できません')
        keep=sorted(expanded)
    # 再生互換性のために追加したキーも、一つずつ除去可否を確認する。
    for index in list(keep):
        if index in required: continue
        k=keep.index(index)
        trial=[i for i in keep if i!=index]
        current=runtime_evaluator(trial)
        left=frames[keep[max(0,k-3)]]*4
        right=frames[keep[min(len(keep)-1,k+3)]]*4
        if all(visible_error(reference[s],current(s/4))<=epsilon for s in range(left,min(sample_count,right+1))):
            keep=trial
    current=runtime_evaluator(keep)
    runtime_error=max(visible_error(reference[s],current(s/4)) for s in range(sample_count))
    require(runtime_error<=epsilon,'再生時の見た目が変わっています')
    remove={rows[i][0] for i in range(n) if i not in keep}
    removed=0
    def edit_layer(m):
        nonlocal removed
        l=E.fromstring(m.group())
        if l.findtext('ClassName')!='ModelMaterialTimelineLayer' or l.findtext('SlotNo')!='0': return m.group()
        def edit_frame(fm):
            nonlocal removed
            f=E.fromstring(fm.group())
            if int(f.findtext('FrameNo')) not in remove: return fm.group()
            def edit_bone(bm):
                nonlocal removed
                if E.fromstring(bm.group()).findtext('Transform/Name')==args.name:
                    removed+=1
                    return ''
                return bm.group()
            result=re.sub(r'(?m)^      <Bone>.*?</Bone>\r?\n',edit_bone,fm.group(),flags=re.S)
            return result if E.fromstring(result).findall('Bone') else ''
        return re.sub(r'(?m)^    <Frame>.*?</Frame>\r?\n',edit_frame,m.group(),flags=re.S)
    result=re.sub(r'(?m)^  <Layer>.*?</Layer>\r?\n',edit_layer,raw.decode('utf-8'),flags=re.S).encode('utf-8')
    require(removed==len(remove), '削除件数が一致しません')
    expected=copy.deepcopy(r)
    for l in expected.findall('Layer'):
        if l.findtext('ClassName')=='ModelMaterialTimelineLayer' and l.findtext('SlotNo')=='0':
            for f in list(l.findall('Frame')):
                if int(f.findtext('FrameNo')) in remove:
                    for b in list(f.findall('Bone')):
                        if b.findtext('Transform/Name')==args.name: f.remove(b)
                    if not f.findall('Bone'): l.remove(f)
    require(canonical(expected)==canonical(E.fromstring(result)), '対象外のデータが変わっています')
    def evaluator(indices,delay):
        fs=[rows[i][0] for i in indices]
        st,ed=fs[:-1],fs[1:]
        if delay:
            for i in range(len(st)-1):
                if fs[i]+1==fs[i+1]:
                    if i>1: ed[i-1]=ed[i]
                    st[i]=ed[i]
        def evaluate(frame):
            k=max(0,min(len(st)-1,bisect.bisect_right(st,frame+0.001)-1))
            u=max(0.,min(1.,(frame-st[k])/(ed[k]-st[k]))) if ed[k]!=st[k] else 0.
            a,b=indices[k],indices[k+1]
            ot=float(rows[a][1].findtext('OutTangents/Value','0'))
            it=float(rows[b][1].findtext('InTangents/Value','0'))
            h=max(0.,min(1.,(u**3-2*u*u+u)*ot+(-2*u**3+3*u*u)+(u**3-u*u)*it))
            return [x*(1-h)+y*h for x,y in zip(values[a],values[b])]
        return evaluate
    errors={}
    for delay in (False,True):
        a,b=evaluator(list(range(n)),delay),evaluator(keep,delay)
        errors['delay' if delay else 'edit']=max((max(abs(x-y) for x,y in zip(a(s/4),b(s/4))),s/4) for s in range(int(r.findtext('MaxFrameNo'))*4+1))
    require(errors['edit'][0]<=epsilon+1e-12, '補間誤差が許容値を超えました')
    report={'sha256':digest,'before':n,'after':len(keep),'removed':removed,'maximum_error':errors,'visible_runtime_error':runtime_error,'frames':[rows[i][0] for i in keep],'outside_target_unchanged':True}
    if args.apply:
        require(args.sha256==digest and args.path.read_bytes()==raw, '原本が検証時から変更されています')
        backup=args.path.with_name(args.path.name+'.bak-'+datetime.now().strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:8])
        with backup.open('xb') as stream: stream.write(raw)
        require(backup.read_bytes()==raw and args.path.read_bytes()==raw, 'バックアップまたは原本の照合に失敗しました')
        args.path.write_bytes(result)
        require(args.path.read_bytes()==result,'保存結果が一致しません')
        E.parse(args.path)
        report['backup']=str(backup)
    print(json.dumps(report,ensure_ascii=False))

if __name__=='__main__': main()
