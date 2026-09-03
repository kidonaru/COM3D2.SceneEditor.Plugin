# BGM のサウンドウィンドウ移設・動画ウィンドウ新設・シーンプリセット対応 設計

作成日: 2026-09-03

## 目的

- タイムライン設定ウィンドウにある「BGM設定」をサウンドウィンドウの BGM タブへ移す
- 「動画設定」を独立した動画ウィンドウへ移す
- BGM（ゲーム側 BGM とタイムライン BGM ファイル）と動画設定をシーンプリセットの「演出」カテゴリで保存・復元できるようにする
- テキストウィンドウと同様に、タイムライン未読込でも設定を編集・保持できるようにする

## 決定事項（ユーザー確認済み）

| 論点 | 決定 |
|---|---|
| プリセットに保存する BGM の範囲 | タイムライン BGM ファイル設定に加え、ゲーム側 BGM（サウンドウィンドウで選んだ PhotoSoundData の曲）も含める。v9 の「ゲーム BGM は再生環境の状態」という方針は覆す |
| プリセットのカテゴリ | 既存の「演出」カテゴリ（`ScenePresetEffects`）に含める。保存/読込トグルは増やさない |
| タイムライン未読込時 | テキスト同様に単独で使えるようにする。値の実体をマネージャ側へ移す |

## 1. MTE 側: 設定値のタイムライン非依存化

### 設定 DTO

- `Timeline/BgmSettings.cs`
  - `string bgmPath`, `float bpm = 120`, `bool isShowBPMLine`, `float bpmLineOffsetFrame`
  - `void CopyFrom(BgmSettings)`
- `Timeline/VideoSettings.cs`
  - 現在 `TimelineData` にある `videoEnabled` 〜 `videoFrontmostAlpha` の 18 フィールドを `video` 接頭辞なしで持つ（`enabled`, `displayType`, `path`, `position`, `rotation`, `scale`, `startTime`, `volume`, `alpha`, `guiPosition`, `guiScale`, `guiAlpha`, `backmostPosition`, `backmostScale`, `backmostAlpha`, `frontmostPosition`, `frontmostScale`, `frontmostAlpha`）。既定値は現行のまま
  - `void CopyFrom(VideoSettings)`

### TimelineData / TimelineXml

- `TimelineData` の個別フィールドを `public BgmSettings bgm = new BgmSettings()` / `public VideoSettings video = new VideoSettings()` に置き換える
- `TimelineXml` の形式は変更しない。`FromXml` / `ToXml` でフィールド単位に写す（既存のタイムラインファイルはそのまま読める）
- `ResetSettings` など既存の初期化経路は新オブジェクトへ差し替える

### マネージャ

- `BGMManager.settings` / `MovieManager.settings` を追加
  - `timeline != null ? timeline.bgm : _standalone` の方式（`TimelineTextManager.textCount` と同じ）
  - `_standalone` はマネージャが保持するインスタンス。タイムライン読込中に編集した値は timeline 側へ入り、未読込時は standalone へ入る
- `BGMManager.Load()` は `timeline == null` で弾かず `settings.bgmPath` を読む
- `MovieManager` の `videoPath` / `isEnabled` / `SetupImpl` は `settings` を読む
- `MoviePlayerImpl` の `timeline.videoXxx` 参照をすべて `MovieManager.instance.settings.xxx` へ置き換える。`timeline.startOffsetTime` は未読込時 0 として扱う
- 未読込時の再生:
  - BGM ファイル: `Play()` / `Pause()` / `Stop()` は既に `IsLoaded()` だけを見るため、そのまま手動再生に使える。`Update()` は `defaultLayer` が null のときは同期処理をスキップする
  - 動画: `MoviePlayerImpl.Update` / `LateUpdate` の timeline null ガードは残す（ループ再生と表示更新のみ）。タイムライン同期（シーク・速度・再生停止追随）はタイムライン読込後にのみ働く。この制約はウィンドウの注記で示す

## 2. SceneEditor 側 UI

### SoundWindow（BGM タブ）

タブ構成は変えず、BGM タブの内容を以下の順にする。

1. 再生中のゲーム BGM 行（既存）
2. 「BGMファイル」区画（`TimelineSettingWindow.DrawBGMSetting` を移設）
   - パス選択 / 再読込 / パス入力欄 / 音量 / BPM ライン表示 / BPM / オフセット
   - タイムライン未読込時のみ「再生」「停止」ボタンを追加する（読込中はタイムライン再生に追随するため出さない）
3. 検索欄とゲーム BGM 一覧（既存）

### VideoWindow（新規）

- `EditorSubWindow` 継承。`WINDOW_ID = 8903396`（既存 ID と重複しない値。`TextWindow` は 8903394）
- タイトル「動画」。`Config` に `videoPosX/Y/Width/Height/Visible` を追加
- `WindowManager.AddWindow(VideoWindow.instance)`、`MenuBarWindow` に「動画」項目を追加（テキストの隣）
- 内容は `TimelineSettingWindow.DrawVideoSetting` を移設。`timeline` ではなく `MovieManager.settings` を読み書きする
- 共通設定の「動画先読み秒数」（`Config.videoPrebufferTime`）はタイムライン設定ウィンドウに残す

### TimelineSettingWindow

- `DrawBGMSetting` / `DrawVideoSetting` と関連フィールド（`_videoDisplayTypeComboBox` 等）を削除

## 3. シーンプリセット（v30）

### データ

`ScenePresetEffects` に以下を追加する。要素なし（null）は「未記録」として適用時に触らない。

```csharp
/// <summary>サウンド (v30)</summary>
public class ScenePresetSound
{
    /// <summary>再生中のゲーム BGM ファイル名 (例 "BGM020.ogg")。空文字は無音 (適用時に停止)</summary>
    [XmlAttribute] public string gameBgmFile = "";
    public string bgmPath = "";
    public float bpm = 120f;
    public bool isShowBPMLine;
    public float bpmLineOffsetFrame;
}

/// <summary>動画 (v30)。VideoSettings と同じ項目</summary>
public class ScenePresetVideo { ... 18 項目 ... }

public class ScenePresetEffects
{
    ... texts, subCameras ...
    public ScenePresetSound sound;
    public ScenePresetVideo video;
}
```

- `ScenePresetData.CurrentVersion = 30`。バージョン注記に「v30: effects に sound / video を追加。ゲーム BGM を再び保存対象にする（v9 の方針を変更）。旧形式は null で読め、適用時に触らない」を追記

### Capture / Apply（`MteEffectsSnapshot`）

- Capture: `BgmUtils.GetPlayingFileName()`（null は空文字）と `BGMManager.settings`、`MovieManager.settings` を DTO へ写す
- Apply:
  - ゲーム BGM: `gameBgmFile` が空なら `BgmUtils.Stop()`、それ以外は `PhotoSoundData.Get(file)` が見つかれば `Play()`。見つからなければ警告ログのみ。`BgmUtils.EnsureSoundDataLoaded()` を先に呼ぶ
  - BGM ファイル: `settings` に写してから `BGMManager.Reload()`（パスが空なら `Stop()` のみ）
  - 動画: `settings` に写してから `MovieManager.ReloadMovie()`（`enabled` が false なら `UnloadMovie()`）

### UI 文言

- 保存ポップアップ: 「演出 (テキスト・サブカメラ・サウンド・動画)」
- `SavePresetPopupWindow` / `PresetWindow` のトグルは増やさない

## 4. テスト

- `ScenePresetEffectsTests`
  - sound / video を含むラウンドトリップで値が保持される
  - v29 形式（sound / video 要素なし）を読むと両方 null になる
- `TimelineSettingXmlTests` / `XmlRoundTripTests` 系で `TimelineData` ↔ `TimelineXml` の bgm / video 写しが壊れていないことを確認する（既存テストがあれば追随、なければ最小のラウンドトリップを追加）

## 5. 影響範囲

| ファイル | 変更 |
|---|---|
| `Timeline/BgmSettings.cs`, `Timeline/VideoSettings.cs` | 新規 |
| `Timeline/TimelineData.cs` | フィールドをオブジェクトへ集約、FromXml/ToXml 追随 |
| `Timeline/Manager/BGMManager.cs`, `Timeline/Manager/MovieManager.cs` | `settings` 追加、timeline 依存の除去 |
| `Timeline/MoviePlayerImpl.cs` | `settings` 参照へ置換 |
| `TimelineSettingWindow.cs` | BGM / 動画セクション削除 |
| `SoundWindow.cs` | BGM ファイル区画の追加 |
| `VideoWindow.cs` | 新規 |
| `Config.cs`, `Manager/WindowManager.cs`, `MenuBarWindow.cs` | ウィンドウ登録 |
| `ScenePresetData.cs`, `Manager/MteEffectsSnapshot.cs`, `SavePresetPopupWindow.cs` | v30 対応 |
| `COM3D2.SceneEditor.Plugin.Tests/*` | テスト追加 |

## 6. 非対象

- 動画先読み秒数の移設
- BGM / 動画のキーフレーム化
- サウンドウィンドウのボイス / 効果音タブ
- 保存 / 読込トグルの新設
