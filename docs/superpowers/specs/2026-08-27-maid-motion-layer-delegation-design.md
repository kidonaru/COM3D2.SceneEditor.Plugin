# メイドアニメレイヤーの編集 UI 委譲 設計書

作成日: 2026-08-27
対象ブランチ: feature/timeline-window
関連: `docs/layer-window-duplication-survey.md`（B 分類「MotionTimelineLayer 編集タブ」を対象外から対象へ引き上げる）

## 目的

`MotionTimelineLayer`（レイヤー名「メイドアニメ」）の編集 UI を SceneEditor の個別ウィンドウへ委譲し、レイヤー編集ウィンドウ側は案内ラベルのみにする。具体的には次の 3 つを移す。

1. 「編集」タブのボーン Transform 編集 → ボーンウィンドウ（`BoneEditWindow`）と Inspector・ギズモ
2. 「編集」タブの IK 固定・接地 → IK ウィンドウ（`MaidIKWindow`）
3. 「追加」タブの拡張ボーン対象選択 → ボーンウィンドウのボーンツリーのチェック（追跡ストア方式）

あわせて「指」タブに残っていた `ブレンド有効` トグルを指ウィンドウ（`MaidFingerWindow`）へ移設し、レイヤー UI からタブを撤去する。

## 背景（現状）

キーフレーム書き込みは `MotionTimelineLayer.UpdateFrame`（`Timeline/TimelineLayer/MotionTimelineLayer.cs:394`）が担い、対象ごとに読み元が異なる。

| 対象 | キーの読み元 | 委譲の可否 |
|---|---|---|
| 体ボーン（`BoneUtils.saveBoneNames`） | `maidCache.GetBoneTransform(name)` のライブ値 | そのまま委譲可（Inspector・ギズモが同じ Transform を編集する） |
| 拡張ボーン | ライブ Transform。対象集合は `timeline.GetExtendBoneNames(slotNo)` | 編集は委譲可。対象集合の橋渡しが必要 |
| IK 固定 | `maidCache.GetIKHoldEntity(name)`（MTE 専用状態） | ブリッジ必須 |
| 接地 | `maidCache` の接地 7 フィールド（MTE 専用状態） | ブリッジ必須 |
| 指ブレンド | `GetBaseFinger()` のライブ値 | 委譲済み |

IK 固定・接地は MTE と SE で二重実装になっている。

- MTE: `Timeline/IKHoldEntity.cs`。FinalIK の `LimbControl` / `FABRIK` とスタジオのドラッグ点を操作する。状態は `MaidCache.ikHoldEntities` と接地 7 フィールド（`Timeline/MaidCache.cs:45,55-61`）
- SE: `MaidManipulation/MaidIKHoldController.cs`。独自の `MaidIKChain` で解く。状態は Maid ごとの `HoldEntity[]` と `MaidIKHoldParams`

両者は「モーション再生中の固定は MTE の担当」として役割分担しており（`MaidIKHoldController.cs:389-393` のコメント）、SE 側は編集モード中かつモーション停止中しか固定しない（`:349-353`, `:389-393`）。

拡張ボーンの対象選択は `timeline.extendBoneNamesMap`（`Timeline/TimelineData.cs:239`）が保持し、レイヤーの「追加」タブだけが編集経路になっている。一方 SE には `EditTargetStore` を軸にした汎用の追跡フレームワーク（`Timeline/TimelineLayer/TimelineLayerBaseTracking.cs`）があり、`ModelBoneTimelineLayer` は既にこれでボーン絞り込みを行っている。

### 調査で確定した事実

- **スロット名は一致する。** MTE の拡張ボーン名は `slotName + "/" + boneName` で、`slotName` は `TBodySkin.Category`（`Timeline/ExtendBoneCache.cs:71,86`）。`Category` は `TBody` が `m_strDefSlotName` から渡す文字列で（`TBody.cs:1268`）、その並びは `TBody.SlotID` と同順・同名。SE 側は `((TBody.SlotID)i).ToString()`（`SlotBoneManager.GetLoadedSlotNames`）なので、変換なしで突き合わせられる
- **IK の座標系と意味は一致する。** MTE `IKHoldEntity.targetPosition` も SE `HoldEntity.targetPosition` も固定点ボーンのワールド座標。接地パラメータは 7 つとも同名・同意味（`MaidIKHoldParams` は MTE からの移植）
- **enum メンバ名が一致する。** `IKHoldType` と `MaidIKHoldType` は `Arm_R_Joint` 等のメンバ名が同じ。キーフレームのボーン名は enum メンバ名そのもの（`MaidCache.ikHoldTypeMap` は `Enum.GetValues` から生成）なので、名前引きで橋渡しできる
- **MTE の IK ドラッグ連携は SE では死にコード。** `StudioHackBase.IsIKDragging` は常に false を返す既定実装のみで、`MaidCache.GetIkFabrik / GetDragPoint / GetAxisObj / IsIkDragging` の外部呼び出しは `MaidManager.GetIkPosition` 経由の 1 本だけ
- **`timeline.fingerBlendEnabled` は SE と競合しない。** SE の `MaidFingerBlendController` は `FingerBlend.BaseFinger.enabled` に一切触れておらず、`enabled` を操作するのは `MotionTimelineLayer.GetBaseFinger`（`:1091`）だけ

## 設計

### 1. IK 固定・接地を SE 側へ一本化

`MaidIKHoldController` を唯一の実体とし、MTE 側の実装を撤去する。

- `MotionTimelineLayer` から SE のコントローラを参照する。レイヤーは `COM3D2.MotionTimelineEditor.Plugin` 名前空間にあり `maidManager` は MTE の `MaidManager` を指すため、`COM3D2.SceneEditor.Plugin.MaidManipulateManager.instance.ikHoldController` を明示的に使う
- `UpdateFrame` の IK ブロック（`:479-494`）: `TransformDataIKHold` の `position` / `isHold` / `isAnime` を SE コントローラから読む。ボーン名 → `MaidIKHoldType` は enum メンバ名で引く
- `UpdateFrame` の接地ブロック（`:496-510`）: `ikHoldController.GetParams(maid)` の 7 値を書き込む
- `ApplyIKHoldMotion`（`:289`）: `SetHold` と `targetPosition` / `isAnime` へ書き戻す
- `ApplyGroundingMotion`（`:326`）: `GetParams(maid)` の 7 値へ書き戻す
- `MotionTimelineLayer.Init` の `ResetIkHoldEntities()` / `ResetGrounding()`（`:71-72`）は廃止する。IK 状態がタイムライン所有ではなくなり、リセットするとユーザーが IK ウィンドウで設定した内容を消してしまうため。タイムラインを読み込めば 0F キーの適用で従来どおりの状態になる
- 削除するもの:
  - `Timeline/IKHoldEntity.cs` 全体（`IKHoldType` enum は下記のとおり扱いを決める）
  - `MaidCache` の `ikHoldEntities` / 接地 7 フィールド / `ResetIkHoldEntities` / `ResetGrounding` / `GetIKHoldEntity` 系 / `GetIkFabrik` / `GetDragPoint` / `GetAxisObj` / `GetIkPosition` / `IsIkDragging` / `OnPoseEditUpdated` と、`LateUpdate` 内の IK 更新ループ
  - `MaidManager.GetIkPosition`、`StudioHackBase.IsIKDragging`
  - `MaidCache.GetBoneTransform` / `GetInitialPosition` の IK 分岐
- `IKHoldType` enum と `MaidCache.ikHoldTypeMap` / `GetIKHoldType` は残す。キーフレームのボーン名テーブルとして `TimelineXml`（`:135` の `isHoldList`、`:458` の旧データ移行）と `MotionTimelineLayer.GetTransformTypeInternal` が参照しているため。enum は `IKHoldEntity.cs` の削除に伴い `Timeline/IKHoldType.cs` へ切り出し、実体の操作を持たない名前テーブルとして扱う
- `Timeline/Extensions.cs` の `ConvertBoneType`（`IKHoldType` → `IKManager.BoneType`）は `IKHoldEntity` からのみ呼ばれているため併せて削除する

### 2. isAnime（再生中も IK 固定）を SE へ移植

SE のコントローラは編集モード中かつモーション停止中しか固定しないため、MTE の `isAnime` に相当する経路を足す。

- `MaidIKHoldController.HoldEntity` に `bool isAnime` を追加
- `LateUpdate` の編集モードゲート（`:349-353`）と `UpdateMaid` のモーション停止ゲート（`:389-393`）を、`isAnime` が立っている箇所については通過させる
- `MaidIKWindow.DrawHoldToggles` の各ペア行に「アニメ」トグルを追加する。固定が OFF の箇所では意味を持たないため、`isHold` が false の間は無効表示にする
- `IKSnapshot`（`Manager/History/IKSnapshot.cs`）は固定状態を含むため、`isAnime` も比較・復元の対象に加える
- `ScenePresetData` の IK セクションにも `isAnime` を追加し、スキーマバージョンを上げる

### 3. 拡張ボーンの対象選択を追跡ストア方式へ

`ModelBoneTimelineLayer` と同じ枠組みに載せる。

- `BoneEditManager` にメイド用の集約ストア `maidBoneTrackedStore`（`EditTargetStore`）を追加する。既存の `_modelBoneTracked` / `SyncModelBoneTrackedStore`（`:596-622`）のメイド版で、`ModelTrackedNameStore<Maid>` を使い、各メイドの `BoneEditStore` のエントリを `slotName + "/" + boneName` の名前へ変換して集約する
  - モデル側は「どのモデルか」をモデル名で修飾するが、メイド側の集約は**操作対象メイド 1 人分**とする。`MotionTimelineLayer` は `slotNo` ごとに別レイヤーで、それぞれ別のメイドを見るため、`Dictionary<Maid, EditTargetStore>` を引く形にする
- `MotionTimelineLayer` に追跡フレームワークの口を実装する
  - `trackedStore` → 当該レイヤーの `maid` に対応する集約ストア
  - `trackedCandidateNames` → `maidCache.extendBoneCache.entities.Keys`（正準順に並べたリスト）
  - `trackedHistoryPrefix` → `"拡張ボーン"`
  - `allBoneNames` の拡張ボーン部分を `timeline.GetExtendBoneNames(slotNo)` から `trackedBoneNames` に差し替える。体ボーン・IK・接地・指ブレンドは従来どおり固定で連結する
  - `InitMenuItems` / `UpdateFrame` / `Init` の `GetExtendBoneNames` 参照も同様に差し替える
- `timeline.extendBoneNamesMap` は**保存互換のために残す**。保存時に追跡集合の内容を書き出し、読み込み側は既存フレームワークの「チェック済み ∪ 既存キーフレーム記載」で拾われるため、旧タイムラインもチェックなしで編集できる
- `TimelineData.AddExtendBoneName` / `RemoveExtendBoneName` と `ITimelineLayer.OnBoneNameAdded` / `OnBoneNameRemoved` は、追跡フレームワークの自動キー登録・削除（`TimelineLayerBaseTracking`）に置き換わるため撤去する
- ボーンツリーで候補外のボーン（`_nub`、体ボーン、`BoneUtils.IsVisibleBoneName` が false のもの）にチェックしても対象集合には入らない。これは黙って無視する（候補テーブルに無い名前は `BuildTrackedBoneNames` が拾わない）

### 4. レイヤー UI を案内ラベルへ

`MotionTimelineLayer.DrawWindow`（`:853`）からタブ（`TabType`）と `DrawTransformEdit` / `DrawMenuItem` / `DrawIKMenuItem` / `DrawExtendBone` を削除し、`PngPlacementTimelineLayer` と同じ案内ラベルだけにする。

```
ボーンの編集はボーンウィンドウ・Inspector で行ってください
IK固定・接地は IK ウィンドウで行ってください
指の編集は指ウィンドウで行ってください
```

`_menuItemComboBox` / `_transComboBox` / `_slotNameComboBox` / `_isExtendBoneAllEnabled` など、削除した UI 専用のフィールドも撤去する。

### 5. 指ブレンド有効トグルの移設

- `MaidFingerWindow.DrawHeader`（`:143`）のヘッダー行に「ブレンド有効」トグルを追加し、`timeline.fingerBlendEnabled` を直接読み書きする
- `timeline` が null のとき（タイムライン未ロード）はトグルを描かない
- メイド単位の設定と誤解されないよう、タイムライン設定であることが分かるラベルにする
- `MotionTimelineLayer` 側の `GetBaseFinger` の `SetEnabledOnly` 呼び出し（`:1091`）はそのまま残す（キー書き込み・再生時の適用経路）

## 影響ファイル

| ファイル | 変更内容 |
|---|---|
| `Timeline/TimelineLayer/MotionTimelineLayer.cs` | 編集/追加/指タブの UI 削除、IK・接地の読み書き先を SE へ、拡張ボーンを追跡ストアへ |
| `Timeline/IKHoldEntity.cs` | 削除（`IKHoldType` enum は `Timeline/IKHoldType.cs` へ退避して残す） |
| `Timeline/Extensions.cs` | `ConvertBoneType` 撤去 |
| `Timeline/MaidCache.cs` | IK エンティティ・接地フィールド・関連メソッドの撤去 |
| `Timeline/Manager/MaidManager.cs` | `GetIkPosition` 撤去 |
| `Timeline/Hack/StudioHackBase.cs` | `IsIKDragging` 撤去 |
| `Timeline/TimelineData.cs` | `AddExtendBoneName` / `RemoveExtendBoneName` 撤去、保存時の集合を追跡集合から生成 |
| `Timeline/TimelineLayer/ITimelineLayer.cs`, `TimelineLayerBase.cs` | `OnBoneNameAdded` / `OnBoneNameRemoved` 撤去 |
| `MaidManipulation/MaidIKHoldController.cs` | `isAnime` 追加とゲート緩和 |
| `MaidManipulation/BoneEditManager.cs` | メイド用集約ストア `maidBoneTrackedStore` 追加 |
| `MaidIKWindow.cs` | アニメトグル追加 |
| `MaidFingerWindow.cs` | ブレンド有効トグル追加 |
| `Manager/History/IKSnapshot.cs` | `isAnime` を比較・復元対象へ |
| `ScenePresetData.cs` | IK セクションへ `isAnime` 追加、スキーマバージョン更新 |
| `docs/layer-window-duplication-survey.md` | B 分類の対応状況を更新 |

## 非対象

- サブカメラレイヤー、モデル系管理タブ（調査ドキュメントの対象外分類のまま）
- `BGModelMaterialTimelineLayer`（同上）
- MTE 側スタジオのドラッグ点表示そのもの（SE では既に `MaidIKDragPoint` が担当）
- 指ブレンドのプリセット・個別編集（既に指ウィンドウ側）

## 検証

ビルドは COM3D2 / COM3D25 の 2 構成とも通す（`debug.bat all`。ゲーム停止中は実機へ反映される点に注意）。実機確認は次の順で行う。

1. 体ボーン: Inspector・ギズモでポーズを変え、キー登録でその姿勢が入ること
2. 拡張ボーン: ボーンウィンドウでスロットボーンにチェック → 0F へ自動キーが入り、タイムラインに項目が出ること。チェックを外すとキーごと消えること
3. IK: IK ウィンドウで固定 → 手足を動かしてキー登録 → 再生で固定位置が再現されること
4. 接地: IK ウィンドウの接地パラメータを変えてキー登録 → 再生で反映されること
5. isAnime: アニメトグル ON で、モーション再生中も固定が効くこと
6. 指: 指ウィンドウのブレンド有効トグルがタイムライン保存へ載ること
7. 既存タイムラインの読み込み: 旧 `extendBoneNames` を持つファイルを開き、チェックなしでも項目が出て編集できること

## リスクと対処

- **IK ソルバの差による見た目のずれ**: FinalIK `FABRIK` から SE の `MaidIKChain` へ解き方が変わるため、同じキーでも解が微妙に変わりうる。既存タイムラインを実機で再生して差を確認する。許容できない差が出た場合は、SE コントローラ側の解法を寄せるか、本設計の見直しを検討する
- **拡張ボーン候補の差**: MTE の候補は `SkinnedMeshRenderer.bones` のうち `IsDefaultBoneName` でなく `IsVisibleBoneName` を満たすものだけ。SE のツリーはスロット配下の全 Transform（`_nub` を除く）なので、SE でチェックできるが候補に無いボーンが存在しうる。無視される（キーが打てない）ことを許容する
- **`isAnime` のゲート緩和**: 編集モード外でも IK が効くようになるため、固定を消し忘れたメイドが再生中に意図せず固定される可能性がある。トグルは既定 OFF、固定 OFF 時は無効表示にして踏みにくくする
