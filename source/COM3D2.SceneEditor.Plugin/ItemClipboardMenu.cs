using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 値のコピー / 貼り付けを出すメニューアイコンボタン。
    /// GUIComboBox をアイコンボタン・矢印なしで使い、ポップアップは Inspector の
    /// ComboBoxPopupWindow.ProcessFocus が出す。ポップアップは押したボタンの位置を
    /// 基準に出るため、描く行ごとに別インスタンスを持つ (owner + key で区別する)
    /// </summary>
    public class ItemClipboardMenu
    {
        public const float ButtonWidth = 20f;

        private enum Command
        {
            Copy,
            Paste,
        }

        private class Entry
        {
            public GUIComboBox<Command> comboBox;
            public bool canCopy;
            public bool canPaste;
            public Action onCopy;
            public Action onPaste;
            public int lastFrame;
        }

        private static readonly List<Command> _commands = new List<Command> { Command.Copy, Command.Paste };

        private readonly Dictionary<object, Dictionary<string, Entry>> _entries =
            new Dictionary<object, Dictionary<string, Entry>>();
        private readonly List<object> _removeOwners = new List<object>();
        private readonly List<string> _removeKeys = new List<string>();
        private int _currentFrame = -1;

        private static ItemClipboardMenu _instance = null;
        public static ItemClipboardMenu instance => _instance ?? (_instance = new ItemClipboardMenu());

        private ItemClipboardMenu()
        {
        }

        /// <summary>右端のボタンを置くために直前の要素の幅から引く量</summary>
        public static float GetReservedWidth(GUIView view)
        {
            return ButtonWidth + view.margin;
        }

        /// <summary>現在位置から、右端のボタンと末尾の余白を除いた残り幅</summary>
        public static float GetRemainingWidth(GUIView view)
        {
            return view.viewRect.width - view.currentPos.x - view.padding.x * 2
                - GetReservedWidth(view) - view.margin;
        }

        /// <summary>
        /// 前のフレームに描かれなかったボタンを捨てる。
        /// OnGUI はイベントごとに複数回呼ばれるので、フレームが変わったときだけ掃除する
        /// </summary>
        public void BeginFrame()
        {
            var frame = Time.frameCount;
            if (frame == _currentFrame)
            {
                return;
            }
            var previous = _currentFrame;
            _currentFrame = frame;

            _removeOwners.Clear();
            foreach (var ownerPair in _entries)
            {
                _removeKeys.Clear();
                foreach (var pair in ownerPair.Value)
                {
                    if (pair.Value.lastFrame < previous)
                    {
                        _removeKeys.Add(pair.Key);
                    }
                }
                foreach (var key in _removeKeys)
                {
                    ownerPair.Value.Remove(key);
                }
                if (ownerPair.Value.Count == 0)
                {
                    _removeOwners.Add(ownerPair.Key);
                }
            }
            foreach (var owner in _removeOwners)
            {
                _entries.Remove(owner);
            }
        }

        /// <summary>現在位置に 20x20 のメニューボタンを描く</summary>
        public void Draw(
            GUIView view, object owner, string key,
            bool canCopy, bool canPaste, Action onCopy, Action onPaste)
        {
            var entry = GetEntry(owner, key ?? string.Empty);
            entry.lastFrame = Time.frameCount;

            // ポップアップを開いている間は対象を開いた時点のものに固定する。
            // 選択全体のメニューはキーが固定なので、開いた後に選択が変わると別の対象へ実行してしまう
            if (view.focusedComboBox != entry.comboBox)
            {
                entry.canCopy = canCopy;
                entry.canPaste = canPaste;
                entry.onCopy = onCopy;
                entry.onPaste = onPaste;
            }

            // コピーは値を書き換えないので、ビューの編集開始フック (AutoEditMode.Enter 等) を通さない。
            // 貼り付けは TimelineItemValueTransfer.Paste / キーフレーム側が自前で履歴を扱う
            var onBeforeValueChanged = view.onBeforeValueChanged;
            view.onBeforeValueChanged = null;
            try
            {
                entry.comboBox.currentIndex = -1;
                entry.comboBox.DrawTextureButton(view);
            }
            finally
            {
                view.onBeforeValueChanged = onBeforeValueChanged;
            }
        }

        /// <summary>行の右端へ寄せてメニューボタンを描く (横並びの中で使う)</summary>
        public void DrawRightAligned(
            GUIView view, object owner, string key,
            bool canCopy, bool canPaste, Action onCopy, Action onPaste)
        {
            view.currentPos.x = view.viewRect.width - view.padding.x * 2 - ButtonWidth;
            Draw(view, owner, key, canCopy, canPaste, onCopy, onPaste);
        }

        private Entry GetEntry(object owner, string key)
        {
            Dictionary<string, Entry> ownerEntries;
            if (!_entries.TryGetValue(owner, out ownerEntries))
            {
                ownerEntries = new Dictionary<string, Entry>();
                _entries[owner] = ownerEntries;
            }

            Entry entry;
            if (ownerEntries.TryGetValue(key, out entry))
            {
                return entry;
            }

            entry = new Entry();
            var captured = entry;
            entry.comboBox = new GUIComboBox<Command>
            {
                items = _commands,
                defaultTexture = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Menu),
                showArrow = false,
                currentIndex = -1,
                buttonSize = new Vector2(ButtonWidth, ButtonWidth),
                contentSize = new Vector2(100, 300),
                getName = (command, _) => command == Command.Copy ? "コピー" : "貼り付け",
                getEnabled = (command, _) => command == Command.Copy ? captured.canCopy : captured.canPaste,
                onSelected = (command, _) =>
                {
                    // ポップアップを開いたまま選択が変わり、この行が描かれなくなった場合は
                    // 古い対象に束縛されたコールバックを発火させない
                    if (captured.lastFrame < Time.frameCount - 1)
                    {
                        return;
                    }
                    var action = command == Command.Copy ? captured.onCopy : captured.onPaste;
                    if (action != null)
                    {
                        action();
                    }
                },
            };
            ownerEntries[key] = entry;
            return entry;
        }
    }
}
