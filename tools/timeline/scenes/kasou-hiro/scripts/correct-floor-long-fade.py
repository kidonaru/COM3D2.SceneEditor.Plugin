import argparse
import copy
import datetime
import hashlib
import importlib.util
import json
import math
import re
from pathlib import Path
import xml.etree.ElementTree as ET


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def structure(e):
    return e.tag, tuple(sorted(e.attrib.items())), (e.text or '').strip(), tuple(structure(c) for c in e)


def main():
    parser = argparse.ArgumentParser(description='動画の長い発光フェードへ修正する')
    parser.add_argument('xml', type=Path)
    parser.add_argument('baseline', type=Path)
    parser.add_argument('plan', type=Path)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    plan = json.loads(args.plan.read_text(encoding='utf-8'))
    current = args.xml.read_bytes()
    baseline = args.baseline.read_bytes()
    require(hashlib.sha256(current).hexdigest() == plan['current_sha256'], '調査後に原本が変更されました')
    require(hashlib.sha256(baseline).hexdigest() == plan['baseline_sha256'], '編集前バックアップが一致しません')
    root = ET.fromstring(baseline)
    old = ET.fromstring(current)
    def target(r):
        found = [l for l in r.findall('Layer') if l.findtext('ClassName') == 'ModelMaterialTimelineLayer' and l.findtext('SlotNo') == '0']
        require(len(found) == 1, '対象レイヤーが一意ではありません')
        return found[0]
    layer = target(root)
    oldlayer = target(old)
    base_model = 'kdnr_midnight_floor_light_i_.menu'
    names = {i: base_model + (f' ({i})' if i > 1 else '') + '/CM3D2_電飾 | マゼンタ' for i in range(1, 25)}
    base_frames = {int(f.findtext('FrameNo')) for f in layer.findall('Frame')}
    removed = 0
    for frame in list(oldlayer.findall('Frame')):
        fr = int(frame.findtext('FrameNo'))
        if plan['start'] <= fr <= plan['end']:
            for bone in list(frame.findall('Bone')):
                if bone.findtext('Transform/Name') in names.values():
                    frame.remove(bone)
                    removed += 1
            if fr not in base_frames and not frame.findall('Bone'):
                oldlayer.remove(frame)
    require(removed == 537 and structure(old) == structure(root), '前回の追加以外に差分があるため自動修正できません')
    templates = {}
    for frame in layer.findall('Frame'):
        for bone in frame.findall('Bone'):
            name = bone.findtext('Transform/Name')
            if name in names.values():
                templates[name] = bone
    require(len(templates) == 24, '材質テンプレートが揃いません')
    assignments = {n: g['start'] for g in plan['groups'] for n in g['lights']}
    require(set(assignments) == set(names), 'ライトの位相設定が不正です')
    require(plan['rise'] + plan['fall'] + plan['rest'] == plan['period'], '周期設定が不正です')
    def intensity(fr, number):
        if fr < plan['start'] or fr > plan['end']:
            return 0.0
        age = (fr - assignments[number]) % plan['period']
        if age <= plan['rise']:
            t = age / plan['rise']
            value = t * t * (3 - 2 * t)
        elif age <= plan['rise'] + plan['fall']:
            t = (age - plan['rise']) / plan['fall']
            value = 1 - t * t * (3 - 2 * t)
        else:
            value = 0.0
        entrance = min(1, (fr - plan['start']) / plan['entry_fade'])
        return value * plan['peak_emission'] * entrance * entrance * (3 - 2 * entrance)
    # 曲線の始点・ピーク・終点と、長い減光の中間点を残す。
    additions = {}
    for number, name in names.items():
        frames = {plan['start'], plan['end'], plan['end'] + 1}
        frames.update(range(plan['start'], plan['start'] + plan['entry_fade'] + 1, 2))
        for cycle in range(-3, 4):
            start = assignments[number] + cycle * plan['period']
            frames.update(start + offset for offset in [0, plan['rise'], 10, 16, 22, 28, 34, 40])
        for fr in sorted(f for f in frames if plan['start'] <= f <= plan['end'] + 1):
            bone = copy.deepcopy(templates[name])
            values = bone.find('Transform').findall('Value')
            require(len(values) == 49 and values[41].text == '0', '材質値数または複製元が不正です')
            values[41].text = format(intensity(fr, number), '.8g')
            additions[fr, name] = bone
    text = baseline.decode('utf-8')
    matches = [m for m in re.finditer(r'  <Layer>\r?\n.*?  </Layer>', text, re.S)
               if '<ClassName>ModelMaterialTimelineLayer</ClassName>' in m.group() and '<SlotNo>0</SlotNo>' in m.group()]
    require(len(matches) == 1, '保存範囲が一意ではありません')
    match = matches[0]
    raw = match.group()
    newline = '\r\n' if '\r\n' in raw else '\n'
    raw_templates = {}
    for bm in re.finditer(r'      <Bone>\r?\n.*?      </Bone>\r?\n', raw, re.S):
        bone = ET.fromstring(bm.group())
        name = bone.findtext('Transform/Name')
        if name in templates and structure(bone) == structure(templates[name]):
            raw_templates[name] = bm.group()
    require(len(raw_templates) == 24, '保存テンプレートが揃いません')
    pending = {}
    for (fr, name), bone in additions.items():
        rawbone = raw_templates[name]
        values = list(re.finditer(r'^          <Value>([^<]*)</Value>', rawbone, re.M))
        require(len(values) == 49, '保存用値数が不正です')
        vm = values[41]
        value = bone.find('Transform').findall('Value')[41].text
        rawbone = rawbone[:vm.start(1)] + value + rawbone[vm.end(1):]
        require(structure(ET.fromstring(rawbone)) == structure(bone), '発光強度以外に差分があります')
        pending.setdefault(fr, []).append(rawbone)
    def insert(fm):
        block = fm.group()
        fr = int(re.search(r'<FrameNo>(\d+)</FrameNo>', block)[1])
        prefix = ''
        for f in sorted(f for f in pending if f < fr):
            prefix += '    <Frame>' + newline + f'      <FrameNo>{f}</FrameNo>' + newline + ''.join(pending.pop(f)) + '    </Frame>' + newline
        if fr in pending:
            block = block.replace('    </Frame>', ''.join(pending.pop(fr)) + '    </Frame>')
        return prefix + block
    updated = re.sub(r'    <Frame>\r?\n.*?    </Frame>\r?\n', insert, raw, flags=re.S)
    require(not pending, '未処理の追加キーがあります')
    candidate = (text[:match.start()] + updated + text[match.end():]).encode('utf-8')
    after = ET.fromstring(candidate)
    afterlayer = target(after)
    found = set()
    for frame in list(afterlayer.findall('Frame')):
        fr = int(frame.findtext('FrameNo'))
        seen = set()
        for bone in list(frame.findall('Bone')):
            name = bone.findtext('Transform/Name')
            require(name not in seen, '同一フレームに重複キーがあります')
            seen.add(name)
            if (fr, name) in additions:
                require(structure(bone) == structure(additions[fr, name]), '保存候補のキーが不一致です')
                found.add((fr, name))
                frame.remove(bone)
        if fr not in base_frames:
            require(not frame.findall('Bone'), '新規フレームに対象外のキーがあります')
            afterlayer.remove(frame)
    require(found == set(additions), '追加キーの検証数が不一致です')
    require(structure(after) == structure(root), '対象外の構造に差分があります')
    require(all(float(b.find('Transform').findall('Value')[41].text) == 0 for (f, n), b in additions.items() if f == plan['end'] + 1), '範囲外の消灯を保護できていません')
    result = {'removed_previous_keys': removed, 'added_keys': len(additions),
              'guard_frame': plan['end'] + 1, 'period': plan['period'],
              'validation': 'XML構文・重複・追加値・対象外構造の一致を確認',
              'observed_timing': '13番相当370/374/410、15番相当394/398/434、16番相当412/416/452',
              'approximation': '隠れた位置の位相と4/36/20の曲線は近似', 'game_verified': False}
    if args.apply:
        require(args.xml.read_bytes() == current, '保存直前に原本が変更されました')
        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
        backup = args.xml.with_name(args.xml.name + '.bak.long-fade-correction-' + stamp)
        with backup.open('xb') as stream:
            stream.write(current)
        require(backup.read_bytes() == current and args.xml.read_bytes() == current, 'バックアップまたは原本の検証に失敗しました')
        args.xml.write_bytes(candidate)
        require(args.xml.read_bytes() == candidate, '保存後の検証に失敗しました')
        result['backup'] = str(backup)
        result['sha256'] = hashlib.sha256(candidate).hexdigest()
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    main()
