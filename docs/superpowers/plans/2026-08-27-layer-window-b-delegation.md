# B分類レイヤー編集UIの個別ウィンドウ委譲 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** B分類（部分被り）のタイムラインレイヤー編集UIを、個別ウィンドウへ不足機能を追加した上で案内ラベル委譲する（A分類と同じ PngPlacement 方式）。

**Architecture:** 各レイヤーの `DrawWindow` を案内ラベルへ置換する前に、個別ウィンドウ側へ欠落機能を移植する。キーフレーム書き込み（`UpdateFrame`）はいずれもライブ状態を読むため、個別ウィンドウでの編集はそのままキー化される。回転の「前キー連続角」処理はUI側から `UpdateFrame`（キー書き込み時）へ移して機能を保つ。

**Tech Stack:** C# / Unity IMGUI（GUIView）/ MTE タイムライン基盤

**Spec:** `docs/layer-window-duplication-survey.md`（B分類の被りマトリクスと差分）

## Global Constraints

- ビルドは COM3D2 / COM3D25 の両構成を必ず通す（メモリ `dual-gameversion-build`）。`debug.bat` はゲームフォルダへコピーするため使わず、MSBuild を直接叩く:
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- コメント・ログは日本語。
- 委譲ラベルの文言・形式は `PngPlacementTimelineLayer.cs:201-206` に合わせる（`view.DrawLabel("〜の編集は 〜ウィンドウで行ってください", -1, 20);`）。SE ではポップアップ描画をホスト側が行うため、委譲後のレイヤーは `view.DrawComboBox()` を呼ばない。
- 個別ウィンドウ側の新規編集操作は既存ウィンドウの流儀に合わせ `HistoryManager.BeforeEdit(...)` で履歴化する（CameraWindow は `HistoryScope.Camera`、LightWindow は `HistoryScope.Light` 等、各ウィンドウの既存呼び出しに倣う）。
- コミットは 1 タスク 1 コミット。メッセージは既存の委譲コミット（例: `719b367 refactor(timeline): メイド脱衣レイヤーの編集UIを脱衣ウィンドウへ委譲する`）に倣う。

## スコープ外（このプランで扱わないもの）と理由

- **SubCameraTimelineLayer**: 対応する個別ウィンドウが存在せず、委譲には新規ウィンドウの丸ごと開発と `SubCameraManager` のライフサイクル見直しが必要。被りが無いので現状維持。
- **MotionTimelineLayer の編集タブ（DrawTransformEdit）**: IK ホールド・接地が MTE 系（`Timeline/IKHoldEntity.cs`, `MaidCache`）と SE 系（`MaidManipulation/MaidIKHoldController.cs`）で完全に分離しておりブリッジが存在しない。委譲するとキーが打てなくなるため不可。将来ブリッジ層を作ってから再検討。
- **BGModelTimelineLayer / ModelTimelineLayer の管理タブ・操作タブ**: 個別ウィンドウに完全対応する機能が無く被りが薄い。現状維持。

---

### Task 1: CameraWindow にメイドフォーカス機能を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs`（Main タブ `DrawMainCameraContent` :353-379 の末尾に追加）
- 参照元: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs:253-292`

**Interfaces:**
- Consumes: `maidManager.maidCaches`（`MaidCache`）、`MaidCache.GetPointTransform(MaidPointType)`、`mainCamera.SetTargetPos(Vector3)`
- Produces: CameraWindow Main タブの「フォーカス」行（メイドコンボ＋ポイントコンボ＋「移動」ボタン）

- [ ] **Step 1: フォーカス UI を実装**

`CameraTimelineLayer.cs:264-292` の UI を CameraWindow へ移植する。レイヤー側は `GUIComboBox<MaidCache>` / `GUIComboBox<MaidPointType>` フィールドを持つ（:253-262 付近の初期化を参照）。CameraWindow の既存フィールド群の並びにコンボ 2 つを追加し、`DrawMainCameraContent` の FOV 行の後に描画する:

```csharp
// フォーカス対象メイドのコンボ（CameraTimelineLayer.cs:264-292 から移植）
private readonly GUIComboBox<MaidCache> _focusMaidComboBox = new GUIComboBox<MaidCache>
{
    getName = (cache, _) => cache == null ? "未選択" : cache.fullName,
};
private readonly GUIComboBox<MaidPointType> _focusPointComboBox = new GUIComboBox<MaidPointType>
{
    items = MTEUtils.GetEnumValues<MaidPointType>(),
    getName = (type, _) => MaidCache.GetMaidPointTypeName(type),
};
```

描画部（メイド選択時に「移動」ボタンでそのポイント位置を注視点にする）:

```csharp
view.DrawLabel("フォーカス", -1, 20);
view.BeginHorizontal();
{
    _focusMaidComboBox.items = maidManager.maidCaches;
    _focusMaidComboBox.DrawButton(view);
    _focusPointComboBox.DrawButton(view);

    var maidCache = _focusMaidComboBox.currentItem;
    if (view.DrawButton("移動", 60, 20, maidCache != null))
    {
        HistoryManager.instance.BeforeEdit(HistoryScope.Camera);
        var point = maidCache.GetPointTransform(_focusPointComboBox.currentItem);
        mainCamera.SetTargetPos(point.position);
    }
}
view.EndHorizontal();
```

※ コンボの正確なフィールド名・getName 実装・コンボ描画 API（`DrawButton` か `DrawComboBox` か）は CameraWindow 既存のコンボ実装と CameraTimelineLayer.cs:253-292 の実物に合わせること。SE のウィンドウはホスト側でポップアップ描画するため、レイヤーではなく**ウィンドウ既存のコンボ利用箇所**（例: BackgroundWindow のカテゴリコンボ :208-226）の呼び方に倣う。

- [ ] **Step 2: 両構成でビルド確認**

Global Constraints の MSBuild 2 コマンドを実行し、両方 0 エラーを確認。

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/CameraWindow.cs
git commit -m "feat(camera): カメラウィンドウにメイドフォーカス機能を追加する"
```

---

### Task 2: CameraTimelineLayer を委譲ラベル化（＋キー書き込み時の連続角化）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs`

**Interfaces:**
- Consumes: Task 1 の CameraWindow フォーカス行（機能欠損なく削除できる前提）
- Produces: `DrawWindow` は案内ラベルのみ。`UpdateFrame` で前フレームキーとの連続角化を行う

- [ ] **Step 1: UpdateFrame に連続角化を移す**

現状はレイヤー UI（`DrawWindow` :198 の `TransformDataBase.GetFixedEulerAngles(angles, prevAngles)`）が編集時に連続角化しており、UI 削除でこの機構が失われる。CameraWindow は ±180 正規化表示のため、委譲後は 360° 跨ぎでキー間補間が巻き戻る恐れがある。対策として `UpdateFrame`（:150-166）でキー角度を書き込む際、直前フレームのキー角度を基準に `GetFixedEulerAngles` を適用する:

```csharp
// カメラ回転は前キーからの連続角へ補正してキー化する（UI委譲に伴いUI側の補正を移設）
var angles = new Vector3(aroundAngle.y, aroundAngle.x, camera.GetRotationZ());
var prevBone = GetPrevBone(frame.frameNo, 対象ボーン名);
if (prevBone != null)
{
    angles = TransformDataBase.GetFixedEulerAngles(angles, prevBone.transform.eulerAngles);
}
```

※ `GetPrevBone` の正確なシグネチャと `UpdateFrame` 内の既存変数名は実物（:150-166）に合わせる。既存の euler 書き込み行を差し替える形にする。
※ `UpdateFrame` のシグネチャにある `initialEdit` / `force` フラグとの相互作用に注意: 前キーが無い（`GetPrevBone` が null）場合は連続角化をスキップして生値を使う（既存 `DrawWindow` :196-198 と同じ挙動）。強制上書き時も同様に prev 基準の補正が二重にかからないことを呼び出し元のフレーム確定ロジックで確認する。Task 7 の MoveTimelineLayer も同じ注意が当てはまる。

- [ ] **Step 2: DrawWindow を案内ラベル化**

`DrawWindow`（:187-300）本体を削除し、PngPlacement 方式へ:

```csharp
public override void DrawWindow(GUIView view)
{
    // レイヤーUI非接続方針: カメラ編集はカメラウィンドウへ委譲する
    view.DrawLabel("カメラの編集は カメラウィンドウで行ってください", -1, 20);
}
```

不要になった `GUIComboBox` フィールド（対象メイド/ポイント :253-292 相当）と関連ヘルパーも削除する（A分類コミット `9e47d6e` と同じ削り方）。

- [ ] **Step 3: 両構成でビルド確認**（Global Constraints のコマンド）

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs
git commit -m "refactor(timeline): カメラレイヤーの編集UIをカメラウィンドウへ委譲する"
```

---

### Task 3: LightWindow に不足機能を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightWindow.cs`
- 参照元: `Timeline/TimelineLayer/LightTimelineLayer.cs:312-513`、`Timeline/Manager/StudioLightManager.cs`（`FollowLight` の実体）

**Interfaces:**
- Consumes: `StudioLightManager`（SE側）のライト列、MTE 側 `Timeline/Manager/StudioLightManager` の `followLight`（`maidSlotNo` / `offset`）
- Produces: LightWindow に (1) 位置 XYZ 数値行、(2) ロール（Z回転）、(3) shadowBias、(4) 追加 Directional ライトの shadowStrength、(5) 追従メイド設定＋オフセット行

- [ ] **Step 1: 位置・ロール・影パラメータを追加**

LightWindow の追加ライト描画（:242-286）に以下を追加する。レイヤー側の種別別マスク（LightTimelineLayer.cs:358-371: Directional=回転のみ / Spot=位置+回転 / Point=位置のみ）に合わせて表示制御する:

```csharp
// 位置（数値直打ち。Directional 以外で表示）
if (light.type != LightType.Directional)
{
    DrawVector3Row(view, "位置", lightTransform.localPosition, v =>
    {
        HistoryManager.instance.BeforeEdit(HistoryScope.Light);
        lightTransform.localPosition = v;
    });
}
// ロール（Z回転。Point 以外で表示）
// 影距（shadowBias 0〜1。Directional のみ）
// 影濃（shadowStrength 0〜1。Directional のみ。メインライト専用だった DrawMainLightParams :224 から追加ライトへも展開）
```

数値行の部品は CameraWindow の `DrawVector3Row`（CameraWindow.cs:477）と同型の実装を LightWindow に用意するか共通化する。スライダー範囲はレイヤー側に合わせる（範囲 0-30 / 強度 0-5 / 角度 0-180 / 影 0-1、LightTimelineLayer.cs:410-468）。

メインライト（index 0）は直書きせず既存どおり `LightMain.SetIntensity/SetRotation/SetShadowStrength` 経由を維持する（LightWindow.cs:217-225 の設計意図）。追加ライトのみ直接 Transform / Light へ書く。

**実装前確認（レビュー指摘）:** レイヤー側 `DrawLightEdit` は index 0 も含め全ライトを `light`/`transform` へ直接編集しており、LightWindow の `LightMain` 経由方針と経路が異なる。`LightMain` の実装（逆コンパイル済み `W:\COM3D2_5\work\Assembly-CSharp\LightMain.cs` 等）を読み、`SetIntensity/SetRotation/SetShadowStrength` が実 `Light`/`Transform` へ即時反映されるか確認する。即時反映なら `UpdateFrame`（ライブ値読み）でそのままキー化されるので LightMain 経由で問題ない。中間状態を持ち即時反映されない場合は、メインライトのみキーが正しく打たれない不具合になるため、反映タイミングを揃える対応（適用後に light へ書き下ろす等）を入れる。

- [ ] **Step 2: 追従メイド設定を追加**

レイヤー側 :484-500 相当。MTE 側 `Timeline/Manager/StudioLightManager` の `followLight`（`maidSlotNo`）へ書く。メインライトへの追従は禁止（`RemoveMainLightFollow` :302-310 に倣い index 0 では非表示）:

```csharp
// 追従メイド（追加ライトかつ非Directionalのみ）
view.BeginHorizontal();
{
    var isFollow = followLight.maidSlotNo >= 0;
    var newFollow = view.DrawToggle("メイド追従", isFollow, 100, 20);
    // トグルON時: メイドコンボで maidSlotNo を選択
    // 追従中: オフセット XYZ 行（followLight.offset）を表示（レイヤー :380 相当）
}
view.EndHorizontal();
```

※ SE 側 LightWindow から MTE 側 `StudioLightManager.instance` の followLight にアクセスできるか実物で確認し、参照が取れない場合は followLight 状態を SE 側マネージャへ持ち上げるのではなく、MTE 側 manager の public API 経由で読み書きする。

- [ ] **Step 3: 両構成でビルド確認**

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/LightWindow.cs
git commit -m "feat(light): ライトウィンドウに位置・ロール・影・メイド追従の編集を追加する"
```

---

### Task 4: LightTimelineLayer の操作タブを委譲ラベル化

**Files:**
- Modify: `Timeline/TimelineLayer/LightTimelineLayer.cs`（`DrawLightEdit` :312-513）
- Modify: `Timeline/TimelineLayer/LightTimelineLayerBase.cs`（管理タブは残す）
- Modify: `Timeline/Manager/StudioLightManager.cs`（visible 同期）

**Interfaces:**
- Consumes: Task 3 の LightWindow 拡張
- Produces: 操作タブは案内ラベルのみ。管理タブ（タイムライン設定トグル 3 種 + 可視/削除、Base :11-59）は残置

- [ ] **Step 1: visible のキー化経路を修正**

`StudioLightStat.visible` は `BuildLightList` 再構築時にしか `light.enabled` から同期されない（LateUpdate 最短 30 フレーム間隔 :104）ため、LightWindow の「有効」トグル（`light.enabled` 直書き :257）が即キー化されない。`LightTimelineLayer.UpdateFrame`（:237-265）で `stat.visible` の代わりに `stat.light.enabled` を読むよう変更する（ライブ状態直読みに統一）。変更前に `stat.visible` の他の参照箇所（`BuildLightList` 以外の用途）を grep で洗い出し、乖離しても問題ないことを確認する。

- [ ] **Step 2: DrawLightEdit を案内ラベル化**

```csharp
private void DrawLightEdit(GUIView view)
{
    // レイヤーUI非接続方針: ライト編集はライトウィンドウへ委譲する
    view.DrawLabel("ライトの編集は ライトウィンドウで行ってください", -1, 20);
}
```

不要になったコンボ・カラーフィールド等のフィールドを削除。管理タブ（`DrawLightManage`、Base :11-59）はタイムライン設定トグル（`isLightColorEasing` 等）がタイムライン固有のため**そのまま残す**。

- [ ] **Step 3: 両構成でビルド確認**

- [ ] **Step 4: コミット**

```bash
git add Timeline/TimelineLayer/LightTimelineLayer.cs Timeline/TimelineLayer/LightTimelineLayerBase.cs Timeline/Manager/StudioLightManager.cs
git commit -m "refactor(timeline): ライトレイヤーの操作UIをライトウィンドウへ委譲する"
```

（パスはリポジトリ実体に合わせて `source/COM3D2.SceneEditor.Plugin/` プレフィックスを付ける）

---

### Task 5: BackgroundWindow に背景 Transform 編集を追加し BGTimelineLayer を委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs`
- Modify: `Timeline/TimelineLayer/BGTimelineLayer.cs`

**Interfaces:**
- Consumes: `bgMgr.current_bg_object.transform`（**レイヤーのキー化対象と同じ Transform**。`bgMgr.Parent` ではない点が重要 — BGTimelineLayer.UpdateFrame :130-144 は current_bg_object の local 値を読む）
- Produces: BackgroundWindow に背景の位置/回転/スケール編集行（local 基準、リセットボタン付き）

- [ ] **Step 1: Transform 編集 UI を追加**

BackgroundWindow の背景一覧の上（「背景を選択」:169 付近）に、背景存在時のみ表示する Transform 行を追加。編集対象は `bgMgr.current_bg_object.transform` の localPosition / localEulerAngles / localScale（初期値 0/0/1、BGTimelineLayer.cs:185-193 と同じ）:

```csharp
// 背景Transform編集（タイムラインの背景レイヤーと同じ current_bg_object の local 値を編集する）
var bgObject = bgMgr.current_bg_object;
if (bgObject != null)
{
    var t = bgObject.transform;
    DrawVector3Row(view, "位置", t.localPosition, v => { HistoryManager.instance.BeforeEdit(HistoryScope.Object); t.localPosition = v; });
    DrawVector3Row(view, "回転", t.localEulerAngles, v => { ... t.localEulerAngles = v; });
    DrawVector3Row(view, "拡縮", t.localScale, v => { ... t.localScale = v; });
    if (view.DrawButton("リセット", 80, 20)) { ... pos=0, euler=0, scale=1 }
}
```

既存 Inspector / `BackgroundSnapshot` は `bgMgr.Parent` の world 値を扱っており別系統（BackgroundSnapshot.cs:60-69 は position/rotation のみ）。今回の行は current_bg_object 側であることをコメントで明記し、UI 上もセクションラベルを「背景Transform (ローカル)」等にして Inspector の Parent 行（world 値）と混同しないようにする。

- [ ] **Step 2: BGTimelineLayer.DrawWindow を委譲ラベル化**

背景コンボ＋Transform 編集（:162-194）を案内ラベルへ。背景選択は BackgroundWindow の一覧が既に同等（`bgData.Apply()`）で、レイヤーは `Update` :66-82 で背景名変化を検知して追従するため機能欠損なし:

```csharp
public override void DrawWindow(GUIView view)
{
    // レイヤーUI非接続方針: 背景の選択とTransform編集は背景ウィンドウへ委譲する
    view.DrawLabel("背景の編集は 背景ウィンドウで行ってください", -1, 20);
}
```

`_bgComboBox` フィールド等を削除。

- [ ] **Step 3: 両構成でビルド確認**

- [ ] **Step 4: コミット**

```bash
git commit -m "refactor(timeline): 背景レイヤーの編集UIを背景ウィンドウへ委譲する"
```

---

### Task 6: 背景色・地面編集を BackgroundWindow へ集約し BGColorTimelineLayer を委譲

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/BGGroundManager.cs`（`BGGround` の所有をレイヤーから持ち上げ）
- Modify: `source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs`
- Modify: `Timeline/TimelineLayer/BGColorTimelineLayer.cs`

**Interfaces:**
- Consumes: `BGColorTimelineLayer` が生成していた `bgGround`（:15, Init :45-50）
- Produces: `BGGroundManager.instance.bgGround`（visible / color / position / scale を持つ既存 `BGGround` 型）。レイヤーとウィンドウ双方がこれを参照する

- [ ] **Step 1: BGGround をマネージャへ持ち上げる**

`BGColorTimelineLayer` のインスタンスフィールド `bgGround`（:15）を新設シングルトン `BGGroundManager` へ移す。生成タイミングはレイヤー Init（:45-50）の処理を `BGGroundManager.instance.GetOrCreate()` に置換し、レイヤー破棄で消さない（ウィンドウからレイヤー非依存で編集可能にするため）。タイムライン未使用時にも生成されないよう遅延生成にする:

```csharp
// 地面オブジェクトの所有をレイヤーから持ち上げる（ウィンドウ委譲のため）
public class BGGroundManager
{
    private static BGGroundManager _instance;
    public static BGGroundManager instance => _instance ?? (_instance = new BGGroundManager());
    private BGGround _bgGround;
    public BGGround GetOrCreate() { ... 既存 Init :45-50 の生成処理を移設 ... }
    public BGGround bgGroundOrNull => _bgGround;
    public void Release() { ... プラグイン無効化時の破棄（StudioLightManager の OnPluginDisable パターンに倣う） ... }
}
```

※ シングルトン化の妥当性: `BGColorTimelineLayer.Create(int slotNo)` は現状 `new BGColorTimelineLayer(0)` 固定（:36-39）で地面は実質 1 インスタンス運用のため、グローバルシングルトンで挙動は変わらない。将来マルチスロット対応する場合はこのマネージャをスロット別に拡張する。

- [ ] **Step 2: BackgroundWindow に背景色＋地面 UI を追加**

- 背景色行（`DrawBgColorRow` :181-193）を**背景の有無に関わらず**表示するよう、:145-153 の early return 経路から通常経路へ移す。
- 地面 UI（表示トグル / 色 / 位置 XYZ / スケール SX,SZ 0〜1000）を BGColorTimelineLayer.cs:214-274 から移植し、`BGGroundManager` 経由で読み書きする。

- [ ] **Step 3: BGColorTimelineLayer.DrawWindow を委譲ラベル化**

`UpdateFrame`（:160-174）は `camera.backgroundColor` と `bgGround`（→ `BGGroundManager` 参照へ変更）のライブ値を読むため、ウィンドウ編集はそのままキー化される:

```csharp
public override void DrawWindow(GUIView view)
{
    // レイヤーUI非接続方針: 背景色・地面の編集は背景ウィンドウへ委譲する
    view.DrawLabel("背景色・地面の編集は 背景ウィンドウで行ってください", -1, 20);
}
```

- [ ] **Step 4: 両構成でビルド確認**

- [ ] **Step 5: コミット**

```bash
git commit -m "refactor(timeline): 背景色レイヤーの編集UIを背景ウィンドウへ委譲する"
```

---

### Task 7: MoveTimelineLayer を委譲ラベル化

**Files:**
- Modify: `Timeline/TimelineLayer/MoveTimelineLayer.cs`

**Interfaces:**
- Consumes: 既存の InspectorWindow メイド Transform 行（InspectorWindow.cs:204-243）とギズモ（GizmoRenderer.cs:245-286）— どちらも `maid.transform` を直接編集済みで、`UpdateFrame`（:120-136）はそのライブ値を読む
- Produces: `DrawWindow` は案内ラベルのみ。回転の連続角化は `UpdateFrame` 側で実施

- [ ] **Step 1: UpdateFrame に連続角化を追加**

Task 2 と同じ方式。`maid.transform.localEulerAngles` をキー化する箇所で、前フレームキーの角度に対し `TransformDataBase.GetFixedEulerAngles` を適用する。

- [ ] **Step 2: DrawWindow を案内ラベル化**

```csharp
public override void DrawWindow(GUIView view)
{
    // レイヤーUI非接続方針: メイド本体のTransform編集はインスペクタ/ギズモへ委譲する
    view.DrawLabel("メイドの移動はギズモ、またはインスペクタで編集してください", -1, 20);
    view.DrawLabel("※キーの値はローカル座標で記録されます", -1, 20);
}
```

Inspector の Local/Global 切替（InspectorWindow.cs:214）で Global 表示中は数値とキー値が食い違うため、2 行目の注記を入れる。

- [ ] **Step 3: 両構成でビルド確認**

- [ ] **Step 4: コミット**

```bash
git commit -m "refactor(timeline): メイド移動レイヤーの編集UIをインスペクタ/ギズモへ委譲する"
```

---

### Task 8: MaidFaceWindow 視線タブに瞳回転・メイド注視先を追加し EyesTimelineLayer 視線タブを委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs`（`DrawLookContent` :307-385）
- Modify: `Timeline/TimelineLayer/EyesTimelineLayer.cs`（視線タブ `DrawEyesLookAt` :391-473 のみ。位置タブ :475-526 は残す）

**Interfaces:**
- Consumes: `maidCache.lookAtTargetType / lookAtTargetIndex / lookAtMaidPointType / eyeEulerAngle`（EyesTimelineLayer.UpdateFrame :259-300 が読む唯一のソース）
- Produces: MaidFaceWindow 視線タブに (1) 瞳回転 水平/垂直スライダー、(2) 注視先メイド＋ポイント指定、(3) 初期化ボタン

- [ ] **Step 1: 視線タブへタイムライン連携セクションを追加**

MaidFaceWindow の視線タブ末尾に「タイムライン視線」セクションを追加する。**書き込み先は `maidCache`**（`MaidLookController` ではない — ウィンドウ既存の視線 UI は `MaidLookController` 系でキー化されないため、キー化される経路をここで提供する）:

```csharp
// タイムライン視線（キーフレーム化される視線設定。maidCache へ書き込む）
var maidCache = maidManager.GetMaidCache(target);  // 実際の取得APIは実物に合わせる
if (maidCache != null)
{
    view.DrawLabel("タイムライン視線", -1, 20);
    // 注視先コンボ（EyesTimelineLayer.cs:409-417 相当。Model は除外）
    // Maid 選択時: メイドコンボ（lookAtTargetIndex）＋ポイントコンボ（lookAtMaidPointType）:422-430
    // 瞳回転 水平/垂直 スライダー -1〜1 step0.01（:461-469。eyeEulerAngle = (h*90, 0, v*90)）
    // 「初期化」ボタン: EyesRot=0 かつ lookAtTargetType=None（:452-456）
    // timeline.useHeadKey が無効なら案内ラベルを出して編集不可にする（:399-403 のガード移植）
    // ※注意: MaidCache.useHeadKey（MaidCache.cs:188-191）は trsLookTarget を書き換える副作用付きの別物。
    //   ガード条件は必ず TimelineData 側の timeline.useHeadKey（TimelineSettingWindow.cs:258 のトグル）を参照する
}
```

`MaidLookController` と `MaidCache.UpdateLookAtTarget()`（MaidCache.cs:608-618）は `body0.trsLookTarget` を奪い合う既存の競合があるが、これは現状でも存在する挙動のため本タスクでは触らない（両セクションが同居する旨のコメントを残す）。

2D ドラッグ画像 UI（:444-447）は移植せずスライダーのみとする（機能等価で、SE ウィンドウに同型部品が無いため）。

- [ ] **Step 2: EyesTimelineLayer の視線タブを委譲ラベル化**

```csharp
private void DrawEyesLookAt(GUIView view)
{
    // レイヤーUI非接続方針: 視線編集は表情ウィンドウの視線タブへ委譲する
    view.DrawLabel("視線の編集は 表情ウィンドウの視線タブで行ってください", -1, 20);
}
```

位置タブ（EyesPos/EyesSca、:475-526）はレイヤー固有のため残す。

- [ ] **Step 3: 両構成でビルド確認**

- [ ] **Step 4: コミット**

```bash
git commit -m "refactor(timeline): メイド瞳レイヤーの視線編集を表情ウィンドウへ委譲する"
```

---

### Task 9: 調査ドキュメントへ対応状況を反映

**Files:**
- Modify: `docs/layer-window-duplication-survey.md`

- [ ] **Step 1: B分類の表に「対応」列を追加して各行の結果を記載**

- カメラ / ライト / 背景 / 背景色 / メイド移動 / 瞳視線: 対応済み（委譲ラベル化、個別ウィンドウへ機能追加）
- サブカメラ / メイドアニメ編集タブ: 対象外（理由を本計画のスコープ外節から転記）
- 「削除方針の示唆」節を更新

- [ ] **Step 2: コミット**

```bash
git add docs/layer-window-duplication-survey.md
git commit -m "docs: B分類レイヤーの委譲対応状況を調査ドキュメントへ反映する"
```

---

## 実機確認（全タスク完了後、任意）

ゲーム起動中なら MCP `com3d25-devbridge` のホットリロード手順で反映し、以下を確認:
1. 各委譲レイヤーのウィンドウに案内ラベルが出る
2. 個別ウィンドウの新 UI で編集 → タイムラインの該当レイヤーでキー登録 → 値がキー化される
3. カメラ/移動: 360° 以上回してキーを 2 つ打ち、補間が巻き戻らない
4. ライト: 有効トグル OFF → キー登録で visible=false が入る
