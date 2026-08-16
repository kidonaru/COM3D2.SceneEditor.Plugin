using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド系ウィンドウの基底。操作対象メイドの選択行を共通で提供する。
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

        /// <summary>矢印ボタン 2 つ分の幅。GUIComboBox が showArrow 時に確保する値と合わせる</summary>
        private static readonly float ComboArrowWidth = 40f;

        /// <summary>コンボをウィンドウ幅に合わせて縮めるときの下限</summary>
        private static readonly float MinComboWidth = 80f;

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
            return Mathf.Max(
                view.viewRect.width - view.padding.x * 2
                    - LABEL_WIDTH - view.margin - ComboArrowWidth,
                MinComboWidth);
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
