using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>トーストの種別。背景色の出し分けにのみ使う</summary>
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error,
    }

    /// <summary>
    /// 画面右上に数秒だけ出す非モーダル通知。
    /// DialogPopupWindow (モーダルな確認・通知) と違い、操作を止めず入力も奪わない。
    /// ウィンドウ描画より後に呼ばれる前提で、GUI.Label と塗りつぶしだけで描く
    /// </summary>
    public static class ToastManager
    {
        /// <summary>表示時間とフェードアウト時間 (秒)。合計が 1 件の寿命になる</summary>
        private const float DisplaySeconds = 3f;
        private const float FadeSeconds = 0.5f;

        /// <summary>同時表示数の上限。超えたら古いものから消す</summary>
        private const int MaxCount = 3;

        private const float ToastWidth = 300f;
        private const float PaddingX = 10f;
        private const float PaddingY = 6f;
        private const float MarginRight = 12f;
        private const float MarginTop = 12f;
        private const float Spacing = 4f;

        private class Toast
        {
            /// <summary>OnGUI は 1 フレームに複数回呼ばれるため、本文は生成時に作り置きする</summary>
            public GUIContent content;
            public ToastType type;

            /// <summary>消滅時刻 (Time.unscaledTime 基準)</summary>
            public float expireTime;
        }

        private static readonly List<Toast> _toasts = new List<Toast>();

        /// <summary>本文を折り返すためのスタイル。GUIStyle は OnGUI 内でしか作れない</summary>
        private static GUIStyle _gsToast = null;

        public static void Show(string message, ToastType type = ToastType.Info)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            _toasts.Add(new Toast
            {
                content = new GUIContent(message),
                type = type,
                expireTime = Time.unscaledTime + DisplaySeconds + FadeSeconds,
            });

            // 新しいものを残したいので、溢れた分は先頭 (古い方) から捨てる
            while (_toasts.Count > MaxCount)
            {
                _toasts.RemoveAt(0);
            }
        }

        /// <summary>全ウィンドウを描いた後に呼ぶ。IMGUI は後に描いたものが手前に来る</summary>
        public static void OnGUI()
        {
            var now = Time.unscaledTime;
            _toasts.RemoveAll(t => t.expireTime <= now);
            if (_toasts.Count == 0)
            {
                return;
            }

            if (_gsToast == null)
            {
                _gsToast = new GUIStyle(GUIView.gsLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft,
                };
            }

            var savedColor = GUI.color;
            var x = Screen.width - ToastWidth - MarginRight;
            var y = MarginTop;

            foreach (var toast in _toasts)
            {
                var textWidth = ToastWidth - PaddingX * 2f;
                var height = _gsToast.CalcHeight(toast.content, textWidth) + PaddingY * 2f;

                // 残り時間がフェード時間を切ったら徐々に薄くする
                var remain = toast.expireTime - now;
                var alpha = remain < FadeSeconds ? Mathf.Clamp01(remain / FadeSeconds) : 1f;

                var bgColor = GetBackgroundColor(toast.type);
                bgColor.a *= alpha;

                GUI.color = bgColor;
                GUI.DrawTexture(new Rect(x, y, ToastWidth, height), Texture2D.whiteTexture);

                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(
                    new Rect(x + PaddingX, y + PaddingY, textWidth, height - PaddingY * 2f),
                    toast.content, _gsToast);

                y += height + Spacing;
            }

            GUI.color = savedColor;
        }

        private static Color GetBackgroundColor(ToastType type)
        {
            switch (type)
            {
                case ToastType.Success:
                    return new Color(0.13f, 0.38f, 0.19f, 0.9f);
                case ToastType.Warning:
                    return new Color(0.45f, 0.36f, 0.09f, 0.9f);
                case ToastType.Error:
                    return new Color(0.45f, 0.14f, 0.14f, 0.9f);
                default:
                    return new Color(0.16f, 0.16f, 0.16f, 0.9f);
            }
        }
    }
}
