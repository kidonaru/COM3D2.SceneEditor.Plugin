using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド系ウィンドウの基底。操作対象メイドの選択行と、
    /// モデル選択行 (DrawModelComboBox) を共通で提供する。
    /// 対象は MaidManipulateManager が保持しているため、どのウィンドウで選び直しても
    /// 全ウィンドウの表示が揃う
    /// </summary>
    public abstract class MaidWindowBase : EditorSubWindow
    {
        /// <summary>全行共通の高さ。列を揃えるためどの行もこの値を使う</summary>
        protected static readonly int ROW_HEIGHT = 20;

        /// <summary>全行共通のラベル幅</summary>
        protected static readonly int LABEL_WIDTH = 70;

        protected static MaidManipulateManager maidManager => MaidManipulateManager.instance;
        protected static CharacterMgr characterMgr => GameMain.Instance.CharacterMgr;

        /// <summary>
        /// ウィンドウ全体を覆うビュー。コンボのフォーカス集約専用。
        /// buttonPos をウィンドウ原点基準で扱うため、内容ビュー
        /// (原点がヘッダー下へずれている) を子にしてここへフォーカスを集める
        /// </summary>
        private readonly GUIView _rootView = new GUIView();

        protected readonly GUIView view = new GUIView();

        /// <summary>対象メイドの選択行を出すか。呼出ウィンドウは一覧から選ぶため不要</summary>
        protected virtual bool showMaidSelector => true;

        /// <summary>ウィンドウ固有の内容を描画する。target は未選択なら null</summary>
        protected abstract void DrawMaidContent(Maid target);

        /// <summary>
        /// ラベル + コンボの 1 行でコンボに使える幅。
        /// 固定幅にするとウィンドウを最小幅まで縮めたときにはみ出して
        /// クリックできなくなるため、残り幅から求める
        /// </summary>
        protected static float CalcLabeledComboWidth(GUIView view)
        {
            return LabeledComboRow.CalcComboWidth(view, LABEL_WIDTH);
        }

        /// <summary>
        /// ラベル + コンボの 1 行を描く。コンボ幅は残り幅から決めるため、
        /// ウィンドウを縮めてもボタンがはみ出さない
        /// </summary>
        /// <param name="trailingWidth">コンボの後ろに置くコントロールのために空ける幅</param>
        /// <param name="drawTrailing">コンボの後ろに置くコントロールの描画</param>
        protected void DrawLabeledComboBox<T>(
            string label,
            GUIComboBox<T> comboBox,
            float trailingWidth = 0f,
            Action drawTrailing = null)
        {
            LabeledComboRow.Draw(
                view, label, comboBox, LABEL_WIDTH, ROW_HEIGHT, trailingWidth, drawTrailing);
        }

        /// <summary>モデル選択行の対象。一覧の増減で位置がずれるため、位置ではなく実体で持つ</summary>
        private GameObject _targetModel;

        /// <summary>選択変更の検出用。前回 DrawModelComboBox を描いたときの選択オブジェクト</summary>
        private GameObject _lastSelectedObject;

        /// <summary>
        /// モデル選択の 1 行を描き、シーンの選択中モデルと双方向で連動させる。
        /// ボーンウィンドウ (BoneEditManager.targetModel) と同じ方針で、
        /// モデル以外 (メイド等) が選択された場合は対象を保持する
        /// (無関係なクリックのたびに編集対象を見失わないため)
        /// </summary>
        protected void DrawModelComboBox<T>(
            string label,
            GUIComboBox<T> comboBox,
            List<T> items,
            Func<T, Transform> getTransform)
        {
            comboBox.items = items;
            SyncTargetModelFromSelection(items, getTransform);

            // 一覧が増減すると同じ対象でも位置が変わるため、コンボの選択は毎回引き直す
            var index = IndexOfModel(items, getTransform, _targetModel);
            if (index >= 0)
            {
                comboBox.currentIndex = index;
            }

            // コンボで選び直したらシーンの選択も揃える。
            // モデルルートのギズモは外部プラグイン側が持つため showGizmo は出さない
            // (BoneEditManager.targetModel と同じ規約)
            comboBox.onSelected = (item, _) =>
            {
                var transform = getTransform(item);
                if (transform == null)
                {
                    return;
                }
                _targetModel = transform.gameObject;
                SelectionManager.instance.Select(_targetModel, false);
            };

            DrawLabeledComboBox(label, comboBox);
        }

        /// <summary>
        /// 選択がモデル (またはその子) へ移ったら対象を揃える。
        /// SceneView クリックではモデルの子メッシュがヒットしうるため祖先も辿るが、
        /// その走査は毎フレーム回すには重いので選択が変わったフレームだけ引き直す
        /// (BoneEditManager.ReleaseBoneOnObjectSelected と同じ流儀)。
        /// モデル以外 (メイド等) が選ばれた場合は対象を保持する
        /// </summary>
        private void SyncTargetModelFromSelection<T>(List<T> items, Func<T, Transform> getTransform)
        {
            var selectedObject = SelectionManager.instance.selectedObject;
            if (selectedObject == _lastSelectedObject)
            {
                return;
            }
            _lastSelectedObject = selectedObject;

            if (selectedObject == null)
            {
                return;
            }

            foreach (var item in items)
            {
                var transform = getTransform(item);
                if (transform != null && selectedObject.transform.IsChildOf(transform))
                {
                    _targetModel = transform.gameObject;
                    return;
                }
            }
        }

        /// <summary>一覧から対象モデルの位置を引く。未設定・見つからないなら -1</summary>
        private static int IndexOfModel<T>(
            List<T> items, Func<T, Transform> getTransform, GameObject model)
        {
            if (model == null)
            {
                return -1;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var transform = getTransform(items[i]);
                if (transform != null && transform.gameObject == model)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// ウィンドウ内部のタブを描く。
        /// DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」に
        /// なるため、通常の行間に合わせて詰める
        /// </summary>
        protected T DrawInnerTabs<T>(T currentTab, float width)
        {
            var result = view.DrawTabs(currentTab, width, ROW_HEIGHT);
            view.currentPos.y -= 5 + GUIView.defaultMargin;
            return result;
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どちらに描いたコンボもフォーカス状態を共有させる
            view.parent = _rootView;
            view.Init(ToLocalRect(contentRect));

            var target = showMaidSelector ? DrawMaidSelector(view) : maidManager.targetMaid;

            DrawMaidContent(target);

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す。
            // 派生クラスの早期 return で呼び忘れないよう、この基底クラスが必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private readonly GUIComboBox<Maid> _targetMaidComboBox = new GUIComboBox<Maid>
        {
            getName = (maid, _) => maid == null ? "なし" : maid.status.fullNameJpStyle,
            buttonSize = new Vector2(200, 20),
        };

        /// <summary>
        /// 操作対象の選択行を描画し、選択中のメイドを返す。
        /// 未選択なら案内を出して null を返すので、派生クラスは null で描画を打ち切る
        /// </summary>
        protected Maid DrawMaidSelector(GUIView view)
        {
            var maids = MTEUtils.GetReadyMaidList();

            view.BeginHorizontal();
            {
                view.DrawLabel("対象", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                // メイド名は長くなりがちなので、残り幅いっぱいまで伸ばして見切れを防ぐ
                var comboWidth = CalcLabeledComboWidth(view);
                _targetMaidComboBox.buttonSize = new Vector2(comboWidth, ROW_HEIGHT);
                _targetMaidComboBox.contentSize = new Vector2(comboWidth, 300f);

                _targetMaidComboBox.items = maids;
                // 未選択のときは currentIndex が -1 になりボタン文字列が決まらないため、既定名で埋める
                _targetMaidComboBox.defaultName =
                    maidManager.targetMaid == null ? "選択してください" : null;
                _targetMaidComboBox.currentIndex = maids.IndexOf(maidManager.targetMaid);
                _targetMaidComboBox.onSelected = (maid, _) => maidManager.targetMaid = maid;
                _targetMaidComboBox.DrawButton(view);
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            var target = maidManager.targetMaid;
            if (target == null)
            {
                view.DrawLabel(maids.Count == 0
                    ? "操作できるメイドがいません"
                    : "操作対象のメイドを選択してください", -1, ROW_HEIGHT);
            }
            return target;
        }
    }
}
