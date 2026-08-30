using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// サブカメラ 1 台分の設定行 (有効 / 追従 / 位置・回転 / FoV / ビューポート)。
    ///
    /// サブカメラには委譲先の個別ウィンドウが無いため、共有元はレイヤーの
    /// SubCameraTimelineLayer.DrawWindow になる。レイヤー本体は MTE 逐語コピーで
    /// 触らない方針のため、同じ書き込み先 (SubCameraData / MaidFollowSubCamera) へ
    /// 同じ意味の編集を行う形でここに用意している。
    /// 書き込み先はどのスナップショットにも含まれないため履歴は記録しない
    /// (レイヤー側の UI も記録していない)。
    ///
    /// コンボボックスの開閉状態と回転オフセットのキャッシュを持つため、
    /// カメラごと・描画するビューごとにインスタンスを分ける
    /// </summary>
    public class SubCameraRowDrawer
    {
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static MTEP.SubCameraManager subCameraManager
            => MTEP.SubCameraManager.instance;

        /// <summary>レイヤー UI の FoV スライダーと同じ既定値</summary>
        private const float DefaultFov = 35f;

        private readonly GUIComboBox<MTEP.MaidCache> _followMaidComboBox =
            new GUIComboBox<MTEP.MaidCache>
            {
                getName = (maidCache, _) => maidCache == null ? "なし" : maidCache.fullName,
            };

        private readonly GUIComboBox<MTEP.MaidPointType> _followPointComboBox =
            new GUIComboBox<MTEP.MaidPointType>
            {
                items = Enum.GetValues(typeof(MTEP.MaidPointType))
                    .Cast<MTEP.MaidPointType>().ToList(),
                getName = (type, _) => MTEP.MaidCache.GetMaidPointTypeName(type),
            };

        /// <summary>先頭に「なし」(null) を含む追従メイドの選択肢</summary>
        private readonly List<MTEP.MaidCache> _followMaidItems = new List<MTEP.MaidCache>();

        private readonly EulerOffsetCache _offsetCache = new EulerOffsetCache();

        public void Draw(
            GUIView view,
            MTEP.SubCameraData cameraData,
            float labelWidth,
            float rowHeight)
        {
            var camera = cameraData.camera;
            var follow = cameraData.follow;

            view.DrawToggle("有効", cameraData.visible, 100, rowHeight,
                newValue => cameraData.visible = newValue);

            DrawFollowMaidRow(view, follow, labelWidth, rowHeight);

            if (follow.isFollow)
            {
                _followPointComboBox.currentIndex = (int)follow.maidPointType;
                _followPointComboBox.onSelected = (type, _) => follow.maidPointType = type;
                LabeledComboRow.Draw(view, "追従ポイント", _followPointComboBox,
                    labelWidth, rowHeight);

                view.DrawToggle("向き反映", follow.followRotation, 100, rowHeight,
                    newValue => follow.followRotation = newValue);
            }

            // 追従中の位置は追従点からのオフセットになる (SubCameraData.position と同じ扱い)
            Vector3RowDrawer.Draw(view,
                follow.isFollow ? "オフセット" : "位置",
                ObjectTransformRowDrawer.PositionSensitivity, labelWidth, rowHeight,
                cameraData.position,
                value => cameraData.position = value,
                () => cameraData.position = Vector3.zero);

            DrawRotationRow(view, cameraData, follow, labelWidth, rowHeight);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "FoV",
                labelWidth = labelWidth,
                width = -1,
                min = 1,
                max = 179,
                step = 0.1f,
                defaultValue = DefaultFov,
                value = camera.fieldOfView,
                onChanged = value => camera.fieldOfView = value,
            });

            DrawViewportRows(view, cameraData, labelWidth, rowHeight);
        }

        /// <summary>追従メイドの選択行。先頭の「なし」を選ぶと追従を解除する</summary>
        private void DrawFollowMaidRow(
            GUIView view, MTEP.MaidFollowSubCamera follow, float labelWidth, float rowHeight)
        {
            _followMaidItems.Clear();
            _followMaidItems.Add(null);
            _followMaidItems.AddRange(maidManager.maidCaches);

            _followMaidComboBox.items = _followMaidItems;
            _followMaidComboBox.currentIndex =
                ToFollowMaidIndex(follow.maidSlotNo, _followMaidItems.Count);
            _followMaidComboBox.onSelected =
                (maidCache, index) => follow.maidSlotNo = ToFollowMaidSlotNo(index);

            LabeledComboRow.Draw(view, "追従メイド", _followMaidComboBox, labelWidth, rowHeight);
        }

        /// <summary>
        /// 追従メイドの maidSlotNo → コンボの添字。先頭に「なし」がある分 1 つずれる。
        /// 退去などで選択肢が減ったスロット番号は末尾へ丸める
        /// </summary>
        public static int ToFollowMaidIndex(int maidSlotNo, int itemCount)
        {
            return Mathf.Clamp(maidSlotNo + 1, 0, itemCount - 1);
        }

        /// <summary>コンボの添字 → 追従メイドの maidSlotNo (「なし」は -1)</summary>
        public static int ToFollowMaidSlotNo(int index)
        {
            return index - 1;
        }

        /// <summary>
        /// 回転行。向き反映中は追従点からの角度オフセットをそのまま編集し、
        /// それ以外はカメラのワールド回転を編集する (SubCameraData.rotation と同じ扱い)。
        /// ワールド回転側は quaternion からの再分解で表示が飛ばないようキャッシュを通す
        /// </summary>
        private void DrawRotationRow(
            GUIView view, MTEP.SubCameraData cameraData, MTEP.MaidFollowSubCamera follow,
            float labelWidth, float rowHeight)
        {
            if (follow.isFollow && follow.followRotation)
            {
                Vector3RowDrawer.Draw(view, "回転",
                    ObjectTransformRowDrawer.RotationSensitivity, labelWidth, rowHeight,
                    follow.eulerAnglesOffset,
                    value => follow.eulerAnglesOffset = value,
                    () => follow.eulerAnglesOffset = Vector3.zero);
                return;
            }

            var cameraTransform = cameraData.camera.transform;
            Vector3RowDrawer.Draw(view, "回転",
                ObjectTransformRowDrawer.RotationSensitivity, labelWidth, rowHeight,
                _offsetCache.GetOffset(cameraTransform, Quaternion.identity, false),
                value => SetWorldEulerAngles(cameraTransform, value),
                () => SetWorldEulerAngles(cameraTransform, Vector3.zero));
        }

        /// <summary>ワールド回転を書き込み、表示に使ったオイラー表現をキャッシュへ控える</summary>
        private void SetWorldEulerAngles(Transform cameraTransform, Vector3 eulerAngles)
        {
            cameraTransform.rotation = Quaternion.Euler(eulerAngles);
            _offsetCache.Store(cameraTransform, Quaternion.identity, eulerAngles, false);
        }

        /// <summary>
        /// ビューポート (画面内での表示位置と大きさ) の 4 行。
        /// カメラへの反映は UpdateCameraViewport 経由で行う (レイヤー UI と同じ)
        /// </summary>
        private void DrawViewportRows(
            GUIView view, MTEP.SubCameraData cameraData, float labelWidth, float rowHeight)
        {
            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("ビューポート設定", -1, rowHeight);

            var viewportRect = cameraData.viewportRect;
            var updated = false;

            var defaultViewport = MTEP.SubCameraManager.DefaultViewport;
            updated |= DrawViewportSlider(view, "X", viewportRect.x, defaultViewport.x,
                labelWidth, value => viewportRect.x = value);
            updated |= DrawViewportSlider(view, "Y", viewportRect.y, defaultViewport.y,
                labelWidth, value => viewportRect.y = value);
            updated |= DrawViewportSlider(view, "幅", viewportRect.width, defaultViewport.width,
                labelWidth, value => viewportRect.width = value);
            updated |= DrawViewportSlider(view, "高さ", viewportRect.height,
                defaultViewport.height, labelWidth, value => viewportRect.height = value);

            if (updated)
            {
                subCameraManager.UpdateCameraViewport(cameraData.name, viewportRect);
            }
        }

        private static bool DrawViewportSlider(
            GUIView view, string label, float value, float defaultValue,
            float labelWidth, Action<float> onChanged)
        {
            return view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = labelWidth,
                width = -1,
                min = 0,
                max = 1,
                step = 0.01f,
                defaultValue = defaultValue,
                value = value,
                onChanged = onChanged,
            });
        }
    }
}
