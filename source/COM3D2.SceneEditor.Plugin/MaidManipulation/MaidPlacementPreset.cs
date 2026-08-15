using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>呼出メイドの一括配置。座標は MultipleMaids の配置テーブルを踏襲</summary>
    public static class MaidPlacementPreset
    {
        public enum PresetType
        {
            V字,
            横一列,
        }

        /// <summary>先頭を原点に、左右交互に後方へ広げる（MM の人数が奇数のときのテーブル）</summary>
        private static readonly Vector3[] VShapePositions =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(-0.6f, 0f, 0.26f),
            new Vector3(0.6f, 0f, 0.26f),
            new Vector3(-1.1f, 0f, 0.69f),
            new Vector3(1.1f, 0f, 0.69f),
            new Vector3(-1.47f, 0f, 1.1f),
            new Vector3(1.47f, 0f, 1.1f),
        };

        /// <summary>中央を空けて左右交互に並べる（MM の人数が偶数のときのテーブル）</summary>
        private static readonly Vector3[] RowPositions =
        {
            new Vector3(0.3f, 0f, 0f),
            new Vector3(-0.3f, 0f, 0f),
            new Vector3(0.7f, 0f, 0.4f),
            new Vector3(-0.7f, 0f, 0.4f),
            new Vector3(1f, 0f, 0.9f),
            new Vector3(-1f, 0f, 0.9f),
        };

        /// <summary>テーブルを使い切った分を後方へ流す間隔</summary>
        private const float OverflowDepthStep = 0.5f;

        /// <summary>index 番目のメイドの配置位置。テーブルを超えた分は最後尾からさらに後方へ流す</summary>
        public static Vector3 GetPosition(PresetType type, int index)
        {
            var positions = type == PresetType.V字 ? VShapePositions : RowPositions;
            return index < positions.Length
                ? positions[index]
                : positions[positions.Length - 1]
                    + new Vector3(0f, 0f, OverflowDepthStep * (index - positions.Length + 1));
        }

        /// <summary>
        /// メイドを瞬間移動させる。揺れ物が前の位置に取り残されて伸びるため物理もリセットするが、
        /// ロード前のボディは内部リストが未初期化で WarpInit が落ちるため完了後だけ呼ぶ
        /// </summary>
        public static void WarpTo(Maid maid, Vector3 pos)
        {
            maid.SetPos(pos);
            maid.SetRot(Vector3.zero);

            if (maid.body0 != null && maid.body0.isLoadedBody)
            {
                maid.body0.WarpInit();
            }
        }
    }
}
