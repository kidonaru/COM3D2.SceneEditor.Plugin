using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Inspector の内容描画を外部プラグインへ委譲する公開 API。
    /// MTEUtils の InspectorHostClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は Register2 等の別名で追加する)。
    /// 契約はプリミティブ + UnityEngine 型 + デリゲートのみ (プラグイン定義型は DLL 間で共有できない)
    /// </summary>
    public static class InspectorHost
    {
        private class Entry
        {
            public string name;
            public Func<GameObject, bool> canDraw;
            public Action<GameObject, Rect> draw;
            /// <summary>連続で例外になった回数。成功したら 0 に戻す</summary>
            public int failureCount;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        // OnGUI は 1 フレームに複数回走るため、壊れた登録者を放置するとログと
        // 例外生成コストが溢れる。この回数だけ連続で失敗したら以後は呼ばない
        private const int MaxConsecutiveFailures = 5;

        // 委譲先がコンボのドロップダウンを自前のウィンドウとして出すには、
        // ボタン座標をスクリーン座標へ直す基準 (Inspector のウィンドウ矩形) と
        // 表示状態が要る。ホストが毎フレーム更新し、委譲先はブリッジ経由で読む
        private static Rect _windowRect;
        private static bool _windowVisible;

        internal static void UpdateWindowState(Rect windowRect, bool visible)
        {
            _windowRect = windowRect;
            _windowVisible = visible;
        }

        /// <summary>Inspector ウィンドウのスクリーン矩形</summary>
        public static Rect GetWindowRect()
        {
            return _windowRect;
        }

        /// <summary>Inspector ウィンドウが描画されているか (タブ非選択中・一時非表示中は false)</summary>
        public static bool IsWindowVisible()
        {
            return _windowVisible;
        }

        public static object Register(
            string name,
            Func<GameObject, bool> canDraw,
            Action<GameObject, Rect> draw)
        {
            if (canDraw == null || draw == null)
            {
                MTEUtils.LogError("InspectorHost.Register: デリゲートに null は指定できません");
                return null;
            }

            // 同名の再登録はプラグインのリロードとみなして置き換える
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].name == name)
                {
                    Unregister(_entries[i]);
                }
            }

            var entry = new Entry
            {
                name = name ?? "",
                canDraw = canDraw,
                draw = draw,
            };
            _entries.Add(entry);
            return entry;
        }

        public static void Unregister(object handle)
        {
            var entry = handle as Entry;
            if (entry == null)
            {
                return;
            }
            _entries.Remove(entry);
        }

        /// <summary>
        /// 選択オブジェクトを管理下に持つ登録者がいれば内容描画を委譲して true を返す。
        /// contentRect はホストが描くヘッダー行 (アクティブ・名前・フォーカス) の下の残り領域で、
        /// 登録者はこの中だけを描く。
        /// 例外は登録者単位で隔離し、失敗した委譲はそのフレームだけ既定描画へ戻す。
        /// 連続で失敗し続ける登録者は打ち切り、以後は既定描画のままにする
        /// </summary>
        public static bool TryDraw(GameObject go, Rect contentRect)
        {
            foreach (var entry in _entries)
            {
                if (entry.failureCount >= MaxConsecutiveFailures)
                {
                    continue;
                }

                try
                {
                    if (!entry.canDraw(go))
                    {
                        continue;
                    }
                    entry.draw(go, contentRect);
                    entry.failureCount = 0;
                    return true;
                }
                catch (Exception e)
                {
                    // 外部プラグインの例外でホストの描画を止めない
                    MTEUtils.LogException(e);
                    if (++entry.failureCount >= MaxConsecutiveFailures)
                    {
                        MTEUtils.LogWarning("InspectorHost: {0} の描画が連続で失敗したため委譲を停止します", entry.name);
                    }
                }
            }
            return false;
        }
    }
}
