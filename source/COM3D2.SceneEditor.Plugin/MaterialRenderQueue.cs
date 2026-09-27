namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シェーダー差し替え時の renderQueue。Unity はシェーダーを代入すると
    /// renderQueue を新シェーダーの既定へ戻すため、差し替え後に決め直す
    /// </summary>
    public static class MaterialRenderQueue
    {
        /// <summary>
        /// 差し替え前の値が差し替え前シェーダーの既定と違えばマテリアルで明示指定されたものとして引き継ぎ、
        /// 既定のままなら新シェーダーの既定に従う (不透明 ↔ 半透明の切り替えで描画順も追従させる)
        /// </summary>
        public static int Resolve(int currentQueue, int currentShaderQueue, int newShaderQueue)
        {
            return currentQueue != currentShaderQueue ? currentQueue : newShaderQueue;
        }
    }
}
