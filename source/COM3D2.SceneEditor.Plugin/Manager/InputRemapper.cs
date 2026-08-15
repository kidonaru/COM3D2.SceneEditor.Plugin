using System.Reflection;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Input.mousePosition をフックし、GameViewの描画領域内のカーソル位置を
    /// RT座標へ変換してゲーム側の位置ベースpicking (メイドクリック・ギズモ等) を成立させる。
    /// RTは画面解像度で描画領域は縮小表示されるため、オフセット + スケール + Y反転で変換する。
    /// 描画領域外 (レターボックスの余白を含む) では生座標をそのまま返し、
    /// 他プラグインの挙動に影響を与えない。
    /// 描画領域内でも他プラグインの IMGUI ウィンドウに覆われている箇所は変換しない
    /// (GuiWindowTracker 参照)
    /// </summary>
    public class InputRemapper : ManagerBase
    {
        private static Harmony _harmony = null;

        // 変換を一時的に無効化するフラグ (プラグイン自身が生座標を読むために使う)
        private static bool _bypass = false;

        // UnityEngine.SendMouseEvents.s_MouseUsed。IMGUI がマウスを消費したフレームを表す
        private static FieldInfo _mouseUsedField = null;

        private static InputRemapper _instance = null;
        public static InputRemapper instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new InputRemapper();
                }
                return _instance;
            }
        }

        private InputRemapper()
        {
        }

        public static Vector3 rawMousePosition
        {
            get
            {
                _bypass = true;
                try
                {
                    return Input.mousePosition;
                }
                finally
                {
                    _bypass = false;
                }
            }
        }

        // 変換前のカーソル位置をGUI座標で読む。プラグイン側の矩形判定はこれを使う
        public static Vector2 rawGuiPosition => ToGuiPosition(rawMousePosition);

        /// <summary>
        /// 生のマウス座標 (左下原点) をGUI座標 (左上原点) へ変換する
        /// </summary>
        public static Vector2 ToGuiPosition(Vector3 rawPosition)
        {
            return new Vector2(rawPosition.x, Screen.height - rawPosition.y);
        }

        /// <summary>
        /// GUI 座標が「GameView の 3D シーンとして扱う領域」上か。
        /// リサイズのつかみ範囲・GameView 以外の IMGUI ウィンドウ (本プラグインの
        /// SceneView 等を含む)・ギアメニューは UI なので除外する。
        /// 座標変換 (picking) とカメラ操作可否の判定はこの条件に揃える
        /// </summary>
        public static bool IsGameViewActiveAt(Vector2 guiPos)
        {
            // 最大化中は全画面が3Dシーン。IMGUIウィンドウ上とギアメニュー上だけUIとして除外する
            if (GameViewManager.instance.isMaximized)
            {
                return !GuiWindowTracker.IsOverWindowExcept(GameViewWindow.WINDOW_ID, guiPos) &&
                    !GameViewManager.instance.IsOverSystemUI(guiPos);
            }

            var window = GameViewWindow.instance;
            var drawRect = window.drawRect;
            return drawRect.width > 0f && drawRect.height > 0f &&
                drawRect.Contains(guiPos) &&
                !window.IsOverResizeHandle(guiPos) &&
                !GuiWindowTracker.IsOverWindowExcept(GameViewWindow.WINDOW_ID, guiPos) &&
                !GameViewManager.instance.IsOverSystemUI(guiPos);
        }

        public override void Init()
        {
            // Init は登録時に1回しか呼ばれないが、二重パッチはHarmonyの例外になるため保険を残す
            if (_harmony != null)
            {
                return;
            }

            _harmony = new Harmony(PluginInfo.PluginFullName);
            PatchMousePosition();
            PatchSendMouseEvents();
            GuiWindowTracker.Patch(_harmony);
            CameraControlArbiter.Patch(_harmony);
        }

        private void PatchMousePosition()
        {
            try
            {
                var original = AccessTools.PropertyGetter(typeof(Input), "mousePosition");
                var postfix = AccessTools.Method(typeof(InputRemapper), nameof(MousePositionPostfix));
                _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                MTEUtils.Log("Input.mousePosition のフックに成功しました");
            }
            catch (System.Exception e)
            {
                // externメソッドのパッチはランタイム依存。失敗してもpicking以外は動作させる
                MTEUtils.LogError("Input.mousePosition のフックに失敗しました。picking座標変換は無効です");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// GameView内でコライダのマウスメッセージ (OnMouseDown / OnMouseEnter 等) を成立させる。
        /// 配送元の SendMouseEvents.DoSendMouseEvents には阻害要因が2つある:
        ///
        /// 1. s_MouseUsed — GameViewは GUI.Window なのでその上の操作は IMGUI に消費され、
        ///    このフラグが立つとレイキャストのループ全体がスキップされる
        /// 2. skipRTCameras — ゲーム本体は非0で呼ぶため、targetTexture を持つ
        ///    メインカメラがループの対象から外れる
        ///
        /// 描画領域内にカーソルがある間だけ両方を無効化して配送を通す
        /// </summary>
        private void PatchSendMouseEvents()
        {
            try
            {
                var type = AccessTools.TypeByName("UnityEngine.SendMouseEvents");
                _mouseUsedField = AccessTools.Field(type, "s_MouseUsed");
                var original = AccessTools.Method(type, "DoSendMouseEvents");
                var prefix = AccessTools.Method(typeof(InputRemapper), nameof(DoSendMouseEventsPrefix));
                _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                MTEUtils.Log("SendMouseEvents のフックに成功しました");
            }
            catch (System.Exception e)
            {
                // 失敗した場合、GameView内でのコライダ操作 (ボーン操作リング等) だけが効かなくなる
                MTEUtils.LogError("SendMouseEvents のフックに失敗しました。GameView内のコライダ操作は無効です");
                MTEUtils.LogException(e);
                _mouseUsedField = null;
            }
        }

        private static void DoSendMouseEventsPrefix(ref int skipRTCameras)
        {
            if (_mouseUsedField == null || !GameViewManager.instance.isWindowMode)
            {
                return;
            }

            if (IsGameViewActiveAt(rawGuiPosition))
            {
                _mouseUsedField.SetValue(null, false);
                skipRTCameras = 0;
            }
        }

        private static void MousePositionPostfix(ref Vector3 __result)
        {
            if (_bypass || !GameViewManager.instance.isWindowMode)
            {
                return;
            }

            var rt = GameViewManager.instance.renderTexture;
            if (rt == null)
            {
                return;
            }

            var guiPos = ToGuiPosition(__result);
            if (!IsGameViewActiveAt(guiPos))
            {
                return;
            }

            // 描画領域内 → RTピクセル座標 (左下原点)
            var drawRect = GameViewWindow.instance.drawRect;
            var scaleX = rt.width / drawRect.width;
            var scaleY = rt.height / drawRect.height;
            __result = new Vector3(
                (guiPos.x - drawRect.x) * scaleX,
                rt.height - (guiPos.y - drawRect.y) * scaleY,
                0f);
        }
    }
}
