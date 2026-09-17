namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 編集操作のドラッグが進行中かの横断的な問い合わせ口。
    /// ギズモ (SceneView / GameView) とメイドのドラッグ点をまとめて見る。
    /// カメラのメイド追従のように「ドラッグ編集中は自動追従を止めたい」処理が使う
    /// (追従したままだとカメラが動いてドラッグ点のスクリーン位置がずれ、
    /// さらに対象が動く自己励起ループになって操作が止まらなくなる)
    /// </summary>
    public static class EditDragState
    {
        /// <summary>いずれかのビューでギズモを掴んでいるか</summary>
        public static bool isGizmoDragging =>
            IsGizmoDragging(SceneViewManager.instance.gizmoRenderer)
            || IsGizmoDragging(GameViewManager.instance.gizmoRenderer);

        /// <summary>ギズモ・ドラッグ点のいずれかで編集中か</summary>
        public static bool isDragging =>
            isGizmoDragging || MaidDragBoneTracker.isDragging;

        private static bool IsGizmoDragging(GizmoRenderer gizmo)
        {
            return gizmo != null && gizmo.isDragging;
        }
    }
}
