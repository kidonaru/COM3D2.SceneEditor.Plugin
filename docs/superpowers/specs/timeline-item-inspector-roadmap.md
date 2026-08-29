# タイムライン項目 ⇔ Inspector 連携ロードマップ

タイムラインウィンドウのボーンメニュー(ドープシート左列)で選択した項目を Inspector と双方向同期し、Inspector に「その項目の現在値の編集UI」(各個別ウィンドウと同じもの)を表示するためのロードマップ。レイヤー編集UIの個別ウィンドウ委譲(`docs/layer-window-duplication-survey.md`)の後続で、委譲で個別ウィンドウへ集約した編集UIを「選択項目単位」で Inspector からも使えるようにする。

作成日: 2026-08-29

## 1. 確定仕様(ブレインストーミング済み)

1. ボーンメニュー項目の選択で Inspector に出すのは**現在値の編集UI**(ライブ編集)。編集後にキーフレーム登録する流れ
2. **キーフレーム選択時は従来どおり KeyFrameInspector が優先**。メニュー項目表示はキーフレーム未選択時のみ
3. 同期は**双方向**。Inspector / 各ウィンドウ / ビューポート側の選択もタイムラインのメニュー選択へ反映する
4. 表示粒度は**選択項目のみ**。セット行(例:「目」)選択時は配下項目をまとめて縦に表示
5. **全レイヤー対応**を段階実施(本ロードマップ)
6. 描画は各ウィンドウの行描画を共通ヘルパーへ抽出して共有する(ウィンドウ側の見た目・挙動は変えない純リファクタリング)

### 設計上の決定

- **レイヤーの自動切り替えはしない**。逆方向同期は「現在レイヤー」のプロバイダで解決できる場合のみメニュー選択へ反映する(誤爆防止)
- プロバイダ未登録のレイヤーは従来動作(メニュー選択しても Inspector は変化しない)。段階移行を安全にするための既定
- レイヤー本体(MTE 逐語コピー)には手を入れない。プロバイダは別ファイル・登録制

## 2. アーキテクチャ(基盤 3 点)

### ① ITimelineItemInspector + TimelineItemInspectorRegistry

```csharp
public interface ITimelineItemInspector
{
    /// 選択中メニュー項目の現在値編集UIを描く(セット行は配下を展開して受け取る)
    void DrawItems(GUIView view, IList<MTEP.IBoneMenuItem> items);

    /// SelectionManager の選択状態から対応するメニュー項目名を逆引きする。
    /// 対応しない選択なら null(逆方向同期しない)
    string FindItemName(SelectionSnapshot selection);
}
```

- `TimelineItemInspectorRegistry`: レイヤー型 → プロバイダの登録辞書。`TimelineIntegration.RegisterLayer` と同じ場所で登録する
- プロバイダは `Timeline/ItemInspector/` 配下に 1 レイヤー 1 ファイル

### ② TimelineSelectionBridge (ManagerBase)

- **順方向**: ボーンメニューの選択集合(`isSelectedMenu` が真の項目)を毎フレーム監視し、変化時に選択項目リストを更新する。監視は項目数と選択フラグの合成バージョンで軽量化し、毎フレームのリスト再構築を避ける
- **逆方向**: `SelectionManager.onSelectionChanged` とボーン選択の変化を購読 → 現在レイヤーのプロバイダ `FindItemName` で逆引き → 該当項目の `SelectMenu(false)` 相当を実行
- **ループ防止**: ブリッジ経由の書き込み中は再入フラグで順方向検知を抑止する

### ③ InspectorWindow の表示分岐

優先順位(上が優先):

1. IK 選択(既存)
2. ボーン選択(既存)
3. ボーン編集モード(既存)
4. キーフレーム選択 → KeyFrameInspector(既存)
5. **タイムライン項目選択 → ItemInspector(新設)**
6. GameObject Inspector(既存)

## 3. 既知のリスク・注意点

- **`MaidBoneMenuItem.isSelectedMenu` の副作用経路は現状発火しない**。setter は `studioHack.SetBoneRotateVisible` への書き込み経路を持つが、`SceneEditorHack` は `HasBoneRotateVisible` をオーバーライドしていない(既定 false)ため、現状は単純なフラグとして動く(plan-review で静的解析済み)。将来別の StudioHackBase 実装を追加する場合のみ再考する
- `BoneSetMenuItem.isSelectedMenu` は「配下すべて選択」で真になる集計値。順方向監視の diff は子項目単位で取ること
- 変更追跡チェック(`FaceEditManager` 等)・履歴(`HistoryManager`)・まばたき停止などの編集ロジックは行描画ヘルパー側に含めて抽出する。Inspector 経由の編集がウィンドウ経由と挙動差を持ってはならない
- Inspector の横幅はウィンドウより狭い場合がある。抽出ヘルパーはラベル幅・スライダー幅を呼び出し側から調整できる形にする
- 順方向でメニュー項目を選択したまま別レイヤーへ切り替えた場合、選択集合はレイヤーごとに独立しているため、ブリッジは「現在レイヤーの選択集合」だけを見る

## 4. フェーズ分割

### Phase S0: 基盤 + パイロット 2 レイヤー

基盤 3 点(Registry / Bridge / InspectorWindow 分岐)を実装し、代表 2 レイヤーで両方向を通す。

| レイヤー | Inspector 表示 | 共有元 | 逆方向同期 |
|---|---|---|---|
| MorphTimelineLayer(表情) | モーフ行(追跡チェック+スライダー/トグル) | MaidFaceWindow `DrawMorphList` の 1 行分を `FaceMorphRowDrawer` へ抽出 | なし(モーフに対応する SelectionManager 選択概念が無い) |
| MotionTimelineLayer(メイドアニメ) | ボーンの Transform 編集(位置/回転) | InspectorWindow ボーン編集 UI(`DrawBoneContent` 系)を共有 | あり(`SelectBone` / ボーン編集選択 → 該当ボーン行) |

完了条件: 目閉じ選択で Inspector に目閉じスライダーが出て編集できる。Inspector でボーンを選ぶとタイムラインの該当行が選択される。ループ・NPE なし。

#### S0 実装済み・実機確認項目(次回ゲーム起動時)

- [ ] 表情レイヤーで「目閉じ」行を選択 → Inspector に目閉じスライダーが出て編集できる
- [ ] セット行(例: 目)を選択 → 配下モーフがまとめて表示される
- [ ] キーフレームを選択 → KeyFrameInspector が優先表示され、解除で項目表示に戻る
- [ ] モーションレイヤーでボーン行を選択 → Inspector にボーンスライダーが出る
- [ ] タイムライン未読込・メイド未解決時は双方向同期が動かない(意図した制約。Inspector 選択がタイムラインに反応しなくても正常)
- [ ] Inspector /ビューポートでボーンを選択 → タイムラインの該当行が選択される(ループしない)
- [ ] メニュー行クリックでボーン/IK 選択が降格して項目表示に切り替わる
- [ ] 簡易表示 (isEasyEdit) では項目表示が出ない
- [ ] レイヤー切り替え・タイムライン閉鎖で NPE が出ない
- [ ] 表情ウィンドウ・Inspector ボーン選択の従来挙動に退行が無い(抽出リファクタリングの確認)

### Phase S1: メイド系レイヤー

| レイヤー | Inspector 表示 | 共有元 | 逆方向 |
|---|---|---|---|
| EyesTimelineLayer(瞳) | 視線・瞳回転(タイムライン視線セクション) | MaidFaceWindow 視線タブの該当行 | なし |
| ShapeKeyTimelineLayer(メイドシェイプ) | シェイプキー重みスライダー | ShapeKeyEditWindow `DrawMaidShapeKeys` の行 | なし |
| UndressTimelineLayer(脱衣) | スロット表示トグル | MaidUndressWindow `DrawCategoryList` の行 | なし |
| MoveTimelineLayer(移動) | メイド Transform | Inspector の Transform 行(既存 `DrawVector3Row`) | あり(`Select(maidのGameObject)`) |
| DressTimelineLayer(衣装) | 選択項目の現在値(差分表示中心のため簡易表示) | レイヤー固有(共有元なし) | なし |

### Phase S2: モデル系レイヤー

| レイヤー | Inspector 表示 | 共有元 | 逆方向 |
|---|---|---|---|
| ModelTimelineLayer / BGModelTimelineLayer | モデル Transform | Inspector の Transform 行 | あり(`Select(モデルGameObject)`) |
| ModelBoneTimelineLayer | モデルボーン Transform | BoneEditWindow `DrawModelContent` の行 | あり(ボーン GameObject 選択) |
| ModelShapeKeyTimelineLayer | ブレンドシェイプ重み | ShapeKeyEditWindow `DrawModelContent` の行 | なし |
| MaidMaterialTimelineLayer / ModelMaterialTimelineLayer / BGModelMaterialTimelineLayer | マテリアルプロパティ | MaterialEditWindow の各 Draw 系の行 | なし |

### Phase S3: カメラ・ライト・背景

| レイヤー | Inspector 表示 | 共有元 | 逆方向 |
|---|---|---|---|
| CameraTimelineLayer | カメラ位置/回転/距離/FoV | CameraWindow `DrawMainCameraContent` の行 | なし |
| SubCameraTimelineLayer | サブカメラ設定(追従/FoV/ビューポート) | レイヤー固有(委譲先ウィンドウなし) | なし |
| LightTimelineLayer | ライト Transform・色・強度 | LightWindow の行 | あり(`Select(ライトGameObject)`) |
| BGTimelineLayer | 背景 Transform | BackgroundWindow の背景 Transform 行 | あり(背景オブジェクト選択) |
| BGColorTimelineLayer | 背景色・地面設定 | BackgroundWindow `DrawBgColorRow` / 地面 UI | なし |

### Phase S4: サウンド・演出系

メニュー行がトラック的なレイヤーは「選択項目の現在キー内容 + 該当ウィンドウの部分UI」を個別判断で出す。

| レイヤー | Inspector 表示 | 共有元 | 逆方向 |
|---|---|---|---|
| VoiceTimelineLayer / SeTimelineLayer | 再生パラメータ | SoundWindow の行 | なし |
| TextTimelineLayer | テキスト内容・スタイル | レイヤー固有 | なし |
| StageLightTimelineLayer / StageLaserTimelineLayer / PsylliumTimelineLayer | 各演出パラメータ | LiveEffectWindow の各タブの行 | なし |
| PostEffectTimelineLayer(5 種) | エフェクトパラメータ | レイヤー固有 | なし |
| PngPlacementTimelineLayer | 配置 PNG のパラメータ | PngPlacementWindow の行 | あり(PNG オブジェクト選択、実装可否は要調査) |
| AnimationTimelineLayer | アニメブレンド設定 | レイヤー固有 | なし |

### Phase S5: 実機通し確認

- 全レイヤーで「メニュー選択 → Inspector 表示 → 編集 → キーフレーム登録」の通し確認
- 双方向同期のループ・選択残留・レイヤー切り替え時の挙動確認
- `MaidBoneMenuItem` のボーン回転表示連動の挙動確認(S0 の判断の再検証)

## 5. スコープ外

- キーフレーム値の編集 UI 変更(KeyFrameInspector は現状維持)
- レイヤーの自動切り替え・レイヤーをまたぐ選択同期
- 各個別ウィンドウの見た目・機能変更(行描画の抽出は挙動を変えない)
- DCM 系レイヤー(未移植のまま)

## 6. 検証方針

- 抽出した行描画ヘルパー・逆引きマッピングなど純粋ロジックは `source/COM3D2.SceneEditor.Plugin.Tests` へ単体テストを追加
- UI 配線はビルド(COM3D2 / COM3D25 両構成)+ 実機確認(各 Phase 末尾。ゲーム起動中は DLL 差し替え不可のためまとめて実施可)
