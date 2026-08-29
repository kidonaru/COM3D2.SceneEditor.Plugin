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
- **脱衣の MaskMode 非対称** (S1 で確認): 脱衣ウィンドウの `MaidUndressController.SetUndressed` は MaskMode (Nude 等) が効いていると個別マスクと干渉するため事前に `SetMaskMode(None)` するが、レイヤーの書き込み経路 `DressUtils.SetSlotVisible` にはその正規化が無い。Inspector はレイヤーと経路を揃える判断をしたため同じ制約を持つ。解消するならレイヤー側 (`DressUtils`) に入れるべきで、Inspector 単体では直さない

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

#### S0 で見送った改善 (S1 以降で対応)

- ~~**非表示中ガードの重複**: 「退避中は表示に戻す際に上書きされるため操作させない」判定 + 警告ラベルが `InspectorWindow` に 3 箇所、`MotionItemInspector` に 1 箇所の計 4 箇所へ複製されている。共有ヘルパーへ集約する~~ → S1 の MoveTimelineLayer 対応で `HiddenMaidGuard` へ集約済み (5 箇所目を足す機会に実施)

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

## 7. Loop 実行プロトコル(S1〜S4 自走用)

S0 の基盤(Registry / Bridge / InspectorWindow 分岐)は実装済み。残りのレイヤー対応を `/loop` で 1 反復ずつ進めるための手順。

### 反復単位

**1 反復 = 下記チェックリストの未完了項目 1 つ**(基本は 1 レイヤー。共有元ウィンドウが同じレイヤー群は 1 項目にまとめてある)。上から順に消化する。

### 1 反復の手順

1. チェックリストから最初の未完了項目を選ぶ
2. **調査**: 対象レイヤーのボーンメニュー構造(項目名の形式・セット行の有無)と、共有元ウィンドウの該当行描画コードを読む。S0 の実装(`Timeline/ItemInspector/` 配下の既存プロバイダ、`FaceMorphRowDrawer` / `BoneSliderRowDrawer`)を参照パターンとする
3. **実装**:
   - 共有元ウィンドウの行描画を `〜RowDrawer` へ抽出(純リファクタリング。変更追跡・履歴・まばたき停止等の編集ロジックも含めて移す。ラベル幅・スライダー幅は呼び出し側から調整可能にする)
   - `Timeline/ItemInspector/` に 1 レイヤー 1 ファイルでプロバイダを追加し、Registry へ登録
   - 逆方向同期「あり」の項目は `FindItemName` を実装(なしの項目は null 返却)
   - レイヤー本体(MTE 逐語コピー)には手を入れない
4. **テスト**: 抽出ヘルパー・逆引きマッピング等の純粋ロジックに単体テストを追加し、テストを実行して通す
5. **ビルド**: COM3D2 / COM3D25 両構成を MSBuild 直接実行で確認(ゲーム停止中に `debug.bat` を使わない)
6. **コードレビュー**: code-review スキルを実行し、妥当な指摘を取り込む
7. **spec 更新**: チェックリストの当該項目を `[x]` にし、その Phase の「実機確認項目」へ確認すべき観点を追記する(実機確認はユーザー操作待ちのため loop では行わない)
8. **コミット**: commit スキルでコミット
9. 全項目完了なら loop を停止して S5(実機通し確認)待ちであることを報告する

### 反復ごとの注意

- 判断に迷う仕様差(共有元が無い「レイヤー固有」表示の粒度など)は、§4 の表の「Inspector 表示」列を仕様とし、最小構成で実装する。過剰な作り込みはしない
- ビルド・テストが通らない状態でチェックを付けない・コミットしない
- 実機でしか確認できない挙動はブロッカーにせず、実機確認項目に積んで先へ進む

### 進行チェックリスト

Phase S1(メイド系):

- [x] EyesTimelineLayer(視線・瞳回転 / MaidFaceWindow 視線タブ / 逆方向なし)
- [x] ShapeKeyTimelineLayer(シェイプキー重み / ShapeKeyEditWindow `DrawMaidShapeKeys` / 逆方向なし)
- [x] UndressTimelineLayer(スロット表示トグル / レイヤー固有: DressSlotID 単位のため脱衣ウィンドウの行とは非対応 / 逆方向なし)
- [x] MoveTimelineLayer(メイド Transform / `DrawVector3Row` / 逆方向あり: `Select(maidのGameObject)`)
- [ ] DressTimelineLayer(簡易表示・レイヤー固有 / 逆方向なし)

Phase S1 実機確認項目(loop 中に追記):

- [ ] 瞳レイヤーで「視線」行を選択 → Inspector に瞳回転左右/上下スライダーが出て編集できる
- [ ] 瞳レイヤーで「注視」行を選択 → Inspector に注視先コンボが出る。メイド選択時はメイド・ポイントのコンボも続けて出る
- [ ] 注視先を手動 (None) 以外にすると瞳回転スライダーが非活性になる(表情ウィンドウと同じ)
- [ ] 「顔/瞳の固定化」が無効なときは Inspector に有効化を促す警告が出る
- [ ] 瞳位置・瞳サイズの行を選択 → 「(未対応)」表示になる(意図した制約)
- [ ] Inspector のコンボが正しく開閉し、表情ウィンドウのコンボと干渉しない
- [ ] 表情ウィンドウ視線タブのタイムライン視線セクションに退行が無い(抽出リファクタリングの確認)
- [ ] ラベル + コンボ行を持つ既存ウィンドウ(表情・シェイプキー・マテリアル・ボーン等)のコンボ幅に退行が無い(LabeledComboRow 抽出の確認)
- [ ] メイドシェイプレイヤーでシェイプキー行を選択 → Inspector に重みスライダー(0〜2)が出て編集できる
- [ ] Inspector 側で重みを変えると変更追跡チェックが自動で ON になり、シェイプキーウィンドウの表示と一致する
- [ ] 追跡チェックを OFF にすると重みが 0 に戻る(ウィンドウ側と同じ挙動)
- [ ] 着替えなどで対象 morph を失ったシェイプキーは「(このメイドには存在しません)」表示になる
- [ ] シェイプキーウィンドウの一覧表示・検索・更新ボタンに退行が無い(抽出リファクタリングの確認)
- [ ] 脱衣レイヤーでスロット行を選択 → Inspector に表示トグルが出て切り替えられる
- [ ] セット行(衣装 / 頭部衣装 / アクセ / めくれ)を選択 → 配下スロットのトグルがまとめて出る
- [ ] 何も装着していないスロットのトグルが無効化されている
- [ ] Inspector で切り替えた状態がそのままキーフレームに載る(脱衣ウィンドウのカテゴリ操作とは粒度が違う点の確認)
- [ ] マスクモード(Nude 等)が有効なときの Inspector トグルの効き方(レイヤー再生時と同じ挙動になるか。差があれば MaskMode の扱いを再検討する)
- [ ] 移動レイヤーで「移動」行を選択 → Inspector にメイドの位置/回転/拡縮が出て編集できる
- [ ] ギズモの Local/Global を切り替えると Inspector の表示座標系も切り替わる(Object 表示と同じ)
- [ ] メイドをビューポート/Hierarchy で選択 → 移動レイヤーの「移動」行が選択される(ループしない)
- [ ] Object 表示(通常のオブジェクト選択)の位置/回転/拡縮・拡縮連動トグル・アクティブトグルの履歴記録に退行が無い(抽出リファクタリングの確認)
- [ ] ボーン表示の拡縮行と拡縮連動トグルに退行が無い(ScaleRowDrawer 切り出しの確認)
- [ ] 退避(非表示)中のメイドでは Inspector に「非表示中は移動を操作できません」が出て編集できない
- [ ] IK・ボーン・ポーズの各表示で従来どおり非表示中の警告が出る(HiddenMaidGuard 集約の確認)
- [ ] 移動レイヤーを開いている間はメイドのルート選択が常に項目表示へ切り替わる(仕様どおりだが操作感に問題が無いか確認する)

Phase S2(モデル系):

- [ ] ModelTimelineLayer / BGModelTimelineLayer(モデル Transform / 逆方向あり: `Select(モデルGameObject)`)
- [ ] ModelBoneTimelineLayer(モデルボーン Transform / BoneEditWindow `DrawModelContent` / 逆方向あり: ボーン GameObject 選択)
- [ ] ModelShapeKeyTimelineLayer(ブレンドシェイプ重み / ShapeKeyEditWindow `DrawModelContent` / 逆方向なし)
- [ ] MaidMaterialTimelineLayer / ModelMaterialTimelineLayer / BGModelMaterialTimelineLayer(マテリアルプロパティ / MaterialEditWindow / 逆方向なし)

Phase S2 実機確認項目(loop 中に追記):

- (なし)

Phase S3(カメラ・ライト・背景):

- [ ] CameraTimelineLayer(カメラ位置/回転/距離/FoV / CameraWindow `DrawMainCameraContent` / 逆方向なし)
- [ ] SubCameraTimelineLayer(サブカメラ設定・レイヤー固有 / 逆方向なし)
- [ ] LightTimelineLayer(ライト Transform・色・強度 / LightWindow / 逆方向あり: `Select(ライトGameObject)`)
- [ ] BGTimelineLayer(背景 Transform / BackgroundWindow / 逆方向あり: 背景オブジェクト選択)
- [ ] BGColorTimelineLayer(背景色・地面設定 / BackgroundWindow `DrawBgColorRow` / 逆方向なし)

Phase S3 実機確認項目(loop 中に追記):

- (なし)

Phase S4(サウンド・演出系):

- [ ] VoiceTimelineLayer / SeTimelineLayer(再生パラメータ / SoundWindow / 逆方向なし)
- [ ] TextTimelineLayer(テキスト内容・スタイル・レイヤー固有 / 逆方向なし)
- [ ] StageLightTimelineLayer / StageLaserTimelineLayer / PsylliumTimelineLayer(演出パラメータ / LiveEffectWindow / 逆方向なし)
- [ ] PostEffectTimelineLayer 5 種(エフェクトパラメータ・レイヤー固有 / 逆方向なし)
- [ ] PngPlacementTimelineLayer(配置 PNG パラメータ / PngPlacementWindow / 逆方向は要調査: 不可なら実装せず理由を追記)
- [ ] AnimationTimelineLayer(アニメブレンド設定・レイヤー固有 / 逆方向なし)

Phase S4 実機確認項目(loop 中に追記):

- (なし)
