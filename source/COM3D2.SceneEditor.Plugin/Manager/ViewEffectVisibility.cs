namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>エフェクト表示の例外判定に使う、タイムラインのカレントレイヤーの種類</summary>
    public enum EffectLayerKind
    {
        None,
        PostEffect,
        StageLight,
        StageLaser,
        Psyllium,
    }

    /// <summary>あるビューでエフェクトのうち何を隠すか</summary>
    public struct ViewEffectState
    {
        public bool suspendPostEffect;
        public bool hideStageLight;
        public bool hideStageLaser;
        public bool hidePsyllium;
    }

    /// <summary>
    /// エフェクト表示トグルから、ポストエフェクトの一時停止とライブ演出の非表示を決める。
    /// OFF でも編集中 (カレント) のエフェクト系レイヤーの種類だけは見せる
    /// </summary>
    public static class ViewEffectVisibility
    {
        public static ViewEffectState Resolve(bool showEffect, EffectLayerKind currentLayer)
        {
            if (showEffect)
            {
                return new ViewEffectState();
            }
            return new ViewEffectState
            {
                suspendPostEffect = currentLayer != EffectLayerKind.PostEffect,
                hideStageLight = currentLayer != EffectLayerKind.StageLight,
                hideStageLaser = currentLayer != EffectLayerKind.StageLaser,
                hidePsyllium = currentLayer != EffectLayerKind.Psyllium,
            };
        }

        /// <summary>SceneView 用。SceneView カメラにはポストエフェクトが掛からないためライブ演出だけを扱う</summary>
        public static ViewEffectState HideAll(bool hide)
        {
            return new ViewEffectState
            {
                hideStageLight = hide,
                hideStageLaser = hide,
                hidePsyllium = hide,
            };
        }
    }
}
