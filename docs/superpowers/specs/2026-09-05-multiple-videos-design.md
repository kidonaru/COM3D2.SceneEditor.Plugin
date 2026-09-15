# 動画の複数表示 設計

作成日: 2026-09-05

## 目的

動画ウィンドウで複数本の動画（最大 4 本）を同時に読み込み・表示・配置できるようにする。
各本は独立した表示形式（GUI / 3D / 最背面 / 最前面）と配置を持ち、タイムラインのシーク・再生速度に全本が同期する。
タイムラインファイルとシーンプリセットの両方で複数本を保存・復元できるようにする。

## 決定事項（ユーザー確認済み）

| 論点 | 決定 |
|---|---|
| 動画ウィンドウの UI | テキストウィンドウと同じ方式（「動画数」の増減行 + 編集対象を選ぶコンボ） |
| 最大本数 | 4 |
| プレビューウィンドウ | 動画ウィンドウで選択中の 1 本だけ表示 |
| タイムラインファイルの保存形式 | 全本を新しいリスト要素へ保存する。旧 `Video*` フラット要素は読込時のみ 1 本目として取り込む |

## 1. データモデル（MTE 側）

### VideoSettings

- `Timeline/VideoSettings.cs` は 1 本分の DTO のまま変更しない（18 項目 + `CopyFrom`）
- `ReadFrom(TimelineXml)` / `WriteTo(TimelineXml)` は `VideoSettingsXml` との相互変換 `ReadFrom(VideoSettingsXml)` / `ToXml()` に置き換える

### TimelineXml

- 新クラス `VideoSettingsXml`（`Timeline/TimelineXml.cs` 内、または同ディレクトリの新ファイル）
  - 現在の `Video*` 18 項目を `Video` 接頭辞なしの `[XmlElement]` として持つ（`Enabled`, `DisplayType`, `Path`, `Position`, `Rotation`, `Scale`, `StartTime`, `Volume`, `Alpha`, `GUIPosition`, `GUIScale`, `GUIAlpha`, `BackmostPosition`, `BackmostScale`, `BackmostAlpha`, `FrontmostPosition`, `FrontmostScale`, `FrontmostAlpha`）。既定値は `VideoSettings` と同じ
- `TimelineXml` に `[XmlElement("Video")] public List<VideoSettingsXml> videos = new List<VideoSettingsXml>()` を追加
- 既存の `videoEnabled` 〜 `videoFrontmostAlpha` と `videoDisplayOnGUI` は**読込互換用**として残す。`ToXml` では値を入れず既定値のまま出力される（要素が残ることは許容する。旧 MTE が読むと「パス空の有効動画」になるだけで実害はない）
- `Initialize()`:
  - `version < 4` の変換（`videoStartTime -= startOffsetTime`、`videoDisplayOnGUI` → `videoDisplayType`）はフラット項目に対して現状どおり行う
  - その後、`videos.Count == 0` ならフラット項目から `VideoSettingsXml` を 1 件作って `videos` に追加する（旧ファイルの取り込み）
- `TimelineData.CurrentVersion` を 32 → 33 に上げる。`Initialize()` の取り込みは version を見ず「リストが空なら」で判定する（v33 以降で保存したファイルは必ず 1 件以上入る）

### TimelineData

- `public VideoSettings video` を `public List<VideoSettings> videos = new List<VideoSettings> { new VideoSettings() }` に置き換える
- `FromXml`: `videos` を `xml.videos` から作り直す。0 件なら 1 件の既定値を入れる。`MovieManager.MaxVideoCount` を超える分は切り捨てる
- `ToXml`: `videos` を全件 `xml.videos` に書く

### MovieManager

- 定数 `MinVideoCount = 1`, `MaxVideoCount = 4`
- `_standaloneSettings` を `List<VideoSettings> _standaloneSettingsList`（初期 1 件）にする
- `public List<VideoSettings> settingsList => timeline != null ? timeline.videos : _standaloneSettingsList`
- `public int videoCount` get は `settingsList.Count`、set は `Min/Max` に丸めてリストを伸縮する。縮める時は該当 index のプレイヤーを `UnloadMovie(index)` してから要素を除く。伸ばす時は既定値の `VideoSettings` を追加する（読込は行わず、ユーザーがパスを選んだ時に読む）
- `public VideoSettings GetSettings(int index)`、`public bool IsValidIndex(int index)`
- プレイヤーは `List<MoviePlayerImpl> _players`（`settingsList` と同じ長さ、未読込は null）と `List<string> _loadedVideoPaths`、`List<VideoDisplayType> _loadedDisplayTypes` を index ごとに持つ
- 既存の公開メソッドは全件対象の一括版として残し、`(int index)` 版を追加する:
  `LoadMovie` / `UnloadMovie` / `ReloadMovie` / `UpdateTransform` / `UpdateVolume` / `UpdateSpeed` / `UpdateSeekTime` / `UpdateColor` / `UpdateMesh` / `UpdateShader`
  - `TimelineManager` のイベント購読（`onStop` / `onAnmSpeedChanged` / `onSeekCurrentFrame`）は一括版を呼ぶ
  - `OnLoad` / `OnPluginDisable` も一括版
- 状態プロパティは index 付きにする: `GetCurrentTime(index)` / `GetDuration(index)` / `GetFrameRate(index)` / `GetTexture(index)` / `RequiresVerticalFlip(index)`。既存の index なし版は削除する
- `isValidPath` / `isEnabled` は `IsValidPath(index)` / `IsEnabled(index)` にする
- `OnClearTimeline`: `_standaloneSettingsList` を `timeline.videos` の深いコピー（件数含む）で置き換える。プレイヤーの実体はそのまま残す（現状と同じ意図）
- `SetupImpl(index)`: 表示形式が変わっていれば `UnloadMovie(index)`。生成時に `MoviePlayerImpl.Setup(VideoSettings)` で設定を注入する。GameObject 名は `MoviePlayer_{index}_{guid}`

### MoviePlayerImpl

- `private static VideoSettings video => MovieManager.instance.settings` を、インスタンスフィールド `private VideoSettings _video` に置き換え、`public void Setup(VideoSettings video)` で注入する。`video` プロパティ名は残して参照箇所を変えない
- `Awake` / `Start` で `_video` を参照するなら、`Setup` は `AddComponent` 直後（同フレーム内、`Start` 前）に呼ぶ。`Awake` で参照している箇所があれば `Setup` 後に呼ぶ初期化へ移す
- 描画・配置ロジック（GUI / 3D / 最背面 / 最前面、グリッド）は変更しない
- 最背面・最前面の複数本は同じ深度に重なる。並び順は index 順で描画されるが、明示的な z オーダー制御は今回は行わない

## 2. UI（SceneEditor 側）

### VideoWindow

- `_videoIndex`（選択中の本）と `_videoIndexItems`（コンボ用 0〜count-1）を TextWindow と同じ方式で持つ
- 先頭に `CountRowDrawer.Draw(view, "動画数", ROW_HEIGHT, movieManager.videoCount, MinVideoCount, MaxVideoCount, count => movieManager.videoCount = count)` を置く
- 次に編集対象コンボ（表示名は「動画 1」〜「動画 4」）。`_videoIndex` を `videoCount - 1` にクランプする
- 以降は選択中の 1 本の既存 UI（有効 / プレビュー / 表示形式 / 動画パス / 開始位置 / 形式別配置 / 音量）を `settings = movieManager.GetSettings(_videoIndex)` に対して描く。各操作の `movieManager.Xxx()` 呼び出しは `Xxx(_videoIndex)` にする
- `public int selectedIndex => _videoIndex` を公開し、プレビューウィンドウが参照する
- 表示形式コンボ（`_videoDisplayTypeComboBox`）の `onSelected` は `_videoIndex` の設定へ書く。ラムダから `_videoIndex` を読むためコンボ生成をインスタンス初期化のままにしてよい

### VideoPreviewWindow

- `VideoWindow.instance.selectedIndex` の `GetTexture` / `RequiresVerticalFlip` を描く
- `windowTitle` は毎回評価されるプロパティなので、`"動画プレビュー (" + (index + 1) + ")"` を返して選択中の本を示す。内容側にはラベルを出さない（ウィンドウ幅を動画へ使う）

## 3. シーンプリセット（v32）

### データ

- `ScenePresetEffects.video`（単体）を `[XmlElement("video")] public List<ScenePresetVideo> videos = new List<ScenePresetVideo>()` に置き換える
- 旧 v30/v31 の単体 `<video>` 要素は同じ要素名なので、XmlSerializer が 1 件のリストとして読む。追加の変換コードは不要
- 空リスト = 保存時に実体なし（`texts` と同じ規約）。ただし動画は常に 1 本以上あるため、保存時は必ず `videoCount` 件入る
- `ScenePresetData.CurrentVersion` を 31 → 32 に上げ、バージョン履歴コメントに「v32: effects.video を複数化（要素名は `video` のまま繰り返し）」を追記する

### MteEffectsSnapshot

- `CaptureVideo` → `CaptureVideos(data)`: `settingsList` を全件 `ScenePresetVideo` へ写す
- `ApplyVideo` → `ApplyVideos(data)`: `videos.Count == 0` なら触らない。`videoCount = Min(videos.Count, MaxVideoCount)` を設定してから各 index の設定へ写し、最後に `ReloadMovie()`（一括）を呼ぶ

## 4. テスト（`BgmVideoSettingsXmlTests`）

- `VideoSettings` ↔ `VideoSettingsXml` の往復で 18 項目が保持される
- `TimelineXml` に `videos` を 3 件入れて `TimelineData.FromXml` → `ToXml` で 3 件が順序どおり保持される
- 旧形式（`videos` 空 + フラット `Video*` 項目）を `Initialize()` すると 1 件として取り込まれる。`version < 4` のときの `videoDisplayOnGUI` 変換も含めて確認する
- `ScenePresetData` の XML 読込で、単体 `<video>` 要素 1 個が `videos` 1 件として読める
- 実機確認: 2 本を GUI と最背面で同時表示し、タイムライン再生でシーク・速度が両方に同期すること。動画数を減らした時に該当プレイヤーが消えること。タイムライン破棄後も本数と配置が維持されること

## 影響範囲

`Timeline/VideoSettings.cs`, `Timeline/TimelineXml.cs`, `Timeline/TimelineData.cs`, `Timeline/Manager/MovieManager.cs`, `Timeline/MoviePlayerImpl.cs`, `VideoWindow.cs`, `VideoPreviewWindow.cs`, `ScenePresetData.cs`, `Manager/MteEffectsSnapshot.cs`, `COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs`

`TimelineIntegration` のマネージャ破棄順（MovieManager → CameraManager）は変更しない。COM3D2 / COM3D25 の両構成でビルドする。

## 対象外

- 動画ごとの z オーダー指定
- プレビューウィンドウでの複数本同時表示
- 動画ごとのタイムライン上の個別開始/停止キーフレーム
