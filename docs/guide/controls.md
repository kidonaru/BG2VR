# 操作方法

OpenXR コントローラの割当一覧です。多くの挙動は **F10 設定パネル**で有効/無効・しきい値を変更できます
（[設定](/guide/configuration) 参照）。下表の「Config」列は対応する設定名です。

## コントローラ ボタン割当

| 物理ボタン | 手 | 操作 | Config |
| --- | --- | --- | --- |
| A | 右 | 決定 / 会話送り | `EnableVrButtons` |
| B（短押し） | 右 | 戻る / キャンセル | `EnableVrButtons` |
| B（長押し） | 右 | 再センター（視点リセット） | 常時有効 |
| X | 左 | バックログ表示 | `EnableVrButtons` |
| Y（長押し中） | 左 | 既読スキップ | `EnableVrButtons` |
| メニューボタン | 左 | ポーズメニュー開閉 | `EnableVrPauseButton` |
| 右スティック 4 方向 | 右 | UI ナビ（上下左右の選択） | `VrNavStickThreshold` |
| 左スティック | 左 | カメラ / アイテム回転 | `VrRStickDeadzone` |
| スティック押し込み | 左 or 右 | オート切替 | `EnableVrButtons` |
| 両スティック押し込み（長押し） | 左+右 | F10 設定パネル開閉 | — |
| トリガー | 自動選択された手 | レーザーで UI クリック | `EnableVrPointer` |
| グリップ | 左 or 右 | 空間を掴んで移動 | `EnableGrabMove` |

> VR ボタン割当を無効（`EnableVrButtons = false`）にすると、再センターのみ「B 単押し（左右いずれか）」で使えます。

## 移動（ロコモーション）

| 操作 | やり方 | Config |
| --- | --- | --- |
| 空間掴み移動 | グリップを握り、手を動かすと視点が平行移動・回転 | `EnableGrabMove` |
| スティック平行移動 | グリップ + 同じ手のスティックで頭基準に移動 | `EnableStickMove` / `StickMoveSpeed` |
| 移動量リセット | 両手グリップを約 1 秒長押し | — |

## ワールド UI の操作

- **レーザーを出す手**: 最後にトリガーを引いた手が自動選択されます（初期は右手）。
- **クリック**: パネルにレーザーを当て、トリガーを引くと選択・会話送り。
- **パネル調整**: パネルにレーザーを当てると下部に調整ボタン（移動 / 拡大 / 曲面）が表示されます。
  - 移動・拡大: 調整ボタン上でトリガーを押したままコントローラを動かす
  - 曲面切替: 調整ボタンをトリガーで単押し
- **固定位置の保存 / 消去**: 下記ホットキー（既定 Alt+S / Alt+D）。

## ミニゲーム別ジェスチャ

| ミニゲーム | 操作 | Config |
| --- | --- | --- |
| カラオケ | 左手を振る＝タンバリン（ZL）/ 右手を振る＝ガヤ（ZR） | `KaraokeShakeEnabled` |
| カルタ | 右スティック＝顔ボタンの札 / 左スティック＝十字キーの札 | `KarutaStickEnabled` / `KarutaStickThreshold` |
| 手押し相撲 | 両手を前に押し出す | `HandSumoPushEnabled` |
| あ〜ん（食べる側） | 食べ物を顔に近づける（HMD と食べ物の距離で判定） | `AhhnForPlayerEatDistance` |
| あ〜ん（食べさせる側） | 食べ物が右手に追従。相手の口元へ運び右トリガーで判定 | `AhhnForCastEatDistance` |
| チェキ | 撮影中は視点固定。右スティック上下でズーム（FOV 調整） | `ShowChekiCamera` / `ChekiCameraRightHand` |
| ドリンク | グラスモデルが手元に追従表示される | `DrinkGlassScale` |

> ジェスチャの発火しきい値（振りの強さ等）は F10 パネルで調整できます。
> 速度情報を供給しないランタイム（VDXR 等）では一部の振り判定が反応しないことがあります。

## ホットキー

| 操作 | 既定キー | Config |
| --- | --- | --- |
| 設定パネル開閉 | F10 | — |
| 固定位置を保存 | Alt + S | `SavePinnedPose` / `PinnedPoseModifier` |
| 固定位置を消去 | Alt + D | `ClearPinnedPose` / `PinnedPoseModifier` |

> 全設定の一覧は [設定リファレンス](/configs) を参照してください。
