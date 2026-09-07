"""COM3D2タイムラインを読み取り専用で検証し、JSON要約を返す。"""

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


# 2026-09-07の実装とversion 33で確認した値数。未知の版には適用しない。
VALUE_COUNTS = {
    "Model": {12}, "Root": {7}, "Rotation": {4}, "Move": {11},
    "BGModel": {11}, "ModelBone": {11}, "Camera": {10, 13},
    "Light": {18}, "ModelShapeKey": {2}, "ModelMaterial": {49},
}
GROUP_SUFFIX = re.compile(r"^(.*) \((\d+)\)$")


def base_name(name):
    match = GROUP_SUFFIX.fullmatch(name)
    return match.group(1) if match else name


def inspect(path, model=None, expect_count=None, require_frame=None, show_models=False):
    raw = Path(path).read_bytes()
    root = ET.fromstring(raw)
    errors, warnings = [], []
    issue_counts = Counter()

    def issue(level, message):
        issue_counts[level] += 1
        target = errors if level == "error" else warnings
        if len(target) < 30:
            target.append(message)

    if root.tag != "TimelineData":
        raise ValueError("ルート要素が名前空間なしのTimelineDataではありません。")
    version_text = root.get("version", "0")
    try:
        version = int(version_text)
    except ValueError:
        raise ValueError("versionが整数ではありません。") from None
    if version != 33:
        issue("warning", f"version {version} は値数検証の対象外です。互換処理を実装で確認してください。")
    if root.findall("Frame"):
        issue("warning", "ルート直下に旧形式のFrameがあります。旧形式のキーはこのツールでは検証しません。")

    registry = [entry.findtext("Name", "") for entry in root.findall("Models/Model")]
    registry_set = set(registry)
    selected = [name for name in registry if model is None or base_name(name) == model]
    for name, count in Counter(registry).items():
        if not name or count > 1:
            issue("error", f"モデル登録名が空または重複しています: {name!r} ({count}件)")
    if model is not None and not selected:
        issue("error", f"対象モデルが登録されていません: {model}")
    if expect_count is not None and len(selected) != expect_count:
        issue("error", f"対象モデル数が異なります: 期待={expect_count}、実際={len(selected)}")

    # 登録とキーの一致だけでなく、ゲーム側の再採番結果にも照合する。
    ordinal = Counter()
    for name in registry:
        base = base_name(name)
        ordinal[base] += 1
        expected = base if ordinal[base] == 1 else f"{base} ({ordinal[base]})"
        if (model is None or base == model) and name != expected:
            issue("error", f"ゲームの複製採番と不一致: {name!r} → {expected!r}。番号1は使いません。")

    keys = defaultdict(list)
    layers = []
    unknown_types = set()
    for layer in root.findall("Layer"):
        class_name = layer.findtext("ClassName", "")
        slot = layer.findtext("SlotNo", "0")
        label = f"{class_name}[{slot}]"
        frames = layer.findall("Frame")
        seen_frames = set()
        key_count = 0
        for frame in frames:
            try:
                frame_no = int(frame.findtext("FrameNo", "0"))
            except ValueError:
                issue("error", f"{label}: FrameNoが整数ではありません。")
                continue
            if frame_no in seen_frames:
                issue("error", f"{label}: フレーム{frame_no}が重複しています。")
            seen_frames.add(frame_no)
            seen_names = set()
            for bone in frame.findall("Bone"):
                transforms = bone.findall("Transform")
                if len(transforms) != 1:
                    issue("error", f"{label}/{frame_no}: Bone直下のTransformが1個ではありません。")
                    continue
                transform = transforms[0]
                name = transform.findtext("Name", "")
                kind = transform.findtext("Type", "")
                where = f"{label}/{frame_no}/{name}"
                key_count += 1
                # 背景等の単一対象レイヤーは空文字の識別名を正規に使う。
                if name in seen_names:
                    issue("error", f"{where}: キー名が同一フレームで重複しています。")
                if not name and class_name == "ModelTimelineLayer":
                    issue("error", f"{where}: モデルキー名が空です。")
                seen_names.add(name)
                values = transform.findall("Value")
                if version == 33 and kind in VALUE_COUNTS and len(values) not in VALUE_COUNTS[kind]:
                    issue("error", f"{where}: {kind}の値数が不正です: {len(values)}、期待={sorted(VALUE_COUNTS[kind])}")
                elif kind not in VALUE_COUNTS:
                    unknown_types.add(kind)
                numeric = values + transform.findall("InTangents/Value") + transform.findall("OutTangents/Value")
                try:
                    if any(not math.isfinite(float(item.text or "")) for item in numeric):
                        raise ValueError
                except ValueError:
                    issue("error", f"{where}: 数値配列に非数値または非有限値があります。")
                if class_name == "ModelTimelineLayer" and kind == "Model":
                    keys[name].append(frame_no)
                    if name not in registry_set and (model is None or base_name(name) == model):
                        issue("error", f"{where}: 対応するModels/Model登録がありません。")
        layers.append({"class": class_name, "slot": slot, "frames": len(frames), "keys": key_count})

    missing = []
    for name in selected:
        if not keys[name]:
            missing.append(name)
            issue("warning", f"モデルにModelキーがありません: {name}")
        if require_frame is not None and require_frame not in keys[name]:
            issue("error", f"フレーム{require_frame}のModelキーがありません: {name}")

    report = {
        "path": str(Path(path).resolve()), "sha256": hashlib.sha256(raw).hexdigest(),
        "version": version, "frame_rate": root.findtext("FrameRate"),
        "max_frame_no": root.findtext("MaxFrameNo"),
        "model_count": len(registry), "selected_model_count": len(selected),
        "models_without_keys": missing[:30], "layers": layers,
        "types_without_count_check": sorted(unknown_types),
        "error_count": issue_counts["error"], "warning_count": issue_counts["warning"],
        "errors": errors, "warnings": warnings,
        "note": "読み取り専用。各問題一覧は最大30件。全型・補間・ゲーム内ロードの検証ではありません。",
    }
    if show_models:
        report["models"] = [{"name": name, "key_count": len(keys[name]),
                             "first_frames": sorted(keys[name])[:8]} for name in selected]
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", help="検証するタイムラインXML")
    parser.add_argument("--model", help="対象の接尾辞なしモデル名。省略時は全モデル")
    parser.add_argument("--expect-count", type=int, help="元を含む対象モデルの合計数")
    parser.add_argument("--require-frame", type=int, help="対象モデルすべてにキーを要求するフレーム")
    parser.add_argument("--show-models", action="store_true", help="対象モデル別のキー数も表示")
    args = parser.parse_args()
    if args.expect_count is not None and args.expect_count < 0:
        parser.error("期待するモデル数は0以上で指定してください。")
    try:
        report = inspect(args.path, args.model, args.expect_count, args.require_frame, args.show_models)
    except (OSError, ET.ParseError, ValueError) as exc:
        report = {"error_count": 1, "errors": [f"タイムラインの検証に失敗しました: {exc}"]}
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if report["error_count"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
