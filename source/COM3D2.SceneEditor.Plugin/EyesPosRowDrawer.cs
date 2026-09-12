using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 瞳の位置・サイズ (EyesPosL/R・EyesScaL/R) の行描画。
    /// 表情ウィンドウの視線タブと TimelineItemInspector (瞳レイヤーの項目表示) で共有する。
    /// 値の読み書きは EyesTimelineLayer の静的メソッドを通し、レイヤーのキーと同じ換算にする。
    /// 書き込み先はどのスナップショットにも含まれない MaidCache のため履歴は記録しない
    /// (レイヤー側の UI も記録していない)。
    /// ドラッグ状態とテクスチャを持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class EyesPosRowDrawer
    {
        /// <summary>位置図の一辺 (px)</summary>
        private const int ImageSize = 150;
        private const float RowHeight = 20f;

        /// <summary>この Drawer が扱う項目。位置図に続けてスライダーを並べる順でもある</summary>
        public static readonly MTEP.MotionEyesType[] AllEyesTypes = new MTEP.MotionEyesType[]
        {
            MTEP.MotionEyesType.EyesPosL,
            MTEP.MotionEyesType.EyesPosR,
            MTEP.MotionEyesType.EyesScaL,
            MTEP.MotionEyesType.EyesScaR,
        };

        /// <summary>位置図でドラッグして動かす項目 (サイズは図では扱わない)</summary>
        private static readonly MTEP.MotionEyesType[] DraggableEyesTypes = new MTEP.MotionEyesType[]
        {
            MTEP.MotionEyesType.EyesPosL,
            MTEP.MotionEyesType.EyesPosR,
        };

        private static MTEP.Config config => MTEP.ConfigManager.instance.config;

        private static MTEP.StudioHackManager studioHackManager =>
            MTEP.StudioHackManager.instance;

        private Texture2D _eyesPositionTex = null;
        private Texture2D _eyesTex = null;

        /// <summary>位置図でドラッグ中の瞳。押下時に近い方のマーカーを掴み、左右を個別に動かす</summary>
        private MTEP.MotionEyesType? _draggingEyesType = null;

        /// <summary>この Drawer が扱う項目か (Inspector の項目種別の振り分けに使う)</summary>
        public static bool IsEyesPosType(MTEP.MotionEyesType eyesType)
        {
            return Array.IndexOf(AllEyesTypes, eyesType) >= 0;
        }

        private void InitTexture()
        {
            if (_eyesPositionTex == null)
            {
                _eyesPositionTex = new Texture2D(ImageSize, ImageSize);
                TextureUtils.ClearTexture(_eyesPositionTex, config.curveBgColor);

                var color1 = config.curveLineColor;
                var color2 = new Color(color1.r, color1.g, color1.b, color1.a * 0.5f);

                // 10x10のグリッドの描画
                for (var i = 1; i < 10; i++)
                {
                    var x = i * (ImageSize / 10);
                    TextureUtils.DrawLineTexture(_eyesPositionTex, x, 0, x, ImageSize, color2);
                    TextureUtils.DrawLineTexture(_eyesPositionTex, 0, x, ImageSize, x, color2);
                }

                // 円の描画
                TextureUtils.DrawCircleLineTexture(
                    _eyesPositionTex,
                    ImageSize / 2 - 0.5f,
                    64,
                    config.curveLineColor);
            }

            if (_eyesTex == null)
            {
                _eyesTex = TextureUtils.CreateCircleTexture(
                    config.frameWidth,
                    Color.white);
            }
        }

        /// <summary>
        /// 左右の瞳位置をドラッグで動かす位置図。
        /// 図の右側に初期化ボタンを並べ、図の下端まで描画位置を進めてから戻る
        /// </summary>
        public void DrawEyesPosImage(GUIView view, MTEP.MaidCache maidCache)
        {
            if (maidCache == null)
            {
                return;
            }

            InitTexture();

            view.BeginAutoEditMode();

            var basePos = view.currentPos;

            DrawEyesImage(view, maidCache, basePos);

            view.currentPos = basePos;
            view.currentPos.x += ImageSize + 10;

            if (view.DrawButton("初期化", 60, RowHeight))
            {
                foreach (var eyesType in AllEyesTypes)
                {
                    MTEP.EyesTimelineLayer.ApplyEyes(maidCache, eyesType, 0, 0);
                }
            }

            view.currentPos = basePos;
            view.currentPos.y += ImageSize;

            view.EndAutoEditMode();
        }

        /// <summary>
        /// 瞳 1 つ分のドラッグ入力欄 2 個を 1 行で描く (位置は水平/垂直、サイズは幅/高さ)。
        /// rowLabel を渡すと行頭に項目名ラベルを付ける
        /// </summary>
        public void DrawEyesSliderRows(
            GUIView view, MTEP.MaidCache maidCache, MTEP.MotionEyesType eyesType,
            string rowLabel = null)
        {
            if (maidCache == null)
            {
                return;
            }

            string[] names;
            switch (eyesType)
            {
                case MTEP.MotionEyesType.EyesPosL:
                case MTEP.MotionEyesType.EyesPosR:
                    names = new string[] { "水平", "垂直" };
                    break;
                case MTEP.MotionEyesType.EyesScaL:
                case MTEP.MotionEyesType.EyesScaR:
                    names = new string[] { "幅", "高さ" };
                    break;
                default:
                    return;
            }

            var eyesValue = MTEP.EyesTimelineLayer.GetEyesValue(maidCache, eyesType);
            var horizon = eyesValue.x;
            var vertical = eyesValue.y;
            var updateTransform = false;

            view.BeginAutoEditMode();

            view.BeginHorizontal();
            {
                if (rowLabel != null)
                {
                    view.DrawLabel(rowLabel, 100, RowHeight);
                }

                updateTransform |= DrawEyesDragField(view, names[0], horizon, x => horizon = x);
                updateTransform |= DrawEyesDragField(view, names[1], vertical, y => vertical = y);
            }
            view.EndLayout();

            view.EndAutoEditMode();

            if (updateTransform)
            {
                MTEP.EyesTimelineLayer.ApplyEyes(maidCache, eyesType, horizon, vertical);
            }
        }

        /// <summary>項目名 (レイヤーの表示名) 付きで 1 行描く</summary>
        public void DrawLabeledEyesSliderRows(
            GUIView view, MTEP.MaidCache maidCache, MTEP.MotionEyesType eyesType)
        {
            DrawEyesSliderRows(view, maidCache, eyesType,
                MTEP.EyesTimelineLayer.EyesDisplayNameMap[eyesType.ToString()]);
        }

        private static bool DrawEyesDragField(
            GUIView view, string label, float value, Action<float> onChanged)
        {
            return view.DrawDragFloatField(new GUIView.DragFloatFieldOption
            {
                label = label,
                labelWidth = 30,
                value = value,
                minValue = -1f,
                maxValue = 1f,
                fieldWidth = 50,
                height = RowHeight,
                onChanged = onChanged,
            });
        }

        /// <summary>位置図とその上の瞳マーカー。左瞳は図の座標と符号が逆になる</summary>
        private void DrawEyesImage(GUIView view, MTEP.MaidCache maidCache, Vector2 basePos)
        {
            // DrawTexture が使うのと同じ矩形を先に取ってドラッグ判定に使う
            var drawRect = view.GetDrawRect(ImageSize, ImageSize);

            view.DrawTexture(
                _eyesPositionTex,
                ImageSize,
                ImageSize,
                Color.white);

            HandleEyesDrag(view, maidCache, drawRect);

            var halfEyesSize = _eyesTex.width / 2;

            foreach (var eyesType in DraggableEyesTypes)
            {
                var pos = GetEyesImagePos(maidCache, eyesType);
                view.currentPos = basePos + pos - new Vector2(halfEyesSize, halfEyesSize);
                view.DrawTexture(_eyesTex);
            }
        }

        /// <summary>瞳マーカーの図上の座標。左瞳は図の座標と符号が逆になる</summary>
        private static Vector2 GetEyesImagePos(
            MTEP.MaidCache maidCache, MTEP.MotionEyesType eyesType)
        {
            var half = ImageSize / 2;
            var eyesValue = MTEP.EyesTimelineLayer.GetEyesValue(maidCache, eyesType);
            var horizon = eyesValue.x;
            var vertical = eyesValue.y;

            if (eyesType == MTEP.MotionEyesType.EyesPosL)
            {
                horizon = -horizon;
                vertical = -vertical;
            }

            var pos = new Vector2(half + horizon * half, half + vertical * half);
            pos.x = Mathf.Clamp(pos.x, 0, ImageSize);
            pos.y = Mathf.Clamp(pos.y, 0, ImageSize);
            return pos;
        }

        /// <summary>
        /// 位置図のドラッグ処理。押下時に近い方の瞳マーカーを掴み、その瞳だけを動かす。
        /// MouseDown を消費してウィンドウの移動ドラッグを始めさせない
        /// (EditorSubWindow は未消費の押下を空き領域ドラッグとして扱うため)
        /// </summary>
        private void HandleEyesDrag(GUIView view, MTEP.MaidCache maidCache, Rect drawRect)
        {
            // ボタン解放のほか、ドラッグ中に編集モードを抜けた場合も掴みを離す
            // (編集モード外はレイヤーが毎フレーム値を書き戻すため、書き続けても巻き戻るだけ)
            if (_draggingEyesType.HasValue &&
                (!Input.GetMouseButton(0) || !studioHackManager.isPoseEditing))
            {
                _draggingEyesType = null;
            }

            var e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 &&
                drawRect.Contains(e.mousePosition) &&
                view.focusedComboBox == null)
            {
                // 位置図は GUIView のコールバックを通らないため、掴んだ時点で編集モードへ入る
                AutoEditMode.Enter();

                var pos = e.mousePosition - drawRect.position;
                _draggingEyesType = FindNearestEyesType(maidCache, pos);
                ApplyEyesDrag(maidCache, pos);
                e.Use();
            }
            else if (_draggingEyesType.HasValue &&
                e.type == EventType.MouseDrag && e.button == 0)
            {
                ApplyEyesDrag(maidCache, e.mousePosition - drawRect.position);
                e.Use();
            }
        }

        /// <summary>図上の座標から掴む対象を決める (マーカーとの距離が近い方)</summary>
        private static MTEP.MotionEyesType FindNearestEyesType(
            MTEP.MaidCache maidCache, Vector2 pos)
        {
            var nearest = DraggableEyesTypes[0];
            var nearestSqr = float.MaxValue;

            foreach (var eyesType in DraggableEyesTypes)
            {
                var sqr = (GetEyesImagePos(maidCache, eyesType) - pos).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = eyesType;
                }
            }

            return nearest;
        }

        /// <summary>図上の座標をドラッグ中の瞳へ適用する。図の外へ出た分は端で止める</summary>
        private void ApplyEyesDrag(MTEP.MaidCache maidCache, Vector2 pos)
        {
            if (!_draggingEyesType.HasValue)
            {
                return;
            }

            var half = ImageSize / 2;
            var horizon = Mathf.Clamp((pos.x - half) / half, -1f, 1f);
            var vertical = Mathf.Clamp((pos.y - half) / half, -1f, 1f);

            var eyesType = _draggingEyesType.Value;
            if (eyesType == MTEP.MotionEyesType.EyesPosL)
            {
                horizon = -horizon;
                vertical = -vertical;
            }

            MTEP.EyesTimelineLayer.ApplyEyes(maidCache, eyesType, horizon, vertical);
        }
    }
}
