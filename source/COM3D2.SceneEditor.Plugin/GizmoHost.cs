using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 外部プラグインのギズモを SceneView / GameView の入力・描画へ参加させる公開 API。
    /// MTEUtils の GizmoHostClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は Register2 等の別名で追加する)。
    /// 契約はプリミティブ + UnityEngine 型 + デリゲートのみ (プラグイン定義型は DLL 間で共有できない)
    /// </summary>
    public static class GizmoHost
    {
        private class Entry
        {
            public string name;
            public Func<Camera, Vector2, bool> tryBeginDrag;
            public Action<Camera, Vector2> updateDrag;
            public Action endDrag;
            public Func<bool> isDragging;
            public Action<Camera> draw;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        // ドラッグ中の外部ギズモ。同時に掴めるのは 1 個だけ
        private static Entry _dragEntry;

        // ドラッグを開始したカメラ。別ビューからの UpdateExternalDrag を無視し、
        // カメラ操作の抑止判定をビュー単位に絞るために使う
        private static Camera _dragCamera;

        public static object Register(
            string name,
            Func<Camera, Vector2, bool> tryBeginDrag,
            Action<Camera, Vector2> updateDrag,
            Action endDrag,
            Func<bool> isDragging,
            Action<Camera> draw)
        {
            if (tryBeginDrag == null || updateDrag == null || endDrag == null ||
                isDragging == null || draw == null)
            {
                MTEUtils.LogError("GizmoHost.Register: デリゲートに null は指定できません");
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
                tryBeginDrag = tryBeginDrag,
                updateDrag = updateDrag,
                endDrag = endDrag,
                isDragging = isDragging,
                draw = draw,
            };
            _entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// 外部ギズモを描画・操作できるビューが 1 つでも稼働しているか。
        /// GameView は window mode でないとメインカメラに GizmoRenderer が付かず
        /// 描画も入力ディスパッチも走らないため、登録済みかどうかとは別に問い合わせる。
        /// false の間は登録側 (外部プラグイン) が自前の standalone 経路で駆動する
        /// </summary>
        public static bool IsViewActive()
        {
            // 外部プラグインの方がロードが早い場合、プラグイン本体の Awake 前に問い合わされうる
            var plugin = SceneEditorPlugin.instance;
            if (plugin == null || !plugin.isEnable)
            {
                return false;
            }

            return SceneViewWindow.instance.isShowWnd || GameViewManager.isGizmoDispatchActive;
        }

        public static void Unregister(object handle)
        {
            var entry = handle as Entry;
            if (entry == null)
            {
                return;
            }
            if (_dragEntry == entry)
            {
                _dragEntry = null;
                _dragCamera = null;
            }
            _entries.Remove(entry);
        }

        /// <summary>
        /// 指定カメラのビューで始まった外部ギズモのドラッグが継続中か。
        /// ウィンドウ側は自ビューのドラッグに対してのみ選択・カメラ操作を抑止する
        /// (グローバルに抑止すると、別ビューのドラッグ中にこちらのカメラ操作まで止まってしまう)
        /// </summary>
        public static bool IsExternalDragging(Camera camera)
        {
            // ドラッグ主が登録解除された場合に isDragging が残らないよう都度問い合わせる
            return _dragEntry != null && _dragCamera == camera && SafeIsDragging(_dragEntry);
        }

        /// <summary>登録順に掴みを試し、最初にヒットした 1 個だけがドラッグを開始する</summary>
        public static bool TryBeginExternalDrag(Camera camera, Vector2 rtPoint)
        {
            foreach (var entry in _entries)
            {
                try
                {
                    if (entry.tryBeginDrag(camera, rtPoint))
                    {
                        _dragEntry = entry;
                        _dragCamera = camera;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    // 外部プラグインの例外でホストの入力処理を止めない
                    MTEUtils.LogException(e);
                }
            }
            return false;
        }

        public static void UpdateExternalDrag(Camera camera, Vector2 rtPoint)
        {
            // 開始ビュー以外からの更新は無視する (両ビューが同フレームに呼んでも二重更新にならない)
            if (_dragEntry == null || camera != _dragCamera)
            {
                return;
            }
            try
            {
                _dragEntry.updateDrag(camera, rtPoint);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                EndExternalDrag();
            }
        }

        public static void EndExternalDrag()
        {
            if (_dragEntry == null)
            {
                return;
            }
            try
            {
                _dragEntry.endDrag();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            _dragEntry = null;
            _dragCamera = null;
        }

        /// <summary>各ビューカメラの OnPostRender から呼ばれ、登録済みギズモを描画する</summary>
        public static void DrawExternals(Camera camera)
        {
            foreach (var entry in _entries)
            {
                try
                {
                    entry.draw(camera);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        private static bool SafeIsDragging(Entry entry)
        {
            try
            {
                return entry.isDragging();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return false;
            }
        }
    }
}
