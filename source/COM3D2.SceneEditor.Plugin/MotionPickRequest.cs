using System;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「モーションウィンドウの一覧から 1 件選んでほしい」という要求。
    /// アニメブレンドウィンドウが層へ載せるモーションを選ばせるために使う。
    /// 一覧の描画 (カテゴリ / 検索 / マイポーズのフォルダ移動) を複製しないための仕組み
    /// </summary>
    public sealed class MotionPickRequest
    {
        /// <summary>載せる対象のメイド</summary>
        public Maid maid;

        /// <summary>載せ先のアニメレイヤー</summary>
        public int layer;

        /// <summary>モーション一覧から選ばれたとき</summary>
        public Action<PhotoMotionData> onMotionPicked;

        /// <summary>マイポーズ一覧から選ばれたとき (第 1 引数はフォルダ、第 2 引数はポーズ名)</summary>
        public Action<string, string> onMyPosePicked;

        /// <summary>
        /// この要求が今も有効か。対象メイドが変わったり外れたりしたら畳む
        /// (別のメイドの層へ載ってしまわないように)
        /// </summary>
        public bool IsValidFor(Maid currentMaid)
        {
            return maid != null
                && maid == currentMaid
                && layer >= MaidAnimationBlendController.MinLayer
                && layer <= MaidAnimationBlendController.MaxLayer;
        }
    }
}
