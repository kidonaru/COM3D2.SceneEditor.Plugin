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
            /// <summary>内容を丸ごと描く委譲先。行の登録では null</summary>
            public Action<GameObject, Rect> draw;
            /// <summary>ホストが描く内容の末尾へ行を足す委譲先。全面委譲の登録では null</summary>
            public Func<GameObject, Rect, float> drawRows;
            /// <summary>委譲先が自前のスクロールビュー内で DrawHeader を呼ぶか</summary>
            public bool drawsHeader;
            /// <summary>連続で例外になった回数。成功したら 0 に戻す</summary>
            public int failureCount;

            public bool isRows => drawRows != null;
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

            return AddEntry(new Entry
            {
                name = name ?? "",
                canDraw = canDraw,
                draw = draw,
                drawsHeader = drawsHeader,
            });
        }

        /// <summary>
        /// ホストが自前で描く内容 (現状は配置モデル・背景モデルの共通表示) の末尾へ、
        /// 委譲先に固有の行だけを足す登録 (後発 API)。
        /// drawRows は rect の左上から描き、使った高さ (末尾の余白を含まない) を返す。
        /// 全面委譲 (Register / Register2) とは別枠で、同名でも互いを置き換えない
        /// </summary>
        public static object RegisterRows(
            string name,
            Func<GameObject, bool> canDraw,
            Func<GameObject, Rect, float> drawRows)
        {
            if (canDraw == null || drawRows == null)
            {
                MTEUtils.LogError("InspectorHost.RegisterRows: デリゲートに null は指定できません");
                return null;
            }

            return AddEntry(new Entry
            {
                name = name ?? "",
                canDraw = canDraw,
                drawRows = drawRows,
            });
        }

        /// <summary>
        /// 同名・同種の既存登録はプラグインのリロードとみなして置き換える。
        /// 種類が違えば残す (同じプラグインが全面委譲と行を併用しても消し合わない)
        /// </summary>
        private static object AddEntry(Entry entry)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].name == entry.name && _entries[i].isRows == entry.isRows)
                {
                    Unregister(_entries[i]);
                }
            }

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
                if (entry.isRows || entry.failureCount >= MaxConsecutiveFailures)
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
                    RecordFailure(entry, e);
                }
            }
            return false;
        }

        /// <summary>
        /// 選択オブジェクトを管理下に持つ行の登録者が居れば、rect の位置へ行を描かせて
        /// 使った高さを返す。居なければ 0。最初に canDraw が true を返した 1 者だけを呼ぶ。
        /// 呼び出し元は戻り値の高さぶんレイアウトを送る (DrawHeader と同じ作法)。
        /// 例外の扱いは TryDraw と同じ
        /// </summary>
        public static float DrawRows(GameObject go, Rect rect)
        {
            foreach (var entry in _entries)
            {
                if (!entry.isRows || entry.failureCount >= MaxConsecutiveFailures)
                {
                    continue;
                }

                try
                {
                    if (!entry.canDraw(go))
                    {
                        continue;
                    }
                    var height = entry.drawRows(go, rect);
                    entry.failureCount = 0;
                    return Math.Max(0f, height);
                }
                catch (Exception e)
                {
                    RecordFailure(entry, e);
                }
            }
            return 0f;
        }

        /// <summary>外部プラグインの例外でホストの描画を止めない。連続失敗が続く登録者は打ち切る</summary>
        private static void RecordFailure(Entry entry, Exception e)
        {
            MTEUtils.LogException(e);
            if (++entry.failureCount >= MaxConsecutiveFailures)
            {
                MTEUtils.LogWarning("InspectorHost: {0} の描画が連続で失敗したため委譲を停止します", entry.name);
            }
        }
    }
}
