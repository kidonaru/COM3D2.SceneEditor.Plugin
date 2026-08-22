# メイドギズモの表示対象対応 設計書

作成日: 2026-08-22

## 背景と目的

ModItemExplorer（以下 MIE）には配置モデルのギズモを「すべて表示 / 選択中」で切り替える
「表示対象」設定がある（`SelfModelPlacer.gizmoTargetType`）。一方 SceneEditor の
`GizmoRenderer` は選択中オブジェクト 1 個にしか `TransformGizmo` を出しておらず、
メイドのギズモは選択したときしか現れない。

本対応では、メイドルートの移動/回転/拡縮ギズモを常時表示できるようにし、
その表示範囲を MIE と共有する「表示対象」設定で制御する。

### スコープ

- 対象は**メイドルートの `TransformGizmo`**（`GizmoRenderer` が描画するもの）のみ
- `MaidManipulateManager` 配下のドラッグ点・ボーン回転ギズモ（「ボーン表示」で出るもの）は対象外
- 配置モデル側の既存挙動は変更しない（設定の持ち主が移るだけ）

## 方式

SceneEditor の `GizmoRenderer` にメイド用の `TransformGizmo` を増設し、表示対象設定は
SceneEditor を正として MIE と双方向同期する。

既存の `GizmoRenderer.currentTool` / `useLocalSpace` が同じ構造（SceneEditor が static で
保持し、MIE が `GizmoToolClient` のリフレクションブリッジ経由でポーリング同期）なので、
その作法をそのまま踏襲する。

### 採用しなかった案

- **MIE の `ModelGizmoManager` にメイドを登録する**: 既存の All/Selected 実装を流用できるが、
  メイドの操作が MIE 依存になり、MIE 未導入時にメイドギズモが出なくなる
- **ゲーム標準の `GizmoRender` をメイドルートに付ける**: `MaidBoneGizmoController` と同じ手だが、
  SceneView / GameView の RT カメラ対応・ツール種別の排他・ヒット優先度を `TransformGizmo` と
  二重管理することになる

## 設計

### 1. 表示対象設定の共有

**enum の定義場所**

`MTEUtils/TransformGizmo.cs`（両リポジトリで同一ソースを複製している）に追加する。
`GizmoTool` と同じ場所に置くことで、複製更新の対象を 1 ファイルに閉じる。

```csharp
/// <summary>ギズモを表示する対象</summary>
public enum GizmoTargetType
{
    All,
    Selected,
}
```

**SceneEditor（ホスト）**

- `GizmoRenderer.gizmoTargetType`（static プロパティ、既定 `All`）を新設
- `Config` に永続化する。MIE 側の `gizmoTargetType` は Config に永続化されているため、
  正の側でも保存しないと再起動でユーザーの選択が失われる

**既存 `useLocalSpace` の永続化（ついで対応）**

`GizmoRenderer.useLocalSpace` は現在 static プロパティのままで永続化されていない。
表示対象と同じくユーザーが一度決めたら変えない性質の設定なので、この機会に
`Config.gizmoUseLocalSpace`（既定 true）へ永続化する。

`currentTool`（移動/回転/拡縮）は Q/W/E 相当のホットキーで頻繁に切り替える一時的な
モードなので永続化しない。

Config へ永続化する 2 項目は、`Config` のロード完了後に `GizmoRenderer` の static へ
反映する必要がある。`GizmoRenderer` は MonoBehaviour で SceneView / GameView の
カメラ生成時に付くため、初期値は Config を直接読む形（プロパティのバッキングを
Config のフィールドにする）にして、生成順への依存をなくす:

```csharp
public static bool useLocalSpace
{
    get => config.gizmoUseLocalSpace;
    set
    {
        if (config.gizmoUseLocalSpace == value) return;
        config.gizmoUseLocalSpace = value;
        config.dirty = true;
    }
}
```

`gizmoTargetType` も同じ形にする（MIE の `SelfModelPlacer.gizmoTargetType` と同型）。

**ブリッジ**

`MTEUtils/GizmoToolClient.cs` に `targetType` プロパティを追加する。既存 `tool` と同じく
enum はアセンブリ間で別型になるため int 経由で授受し、ホスト側 enum 型は
`_hostTargetTypeProp.PropertyType` から取得する。取得失敗時は `GizmoTargetType.All`
（＝ SceneEditor の既定値）を返し、`isTargetTypeAvailable` を false へ倒す
（`isAvailable` は倒さない。理由は下記）。

既存の初期化は `GizmoRenderer` から `tool` / `useLocalSpace` の 2 プロパティが揃って
見つかることを条件にしている。ここに `gizmoTargetType` を必須として加えると、
プロパティを持たない旧版 SceneEditor と組み合わせたときに `tool` / `useLocalSpace` の
同期まで一緒に止まる。そのため **`targetType` は任意扱い**とし、見つからない場合は
`targetType` の getter/setter だけが no-op になるようにする（`isTargetTypeAvailable` を別に持つ）。

**MIE（クライアント）**

`SelfModelPlacer.UpdateGizmoToolSync()` に `gizmoTargetType` の同期を 1 組追加する。
`dragType` / `useLocalSpace` と全く同じ形:

- 初回同期時は SceneEditor 側の値へ合わせる（SceneEditor を正とする）
- 以降は `_lastSyncedGizmoTargetType` との差分で MIE 側が動いたかを判別し、
  動いていれば host へ書き、動いていなければ host の値を取り込む
- `GizmoToolClient.isTargetTypeAvailable` が false の間はこの組をスキップする
  （MIE 単体起動時・旧版 SceneEditor 併用時は MIE の `Config.gizmoTargetType` が正のまま）

### 2. メイドギズモの描画・操作

`GizmoRenderer` に非選択メイド用の `TransformGizmo` プールを持たせる。

```csharp
/// <summary>非選択メイド用のギズモ。対象が増減した分だけ確保して使い回す</summary>
private readonly List<TransformGizmo> _maidGizmos = new List<TransformGizmo>();

/// <summary>_maidGizmos と同じ添字で対象メイドのルートを保持する</summary>
private readonly List<GameObject> _maidGizmoTargets = new List<GameObject>();
```

**対象メイドの決定**

毎フレーム（`SyncGizmo` 内）に以下の条件で組み直す:

1. `gizmoTargetType == Selected` なら 0 件（＝現状の挙動）
2. `externalTargetProvider` が非 null（ボーン編集ウィンドウがギズモを乗っ取っている）なら 0 件
3. `selectionManager.gizmoSuppressed`（ポーズボーン選択 / IK 選択中）なら 0 件
4. それ以外は `MaidManipulateManager.instance.calledMaids` のうち
   - `IsVisible(maid)` が true（退避中のメイドは画面外なので除外）
   - `maid.body0?.m_Bones` の親（メイドルート GameObject）が取得できる
   - そのルートが `gizmoTarget`（選択中オブジェクト）と同一ではない
     — 選択中メイドは既存の `_gizmo` が担当するため二重描画しない

2 と 3 は「ボーン編集中はボーン用ギズモだけを見せる」ための抑止。根本のギズモが重なると
掴み間違いが起きるため、メイドルートのギズモは一切出さない。

> メイドルート GameObject の取り方は `SelectionManager` の既存規約（ボーンヒットを
> メイドルートへ丸める処理）と揃える。実装時に該当処理を参照して同じ経路を使うこと。

**描画**

`OnPostRender` の末尾、既存の `_gizmo.Draw(_camera)` に続けて `_maidGizmos` を順に
`Draw(_camera)` する。各インスタンスには `_gizmo` と同じ `tool` / `useLocalSpace` を反映する。

選択バウンズ枠（オレンジ）とライトギズモは従来通り選択中オブジェクトにだけ描く。

**ヒット判定・ドラッグ**

`TryBeginDrag` は「選択中の `_gizmo` → `_maidGizmos` の順」で試す。
選択中を先に試すことで、ギズモが重なっている場合に選択中のものが優先される。

ヒットしたインスタンスを `_activeDragGizmo` として保持し、`UpdateDrag` / `EndDrag` は
そこへ流す（現状は `_gizmo` 直参照）。`EndDrag` 後に `_activeDragGizmo` を null へ戻す。

非選択メイドのギズモを掴んだ場合は、ドラッグ開始時に選択を移す:

```csharp
// 掴んだメイドを選択へ移す。フォーカス（カメラ寄せ）はしない
selectionManager.Select(maidRoot, showGizmo: true, focus: false);
```

履歴記録（`HistoryManager.BeforeEdit`）は掴んだギズモの対象で行う。既存コードは
`gizmoTarget` を参照しているが、掴んだ対象を引数に取る形へ変える。

> 注意: `Select` は `onSelectionChanged` を発火し、`gizmoTarget` を掴んだメイドへ切り替える。
> その結果、次フレームの `SyncGizmo` で `_maidGizmos` の構成が変わり、掴んだインスタンスが
> リストから外れる（選択中は `_gizmo` が担当するため）。`_activeDragGizmo` は
> `TransformGizmo` の参照を直接保持しているのでドラッグ自体は継続できるが、
> プールの再構成でインスタンスが別の対象へ使い回されると破綻する。
> **ドラッグ中は `_maidGizmos` の再構成を行わない**（`_activeDragGizmo != null` の間は
> 前フレームの構成を維持する）ことで回避する。

**GameView との関係**

`GizmoRenderer` は SceneView と GameView の両カメラに付く。メイドギズモも両方で出す
（配置モデルのギズモと同じ扱い）。`showSelectionBounds` / `showLightGizmos` のような
ビュー別フラグは設けない。

### 3. UI

**SceneEditor**

`GizmoTargetRowDrawer`（新規）を `source/COM3D2.SceneEditor.Plugin/` 直下
（`InspectorWindow.cs` と同階層）へ追加する。`MTEUtils/` は両リポジトリで複製している
共有ソース置き場だが、この行は SceneEditor 固有の `GizmoRenderer.gizmoTargetType` を直接
参照するため共有できない。呼び出しは `InspectorWindow.DrawGizmoToolRow()` の直下に置く。
`DrawGizmoToolRow` は
2 箇所（オブジェクト用・メイド用の Inspector）から呼ばれているため、
表示対象行も `DrawGizmoToolRow` の中に含めて 1 箇所の変更で両方へ出す。

見た目・文言は MIE の `GizmoTargetRowDrawer` に合わせる（「表示対象」/「すべて表示」/「選択中」、
ボタン幅 80f、選択中を再度押しても解除しない）。

**ModItemExplorer**

`ModelInspectorDrawer` は SceneEditor の Inspector へ委譲描画されるものなので、
そこから `GizmoTargetRowDrawer.Draw` の呼び出しを削除する。削除しないと SceneEditor 側の
行と並んで同じ設定の行が 2 つ出る。

MIE のモデル操作ウィンドウ（`ModelOperationWindow`）の行はそのまま残す
（MIE 単体起動時の唯一の切替手段になるため）。

## 影響ファイル

| リポジトリ | ファイル | 変更 |
|---|---|---|
| SceneEditor | `MTEUtils/TransformGizmo.cs` | `GizmoTargetType` 追加 |
| SceneEditor | `MTEUtils/GizmoToolClient.cs` | `targetType` / `isTargetTypeAvailable` 追加 |
| SceneEditor | `Manager/GizmoRenderer.cs` | static 設定、メイドギズモのプール・描画・ヒット判定・ドラッグ委譲 |
| SceneEditor | `Config.cs` | `gizmoTargetType` / `gizmoUseLocalSpace` の永続化 |
| SceneEditor | `InspectorWindow.cs` | `DrawGizmoToolRow` に表示対象行を追加 |
| SceneEditor | `GizmoTargetRowDrawer.cs`（新規） | 表示対象の切替行 |
| ModItemExplorer | `MTEUtils/TransformGizmo.cs` | 同一ソースの複製更新 |
| ModItemExplorer | `MTEUtils/GizmoToolClient.cs` | 同一ソースの複製更新 |
| ModItemExplorer | `ModelPlacement/SelfModelPlacer.cs` | `UpdateGizmoToolSync` に同期を 1 組追加 |
| ModItemExplorer | `ModelPlacement/ModelInspectorDrawer.cs` | 重複する表示対象行の削除 |

## エラー処理・後方互換

- `GizmoToolClient` は SceneEditor 不在・シグネチャ不一致で `isAvailable = false` へ倒れ、
  MIE は同期をスキップする（既存の挙動）
- `targetType` のみ見つからない場合（旧版 SceneEditor + 新版 MIE）は
  `isTargetTypeAvailable = false` となり、表示対象だけ同期されず他の 2 項目は動き続ける
- 新版 SceneEditor + 旧版 MIE の場合、MIE は `targetType` を知らないので同期されない。
  SceneEditor 側のメイドギズモは動作し、MIE の Inspector には旧版の表示対象行が出る
  （行が 2 つ並ぶが機能上の破綻はない）
- `calledMaids` から破棄済みメイドを拾わないよう、対象決定時に null / 破棄チェックを行う

## テスト

自動テストの仕組みが無いプロジェクトのため、実機（devbridge）での確認とする。

1. ビルド: `MSBuild <Plugin>.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 ...`
   （`debug.bat` はゲーム停止中に実行するとゲームフォルダへ反映されるため注意）
2. メイドを 2 体呼び出し、表示対象「すべて表示」で両方にギズモが出ること
3. 非選択メイドのギズモを掴むと選択がそのメイドへ移り、そのまま移動できること。
   Undo が掴んだメイドに対して効くこと
4. 表示対象「選択中」で選択中メイドのギズモだけになること
5. SceneEditor 側で切り替えると MIE のモデル操作ウィンドウの表示が追従し、逆も成立すること
6. ボーン編集ウィンドウを開いている間・ポーズボーン選択中はメイドルートのギズモが出ないこと
7. 退避（非表示）中のメイドにギズモが出ないこと
8. SceneView / GameView の両方で 2〜7 が成立すること
9. 表示対象と軸空間 (Local/Global) がゲーム再起動後も保持されること。
   再起動後の初回同期で MIE 側も保存値へ揃うこと
