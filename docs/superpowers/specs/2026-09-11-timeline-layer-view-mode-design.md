# タイムラインのレイヤー表示モード（カテゴリ / レイヤー）設計

作成日: 2026-09-11

## 1. 目的

タイムラインの表示/編集対象レイヤーの決め方を「アクティブレイヤー + レイヤーごとの表示トグル」から、**2 つの表示モードの切替**へ置き換える。

| モード | 表示 | 編集 |
|---|---|---|
| カテゴリモード | アクティブレイヤーと同じカテゴリの操作対象レイヤーを全部並べる | 表示中のレイヤーだけ。編集基準はアクティブレイヤー（従来どおり） |
| レイヤーモード | アクティブレイヤーだけ | アクティブレイヤーだけ |

あわせて、UI 上すでに無効化されている「簡易表示」（`isEasyEdit`）を機能ごと削除する。

前提となる現行仕様は `2026-08-29-timeline-multi-layer-display-design.md`（レイヤー複数表示）。本設計はその表示トグル部分を置き換える。

## 2. 決定事項（ブレインストーミングで確定）

1. カテゴリは `TimelineLayerDesc` の priority 番号帯に沿った 6 区分（§3.1）
2. カテゴリモードは**アクティブカテゴリのレイヤーだけ表示**する（他カテゴリはグレーアウト表示しない）
3. 編集の単位は従来どおり**単一のアクティブレイヤー**。カテゴリ内の別レイヤーはキーフレームクリック / 見出し行クリックでアクティブ化する（現行動作をカテゴリ内に限定）。A/D ボタン・自動キーフレーム登録・コピー/ペースト・テンプレート・カーブエディタ・Inspector 連携は変更しない
4. レイヤーごとの表示トグル（非表示集合）は**廃止**。折りたたみは残す
5. アクティブカテゴリは独立した状態として持たず、**アクティブレイヤーの所属カテゴリから導出**する。新規に持つ状態はモードのみ
6. モード切替は**アイコンボタン**（`assets/icons` に SVG を追加し `ToolbarIcons` へ組み込む）
7. `isEasyEdit`（簡易表示）は機能削除

## 3. データモデル

### 3.1 レイヤーカテゴリ

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    public enum TimelineLayerCategory
    {
        Maid,        // メイド
        Camera,      // カメラ
        Model,       // モデル
        Background,  // 背景
        Effect,      // ライト・演出
        Other,       // その他
    }
}
```

`TimelineLayerDescAttribute` に第 3 引数 `TimelineLayerCategory category` を追加し（省略不可）、`TimelineLayerInfo.category` で参照できるようにする。表示名は `TimelineLayerCategory` の拡張メソッド `ToDisplayName()` で返す（`Extensions.cs` の既存 enum 拡張と同じ置き場所）。

| カテゴリ | 表示名 | レイヤー（priority） |
|---|---|---|
| Maid | メイド | メイドアニメ(0) / メイドアニメブレンド(1) / メイド表情(10) / メイド移動(11) / メイド瞳(12) / メイドシェイプ(13) / メイドボイス(14) / メイド衣装(15) / メイド脱衣(16) / メイドマテリアル(17) |
| Camera | カメラ | カメラ(20) / サブカメラ(25) |
| Model | モデル | モデル(21) / モデルボーン(22) / モデルシェイプ(23) / モデルマテリアル(24) |
| Background | 背景 | 背景(31) / 背景色(32) / 背景モデル(33) / 背景モデルマテリアル(34) / PNG配置(35) |
| Effect | ライト・演出 | ライト(41) / ステージライト(42) / ステージレーザー(43) / サイリウム(44) |
| Other | その他 | 効果音(51) / ポストエフェクト(52) / テキスト(53) |

28 レイヤーすべてに付与する。付け忘れはコンパイルエラー（属性の必須引数）で検出する。

### 3.2 表示モード

```csharp
public enum TimelineLayerViewMode
{
    Category,
    Layer,
}
```

`Config`（`Timeline/Config.cs`）に `public TimelineLayerViewMode layerViewMode = TimelineLayerViewMode.Category;` を追加し永続化する。`Config.CurrentVersion` は上げない（新フィールドは既定値で読める）。

### 3.3 `TimelineLayerRowState` の縮退

非表示集合とその API（`IsVisible` / `ToggleVisible` / `SetAllVisible` / `AreAllVisible`）を削除する。残すのは折りたたみ集合（`IsCollapsed` / `ToggleCollapsed` / `SetAllCollapsed` / `AreAllCollapsed`）、`Prune`、`BuildRows`。

- `BuildRows(layers, currentLayer, collectItems, result)` は渡された `layers` をすべて行にする（表示判定は呼び出し側で済ませて渡す）
- `SetAllCollapsed` / `AreAllCollapsed` は `currentLayer` 引数による表示判定が不要になるため、渡された `layers` 全件を対象にする
- 対応するユニットテスト（`TimelineLayerRowStateTests`）の非表示系 6 件を削除し、折りたたみ系・`BuildRows` 系は新シグネチャへ追従する

## 4. 表示範囲の決定ロジック

Unity 非依存の静的クラス `TimelineLayerViewFilter`（`Timeline/TimelineLayerViewFilter.cs`、ジェネリック）を新設し、ユニットテスト対象にする。

```csharp
public static class TimelineLayerViewFilter
{
    /// 表示モードに従って表示レイヤーを result へ詰める (result は Clear してから使う)
    public static void Filter<TLayer>(
        IList<TLayer> targetLayers,
        TLayer currentLayer,
        TimelineLayerViewMode mode,
        Func<TLayer, TimelineLayerCategory> getCategory,
        List<TLayer> result) where TLayer : class;

    /// カテゴリ内の先頭レイヤー (priority 昇順、同値は targetLayers の順)。無ければ null
    public static TLayer FindFirstLayer<TLayer>(
        IList<TLayer> targetLayers,
        TimelineLayerCategory category,
        Func<TLayer, TimelineLayerCategory> getCategory,
        Func<TLayer, int> getPriority) where TLayer : class;
}
```

- `Layer` モード: `result = [currentLayer]`（`currentLayer` が null なら空）
- `Category` モード: `targetLayers` のうち `getCategory(layer) == getCategory(currentLayer)` のもの。`currentLayer` が `targetLayers` に無い場合も `BuildTargetLayers` が常にアクティブレイヤーを含める現行仕様（他メイドのレイヤーでも出す）により起きないが、防御として `currentLayer` を必ず含める

`TimelineWindow` 側は `BuildTargetLayers()` → `TimelineLayerViewFilter.Filter(_targetLayers, currentLayer, mode, ...)` → `_displayLayers` → `_rowState.BuildRows(_displayLayers, ...)` の順で行を組む。`_targetLayers`（操作対象）と `_displayLayers`（表示対象）を分けて持つのは、カテゴリコンボの列挙とレイヤーモードのコンボが `_targetLayers` を使うため。

編集ガードは現状の `row.layer == currentLayer` 判定をそのまま使う。表示範囲がモードで絞られるため「アクティブカテゴリ / アクティブレイヤー以外は編集できない」は追加のガードなしに成立する。

## 5. UI

### 5.1 レイヤー操作行

ボーンメニュー上部（フレーム番号バーと同じ高さの行）を次の並びにする。

```
[モード][選択コンボ ......................][-][+]
```

- **モード**: `FRAME_LABEL_HEIGHT` 角のアイコンボタン。現在のモードのアイコンを表示し、クリックで切り替える。ツールチップは「カテゴリモード」/「レイヤーモード」。アイコン取得に失敗したときは文字ボタン（「カ」/「レ」）にフォールバックする（`TimelineControlWindow.DrawIconToggle` と同じ流儀）
- **選択コンボ**: 幅は `menuWidth - FRAME_LABEL_HEIGHT - LAYER_BUTTON_WIDTH * 2`（下限 `LAYER_BUTTON_WIDTH`）。中身はモードで切り替える
  - カテゴリモード: `_targetLayers` に 1 件以上あるカテゴリを enum 順で列挙。項目名は「カテゴリ名 (N)」で N はそのカテゴリの操作対象レイヤー数。選択で `FindFirstLayer` の結果へ `SetCurrentLayer`。ボタン面はアクティブレイヤーのカテゴリ名 + 「(N)」
  - レイヤーモード: `_targetLayers` を列挙（現行の `_displayLayerComboBox` と同じ命名規則、メイド名は省く）。選択で `SetCurrentLayer`。ボタン面はアクティブレイヤー名。現行の「他N」表記は廃止
  - `GUIComboBox` の `getChecked` / `onToggle` / `getHeaderName` / `getHeaderChecked` / `onHeader` は設定しない（チェック列・「全て表示」ヘッダーを撤去）
- **-** / **+**: 現行どおり

### 5.2 行の描画

- カテゴリモードのレイヤー見出し行（折りたたみトグル + レイヤー名、クリックでアクティブ化）は現行のまま。カテゴリ内のレイヤーが 1 件でも見出し行を出す
- レイヤーモードでも見出し行を出す（折りたたみを使えるようにし、両モードで行構造を揃える）
- `DrawRowStateControls`（一括折りたたみ）は `_displayLayers` を対象にする

### 5.3 アイコン

`assets/icons` に SVG を 2 つ追加し、`generate.js` の `ICONS` と `ToolbarIcons.Kind` / `PNG_BASE64` へ組み込む。

| Kind | ファイル | 図案 |
|---|---|---|
| `CategoryMode` | `CategoryMode.svg` | 横線 3 本を角丸の枠で囲む（グループ化された行） |
| `LayerMode` | `LayerMode.svg` | 横線 1 本を角丸の枠で囲む（単一の行） |

同時に `EasyEdit` を `Kind` / `ICONS` / `PNG_BASE64` から外し、`EasyEdit.svg` / `EasyEdit.png` を削除する。`Kind` は添字で `PNG_BASE64` と対応するため、順序の整合を `generate.js` の再実行で担保する。

## 6. 簡易表示（isEasyEdit）の削除

削除対象:

- `Config.isEasyEdit` フィールド
- `BoneMenuManager.easyMenuItems` と `GetVisibleItems()` / `GetSelectedItems()` の分岐、`EasyMenuItem` クラスとその `Compile Include`
- `TimelineManager.SelectFramesRange` / `FindFrame` / `SelectVerticalBones` の分岐（非簡易側の処理を残す）
- `TimelineItemInspector.ShouldDraw` の分岐
- `TimelineControlWindow` の「縦選択」ボタンの enabled 条件（常時有効）
- `TimelineWindow` の `BuildRows` 分岐、`alwaysShowVertical`、`DrawRowStateControls` 早期 return、`indent` の三項演算
- `TimelineIntegration` の簡易表示に関するコメント
- `ToolbarIcons.Kind.EasyEdit`（§5.3）

残すもの:

- `EasySettingType.簡易表示`（`Config._easySettingVisibleMap` の XML キーとして既存設定に残っている可能性があり、enum 値を消すとデシリアライズで落ちる。`EasySettingType` 自体が SE 内で未参照なので触らない）

既存の `config.xml` に `<isEasyEdit>` 要素が残っていても、`XmlSerializer` は未知要素を無視するため互換問題はない。

## 7. 変更ファイル一覧

| ファイル | 変更 |
|---|---|
| `Timeline/TimelineLayerInfo.cs` | `TimelineLayerCategory` enum、属性の第 3 引数、`TimelineLayerInfo.category` |
| `Timeline/TimelineLayer/*.cs`（28 件） | 属性にカテゴリを追加 |
| `Timeline/Extensions.cs` | `TimelineLayerCategory.ToDisplayName()` |
| `Timeline/Config.cs` | `TimelineLayerViewMode` enum、`layerViewMode`、`isEasyEdit` 削除 |
| `Timeline/TimelineLayerViewFilter.cs`（新規） | §4 |
| `Timeline/TimelineLayerRowState.cs` | 非表示集合の削除 |
| `Timeline/BoneMenu/BoneMenuManager.cs` / `EasyMenuItem.cs`（削除） | §6 |
| `Timeline/Manager/TimelineManager.cs` / `Timeline/ItemInspector/TimelineItemInspector.cs` / `Timeline/TimelineIntegration.cs` | §6 |
| `TimelineWindow.cs` | `_displayLayers`、`DrawLayerControls`、コンボ初期化、§6 |
| `TimelineControlWindow.cs` | §6 |
| `ToolbarIcons.cs` / `assets/icons/*` | §5.3 |
| `COM3D2.SceneEditor.Plugin.csproj` | `TimelineLayerViewFilter.cs` 追加、`EasyMenuItem.cs` 削除 |
| `COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs` | 非表示系テスト削除・シグネチャ追従 |
| `COM3D2.SceneEditor.Plugin.Tests/TimelineLayerViewFilterTests.cs`（新規） | §8 |
| `COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs`（新規） | §8 |
| `docs-site`（timeline ガイド） | 表示トグル・簡易表示の記述をモード説明へ差し替え |

## 8. テスト

ユニットテスト（`COM3D2.SceneEditor.Plugin.Tests`、COM3D2 / COM3D25 両構成でビルド）:

- `TimelineLayerViewFilterTests`
  - Layer モードはアクティブレイヤーだけを返す
  - Category モードは同カテゴリのレイヤーだけを `targetLayers` の順で返す
  - Category モードでアクティブレイヤーが `targetLayers` に無くても含める
  - `currentLayer` が null なら空
  - `FindFirstLayer` は priority 昇順の先頭、同値は入力順、該当なしは null
  - `result` は呼ぶたびにクリアされる
- `TimelineLayerCategoryTests`
  - 全 `ITimelineLayer` 実装型が `TimelineLayerDesc` を持ち、カテゴリが §3.1 の表と一致する（反射で列挙。`MteCompatibilityTests` の網羅方式に倣う）
  - `ToDisplayName()` が全 enum 値に対して空でない
- `TimelineLayerRowStateTests`: 折りたたみ系と `BuildRows` 系が新シグネチャで通る

実機確認（devbridge）:

1. モードボタンで両モードが切り替わり、`config.xml` に保存される
2. カテゴリコンボで別カテゴリを選ぶと先頭レイヤーがアクティブになり、表示が切り替わる
3. Hierarchy でメイド / ライトを選択したときアクティブレイヤーが変わり、カテゴリモードの表示が追従する
4. 「+」でレイヤーを追加した直後、そのレイヤーが表示範囲に入っている
5. レイヤーモードでアクティブ以外のレイヤーの行が出ない
6. 「縦選択」が常時押せて動作する

## 9. スコープ外

- カテゴリごとの「最後にアクティブだったレイヤー」の記憶（カテゴリ切替は常に先頭レイヤー）
- カテゴリの折りたたみ・並べ替え・ユーザー定義
- 複数レイヤー同時編集（A/D ボタンの全行表示、複数レイヤーへの自動キーフレーム登録）
- `EasySettingType` の整理
