using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// GameObject の Transform 3 行 (位置 / 回転 / 拡縮)。
    /// InspectorWindow の Object 表示と TimelineItemInspector (移動レイヤーの項目表示) で共有する。
    /// 履歴記録とギズモの Local/Global 追従までここで面倒を見る。
    /// 回転オフセットのキャッシュを持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class ObjectTransformRowDrawer
    {
        public const float PositionSensitivity = 0.01f;
        public const float RotationSensitivity = 1f;

        /// <summary>入力した角度を保つためのキャッシュ (Quaternion 経由だと表示が跳ねるため)</summary>
        private readonly EulerOffsetCache _eulerCache = new EulerOffsetCache();

        /// <param name="labelWidth">位置・回転行のラベル幅</param>
        /// <param name="scaleLabelWidth">拡縮行のラベル幅 (連動トグル分を差し引いた幅)</param>
        public void Draw(
            GUIView view, GameObject go, float labelWidth, float scaleLabelWidth, float rowHeight)
        {
            var t = go.transform;
            // ギズモの Local/Global 切替に合わせて表示・編集する座標系も切り替える
            var useLocal = GizmoRenderer.useLocalSpace;

            Vector3RowDrawer.Draw(view, "位置", PositionSensitivity, labelWidth, rowHeight,
                useLocal ? t.localPosition : t.position,
                value =>
                {
                    RecordEdit(go);
                    SetPosition(t, value, useLocal);
                },
                () =>
                {
                    RecordEdit(go);
                    SetPosition(t, Vector3.zero, useLocal);
                });

            Vector3RowDrawer.Draw(view, "回転", RotationSensitivity, labelWidth, rowHeight,
                _eulerCache.GetOffset(t, Quaternion.identity, useLocal),
                value =>
                {
                    RecordEdit(go);
                    ApplyEulerAngles(t, value, useLocal);
                },
                () =>
                {
                    RecordEdit(go);
                    ApplyEulerAngles(t, Vector3.zero, useLocal);
                });

            // ワールドスケールは Transform に書き戻せないため拡縮は常にローカル
            ScaleRowDrawer.Draw(view, t.localScale, scaleLabelWidth, rowHeight,
                value =>
                {
                    RecordEdit(go);
                    t.localScale = value;
                },
                () =>
                {
                    RecordEdit(go);
                    t.localScale = Vector3.one;
                });
        }

        /// <summary>
        /// Object 行の編集を操作履歴へ記録する (ドラッグ中の連続変更は 1 件に集約される)。
        /// Transform 以外の Object 編集 (ヘッダーのアクティブ切替) からも使う
        /// </summary>
        public static void RecordEdit(GameObject go)
        {
            HistoryManager.instance.BeforeEdit(
                go.GetComponent<Maid>(), HistoryScope.Object,
                "オブジェクト編集: " + go.name, new[] { go.transform });
        }

        private static void SetPosition(Transform t, Vector3 value, bool useLocal)
        {
            if (useLocal)
            {
                t.localPosition = value;
            }
            else
            {
                t.position = value;
            }
        }

        private void ApplyEulerAngles(Transform t, Vector3 eulerAngles, bool useLocal)
        {
            if (useLocal)
            {
                t.localEulerAngles = eulerAngles;
            }
            else
            {
                t.eulerAngles = eulerAngles;
            }
            _eulerCache.Store(t, Quaternion.identity, eulerAngles, useLocal);
        }
    }
}
