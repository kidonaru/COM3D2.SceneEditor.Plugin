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
            /// <summary>委譲先が自前のスクロールビュー内で DrawHeader を呼ぶか</summary>
            public bool drawsHeader;
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

        /// <summary>
        /// ホストのヘッダー行 (ギズモ行 + アクティブ・名前・フォーカス行) を指定矩形へ描く。
        /// Register2 で drawsHeader: true を指定した登録者が、自前のスクロールビューの
        /// 先頭で呼ぶための API。戻り値は描画に使った高さで、呼び出し側はこのぶん
        /// 次の要素を下げる (末尾の余白は含まない)
        /// </summary>
        public static float DrawHeader(GameObject go, Rect rect)
        {
            return InspectorWindow.instance.DrawDelegatedHeader(go, rect);
        }

        public static object Register(
            string name,
            Func<GameObject, bool> canDraw,
            Action<GameObject, Rect> draw)
        {
            return Register2(name, canDraw, draw, false);
        }

        /// <summary>
        /// ヘッダー行の描画者を選べる登録 (後発 API)。
        /// drawsHeader が true の登録者へは、ホストのヘッダー行のぶんを引かない
        /// 内容領域を渡す。代わりに登録者が自前のスクロールビューの先頭で
        /// <see cref="DrawHeader"/> を呼び、ヘッダーも一緒にスクロールさせる
        /// </summary>
        public static object Register2(
            string name,
            Func<GameObject, bool> canDraw,
            Action<GameObject, Rect> draw,
            bool drawsHeader)
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
                drawsHeader = drawsHeader,
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
        /// 登録者は渡された領域の中だけを描く。領域は drawsHeader で使い分け、
        /// false の登録者へは contentRect (ホストが描くヘッダー行の下の残り) を、
        /// true の登録者へは fullContentRect (ヘッダー行のぶんを引かない領域) を渡し、
        /// headerDelegated に true を返す。呼び出し側はこの場合ヘッダーを描いてはならない
        /// (委譲先が自前のスクロールビュー内で DrawHeader を呼んで描くため)。
        /// 例外は登録者単位で隔離し、失敗した委譲はそのフレームだけ既定描画へ戻す。
        /// 連続で失敗し続ける登録者は打ち切り、以後は既定描画のままにする
        /// </summary>
        public static bool TryDraw(
            GameObject go, Rect contentRect, Rect fullContentRect, out bool headerDelegated)
        {
            headerDelegated = false;

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
                    entry.draw(go, entry.drawsHeader ? fullContentRect : contentRect);
                    entry.failureCount = 0;
                    headerDelegated = entry.drawsHeader;
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
