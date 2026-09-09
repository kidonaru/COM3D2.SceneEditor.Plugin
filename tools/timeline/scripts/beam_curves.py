"""同期時に計算したフレーム値から、誤差を検証した補間区間を作る。"""
import math


def hermite(t, start, end, out_tangent, in_tangent):
    return ((2*t**3-3*t*t+1)*start + (t**3-2*t*t+t)*out_tangent
            + (-2*t**3+3*t*t)*end + (t**3-t*t)*in_tangent)


def fit_tangents(samples, start, end):
    """端点を固定し、正規化時間に対する二つの傾きを最小二乗で求める。"""
    aa = ab = bb = ay = by = 0.
    for t, value in samples:
        a, b = t**3-2*t*t+t, t**3-t*t
        y = value-hermite(t, start, end, 0., 0.)
        aa += a*a; ab += a*b; bb += b*b; ay += a*y; by += b*y
    determinant = aa*bb-ab*ab
    if abs(determinant) < 1e-20:
        return end-start, end-start
    return (ay*bb-by*ab)/determinant, (by*aa-ay*ab)/determinant


def partition(count, fit, anchors=()):
    """許容誤差を超える区間を最大誤差点で分割する。"""
    kept = sorted({0, count-1, *anchors})
    pending = list(zip(kept, kept[1:]))
    segments = []
    while pending:
        left, right = pending.pop()
        curve, score, split = fit(left, right)
        if score > 1. and right-left > 1:
            split = max(left+1, min(right-1, split))
            pending.extend(((left, split), (split, right)))
        else:
            if score > 1.00001:
                raise ValueError('補間区間の誤差が許容値を超えています')
            segments.append((left, right, curve))
    return sorted(segments)


def angle(a, b):
    denominator = math.sqrt(sum(x*x for x in a)*sum(x*x for x in b))
    if denominator < 1e-12:
        return 360.
    cosine = min(1., abs(sum(x*y for x,y in zip(a,b)))/denominator)
    return math.degrees(2*math.acos(cosine))


def set_tangent(transform, side, index, value):
    import xml.etree.ElementTree as E
    bit = transform.find(side+'SmoothBit')
    if bit is None:
        bit = E.SubElement(transform, side+'SmoothBit'); bit.text = '0'
    bit.text = str(int(bit.text) & ~(1 << index))
    tangents = transform.find(side+'Tangents')
    if tangents is None: tangents = E.SubElement(transform, side+'Tangents')
    while len(tangents) <= index: E.SubElement(tangents, 'Value').text = '0'
    tangents[index].text = format(value, '.9g')


def rewrite_tracks(raw, class_name, slot, replacements):
    import re
    import sync_beam_materials as s
    text = raw.decode('utf-8'); nl = '\r\n' if '\r\n' in text else '\n'
    def edit_layer(match):
        block = match.group(); layer = s.parse(block)
        if layer.findtext('ClassName') != class_name or layer.findtext('SlotNo') != str(slot): return block
        def edit_frame(match):
            block = match.group(); frame = int(s.parse(block).findtext('FrameNo'))
            def edit_bone(match):
                bone = s.parse(match.group()); name = bone.findtext('Transform/Name')
                if name not in replacements: return match.group()
                replacement = replacements[name].get(frame)
                if replacement is None:
                    s.require(not bone.attrib and len(bone) == 1, '削除対象Boneに未知の情報があります')
                    return ''
                rendered = s.E.tostring(replacement, encoding='unicode').rstrip().replace('\n', nl)
                return re.sub(r'<Transform(?:\s[^>]*)?>.*?</Transform>', lambda _: rendered, match.group(), flags=re.S)
            result = re.sub(r'<Bone(?:\s[^>]*)?>.*?</Bone>', edit_bone, block, flags=re.S)
            element = s.parse(result)
            if len(element) == 1 and element[0].tag == 'FrameNo' and not element.attrib: return ''
            return result
        return re.sub(r'<Frame>.*?</Frame>', edit_frame, block, flags=re.S)
    out = re.sub(r'<Layer>.*?</Layer>', edit_layer, text, flags=re.S).encode('utf-8')
    before, after = s.parse(raw), s.parse(out)
    old_layer, new_layer = s.layer(before,class_name,slot), s.layer(after,class_name,slot)
    s.require([s.canonical(x) for x in before if x is not old_layer] == [s.canonical(x) for x in after if x is not new_layer], '対象外レイヤーが変更されました')
    old, new = s.tracks(old_layer), s.tracks(new_layer)
    for name, rows in old.items():
        expected = replacements.get(name, dict(rows))
        s.require(set(expected) == {f for f,_ in new[name]}, '保存後のキー集合が一致しません')
        s.require(all(s.canonical(t) == s.canonical(expected[f]) for f,t in new[name]), '保存後の補間情報が一致しません')
    return out


def reduce_material_keys(raw, config, tolerance):
    import copy
    import sync_beam_materials as s
    s.require(math.isfinite(tolerance) and 0 < tolerance <= 1e-4, '削減許容誤差は0より大きく1e-4以下にしてください')
    root = s.parse(raw); slot = config.get('slot',0)
    all_tracks = s.tracks(s.layer(root,'ModelMaterialTimelineLayer',slot))
    end, fps = int(root.findtext('MaxFrameNo')), float(root.findtext('FrameRate'))
    delay = root.findtext('SingleFrameType') in ('Delay','Advance')
    replacements = {}; summaries = []; maximum_error = 0.; removed = 0
    for mapping in config['mappings']:
        name = mapping['material']; rows = all_tracks[name]
        if end == 0:
            replacements[name] = dict(rows)
            summaries.append({'material':name,'before':len(rows),'after':len(rows),'max_error':0.})
            continue
        original = s.Track(rows,end,fps,delay)
        series = [original.material(f) for f in range(end+1)]
        available = [f for f,_ in rows]
        anchors = {f for f,t in rows if t.attrib or t.find('StrValues') is not None or any(c.tag not in {'Name','Type','Value','InTangents','OutTangents','InSmoothBit','OutSmoothBit'} or c.attrib for c in t)}
        for f in range(end):
            if (series[f][s.EMISSION] == 0) != (series[f+1][s.EMISSION] == 0): anchors.update((f,f+1))
        def fit(left, right):
            a, b = series[left], series[right]; span = right-left
            delta = b[s.EMISSION]-a[s.EMISSION]
            if delta and span > 2:
                samples = [((f-left)/span,(series[f][s.EMISSION]-a[s.EMISSION])/delta) for f in range(left+1,right)]
                m0, m1 = fit_tangents(samples,0.,1.)
            else: m0 = m1 = 1.
            m0, m1 = float(s.fmt(m0)), float(s.fmt(m1))
            worst, split = 0., left+1
            for f in range(left+1,right):
                t = (f-left)/span; u = max(0.,min(1.,hermite(t,0.,1.,m0,m1)))
                error = max(abs(v-(x+(y-x)*(t if i in s.COLOR_INDICES else u))) for i,(v,x,y) in enumerate(zip(series[f],a,b)))
                if error > worst: worst, split = error, f
            if worst > tolerance:
                candidates = [f for f in available if left < f < right]
                s.require(candidates, '材質補間区間を分割できません')
                split = min(candidates,key=lambda f:abs(f-split))
            return (m0,m1), worst/tolerance, split
        segments = partition(end+1,fit,anchors)
        kept = sorted({f for left,right,_ in segments for f in (left,right)})
        source = dict(rows); target = {}
        for frame in kept:
            s.require(frame in source, '補間端点の元キーが見つかりません')
            t = copy.deepcopy(source[frame])
            target[frame] = t
        for left,right,(m0,m1) in segments:
            set_tangent(target[left],'Out',0,m0); set_tangent(target[right],'In',0,m1)
        reduced = s.Track(sorted(target.items()),end,fps,delay)
        track_error = max(max(abs(a-b) for a,b in zip(series[f],reduced.material(f))) for f in range(end+1))
        s.require(track_error <= tolerance+1e-9, '材質曲線の整数フレーム検証に失敗しました: '+name)
        maximum_error = max(maximum_error,track_error); removed += len(rows)-len(target)
        replacements[name] = target
        summaries.append({'material':name,'before':len(rows),'after':len(target),'max_error':track_error})
    out = rewrite_tracks(raw,'ModelMaterialTimelineLayer',slot,replacements)
    return out, {'removed_keys':removed,'reduction':summaries,'reduction_max_error':maximum_error,'reduction_tolerance':tolerance}


def reduce_pose_keys(raw, config, series_by_name, tolerance):
    import copy
    import sync_beam_materials as s
    s.require(math.isfinite(tolerance) and 0 < tolerance <= 1., '回転許容誤差は0より大きく1度以下にしてください')
    root = s.parse(raw); slot = config.get('slot',0)
    end, fps = int(root.findtext('MaxFrameNo')), float(root.findtext('FrameRate'))
    delay = root.findtext('SingleFrameType') in ('Delay','Advance')
    all_tracks = s.tracks(s.layer(root,'ModelTimelineLayer',slot))
    replacements = {}; summaries = []
    for name, series in series_by_name.items():
        rows = all_tracks[name]; source = dict(rows)
        if end == 0:
            replacements[name] = source
            summaries.append({'model':name,'before':len(rows),'after':len(rows),'max_angle_error':0.})
            continue
        anchors = {f for f,t in rows if t.attrib or t.find('StrValues') is not None or any(c.tag not in {'Name','Type','Value','InTangents','OutTangents','InSmoothBit','OutSmoothBit'} or c.attrib for c in t)}
        def fit(left,right):
            span = right-left
            slopes = [fit_tangents([((f-left)/span,series[f][j]) for f in range(left+1,right)],series[left][j],series[right][j]) for j in range(4)]
            worst,split = 0.,left+1
            for f in range(left+1,right):
                q = [hermite((f-left)/span,series[left][j],series[right][j],*slopes[j]) for j in range(4)]
                error = angle(q,series[f])
                if error > worst: worst,split = error,f
            if worst > tolerance:
                candidates = [f for f in source if left < f < right]
                s.require(candidates, '回転補間区間を分割できません')
                split = min(candidates,key=lambda f:abs(f-split))
            return slopes,worst/tolerance,split
        segments = partition(end+1,fit,anchors)
        kept = sorted({f for left,right,_ in segments for f in (left,right)})
        s.require(all(f in source for f in kept), '回転補間端点の元キーが見つかりません')
        target = {f:copy.deepcopy(source[f]) for f in kept}
        for t in target.values():
            for side in ('In','Out'):
                for j in range(3,7): set_tangent(t,side,j,1.)
        track = s.Track(sorted(target.items()),end,fps,delay); indices = {f:i for i,f in enumerate(kept)}
        for left,right,slopes in segments:
            seconds = (right-left)/fps
            for frame,side,k in ((left,'Out',0),(right,'In',1)):
                for j in range(4):
                    base = track.tangent(indices[frame],j+3,side)[1]
                    value = slopes[j][k]/seconds/base if base else 0.
                    set_tangent(target[frame],side,j+3,value)
        reduced = s.Track(sorted(target.items()),end,fps,delay)
        maximum = max(angle(series[f],[reduced.hermite(f,j) for j in range(3,7)]) for f in range(end+1))
        s.require(maximum <= tolerance+1e-6, '回転補間の整数フレーム検証に失敗しました: '+name)
        replacements[name] = target
        summaries.append({'model':name,'before':len(rows),'after':len(kept),'max_angle_error':maximum})
    return rewrite_tracks(raw,'ModelTimelineLayer',slot,replacements), summaries
