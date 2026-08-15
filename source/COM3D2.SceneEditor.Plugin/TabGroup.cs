using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タブ統合されたドック可能ウィンドウのグループ。
    /// 所属ウィンドウは矩形を共有し、アクティブタブのウィンドウだけが描画される。
    /// 生成・解体・マージ・分離の判断は TabGroupManager が行い、本クラスは状態だけを持つ
    /// </summary>
    public class TabGroup
    {
        /// <summary>所属ウィンドウ。リスト順がタブの並び順</summary>
        public readonly List<IDockableWindow> windows = new List<IDockableWindow>();

        private IDockableWindow _activeWindow;
        public IDockableWindow activeWindow => _activeWindow;

        public bool Contains(IDockableWindow window)
        {
            return windows.Contains(window);
        }

        /// <summary>
        /// 末尾タブとして追加する。矩形はグループ側に合わせる。
        /// activate=false は復元時など「並びだけ作りたい」場合に使う (Activate/Deactivate の連鎖防止)
        /// </summary>
        public void Add(IDockableWindow window, bool activate = true)
        {
            if (windows.Contains(window))
            {
                return;
            }

            windows.Add(window);
            window.group = this;

            if (_activeWindow != null)
            {
                window.windowRect = _activeWindow.windowRect;
            }

            if (activate || _activeWindow == null)
            {
                // SetActive が push まで行う
                SetActive(window);
            }
            else
            {
                // 非アクティブタブとして加入したことを本人へ通知する
                window.NotifyTabVisibleChanged();
                PushTabBarState();
            }
        }

        /// <summary>グループから外す。アクティブタブが抜けたら先頭をアクティブにする</summary>
        public void Remove(IDockableWindow window)
        {
            if (!windows.Remove(window))
            {
                return;
            }

            window.group = null;
            // 独立ウィンドウへ戻ったことを通知する (非アクティブタブだった場合は可視へ変化)
            window.NotifyTabVisibleChanged();
            // 外れた側は通常タイトル描画へ戻す
            window.SetTabBarState(null, -1);

            if (_activeWindow == window)
            {
                _activeWindow = null;
                if (windows.Count > 0)
                {
                    SetActive(windows[0]);
                }
            }

            // 非アクティブメンバーが抜けた場合は SetActive を通らないため末尾でも push する
            PushTabBarState();
        }

        /// <summary>
        /// アクティブタブを切り替え、旧・新それぞれへ可視状態の変化を通知する。
        /// この通知を欠くと SceneView が非アクティブになってもカメラ・RT が動き続ける
        /// </summary>
        public void SetActive(IDockableWindow window)
        {
            if (_activeWindow == window || !windows.Contains(window))
            {
                return;
            }

            var prev = _activeWindow;
            _activeWindow = window;

            if (prev != null)
            {
                prev.NotifyTabVisibleChanged();
            }
            window.NotifyTabVisibleChanged();

            PushTabBarState();
        }

        /// <summary>
        /// 現在のタブ状態を全メンバーへ push する。
        /// 状態変更時のみ呼ぶ (毎フレームの配列生成を避ける契約)
        /// </summary>
        public void PushTabBarState()
        {
            var titles = new string[windows.Count];
            for (var i = 0; i < windows.Count; i++)
            {
                titles[i] = windows[i].windowTitleForTab;
            }
            var activeIndex = _activeWindow != null ? windows.IndexOf(_activeWindow) : -1;
            foreach (var window in windows)
            {
                window.SetTabBarState(titles, activeIndex);
            }
        }

        /// <summary>アクティブタブの矩形変更を全ウィンドウへ同期する</summary>
        public void SyncRect(Rect rect)
        {
            foreach (var window in windows)
            {
                window.windowRect = rect;
            }
        }
    }
}
