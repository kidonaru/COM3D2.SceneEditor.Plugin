# サウンド／ライブ演出ウィンドウ追加とC分類レイヤーUI委譲 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** サウンドウィンドウ（ボイス/効果音）とライブ演出ウィンドウ（ステージライト/レーザー/サイリウム）を新設し、対応する 5 つの TimelineLayer の編集 UI を PngPlacement 方式（案内ラベル委譲）へ移行する。

**Architecture:** 既存の EditorSubWindow 基盤に 2 ウィンドウを追加（登録は csproj / Config / WindowManager / MenuBarWindow の 4 箇所）。レイヤーの `DrawWindow` 内 UI コードは GUIView がウィンドウ側と同一クラスのためほぼ verbatim 移設できる。SE（効果音）だけはレイヤー private フィールドが再生状態を握っているため、先に `TimelineSeManager` へ状態と再生ロジックを移管してから委譲する。

**Tech Stack:** C# (.NET Framework / Unity IMGUI), MSBuild, COM3D2/COM3D25 の 2 構成ビルド

**Spec:** `docs/layer-window-duplication-survey.md`（C 分類のうちサウンド系・ライブ演出系を委譲対象とする。ユーザー指示: 「Cの対応。サウンドウィンドウとライブ演出ウィンドウを追加して移動させる」）

## Global Constraints

- ビルド確認は `debug.bat all`（COM3D2 / COM3D25 の**両構成**を必ずビルドする）。ゲーム停止中の実行は実機へ DLL が反映される点に留意
- `deploy.bat` / `release.bat` は絶対に実行しない
- コメント・ログメッセージは日本語
- 新規 WINDOW_ID: サウンド = `8903392`、ライブ演出 = `8903393`（予約帯 8903349-8903377 と既存最大 8903391 を回避）
- SE 側ウィンドウから Timeline (MTE) 側へのアクセスは `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` alias 経由（MaidFingerWindow.cs:156-167 / TimelineControlWindow.cs:34 が先例）
- レイヤー側の委譲後 DrawWindow は PngPlacementTimelineLayer.cs:201-206 と同形式（コメント 2 行＋`view.DrawLabel("...ウィンドウで行ってください", -1, 20)`）
- 対象外: BGM（既存 BgmWindow のまま）、テキスト / ポストエフェクト / アニメブレンド / 衣装 / MTE 固有オブジェクトの管理タブ以外の C 分類
- **本計画の行番号は執筆時点の参考値**。移設・削除時は行番号だけを頼りに機械的に抜き出さず、必ず Read でメソッド境界を特定してから作業する

## 対象ファイル一覧

- Create: `source/COM3D2.SceneEditor.Plugin/SoundWindow.cs`（ボイス/効果音タブ）
- Create: `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs`（ライト/レーザー/サイリウムタブ）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（Compile Include 追加、アルファベット順）
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（配置・可視の永続化フィールド）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`（AddWindow。コンボ/色欄を持つため ComboBoxPopupWindow / ColorPickerWindow の**登録より前**に置く。同ファイル 71-75 行のコメント参照）
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`（Window メニューへ項目追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineSeManager.cs`（SE 再生状態・間欠再生の移管先）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/VoiceTimelineLayer.cs`（案内ラベル化）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs`（状態参照の付け替え→案内ラベル化）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLightTimelineLayer.cs`（案内ラベル化）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLaserTimelineLayer.cs`（案内ラベル化）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs`（案内ラベル化）
- Modify: `docs/layer-window-duplication-survey.md`（対応状況の追記）

テストプロジェクトは無いため、各タスクの検証は「両構成ビルド成功」＋最終タスクでの実機確認（ユーザー依頼時）とする。

---

### Task 1: TimelineSeManager へ SE 再生状態を移管

SeTimelineLayer の private フィールド `_currentSeName` / `_currentInterval` / `_currentIsLoop`（SeTimelineLayer.cs:84-87）と、間欠再生ロジック（`PlaySe(fileName, interval, isLoop)` と `_currentTime` を使う繰り返し処理）を `TimelineSeManager` へ移す。ウィンドウ（Task 2）とレイヤー（キーフレーム書き込み `UpdateFrame` :122-128、再生適用 `ApplyMotion` :76-82）が同じ状態を共有するための前提。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineSeManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs`

**Interfaces:**
- Produces: `TimelineSeManager.instance.currentSeName : string`（初期値 ""）、`currentInterval : float`（初期値 0f）、`currentIsLoop : bool`（初期値 false）、`PlaySe(string fileName, float interval, bool isLoop)`（間欠再生対応版）、既存 `StopSe()` / `seNames`

- [ ] **Step 1: ManagerBase の Update フックを確認**

`ManagerBase` に virtual な `Update()`（`IManager` 契約）があることを確認する。`TimelineSeManager` は `TimelineIntegration.cs:38-44` の `TimelineUpdateManager._managers` に列挙済みで、`UpdateGuards()`（同 :67-84、studioHack 有効 → メイド存在 → `timelineManager.IsValidData()`）を通過したフレームで `Update()` が呼ばれる。**間欠再生はマネージャー駆動に確定する**（レイヤー駆動のままだと、SE レイヤーがタイムラインに追加されていない状態でウィンドウから再生した場合に繰り返しが止まるため）。ウィンドウの SE タブはタイムラインロード時のみ有効化する（Task 2）ので、ガード条件と利用可能条件は一致する。

- [ ] **Step 2: TimelineSeManager へ状態とロジックを移設**

`SeTimelineLayer.cs:84-120` の状態フィールドと `PlaySe` / `UpdateSe` を `TimelineSeManager` へ移設する:

```csharp
        // 現在編集・再生中の SE 状態。ウィンドウとレイヤー (キー書き込み) が共有する
        public string currentSeName = "";
        public float currentInterval = 0f;
        public bool currentIsLoop = false;
        private float _currentTime = 0f;

        /// <summary>SE を再生し、間欠再生用の現在状態を更新する</summary>
        public void PlaySe(string fileName, float interval, bool isLoop)
        {
            if (fileName == "")
            {
                StopSe();
            }
            else
            {
                PlaySe(fileName, isLoop);
            }

            currentSeName = fileName;
            currentInterval = interval;
            currentIsLoop = isLoop;
            _currentTime = 0f;
        }

        /// <summary>再生間隔が指定されている非ループ SE を一定間隔で鳴らし直す</summary>
        public override void Update()
        {
            base.Update();

            if (currentSeName == "" || currentInterval <= 0f || currentIsLoop)
            {
                return;
            }

            _currentTime += UnityEngine.Time.deltaTime;
            if (_currentTime >= currentInterval)
            {
                PlaySe(currentSeName, currentIsLoop);
                _currentTime = 0f;
            }
        }
```

既存の `PlaySe(string, bool)`（引数 2 個）はそのまま残す。引数の個数が違うためオーバーロード解決の曖昧さは生じないが、念のため既存呼び出し元を `grep -rn "PlaySe(" source/` で確認する。

- [ ] **Step 3: SeTimelineLayer の参照を付け替える**

レイヤーの `_currentSeName/_currentInterval/_currentIsLoop/_currentTime`・`PlaySe`・`UpdateSe` を削除し、`LateUpdate()`（:63-73）の `UpdateSe()` 呼び出しも削除（manager の `Update()` が代替）。`ApplyMotion`（:75-82）は `seManager.PlaySe(start.fileName, start.interval, start.isLoop)`、`UpdateFrame`（:122-128）は `seManager.currentSeName` 等を読む形へ付け替える。`DrawSeControl` の参照も同様に置換する（このタスクではレイヤー UI はまだ残す。UI の移設・削除は Task 2〜3 で行う）。

- [ ] **Step 4: 両構成ビルド**

Run: `debug.bat all`
Expected: COM3D2 / COM3D25 とも Build succeeded

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineSeManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs
git commit -m "refactor(timeline): 効果音の再生状態をTimelineSeManagerへ移管する"
```

---

### Task 2: SoundWindow 新設（ボイス/効果音タブ）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/SoundWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`

**Interfaces:**
- Consumes: Task 1 の `TimelineSeManager.instance.currentSeName/currentInterval/currentIsLoop/PlaySe/StopSe/seNames`、`MTEP.MaidManager.instance.GetMaidCache(Maid)`、`MTEP.StudioHackManager.instance.isPoseEditing`、`MTEP.ConfigManager.instance.config.voiceMaxLength`（Timeline/Config.cs:75）、`MTEP.TimelineManager.instance.timeline.additionalSeNames`
- Produces: `SoundWindow.instance`（WINDOW_ID = 8903392、タイトル「サウンド」）

- [ ] **Step 1: Config へ配置フィールドを追加**

`Config.cs` の既存ウィンドウ群（例: timelineControl 一式 :195-199）の並びに追加:

```csharp
        // サウンドウィンドウ
        public int soundPosX = -1;
        public int soundPosY = -1;
        public int soundWidth = 400;
        public int soundHeight = 320;
        public bool soundVisible = false;
```

- [ ] **Step 2: SoundWindow.cs を作成**

`MaidWindowBase` を継承する（ボイスタブが対象メイド選択行を必要とするため。効果音タブでも選択行は表示されたままだが、TimelineLayerWindow のヘッダーも同方式のため許容）。骨格は BgmWindow.cs:1-70 の singleton / LoadPlacement / StorePlacement / savedVisible パターンに従い、`DrawMaidContent(Maid target)` を実装:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボイス・効果音の編集ウィンドウ。
    /// タイムラインのメイドボイス/効果音レイヤーから編集 UI を委譲された受け皿。
    /// 値はライブ状態 (maidCache / TimelineSeManager) を直接編集するため、
    /// キーフレーム登録はタイムライン操作ウィンドウ側で行えばそのままキー化される
    /// </summary>
    public class SoundWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903392;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "サウンド";

        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.TimelineSeManager seManager => MTEP.TimelineSeManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private enum TabType
        {
            ボイス,
            効果音,
        }

        private TabType _tabType = TabType.ボイス;
        // (singleton / placement / savedVisible は BgmWindow と同形のため省略せず実装する)
```

- [ ] **Step 3: ボイスタブを移設**

`VoiceTimelineLayer.DrawWindow`（VoiceTimelineLayer.cs:105-186）の中身を `DrawVoice(GUIView view, Maid target)` として verbatim 移設。冒頭ガードは MaidFaceWindow.cs:430-432 の先例に従う:

```csharp
            var timeline = MTEP.TimelineManager.instance.timeline;
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (timeline == null || maidCache == null)
            {
                view.DrawLabel("タイムラインとメイドの読込後に使用できます", -1, 20);
                return;
            }
```

機械的な書き換え:
- `maidCache` → 上記のローカル変数（対象メイド由来）
- `config.voiceMaxLength` → `timelineConfig.voiceMaxLength`
- `studioHackManager.isPoseEditing` ガードはそのまま（上記ショートカット経由）

- [ ] **Step 4: 効果音タブを移設**

`SeTimelineLayer` の `DrawSeControl`（:166-224）と `DrawSeManage`（:227-267）、コンボ `_seNameComboBox`（:135-138）、SE 名一覧構築 `UpdateSeNames`（:41-46 相当。`timeline.additionalSeNames + seManager.seNames` の連結）、`_additionalSeName` と `AddSe` 相当（:261 付近）をウィンドウへ移設。サブタブは `操作` / `管理`。書き換え:
- タブ描画の冒頭で `var timeline = MTEP.TimelineManager.instance.timeline;` を取得し、**`timeline == null` なら「タイムライン読込後に使用できます」ラベルを出して return**（間欠再生の駆動元 `TimelineSeManager.Update` もタイムライン有効時のみ回るため、利用可能条件を揃える）
- `_currentSeName/_currentInterval/_currentIsLoop` → `seManager.currentSeName` 等（Task 1 の成果）
- 再生ボタンは `seManager.StopSe(); seManager.PlaySe(seManager.currentSeName, seManager.currentInterval, seManager.currentIsLoop);`

- [ ] **Step 5: DrawMaidContent とコンボフォーカス処理**

`DrawMaidContent(Maid target)` で `_tabType = view.DrawTabs(_tabType, 50, 20);` により 2 タブを切り替える。コンボのポップアップ処理（`ComboBoxPopupWindow.instance.ProcessFocus`）は MaidWindowBase 側の実装を確認し、基底が呼んでいなければ LightWindow.cs:111-137 と同じ `_rootView` 構成で自前実装する。

- [ ] **Step 6: 登録 4 点セット**

- csproj: `<Compile Include="SoundWindow.cs" />` をアルファベット順の位置へ
- `WindowManager.cs`: `AddWindow(SoundWindow.instance);` を ComboBoxPopupWindow 登録より前へ
- `MenuBarWindow.cs`: Window メニュー（:116-141）の `CreateWindowItem("BGM", BgmWindow.instance)`（:127）の隣に `CreateWindowItem("サウンド", SoundWindow.instance),`

- [ ] **Step 7: 両構成ビルド**

Run: `debug.bat all`
Expected: 両構成 Build succeeded

- [ ] **Step 8: Commit**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "feat(sound): ボイスと効果音を編集するサウンドウィンドウを追加する"
```

---

### Task 3: ボイス/効果音レイヤーを案内ラベル化

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/VoiceTimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs`

**Interfaces:**
- Consumes: Task 2 完了（受け皿が存在すること）
- Produces: 両レイヤーの `DrawWindow` が案内ラベルのみ

- [ ] **Step 1: VoiceTimelineLayer.DrawWindow を差し替え**

```csharp
        public override void DrawWindow(GUIView view)
        {
            // ボイスの編集 UI は SE のサウンドウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("ボイスの編集はサウンドウィンドウで行ってください", -1, 20);
        }
```

- [ ] **Step 2: SeTimelineLayer.DrawWindow を差し替え**

同形式でラベルは「効果音の編集はサウンドウィンドウで行ってください」。移設済みの `DrawSeControl` / `DrawSeManage` / `_seNameComboBox` / `_additionalSeName` / `AddSe` / `TabType` / `_seNames`・`UpdateSeNames`（レイヤー側で他に使っていなければ）を削除。`UpdateFrame` / `ApplyMotion` / 再生処理は残す。

- [ ] **Step 3: 両構成ビルド**

Run: `debug.bat all`
Expected: 両構成 Build succeeded

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/VoiceTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs
git commit -m "refactor(timeline): ボイスと効果音レイヤーの編集UIをサウンドウィンドウへ委譲する"
```

---

### Task 4: LiveEffectWindow 新設＋ステージライトタブ移設

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs`
- Modify: csproj / `Config.cs` / `Manager/WindowManager.cs` / `MenuBarWindow.cs`（Task 2 と同じ 4 点セット）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLightTimelineLayer.cs`

**Interfaces:**
- Consumes: `MTEP.StageLightManager.instance`（controllers/lights/Add・Remove、ManagerBase.cs:32-35 参照）、`MTEP.TimelineBundleManager.instance.IsValid()`
- Produces: `LiveEffectWindow.instance`（WINDOW_ID = 8903393、タイトル「ライブ演出」）、トップタブ `enum TopTab { ライト, レーザー, サイリウム }`、タブごとの描画メソッド `DrawStageLight(GUIView view)`（Task 5, 6 が `DrawStageLaser` / `DrawPsyllium` を同名規約で追加）

- [ ] **Step 1: Config 追加**

```csharp
        // ライブ演出ウィンドウ
        public int liveEffectPosX = -1;
        public int liveEffectPosY = -1;
        public int liveEffectWidth = 440;
        public int liveEffectHeight = 600;
        public bool liveEffectVisible = false;
```

- [ ] **Step 2: LiveEffectWindow.cs を作成**

`EditorSubWindow` 直接継承（メイド非依存）。BgmWindow の骨格＋LightWindow.cs:111-137 の `_rootView` / `ComboBoxPopupWindow.instance.ProcessFocus` パターン。`DrawContent()` の冒頭ガード:

```csharp
            if (!MTEP.TimelineBundleManager.instance.IsValid())
            {
                // ステージ演出はタイムライン同梱のアセットバンドルが前提 (レイヤーの ValidateLayer と同等)
                _view.DrawLabel("アセットバンドル未読込のため使用できません", -1, 20);
                return;
            }

            if (MTEP.TimelineManager.instance.timeline == null)
            {
                // ステージ演出オブジェクトはタイムライン文脈でのみ生成・更新される
                // (各マネージャの Update は TimelineIntegration.UpdateGuards 通過時のみ回る)
                _view.DrawLabel("タイムライン読込後に使用できます", -1, 20);
                return;
            }
```

ショートカット:

```csharp
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.StageLightManager stageLightManager => MTEP.StageLightManager.instance;
        private static MTEP.StageLaserManager stageLaserManager => MTEP.StageLaserManager.instance;
        private static MTEP.PsylliumManager psylliumManager => MTEP.PsylliumManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;
```

- [ ] **Step 3: ステージライト UI を移設**

`StageLightTimelineLayer` から `DrawStageLightControllEdit`（:512-789）/ `DrawStageLightEdit`（:791-975）、コンボ・色キャッシュ（`_controllerComboBox`/`_lightComboBox`/`_copyToLightComboBox`/`_color1FieldValue`/`_color2FieldValue`, :455-480）、サブタブ `enum { 一括, 個別 }` を verbatim 移設し、`DrawStageLight(GUIView view)` でサブタブ切り替え。書き換えは Global Constraints の alias 経由参照のみ（`view.SetEnabled(view.focusedComboBox == null)` / `studioHackManager.isPoseEditing` ガードは原文のまま）。移設コードが `timeline` や `config` を参照している箇所は `MTEP.TimelineManager.instance.timeline`（null ガード必須）/ `timelineConfig` へ置換する。

- [ ] **Step 4: StageLightTimelineLayer を案内ラベル化**

DrawWindow を「ステージライトの編集はライブ演出ウィンドウで行ってください」ラベルへ差し替え、移設済み UI メソッド・コンボ・TabType を削除。`UpdateFrame`（:420-453）・イベント購読（Init :45-48）・`_allBoneNames` は残す。

- [ ] **Step 5: 登録 4 点セット**

Task 2 Step 6 と同様。メニューは Window メニューの「サウンド」の下に `CreateWindowItem("ライブ演出", LiveEffectWindow.instance),`。`AddWindow(LiveEffectWindow.instance);` は色欄を持つため ColorPickerWindow 登録より前。

- [ ] **Step 6: 両構成ビルド**

Run: `debug.bat all`
Expected: 両構成 Build succeeded

- [ ] **Step 7: Commit**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "feat(live-effect): ライブ演出ウィンドウを追加しステージライト編集を委譲する"
```

---

### Task 5: ステージレーザータブ移設

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLaserTimelineLayer.cs`

**Interfaces:**
- Consumes: Task 4 の `LiveEffectWindow` トップタブ構造と `stageLaserManager` ショートカット
- Produces: `DrawStageLaser(GUIView view)`

- [ ] **Step 1: レーザー UI を移設**

`StageLaserTimelineLayer` から `DrawStageLaserControllEdit`（:511-813）/ `DrawStageLaserEdit`（:815-1002）、コンボ（:446-477）、サブタブ enum を Task 4 Step 3 と同じ書き換え規則で移設。

- [ ] **Step 2: StageLaserTimelineLayer を案内ラベル化**

ラベル「ステージレーザーの編集はライブ演出ウィンドウで行ってください」。`UpdateFrame`（:411-444）・イベント購読は残す。

- [ ] **Step 3: 両構成ビルド**

Run: `debug.bat all`
Expected: 両構成 Build succeeded

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLaserTimelineLayer.cs
git commit -m "refactor(timeline): ステージレーザーレイヤーの編集UIをライブ演出ウィンドウへ委譲する"
```

---

### Task 6: サイリウムタブ移設

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs`

**Interfaces:**
- Consumes: Task 4 のトップタブ構造と `psylliumManager` ショートカット
- Produces: `DrawPsyllium(GUIView view)`

- [ ] **Step 1: サイリウム UI を移設**

`PsylliumTimelineLayer` から 5 サブタブ（`基本, バー, 持ち手, アニメ, エリア`）の描画一式を移設: `DrawPsylliumControllEdit`（:853-974）/ `DrawPsylliumBarConfigEdit`（:976-1098）/ `DrawPsylliumHandConfigEdit`（:1100-1185、`_handTabType` サブタブ :820 含む）/ `DrawPsylliumPatternConfigEdit`（:1187-1357、内部呼び出しの `DrawPsylliumTransformConfigEdit` :1359-1490 含む）/ `DrawPsylliumAreaEdit`（:1492-1799）、コンボ 7 種（:740-795）。書き換え規則は Task 4 Step 3 と同じ。

- [ ] **Step 2: PsylliumTimelineLayer を案内ラベル化**

ラベル「サイリウムの編集はライブ演出ウィンドウで行ってください」。`UpdateFrame`（:667-738）・イベント購読は残す。

- [ ] **Step 3: 両構成ビルド**

Run: `debug.bat all`
Expected: 両構成 Build succeeded

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs
git commit -m "refactor(timeline): サイリウムレイヤーの編集UIをライブ演出ウィンドウへ委譲する"
```

---

### Task 7: 調査ドキュメント更新

**Files:**
- Modify: `docs/layer-window-duplication-survey.md`

- [ ] **Step 1: C 分類表を更新**

`docs/layer-window-duplication-survey.md` の C 分類（:54-66）で VoiceTimelineLayer / SeTimelineLayer / StageLight / StageLaser / Psyllium の行に「**対応済み**（サウンドウィンドウ or ライブ演出ウィンドウを新設して委譲）」を追記し、冒頭に対応状況の日付付き注記（先例: :22, :40 の形式）を追加する。効果音の状態移管（TimelineSeManager）も実装知見として「削除方針の示唆」節へ 1 行追記する。

- [ ] **Step 2: Commit**

```bash
git add docs/layer-window-duplication-survey.md
git commit -m "docs: サウンド・ライブ演出ウィンドウへの委譲対応状況を調査ドキュメントへ反映する"
```

---

## 検証メモ（実機確認の観点）

ビルドのみでは UI 動作を保証できないため、実装完了後にユーザー実機（または devbridge 稼働中なら screenshot / eval_csharp）で以下を確認する:

1. メニューバー Window メニューに「サウンド」「ライブ演出」が出る・開閉できる・配置が保存される
2. サウンド: ボイス再生（メイド選択反映）、SE 再生・間欠再生、SE 追加（タイムラインロード時のみ）、タイムライン未ロード時に管理タブが安全に無効化される
3. ライブ演出: バンドル未読込時のガードラベル、ライト/レーザー/サイリウムの追加・編集がキーフレーム登録（TimelineControlWindow）でキー化される
4. 各レイヤーのレイヤー編集ウィンドウ表示が案内ラベルになっている

## リスク

- **SE 間欠再生の移管**（Task 1）: マネージャー駆動（`TimelineSeManager.Update`、ガードは TimelineIntegration.cs:67-84）に確定。ガード条件とウィンドウの利用可能条件（`timeline != null`）を揃えているが、実機で「間隔指定の非ループ SE が繰り返し鳴る」ことを必ず確認する
- **タイムライン未ロード時の新ウィンドウ**: レイヤー UI は常にタイムライン文脈で開かれていたが、ウィンドウは単独で開ける。両ウィンドウとも冒頭で `timeline == null` ガードを敷いたうえで、移設コードの `timeline` / `maidCache` 参照を全数チェックする
- **静的タブ状態**: レイヤーの `_tabType` は static だった。ウィンドウ側ではインスタンスフィールドにする（singleton なので実質同じだが、レイヤー側の削除漏れに注意）

## レビュー却下メモ

- ボイスの `hasSlotNo`（メイドごとレイヤーインスタンス）で単一ウィンドウ実装が代替になるか — 却下（旧 UI もレイヤー編集ウィンドウのメイド選択で 1 体ずつ編集する方式であり、キー書き込みは各レイヤーインスタンスが自スロットの `maidCache` ライブ値を読むため等価）
- SE 間欠再生中にウィンドウを閉じた場合の停止処理が未記載 — 却下（旧レイヤー UI でもレイヤー切替で再生は止めておらず、挙動を変えない。停止は「初期化」ボタン相当で可能）
