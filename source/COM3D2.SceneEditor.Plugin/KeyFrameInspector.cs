using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 選択中キーフレームの詳細表示・編集。
    /// 選択キーフレームごとに折りたたみ可能なブロックを縦に並べ、
    /// Transform は標準 Inspector と同じ横並び行、色・カスタム値・表示・文字列値は
    /// タイムライン項目の現在値 UI (TimelineItemInspector) と同じ部品で 1 値 1 行に描く
    /// (「補間曲線」タブに KeyFrameTangentDrawer のタンジェント曲線エディタを表示する。
    /// 区間ごとの実値カーブ編集は TimelineCurveEditor が担当)
    /// </summary>
    public class KeyFrameInspector
    {
        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private const float RowHeight = 20f;
        /// <summary>Transform 行のラベル幅 (InspectorWindow と揃える)</summary>
        private const float TransformLabelWidth = 50f;
        /// <summary>ブロックヘッダーの開閉マーク幅</summary>
        private const float FoldMarkWidth = 16f;
        /// <summary>ブロックヘッダー右端のボタン幅</summary>
        private const float HeaderButtonWidth = 44f;
        /// <summary>一括開閉ボタンの幅 (「すべて折りたたみ」が収まる幅)</summary>
        private const float FoldAllButtonWidth = 90f;
        /// <summary>ヘッダーのボーン名を切り詰める下限 (これ以下だと名前が読めない)</summary>
        private const float MinHeaderLabelWidth = 40f;
        /// <summary>文字列値のラベル幅 (「ﾎﾟｰｽﾞ名」等が収まる幅)</summary>
        private const float StrLabelWidth = 70f;

        // 1px ドラッグあたりの増減量 (InspectorWindow と揃える)
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        /// <summary>タブ 1 個の幅 (「補間曲線」が収まる幅)</summary>
        private const float TabWidth = 70f;

        /// <summary>キーフレーム詳細のタブ (列挙名がそのままタブ名になる)</summary>
        private enum TabType
        {
            値,
            補間曲線,
        }

        /// <summary>一括開閉ボタンの要求</summary>
        private enum FoldAllRequest
        {
            None,
            Expand,
            Collapse,
        }

        /// <summary>
        /// 折りたたみ中のキーフレーム。既定は展開なので「畳んだもの」だけを覚える。
        /// Inspector 外 (タイムラインのショートカット等) からも削除されうるので、
        /// 毎フレーム選択中のものだけへ絞り込んで古い BoneData 参照を残さない
        /// (セッション中のみ有効。config へは永続化しない)
        /// </summary>
        private readonly HashSet<MTEP.BoneData> _collapsedBones = new HashSet<MTEP.BoneData>();

        /// <summary>描画順を安定させるための並べ替えバッファ (毎フレームの確保を避ける)</summary>
        private readonly List<MTEP.BoneData> _sortedBones = new List<MTEP.BoneData>();

        /// <summary>描画ループ終了後にまとめて削除するボーン</summary>
        private MTEP.BoneData _pendingDeleteBone = null;

        /// <summary>描画ループ終了後に開閉を切り替えるボーン</summary>
        private MTEP.BoneData _pendingToggleBone = null;

        /// <summary>描画ループ終了後に反映する一括開閉の要求</summary>
        private FoldAllRequest _pendingFoldAll = FoldAllRequest.None;

        /// <summary>表示中のタブ (セッション中のみ有効。config へは永続化しない)</summary>
        private TabType _tabType = TabType.値;

        private static KeyFrameInspector _instance = null;
        public static KeyFrameInspector instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new KeyFrameInspector();
                }
                return _instance;
            }
        }

        private KeyFrameInspector()
        {
        }

        /// <summary>キーフレーム選択中で Inspector に表示すべき状態か</summary>
        public static bool ShouldDraw()
        {
            return timelineManager.timeline != null
                && currentLayer != null
                && timelineManager.HasSelected();
        }

        public void Draw(GUIView view)
        {
            if (!ShouldDraw())
            {
                _collapsedBones.Clear();
                KeyFrameTangentDrawer.instance.CancelDrag();
                view.DrawLabel("キーフレームが選択されていません", -1, RowHeight);
                return;
            }

            // Inspector 外の削除経路を通ると選択から外れた BoneData が残るため、ここで掃除する
            _collapsedBones.RemoveWhere(bone => !selectedBones.Contains(bone));

            _tabType = view.DrawTabs(_tabType, TabWidth, RowHeight);
            // DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」に
            // なるため、通常の行間に合わせて詰める (BackgroundWindow と同じ流儀)
            view.currentPos.y -= 5 + GUIView.defaultMargin;
            view.DrawHorizontalLine(Color.gray);

            if (_tabType == TabType.補間曲線)
            {
                if (!KeyFrameTangentDrawer.instance.Draw(view))
                {
                    view.DrawLabel("補間曲線を持つキーフレームが選択されていません", -1, RowHeight);
                }
                return;
            }

            // 曲線タブを離れている間はハンドルのマウスアップを拾えないので打ち切らせる
            KeyFrameTangentDrawer.instance.CancelDrag();
            DrawValues(view);
        }

        /// <summary>「値」タブ。選択キーフレームのブロックを縦に並べる</summary>
        private void DrawValues(GUIView view)
        {
            var maxCount = Mathf.Max(1, config.detailTransformCount);
            var totalCount = selectedBones.Count;

            // 選択順は HashSet で不定なので、フレーム番号 → 名前で毎回同じ並びにする
            _sortedBones.Clear();
            _sortedBones.AddRange(selectedBones);
            _sortedBones.Sort(CompareBone);

            var drawCount = Mathf.Min(totalCount, maxCount);

            DrawTitleRow(view, totalCount, drawCount);

            for (var i = 0; i < drawCount; i++)
            {
                DrawBoneBlock(view, _sortedBones[i]);
            }

            if (totalCount > drawCount)
            {
                view.DrawLabel(
                    string.Format("他 {0} 個は非表示", totalCount - drawCount), -1, RowHeight);
            }

            // 一括開閉は表示中のブロックが対象なので、_sortedBones を捨てる前に反映する
            ProcessPendingFold(drawCount);
            _sortedBones.Clear();

            ProcessPendingDelete();
        }

        private static int CompareBone(MTEP.BoneData a, MTEP.BoneData b)
        {
            var result = a.frameNo.CompareTo(b.frameNo);
            if (result != 0)
            {
                return result;
            }
            return string.CompareOrdinal(a.name, b.name);
        }

        /// <summary>選択数の表示と、表示中ブロックの一括開閉ボタンの 1 行</summary>
        private void DrawTitleRow(GUIView view, int totalCount, int drawCount)
        {
            var allCollapsed = IsAllCollapsed(drawCount);

            var available = view.viewRect.width - view.padding.x * 2;
            var labelWidth = Mathf.Max(
                available - FoldAllButtonWidth - view.margin * 2, MinHeaderLabelWidth);

            view.BeginHorizontal();
            {
                view.DrawLabel(
                    string.Format("キーフレーム詳細 ({0}個選択中)", totalCount),
                    labelWidth, RowHeight);

                // 全部畳んでいるときだけ「すべて展開」にして、押すたびに全開・全閉を往復させる
                if (view.DrawButton(
                        allCollapsed ? "すべて展開" : "すべて折りたたみ",
                        FoldAllButtonWidth, RowHeight))
                {
                    _pendingFoldAll = allCollapsed
                        ? FoldAllRequest.Expand : FoldAllRequest.Collapse;
                }
            }
            view.EndLayout();
        }

        private bool IsAllCollapsed(int drawCount)
        {
            for (var i = 0; i < drawCount; i++)
            {
                if (!_collapsedBones.Contains(_sortedBones[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private void DrawBoneBlock(GUIView view, MTEP.BoneData bone)
        {
            view.DrawHorizontalLine(Color.gray);

            var expanded = !_collapsedBones.Contains(bone);
            DrawBlockHeader(view, bone, expanded);

            if (!expanded)
            {
                return;
            }

            var transform = bone.transform;
            DrawTransform(view, bone, transform);
            DrawColorRows(view, bone, transform);
            DrawCustomValues(view, bone, transform);
            DrawStrValues(view, bone, transform);
        }

        /// <summary>開閉マーク + ボーン名(フレーム番号) + 初期化 / 削除ボタンの 1 行</summary>
        private void DrawBlockHeader(GUIView view, MTEP.BoneData bone, bool expanded)
        {
            var available = view.viewRect.width - view.padding.x * 2;
            // 要素は 4 個 (マーク・名前・初期化・削除) なので margin を 4 個ぶん引く
            var labelWidth = available
                - FoldMarkWidth - HeaderButtonWidth * 2 - view.margin * 4;
            labelWidth = Mathf.Max(labelWidth, MinHeaderLabelWidth);

            view.BeginHorizontal();
            {
                Action toggle = () => _pendingToggleBone = bone;

                view.DrawLabel(expanded ? "▼" : "▶", FoldMarkWidth, RowHeight,
                    onClickAction: toggle);
                view.DrawLabel(
                    string.Format("{0} (F{1})", bone.name, bone.frameNo),
                    labelWidth, RowHeight, onClickAction: toggle);

                if (view.DrawButton("初期化", HeaderButtonWidth, RowHeight))
                {
                    bone.transform.Reset();
                    MTEUtils.LogDebug("キーフレームを初期化します：" + bone.name);
                    bone.parentLayer.ApplyCurrentFrame(true);
                }

                if (view.DrawButton("削除", HeaderButtonWidth, RowHeight))
                {
                    _pendingDeleteBone = bone;
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 開閉の反映を描画ループの外へ遅延させる。
        /// GUIView は FloatFieldCache を描画順のインデックスで採番するため、
        /// 同じ Unity フレームの途中で描画要素数が変わるとキャッシュの対応がずれる
        /// </summary>
        private void ProcessPendingFold(int drawCount)
        {
            if (_pendingFoldAll == FoldAllRequest.Collapse)
            {
                for (var i = 0; i < drawCount; i++)
                {
                    _collapsedBones.Add(_sortedBones[i]);
                }
            }
            else if (_pendingFoldAll == FoldAllRequest.Expand)
            {
                for (var i = 0; i < drawCount; i++)
                {
                    _collapsedBones.Remove(_sortedBones[i]);
                }
            }
            _pendingFoldAll = FoldAllRequest.None;

            var bone = _pendingToggleBone;
            if (bone == null)
            {
                return;
            }
            _pendingToggleBone = null;

            if (!_collapsedBones.Remove(bone))
            {
                _collapsedBones.Add(bone);
            }
        }

        /// <summary>削除は selectedBones を変更するため、描画ループを抜けてから実行する</summary>
        private void ProcessPendingDelete()
        {
            var bone = _pendingDeleteBone;
            if (bone == null)
            {
                return;
            }
            _pendingDeleteBone = null;

            // RemoveBone で parentFrame が外れる前に所属レイヤーを確保する
            var layer = bone.parentLayer;
            var frame = bone.parentFrame;
            if (frame != null)
            {
                frame.RemoveBone(bone);
            }

            selectedBones.Remove(bone);
            _collapsedBones.Remove(bone);

            layer.CleanFrames();
            layer.ApplyCurrentFrame(true);

            MTEUtils.LogDebug("キーフレームを削除します：" + bone.name);
            timelineManager.RequestHistory("キーフレーム削除");
        }

        /// <summary>位置 / 回転 / 拡縮 を標準 Inspector と同じ横並び行で描く</summary>
        private void DrawTransform(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            if (transform.hasPosition)
            {
                DrawVector3Row(view, bone, "位置", PositionSensitivity,
                    transform.position,
                    value => transform.position = value,
                    () => transform.position = transform.initialPosition);
            }

            if (transform.hasRotation || transform.hasEulerAngles)
            {
                DrawVector3Row(view, bone, "回転", RotationSensitivity,
                    transform.normalizedEulerAngles,
                    value => transform.eulerAngles = value,
                    () => transform.eulerAngles = transform.initialEulerAngles);
            }

            if (transform.hasScale)
            {
                DrawVector3Row(view, bone, "拡縮", ScaleSensitivity,
                    transform.scale,
                    value => transform.scale = value,
                    () => transform.scale = transform.initialScale);
            }
        }

        private void DrawVector3Row(
            GUIView view,
            MTEP.BoneData bone,
            string label,
            float sensitivity,
            Vector3 value,
            Action<Vector3> setValue,
            Action resetValue)
        {
            // GUIView.DrawVector3Row は数値入力の幅を viewRect.width から算出するため、
            // 外側を BeginHorizontal + AddSpace で包むと右端をはみ出す。インデントしないこと
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = label,
                labelWidth = TransformLabelWidth,
                height = RowHeight,
                dragSensitivity = sensitivity,
                value = value,
                onChanged = newValue =>
                {
                    setValue(newValue);
                    Apply(bone);
                },
                onReset = () =>
                {
                    resetValue();
                    Apply(bone);
                },
            });
        }

        /// <summary>
        /// 色マップの全色をカラーピッカー付きの行で描く (現在値 UI と同じ部品)。
        /// ColorPickerWindow はラベル文字列で編集対象を同定するため、
        /// 先頭に色名を置きつつフレーム番号とボーン名でキーフレームごとに一意にする。
        /// コールバックは ColorPickerWindow の描画中に呼ばれるが、bone.transform は
        /// 使い捨てではなく永続参照なので、そのまま書き換えてよい
        /// </summary>
        private void DrawColorRows(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            foreach (var pair in transform.GetColorValueInfoMap())
            {
                var colorKey = pair.Key;
                var info = pair.Value;
                var fieldCache = view.GetColorFieldCache(
                    string.Format("{0} (F{1}) {2}", info.name, bone.frameNo, bone.name), info.hasAlpha);
                view.DrawColor(fieldCache, transform.GetColorValue(colorKey), info.defaultValue, newValue =>
                {
                    transform.SetColorValue(colorKey, newValue);
                    Apply(bone);
                });
            }
        }

        /// <summary>カスタム値と表示トグルを 1 値 1 行で描く</summary>
        private void DrawCustomValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            foreach (var pair in transform.GetCustomValueInfoMap())
            {
                if (!transform.HasCustomValue(pair.Key))
                {
                    continue;
                }
                DrawCustomValueRow(view, bone, transform, pair.Key, pair.Value);
            }

            if (transform.hasVisible)
            {
                view.DrawToggle("表示", transform.visible, -1, RowHeight, newValue =>
                {
                    transform.visible = newValue;
                    Apply(bone);
                });
            }
        }

        /// <summary>
        /// カスタム値 1 個。現在値 UI (各 RowDrawer) と同じ DrawCustomValue* を使う。
        /// スライダー行の R は info.defaultValue へ戻す。数値欄のみの float 値は現在値 UI では
        /// リセットを持たないが、キーフレームでは従来どおり戻せるよう onReset を渡す
        /// (どちらも GetDefaultCustomValue と同じ値になる)
        /// </summary>
        private void DrawCustomValueRow(
            GUIView view,
            MTEP.BoneData bone,
            MTEP.ITransformData transform,
            string customKey,
            MTEP.CustomValueInfo info)
        {
            var value = transform.GetCustomValue(customKey).value;

            switch (info.type)
            {
                case MTEP.CustomValueType.BoolValue:
                    view.DrawCustomValueBool(info, value != 0f, newValue =>
                    {
                        transform.GetCustomValue(customKey).value = newValue ? 1f : 0f;
                        Apply(bone);
                    });
                    break;

                case MTEP.CustomValueType.IntValue:
                    view.DrawCustomValueInt(info, Mathf.RoundToInt(value), newValue =>
                    {
                        transform.GetCustomValue(customKey).value = newValue;
                        Apply(bone);
                    });
                    break;

                default:
                    view.DrawCustomValueFloat(info, value,
                        newValue =>
                        {
                            transform.GetCustomValue(customKey).value = newValue;
                            Apply(bone);
                        },
                        () =>
                        {
                            transform.GetCustomValue(customKey).value =
                                transform.GetDefaultCustomValue(customKey);
                            Apply(bone);
                        });
                    break;
            }
        }

        /// <summary>文字列値は幅が読めないので 1 行ずつ全幅で描く</summary>
        private void DrawStrValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            foreach (var pair in transform.GetStrValueInfoMap())
            {
                var strKey = pair.Key;
                if (!transform.HasStrValue(strKey))
                {
                    continue;
                }

                var value = transform.GetStrValue(strKey);

                view.DrawTextField(
                    transform.GetStrValueName(strKey),
                    StrLabelWidth,
                    value,
                    -1,
                    RowHeight,
                    newValue =>
                    {
                        if (newValue == value)
                        {
                            return;
                        }
                        transform.SetStrValue(strKey, newValue);
                        Apply(bone);
                    });
            }
        }

        /// <summary>編集を即時反映する</summary>
        private void Apply(MTEP.BoneData bone)
        {
            MTEUtils.LogDebug("キーフレームを更新します：" + bone.name);
            // 選択は複数レイヤーにまたがるため、所属レイヤーへ反映する
            bone.parentLayer.ApplyCurrentFrame(true);
        }
    }
}
