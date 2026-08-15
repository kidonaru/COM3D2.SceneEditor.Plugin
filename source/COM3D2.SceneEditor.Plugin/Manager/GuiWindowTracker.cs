using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 描画中の IMGUI ウィンドウ矩形を収集する。
    ///
    /// InputRemapper は GameView の描画領域内で Input.mousePosition を RT 座標へ書き換えるが、
    /// その上に他プラグインの GUI.Window が乗っていると、スクリーン座標前提の窓上判定
    /// (MTEUtils.IsMouseOverWindowRect 等) が壊れてカメラ操作の抑止などが効かなくなる。
    /// UI に覆われた場所はそもそも GameView ではないので、そこでは変換しないのが正しい。
    ///
    /// 本プラグインの SceneView 等も収集対象に含め、判定側が自ウィンドウの ID を除外する
    /// (IsOverWindowExcept)。こうしないと SceneView が自分自身に覆われていると判定してしまう。
    /// GameView だけは常に最背面へ送っている (GUI.BringWindowToBack) ため収集しない。
    /// 収集すると画面の大半を占める GameView が全てのウィンドウを覆っていることになってしまう。
    ///
    /// GUI.Window / GUILayout.Window の全オーバーロードは private な GUI.DoWindow に合流するため、
    /// そこを 1 箇所フックするだけでプラグインの実装を問わず矩形を拾える
    /// </summary>
    public static class GuiWindowTracker
    {
        // 描画されなくなったウィンドウを破棄するまでの猶予フレーム数。
        // 収集は OnGUI、参照は Update と実行タイミングがずれるため 1 フレーム分の余裕が要る
        private const int EXPIRE_FRAMES = 2;

        private struct Entry
        {
            public Rect rect;
            public int frame;
        }

        private static readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private static readonly List<int> _expiredIds = new List<int>();
        private static int _lastPruneFrame = -1;

        // フックに失敗した場合は判定を常に false にして、従来どおりの挙動へフォールバックする
        public static bool isEnabled { get; private set; }

        public static void Patch(Harmony harmony)
        {
            // 呼び出し側にも二重パッチのガードがあるが、Harmony の例外になるため保険を残す
            if (isEnabled)
            {
                return;
            }

            var postfix = new HarmonyMethod(AccessTools.Method(typeof(GuiWindowTracker), nameof(DoWindowPostfix)));

            try
            {
                var doWindow = AccessTools.Method(typeof(GUI), "DoWindow");
                if (doWindow == null)
                {
                    throw new MissingMethodException(typeof(GUI).FullName, "DoWindow");
                }

                harmony.Patch(doWindow, postfix: postfix);
                isEnabled = true;
                MTEUtils.Log("GUI.DoWindow のフックに成功しました");
                return;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("GUI.DoWindow のフックに失敗しました。GUI.Window のフックへフォールバックします");
                MTEUtils.LogException(e);
            }

            // DoWindow は private のため将来のランタイム差で消える可能性がある。
            // public な GUI.Window 側なら合流点ではないが同じ矩形を拾える
            try
            {
                var patchedCount = 0;
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(GUI)))
                {
                    if (method.Name != "Window")
                    {
                        continue;
                    }

                    harmony.Patch(method, postfix: postfix);
                    patchedCount++;
                }

                if (patchedCount == 0)
                {
                    throw new MissingMethodException(typeof(GUI).FullName, "Window");
                }

                isEnabled = true;
                MTEUtils.Log($"GUI.Window のフックに成功しました ({patchedCount}件)");
            }
            catch (Exception e)
            {
                // 他プラグインのウィンドウ上でゲーム側 picking が誤動作するが、GameView 自体は動作する
                MTEUtils.LogError("GUI.Window のフックに失敗しました。他プラグインのウィンドウ上での座標変換は抑止されません");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// GUI 座標が selfWindowId 以外の IMGUI ウィンドウに覆われているか。
        /// 描画順 (Z オーダー) は考慮しないため、自ウィンドウを他ウィンドウの手前に重ねても
        /// 裏のウィンドウの矩形が優先される
        /// </summary>
        public static bool IsOverWindowExcept(int selfWindowId, Vector2 guiPos)
        {
            if (!isEnabled)
            {
                return false;
            }

            // ウィンドウが全て閉じられると収集側が呼ばれなくなるので、参照側でも期限切れを掃除する
            Prune();

            foreach (var pair in _entries)
            {
                if (pair.Key != selfWindowId && pair.Value.rect.Contains(guiPos))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DoWindowPostfix(int id, ref Rect __result)
        {
            // 参照側は全て GameView モード中のみなので、モード外では収集しない。
            // フォールバックが途中で失敗して一部だけパッチが残った場合も、
            // isEnabled で参照側と歩調を揃えて収集を止める
            if (!isEnabled || !GameViewManager.instance.isWindowMode)
            {
                return;
            }

            // GameView は常に最背面なので、他のウィンドウを覆うことはない
            if (id == GameViewWindow.WINDOW_ID)
            {
                return;
            }

            // GUI.matrix でスケールしているプラグインでもスクリーンGUI座標に揃える。
            // __result はドラッグ移動後の矩形なので、掴んで動かしている最中も追従する
            _entries[id] = new Entry
            {
                rect = ToScreenRect(__result),
                frame = Time.frameCount,
            };

            Prune();
        }

        /// <summary>
        /// GUI 座標の矩形をスクリーンGUI座標へ変換する。
        /// GUIUtility.GUIToScreenRect は COM3D2 (Unity 5.6) に無いため 2 隅を個別に変換する
        /// </summary>
        private static Rect ToScreenRect(Rect rect)
        {
            var min = GUIUtility.GUIToScreenPoint(new Vector2(rect.xMin, rect.yMin));
            var max = GUIUtility.GUIToScreenPoint(new Vector2(rect.xMax, rect.yMax));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void Prune()
        {
            var frame = Time.frameCount;
            if (_lastPruneFrame == frame)
            {
                return;
            }
            _lastPruneFrame = frame;

            _expiredIds.Clear();
            foreach (var pair in _entries)
            {
                if (frame - pair.Value.frame > EXPIRE_FRAMES)
                {
                    _expiredIds.Add(pair.Key);
                }
            }

            foreach (var id in _expiredIds)
            {
                _entries.Remove(id);
            }
        }
    }
}
