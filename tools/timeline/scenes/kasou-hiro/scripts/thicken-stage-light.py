"""ステージライト円錐の見た目を太くする。

ビルボード描画から視線積分の体積円錐へ切り替わったことで、円錐の縁が
coreRadius から外側へ向かって減衰し、同じ spotAngle でも細く見える。
StageLightTimelineLayer の StageLight / StageLightController 全キーに対し、
spotAngle を倍率で拡大し、coreRadius を下限値まで引き上げる。

使い方:
  python thicken-stage-light.py 対象.xml [--angle-scale 1.3] [--core-radius-min 0.5]
                                [--sha256 原本のSHA256] [--apply]
"""
from pathlib import Path
import argparse, re, datetime, hashlib, collections
import xml.etree.ElementTree as E

LAYER_CLASS = 'StageLightTimelineLayer'
# Type -> (値数, spotAngle の添字, coreRadius の添字)
LAYOUT = {'StageLight': (23, 12, 18), 'StageLightController': (37, 21, 27)}
SPOT_ANGLE_MAX = 179.0
CORE_RADIUS_MAX = 1.0


def fmt(x: float) -> str:
    return format(x, '.9g')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('xml', type=Path)
    ap.add_argument('--angle-scale', type=float, default=1.3, help='spotAngle に掛ける倍率')
    ap.add_argument('--core-radius-min', type=float, default=0.5, help='coreRadius の下限。既存値がこれ以上なら保持')
    ap.add_argument('--sha256', help='検証時の原本 SHA-256。--apply 時は必須')
    ap.add_argument('--apply', action='store_true')
    a = ap.parse_args()
    assert a.angle_scale > 0, 'angle-scale は正の値にしてください'
    assert 0 <= a.core_radius_min <= CORE_RADIUS_MAX, 'core-radius-min は 0〜1 にしてください'

    raw = a.xml.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    if a.apply:
        assert a.sha256, '--apply には --sha256 が必要です'
    if a.sha256:
        assert digest == a.sha256, '原本が更新されたため中止します'
    root = E.fromstring(raw)

    # 変更対象 Value 要素の id -> 期待する新しい文字列
    expected = {}
    counts = collections.Counter()
    angle_stats = collections.defaultdict(set)
    for layer in root.findall('Layer'):
        if layer.findtext('ClassName') != LAYER_CLASS:
            continue
        for tr in layer.findall('Frame/Bone/Transform'):
            kind = tr.findtext('Type')
            assert kind in LAYOUT, f'未対応のステージライト型です: {kind}'
            n, ia, ic = LAYOUT[kind]
            vals = tr.findall('Value')
            assert len(vals) == n, f'値数が一致しません: {kind} {len(vals)}'
            old_angle = float(vals[ia].text)
            new_angle = min(old_angle * a.angle_scale, SPOT_ANGLE_MAX)
            expected[id(vals[ia])] = fmt(new_angle)
            angle_stats[kind].add((fmt(old_angle), fmt(new_angle)))
            old_core = float(vals[ic].text)
            new_core = min(max(old_core, a.core_radius_min), CORE_RADIUS_MAX)
            expected[id(vals[ic])] = fmt(new_core)
            counts[kind] += 1

    def edit_layer(m):
        if E.fromstring(m.group()).findtext('ClassName') != LAYER_CLASS:
            return m.group()

        def edit_transform(tm):
            t = E.fromstring(tm.group())
            n, ia, ic = LAYOUT[t.findtext('Type')]
            vals = t.findall('Value')
            matches = list(re.finditer(r'<Value>[^<]*</Value>', tm.group()))
            assert len(matches) == n, '値数が一致しません'
            s = tm.group()
            # 再パースで id が変わるため添字で対応付け、後ろから置換して位置ずれを防ぐ
            for idx in sorted((ia, ic), reverse=True):
                old = float(vals[idx].text)
                new = fmt(min(old * a.angle_scale, SPOT_ANGLE_MAX)) if idx == ia else fmt(min(max(old, a.core_radius_min), CORE_RADIUS_MAX))
                mt = matches[idx]
                s = s[:mt.start()] + '<Value>' + new + '</Value>' + s[mt.end():]
            return s

        return re.sub(r'<Transform>.*?</Transform>', edit_transform, m.group(), flags=re.S)

    out = re.sub(r'<Layer>.*?</Layer>', edit_layer, raw.decode('utf-8'), flags=re.S).encode('utf-8')

    # 再読して対象外が変わっていないことを確認する
    after = E.fromstring(out)
    before_nodes, after_nodes = list(root.iter()), list(after.iter())
    assert len(before_nodes) == len(after_nodes), '要素数が変わりました'
    changed = 0
    for x, y in zip(before_nodes, after_nodes):
        assert x.tag == y.tag and x.attrib == y.attrib and x.tail == y.tail, '対象外の構造が変わりました'
        if id(x) in expected:
            assert y.text == expected[id(x)], f'期待値と一致しません: {x.text} -> {y.text} (期待 {expected[id(x)]})'
            changed += 1
        else:
            assert x.text == y.text, '対象外の値が変わりました'
    assert changed == len(expected), '変更件数が一致しません'

    print('原本SHA-256:', digest)
    print('検証済み: 対象Transform', dict(counts), 'angle-scale', a.angle_scale, 'core-radius-min', a.core_radius_min)
    for kind, pairs in angle_stats.items():
        sample = sorted(pairs, key=lambda p: float(p[0]))
        print(f'  {kind} spotAngle 旧->新 ({len(sample)}種):', ', '.join(f'{o}->{n}' for o, n in sample[:12]), '...' if len(sample) > 12 else '')
    if not a.apply:
        print('--apply 未指定のため書き込みなし')
        return
    if out == raw:
        print('変更なし')
        return
    assert a.xml.read_bytes() == raw, '原本が更新されたため中止します'
    backup = a.xml.with_name(a.xml.name + '.stagelight-thicken-backup-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
    with backup.open('xb') as f:
        f.write(raw)
    assert a.xml.read_bytes() == raw, '原本が更新されたため中止します'
    a.xml.write_bytes(out)
    assert a.xml.read_bytes() == out, '保存結果が一致しません'
    E.parse(a.xml)
    print('バックアップ:', backup)
    print('反映後SHA-256:', hashlib.sha256(out).hexdigest())


if __name__ == '__main__':
    main()
