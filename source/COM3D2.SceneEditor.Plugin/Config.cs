using System.Collections.Generic;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    public enum KeyBindType
    {
        PluginToggle,
        GizmoMove,
        GizmoRotate,
        GizmoScale,
        Undo,
        Redo,
        EditModeToggle,
    }

    public class Config
    {
        public static readonly int CurrentVersion = 1;

        [XmlAttribute]
        public int version = 0;

        // 動作設定
        public bool pluginEnabled = true;
        public float keyRepeatTimeFirst = 0.15f;
        public float keyRepeatTime = 1f / 30f;
        public bool useHSVColor = false;

        // GameViewウィンドウ (-1 は未初期化。初回は画面中央に配置)
        public int gameViewPosX = -1;
        public int gameViewPosY = -1;
        public int gameViewWidth = 960;
        public int gameViewHeight = 560;

        // GameView の最大化状態。プラグイン再有効化・再起動をまたいで復元する
        public bool gameViewMaximized = false;

        // メニューバーウィンドウ (-1 は未初期化。初回は画面左上に配置)
        public int menuBarPosX = -1;
        public int menuBarPosY = -1;

        // SceneView / Hierarchy / Inspector ウィンドウ (-1 は未初期化)
        public int sceneViewPosX = -1;
        public int sceneViewPosY = -1;
        public int sceneViewWidth = 640;
        public int sceneViewHeight = 400;
        public bool sceneViewVisible = false;

        // SceneView ツールバーの表示トグル (SceneView にのみ適用。ゲーム画面には影響しない)
        public bool sceneViewShowBg = false;
        public bool sceneViewShowMaid = true;
        public bool sceneViewShowGizmo = true;
        public bool sceneViewOrthographic = false;

        public int hierarchyPosX = -1;
        public int hierarchyPosY = -1;
        public int hierarchyWidth = 260;
        public int hierarchyHeight = 480;
        public bool hierarchyVisible = false;

        public int inspectorPosX = -1;
        public int inspectorPosY = -1;
        public int inspectorWidth = 280;
        public int inspectorHeight = 360;
        public bool inspectorVisible = false;
        // 拡縮の XYZ 連動 (1 軸の編集を比率で全軸へ反映)
        public bool inspectorScaleLinked = false;

        // メイド配置機能
        // 配置プリセット。XML へ永続化するため MaidPlacementPreset.PresetType の序数で持つ
        public int maidPlacementMode = 0;

        // メイド系ウィンドウ (-1 は未初期化)
        public int maidCallPosX = -1;
        public int maidCallPosY = -1;
        public int maidCallWidth = 300;
        public int maidCallHeight = 400;
        public bool maidCallVisible = false;

        public int maidPosePosX = -1;
        public int maidPosePosY = -1;
        public int maidPoseWidth = 300;
        public int maidPoseHeight = 450;
        public bool maidPoseVisible = false;

        public int maidFacePosX = -1;
        public int maidFacePosY = -1;
        public int maidFaceWidth = 300;
        public int maidFaceHeight = 400;
        public bool maidFaceVisible = false;

        public int maidFingerPosX = -1;
        public int maidFingerPosY = -1;
        public int maidFingerWidth = 320;
        public int maidFingerHeight = 450;
        public bool maidFingerVisible = false;

        // カメラウィンドウ (-1 は未初期化)
        public int cameraPosX = -1;
        public int cameraPosY = -1;
        public int cameraWidth = 300;
        public int cameraHeight = 300;
        public bool cameraVisible = false;

        // 背景ウィンドウ (-1 は未初期化)
        public int backgroundPosX = -1;
        public int backgroundPosY = -1;
        public int backgroundWidth = 300;
        public int backgroundHeight = 400;
        public bool backgroundVisible = false;

        // BGMウィンドウ (-1 は未初期化)
        public int bgmPosX = -1;
        public int bgmPosY = -1;
        public int bgmWidth = 300;
        public int bgmHeight = 400;
        public bool bgmVisible = false;

        public int maidUndressPosX = -1;
        public int maidUndressPosY = -1;
        public int maidUndressWidth = 300;
        public int maidUndressHeight = 400;
        public bool maidUndressVisible = false;

        public int maidGravityPosX = -1;
        public int maidGravityPosY = -1;
        public int maidGravityWidth = 320;
        public int maidGravityHeight = 400;
        public bool maidGravityVisible = false;

        public int maidIKPosX = -1;
        public int maidIKPosY = -1;
        public int maidIKWidth = 320;
        public int maidIKHeight = 420;
        public bool maidIKVisible = false;

        public int boneEditPosX = -1;
        public int boneEditPosY = -1;
        public int boneEditWidth = 280;
        public int boneEditHeight = 500;
        public bool boneEditVisible = false;

        // プリセットウィンドウ (-1 は未初期化)
        public int presetPosX = -1;
        public int presetPosY = -1;
        public int presetWidth = 400;
        public int presetHeight = 420;
        public bool presetVisible = false;

        // ライトウィンドウ
        public int lightPosX = -1;
        public int lightPosY = -1;
        public int lightWidth = 300;
        public int lightHeight = 400;
        public bool lightVisible = false;

        // PNG配置ウィンドウ
        public int pngPlacementPosX = -1;
        public int pngPlacementPosY = -1;
        public int pngPlacementWidth = 400;
        public int pngPlacementHeight = 500;
        public bool pngPlacementVisible = false;

        // 操作履歴ウィンドウ
        public int historyPosX = -1;
        public int historyPosY = -1;
        public int historyWidth = 300;
        public int historyHeight = 400;
        public bool historyVisible = false;

        /// <summary>操作履歴の最大保持数。0 以下で履歴を無効化する</summary>
        public int historyLimit = 20;

        // 設定ウィンドウ
        public int settingPosX = -1;
        public int settingPosY = -1;
        public int settingWidth = 300;
        public int settingHeight = 420;
        public bool settingVisible = false;

        /// <summary>スクリーンショットの解像度倍率 (画面サイズの何倍で撮るか)</summary>
        public int screenshotScale = 1;

        // カメラプリセット。1 要素 = 1 スロット、書式 "slot:tx,ty,tz,dist,yaw,pitch,roll,fov"
        [XmlElement("cameraPreset")]
        public List<string> cameraPresets = new List<string>();

        private static string GetCameraPresetPrefix(int slot)
        {
            return slot + ":";
        }

        /// <summary>slot 番号のエントリの添字を返す。未保存なら -1</summary>
        private int FindCameraPresetIndex(int slot)
        {
            var prefix = GetCameraPresetPrefix(slot);
            for (var i = 0; i < cameraPresets.Count; i++)
            {
                if (cameraPresets[i].StartsWith(prefix))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>slot 番号のカメラプリセット文字列を取得する。未保存なら null</summary>
        public string GetCameraPreset(int slot)
        {
            var index = FindCameraPresetIndex(slot);
            if (index < 0)
            {
                return null;
            }
            return cameraPresets[index].Substring(GetCameraPresetPrefix(slot).Length);
        }

        /// <summary>slot 番号のカメラプリセット文字列を記録する (既存なら上書き、なければ追加)</summary>
        public void SetCameraPreset(int slot, string value)
        {
            var entry = GetCameraPresetPrefix(slot) + value;
            var index = FindCameraPresetIndex(slot);
            if (index < 0)
            {
                cameraPresets.Add(entry);
            }
            else
            {
                cameraPresets[index] = entry;
            }
        }

        /// <summary>slot 番号のカメラプリセットを削除する。削除したら true</summary>
        public bool RemoveCameraPreset(int slot)
        {
            var index = FindCameraPresetIndex(slot);
            if (index < 0)
            {
                return false;
            }
            cameraPresets.RemoveAt(index);
            return true;
        }

        // シーンプリセット保存対象 (保存ポップアップのチェック状態を次回デフォルトとして持つ)
        public bool scenePresetSaveCamera = true;
        public bool scenePresetSaveMaids = true;
        // 無効化した外部プロバイダ id のカンマ区切り (未指定は全有効)
        public string scenePresetDisabledProviders = "";
        // SceneDaily 遷移時に自動ロードするプリセットのキー (相対パス・拡張子なし)。空で無効
        public string scenePresetAutoLoadKey = "";
        // 自動ロードをセッション (ゲーム起動) 中 1 回だけにする
        public bool scenePresetAutoLoadOnceOnly = false;

        // タブグループ構成。1 要素 = 1 グループ、書式 "activeWindowId:id1,id2,..."
        [XmlElement("tabGroup")]
        public List<string> tabGroups = new List<string>();

        // コネクトグループ構成。1 要素 = 1 グループ、書式 "id1,id2,..."
        [XmlElement("connectGroup")]
        public List<string> connectGroups = new List<string>();

        // 配置を保存した時点の画面サイズ。1 要素 = 1 ウィンドウ、書式 "windowId:width,height"。
        // 画面サイズが変わったとき、保存時の比率を保つよう配置をスケールするために使う
        [XmlElement("windowScreen")]
        public List<string> windowScreens = new List<string>();

        /// <summary>windowId の配置を保存した時点の画面サイズを取得する。未記録なら false</summary>
        public bool TryGetWindowScreenSize(int windowId, out int width, out int height)
        {
            width = 0;
            height = 0;

            var prefix = windowId + ":";
            foreach (var entry in windowScreens)
            {
                if (!entry.StartsWith(prefix))
                {
                    continue;
                }

                var parts = entry.Substring(prefix.Length).Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out width) &&
                    int.TryParse(parts[1], out height) &&
                    width > 0 && height > 0)
                {
                    return true;
                }

                // 不正エントリ。片側だけパース成功した値を返さないようゼロクリアする
                width = 0;
                height = 0;
                return false;
            }
            return false;
        }

        /// <summary>windowId の配置を保存した時点の画面サイズを記録する</summary>
        public void SetWindowScreenSize(int windowId, int width, int height)
        {
            var prefix = windowId + ":";
            var entry = prefix + width + "," + height;

            for (var i = 0; i < windowScreens.Count; i++)
            {
                if (windowScreens[i].StartsWith(prefix))
                {
                    windowScreens[i] = entry;
                    return;
                }
            }
            windowScreens.Add(entry);
        }

        // ロック中 (移動・リサイズ禁止) のウィンドウ ID。1 要素 = 1 ウィンドウ
        [XmlElement("lockedWindow")]
        public List<int> lockedWindows = new List<int>();

        /// <summary>windowId のウィンドウがロック中か</summary>
        public bool IsWindowLocked(int windowId)
        {
            return lockedWindows.Contains(windowId);
        }

        /// <summary>windowId のロック状態を記録する</summary>
        public void SetWindowLocked(int windowId, bool locked)
        {
            if (locked == lockedWindows.Contains(windowId))
            {
                return;
            }
            if (locked)
            {
                lockedWindows.Add(windowId);
            }
            else
            {
                lockedWindows.Remove(windowId);
            }
        }

        // グリッド設定
        // 既定値は設定 UI のリセットボタンからも参照するため定数で持つ
        public const int DefaultGridCountInWorld = 20;
        public const float DefaultGridCellSize = 0.5f;
        public const float DefaultGridAlphaInWorld = 0.3f;
        public const float DefaultGridLineWidthInWorld = 2f;
        public const int DefaultGridCountInDisplay = 4;
        public const float DefaultGridAlphaInDisplay = 0.3f;
        public const float DefaultGridLineWidthInDisplay = 1f;

        /// <summary>グリッド全体の表示スイッチ</summary>
        public bool isGridVisible = true;
        /// <summary>編集モード中だけグリッドを出す</summary>
        public bool isGridVisibleOnlyEdit = true;

        // ワールドグリッド (床の XZ 平面。SceneView / GameView 両方に描画する)
        public bool isGridVisibleInWorld = true;
        /// <summary>原点を中心とした 1 辺のマス数</summary>
        public int gridCountInWorld = DefaultGridCountInWorld;
        /// <summary>1 マスの大きさ (m)</summary>
        public float gridCellSize = DefaultGridCellSize;
        public float gridAlphaInWorld = DefaultGridAlphaInWorld;
        public Color gridColorInWorld = Color.white;
        /// <summary>線の幅の倍率 (MTE と同じくカメラ距離 × 0.001 を基準にした倍率)</summary>
        public float gridLineWidthInWorld = DefaultGridLineWidthInWorld;
        /// <summary>XYZ 軸線 (赤/緑/青) の表示</summary>
        public bool isGridAxisVisible = true;

        // 画面分割グリッド (構図合わせ用。GameView にのみ描画する)
        public bool isGridVisibleInDisplay = false;
        /// <summary>画面の分割数 (3 で三分割法)</summary>
        public int gridCountInDisplay = DefaultGridCountInDisplay;
        public float gridAlphaInDisplay = DefaultGridAlphaInDisplay;
        public Color gridColorInDisplay = Color.white;
        /// <summary>分割線の幅 (px)</summary>
        public float gridLineWidthInDisplay = DefaultGridLineWidthInDisplay;

        // 色設定
        public Color windowHoverColor = new Color(48 / 255f, 48 / 255f, 48 / 255f, 224 / 255f);
        public Color backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        [XmlIgnore]
        public Dictionary<KeyBindType, KeyBind> keyBinds = new Dictionary<KeyBindType, KeyBind>
        {
            { KeyBindType.PluginToggle, new KeyBind("F10") },
            { KeyBindType.GizmoMove, new KeyBind("Z") },
            { KeyBindType.GizmoRotate, new KeyBind("X") },
            { KeyBindType.GizmoScale, new KeyBind("C") },
            { KeyBindType.Undo, new KeyBind("Ctrl+Z") },
            { KeyBindType.Redo, new KeyBind("Ctrl+X") },
            { KeyBindType.EditModeToggle, new KeyBind("Tab") },
        };

        public struct KeyBindPair
        {
            public KeyBindType key;
            public string value;
        }

        [XmlElement("keyBind")]
        public KeyBindPair[] keyBindsXml
        {
            get
            {
                var result = new List<KeyBindPair>(keyBinds.Count);
                foreach (var pair in keyBinds)
                {
                    result.Add(new KeyBindPair { key = pair.Key, value = pair.Value.ToString() });
                }
                return result.ToArray();
            }
            set
            {
                if (value == null)
                {
                    return;
                }

                foreach (var pair in value)
                {
                    keyBinds[pair.key] = new KeyBind(pair.value);
                }
            }
        }

        [XmlIgnore]
        public bool dirty = false;

        public void ConvertVersion()
        {
            version = CurrentVersion;
        }

        public bool GetKey(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKey();
        }

        public bool GetKeyDown(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKeyDown();
        }

        public bool GetKeyUp(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKeyUp();
        }

        public string GetKeyName(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].ToString();
        }
    }
}
