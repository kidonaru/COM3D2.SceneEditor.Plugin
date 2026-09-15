import argparse
import copy
import datetime
import hashlib
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def structural(element):
    return (element.tag, tuple(sorted(element.attrib.items())),
            (element.text or '').strip(), tuple(structural(c) for c in element))


def main():
    parser = argparse.ArgumentParser(description='床ライトの発光キーを限定追加する')
    parser.add_argument('xml', type=Path)
    parser.add_argument('plan', type=Path)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    plan = json.loads(args.plan.read_text(encoding='utf-8'))
    original = args.xml.read_bytes()
    digest = hashlib.sha256(original).hexdigest()
    require(digest == plan['source_sha256'], '原本が調査時から変更されています')
    text = original.decode('utf-8')
    root = ET.fromstring(original)
    layers = [l for l in root.findall('Layer')
              if l.findtext('ClassName') == plan['layer'] and l.findtext('SlotNo') == plan['slot']]
    require(len(layers) == 1, '対象レイヤーを一意に特定できません')
    layer = layers[0]
    model = plan['model']
    registered = [m.findtext('Name') for m in root.findall('Models/Model')
                  if m.findtext('Name') == model or m.findtext('Name', '').startswith(model + ' (')]
    expected = [model] + [f'{model} ({i})' for i in range(2, len(registered) + 1)]
    require(registered == expected, 'モデル登録の採番が一致しません')
    names = {i: n + '/' + plan['material'] for i, n in enumerate(expected, 1)}
    reverse = {n: i for i, n in names.items()}
    keys = {}
    for frame in layer.findall('Frame'):
        fr = int(frame.findtext('FrameNo'))
        for bone in frame.findall('Bone'):
            name = bone.findtext('Transform/Name')
            if name in reverse:
                require((fr, name) not in keys, '既存の対象キーが重複しています')
                keys[fr, name] = bone
    require(not any(fr >= plan['start'] for fr, n in keys), '指定区間以降に既存キーがあります')
    templates = {}
    for name in reverse:
        fr = max(f for f, n in keys if n == name)
        bone = keys[fr, name]
        values = bone.find('Transform').findall('Value')
        require(len(values) == 49 and values[41].text == '0', '複製元の値数または消灯状態が不正です')
        templates[name] = bone
    patterns = []
    maximum = set()
    for peak in plan['reference_peaks']:
        patterns.append({reverse[n] for (f, n), b in keys.items()
                         if f == peak and float(b.find('Transform').findall('Value')[41].text) > 0})
        maximum.update(b.find('Transform').findall('Value')[41].text for (f, n), b in keys.items()
                       if f == peak and float(b.find('Transform').findall('Value')[41].text) > 0)
    require(len(maximum) == 1, '既存の最大発光強度が一意ではありません')
    peak_value = maximum.pop()
    additions = {}
    pulses = []
    for observation in plan['observations']:
        peak = observation['peak']
        phase = (peak - plan['reference_peaks'][0]) // plan['period']
        active = (patterns[phase % len(patterns)] | set(observation['on'])) - set(observation['off'])
        require(active <= set(names), '存在しないライト番号があります')
        pulses.append({'start': peak-plan['fade'], 'peak': peak, 'end': peak+plan['fade'],
                       'lights': sorted(active)})
        for fr, emission in [(peak-plan['fade'], '0'), (peak, peak_value), (peak+plan['fade'], '0')]:
            require(plan['start'] <= fr <= plan['end'], '追加キーが対象区間外です')
            for number in sorted(active):
                name = names[number]
                bone = copy.deepcopy(templates[name])
                bone.find('Transform').findall('Value')[41].text = emission
                require((fr, name) not in additions, '追加キーが重複しています')
                additions[fr, name] = bone
    require(len(pulses) == 12 and pulses[-1]['end'] == plan['end'], '周期数または終端が不正です')
    for a, b in zip(pulses, pulses[1:]):
        require(b['start'] - a['end'] == plan['fade'], '休止区間が4フレームではありません')
    # 原本のBone文字列を利用し、発光強度だけを置換する。
    layer_matches = list(re.finditer(r'  <Layer>\r?\n.*?  </Layer>', text, re.S))
    matches = [m for m in layer_matches if '<ClassName>' + plan['layer'] + '</ClassName>' in m.group()
               and '<SlotNo>' + plan['slot'] + '</SlotNo>' in m.group()]
    require(len(matches) == 1, '保存用レイヤー範囲を特定できません')
    match = matches[0]
    raw = match.group()
    newline = '\r\n' if '\r\n' in raw else '\n'
    raw_templates = {}
    for fm in re.finditer(r'    <Frame>\r?\n.*?    </Frame>\r?\n', raw, re.S):
        fr = int(re.search(r'<FrameNo>(\d+)</FrameNo>', fm.group())[1])
        for bm in re.finditer(r'      <Bone>\r?\n.*?      </Bone>\r?\n', fm.group(), re.S):
            name = ET.fromstring(bm.group()).findtext('Transform/Name')
            if name in templates and structural(ET.fromstring(bm.group())) == structural(templates[name]):
                raw_templates[name] = bm.group()
    require(set(raw_templates) == set(templates), '全モデルの保存用テンプレートが揃いません')
    by_frame = {}
    for (fr, name), bone in additions.items():
        raw_bone = raw_templates[name]
        values = list(re.finditer(r'^          <Value>([^<]*)</Value>', raw_bone, re.M))
        require(len(values) == 49, '保存用材質値数が不正です')
        vm = values[41]
        value = bone.find('Transform').findall('Value')[41].text
        raw_bone = raw_bone[:vm.start(1)] + value + raw_bone[vm.end(1):]
        require(structural(ET.fromstring(raw_bone)) == structural(bone), '追加キーの限定置換が不一致です')
        by_frame.setdefault(fr, []).append(raw_bone)
    pending = dict(by_frame)
    def update_frame(fm):
        block = fm.group()
        fr = int(re.search(r'<FrameNo>(\d+)</FrameNo>', block)[1])
        prefix = ''
        for new_fr in sorted(f for f in pending if f < fr):
            prefix += '    <Frame>' + newline + f'      <FrameNo>{new_fr}</FrameNo>' + newline
            prefix += ''.join(pending.pop(new_fr)) + '    </Frame>' + newline
        if fr in pending:
            block = block.replace('    </Frame>', ''.join(pending.pop(fr)) + '    </Frame>')
        return prefix + block
    updated = re.sub(r'    <Frame>\r?\n.*?    </Frame>\r?\n', update_frame, raw, flags=re.S)
    require(not pending, '末尾への未処理キーがあります')
    candidate = (text[:match.start()] + updated + text[match.end():]).encode('utf-8')
    after = ET.fromstring(candidate)
    after_layer = next(l for l in after.findall('Layer') if l.findtext('ClassName') == plan['layer']
                       and l.findtext('SlotNo') == plan['slot'])
    original_frames = {int(f.findtext('FrameNo')) for f in layer.findall('Frame')}
    found = set()
    for frame in list(after_layer.findall('Frame')):
        fr = int(frame.findtext('FrameNo'))
        seen = set()
        for bone in list(frame.findall('Bone')):
            name = bone.findtext('Transform/Name')
            require(name not in seen, '保存候補に重複キーがあります')
            seen.add(name)
            if (fr, name) in additions:
                require(structural(bone) == structural(additions[fr, name]), '追加キーの検証が不一致です')
                found.add((fr, name))
                frame.remove(bone)
        if fr not in original_frames:
            require(not frame.findall('Bone'), '新規フレームに対象外キーがあります')
            after_layer.remove(frame)
    require(found == set(additions), '追加キーの件数が不一致です')
    require(structural(after) == structural(root), '対象外に構造差分があります')
    require(candidate.startswith(original[:3]) and candidate.count(b'\r\n') - original.count(b'\r\n')
            == candidate.count(b'\n') - original.count(b'\n'), '改行形式が変わっています')
    summary = {'source': str(args.xml), 'original_sha256': digest, 'added_keys': len(additions),
               'added_frames': len(set(f for f, n in additions) - original_frames),
               'peak_emission': peak_value, 'pulses': pulses,
               'validation': 'XML構文・登録採番・値数・重複・4/4/4周期・対象外構造一致を確認',
               'game_verified': False}
    if args.apply:
        require(args.xml.read_bytes() == original, '書き込み直前に原本が変更されました')
        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
        backup = args.xml.with_name(args.xml.name + '.bak.floor355-498-' + stamp)
        with backup.open('xb') as stream:
            stream.write(original)
        require(backup.read_bytes() == original, 'バックアップの検証に失敗しました')
        require(args.xml.read_bytes() == original, 'バックアップ中に原本が変更されました')
        args.xml.write_bytes(candidate)
        require(args.xml.read_bytes() == candidate, '保存後のバイト列が不一致です')
        ET.parse(args.xml)
        summary['backup'] = str(backup)
        summary['saved_sha256'] = hashlib.sha256(candidate).hexdigest()
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == '__main__':
    main()
