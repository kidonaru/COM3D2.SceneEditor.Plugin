using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 修飾キーによって掴めるかどうかが変わるドラッグ点。
    /// 各点の掴み判定と MaidDragPointRing の表示判定を 1 箇所に揃えるための口。
    /// ドラッグ操作も共通の口にしてあり、ゲーム画面 (Unity のマウスメッセージ) と
    /// SceneView (自前のレイキャスト) の双方から同じ経路で駆動できる
    /// </summary>
    public interface IMaidDragPoint
    {
        /// <summary>今の修飾キーの状態でドラッグを受け付けるか</summary>
        bool canDrag { get; }

        /// <summary>IK 固定が効いている点か。効いている間は円を固定色で描く</summary>
        bool isHeld { get; }

        /// <summary>点の実体。MaidDragPointRing の強調表示の指定に使う</summary>
        GameObject gameObject { get; }

        /// <summary>
        /// ドラッグ開始。掴めたら true。
        /// pointerPos は camera の描画面上の座標
        /// (ゲーム画面は Input.mousePosition、SceneView は RT 座標)
        /// </summary>
        bool BeginDrag(Camera camera, Vector3 pointerPos);

        void UpdateDrag(Vector3 pointerPos);

        /// <summary>クリック判定を伴うドラッグ終了</summary>
        void EndDrag(Vector3 pointerPos);

        /// <summary>ポインタ位置が意味を持たない形で入力が絶たれたときの中断</summary>
        void CancelDrag();
    }

    /// <summary>
    /// ドラッグ点が掴んでいるボーン名の共有トラッカー。
    /// ポーズタブがスライダー表示の自動追従に使う（同時に掴めるのは 1 点なので単一値でよい）。
    /// 各ドラッグ点は掴んだら Begin、離したら End を呼ぶ。ドラッグ中に破棄された場合も
    /// End を呼び、掴み中表示が残らないようにする
    /// </summary>
    public static class MaidDragBoneTracker
    {
        public static string draggingBoneName { get; private set; }

        public static void BeginDrag(string boneName)
        {
            draggingBoneName = boneName;
        }

        public static void EndDrag()
        {
            draggingBoneName = null;
        }
    }
}
