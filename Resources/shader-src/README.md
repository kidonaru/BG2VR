# コントローラモデル用 shader bundle のソース

`Resources/bg2vr_shaders`（埋め込み AssetBundle・`BundledShaders.cs` がロード）の**ソース**。
ゲーム本体には unlit-opaque-textured shader が無い（lit 系は VR eye パイプラインで真っ黒・
unlit 既製 = `UI/Default` は ZWrite Off でボタン面が崩れる・実測 2026-06-08）ため、自前 shader を同梱する。

## ファイル

- `ControllerUnlit.shader` — unlit / texture / ZWrite On / `_Cull` 可変（既定 Back、手モデルは実行時 Off=両面）/
  `_ZTest` 可変（既定 Always、実行時に LEqual=4 を設定＝自己オクルージョン正常 + queue 4002 で UI より手前）。
  `_Cull` は手モデルを片手 FBX の mirror（negative X scale）で出すとき winding 反転で裏面化するのを防ぐため追加
- `BG2VRBundleBuilder.cs` — Editor ビルドスクリプト（メニュー `BG2VR/Build Shader Bundle`）

## 再ビルド手順

ゲームと**同一 Unity バージョン**（現状 `6000.0.61f1`）のプロジェクトに上記 2 ファイルを置く
（外部プロジェクト例: `C:\Users\kidon\BG2VR.Unity` の `Assets/BG2VRShaders/` と `Assets/Editor/`）。

```bash
# batchmode ビルド（エディタは閉じておく）
"/mnt/c/Program Files/Unity/Hub/Editor/6000.0.61f1/Editor/Unity.exe" \
  -batchmode -quit -nographics \
  -projectPath "C:\Users\kidon\BG2VR.Unity" \
  -executeMethod BG2VRBundleBuilder.Build \
  -logFile "C:\Users\kidon\BG2VR.Unity\bundlebuild.log"
# 出力 BundleOutput/bg2vr_shaders を BG2VR/Resources/bg2vr_shaders へ上書きコピー
```

- bundle 内 shader はアセット名で引けないため、`BundledShaders` は `LoadAllAssets<Shader>()` の先頭を使う
- Unity バージョンが変わったら bundle を再ビルドしないとゲーム側でロード不可になる

## コントローラモデル bundle (`bg2vr_models`) の bake

Meta 公式 Touch Plus モデルを同梱 bundle 化する（`BundledControllerModels.cs` がロード）。
入手元 = Meta for Developers「Meta Quest Hardware Art」(`oculus-controller-art-v1.8`)。
**実 FBX は複数メッシュ + リグ階層**（本体 `oculus_controller_l_MeshX` + 電池表示 quad + ボーン）。
ローダは Mesh 単位でなく **GameObject ルートをアセット名 `MetaQuestTouchPlus_Left`/`_Right` で引いて Instantiate** し、
全 renderer をコード側で `ControllerUnlit` + BaseColor_AO に再マテリアルする。

`BG2VR.Unity`（`6000.0.61f1`）の `Assets/BG2VRModels/` に**この 4 ファイルだけ**を配置
（`oculus-controller-art-v1.8/Meta Quest Touch Plus/` から）:
- models/ `MetaQuestTouchPlus_Left.fbx`
- models/ `MetaQuestTouchPlus_Right.fbx`
- textures/ `MetaQuestTouchPlus_Left_BaseColor_AO.png`  ← **`_BaseColor`(AO 無) や `_BaseColor_AO_AlphaRoughness` ではない**
- textures/ `MetaQuestTouchPlus_right_BaseColor_AO.png`  ← right は小文字

import 設定:
- テクスチャ: Max Size = 1024、sRGB (Color Texture) = ON（unlit でそのまま albedo として使う）
- FBX: **Rig > Animation Type = None**（静的表示＝スケルトン/Animator 不要・MeshRenderer で import される）。
  Scale Factor を実寸（コントローラ ~15cm）に。マテリアルは import 時の自動 lit を使わない（コードで再マテリアル）
- **AssetBundle タグ**: 上記 4 アセットだけに `bg2vr_models` を設定（他派生テクスチャは入れない＝
  `LoadAllAssets` の fallback が誤テクスチャを拾わないため）

```bash
# batchmode ビルド（エディタは閉じておく）
"/mnt/c/Program Files/Unity/Hub/Editor/6000.0.61f1/Editor/Unity.exe" \
  -batchmode -quit -nographics \
  -projectPath "C:\Users\kidon\BG2VR.Unity" \
  -executeMethod BG2VRBundleBuilder.BuildModels \
  -logFile "C:\Users\kidon\BG2VR.Unity\bundlebuild.log"
# 出力 BundleOutput/bg2vr_models を BG2VR/Resources/bg2vr_models へ上書きコピー
```

- ローダは `LoadAsset<GameObject>("MetaQuestTouchPlus_Left")` 等のアセット名で引く（取れない時のみ名前 substring fallback）
- `BG2VR.csproj` の `bg2vr_models` 埋め込みは `Condition="Exists(...)"` で、bake 前は skip され build が通る

### 手モデル（Hand for VR）の bake

コントローラの代わりに手モデルを出す。per-hand 種別は `HandModelSelector`（grip+トリガーで Controller⇔Hand を切替・既定は両手 Controller）。**コントローラと同じ `bg2vr_models` bundle に同梱**する。
モデル = Hand for VR by FFeller（Sketchfab・**CC-BY 4.0**・帰属は `THIRD_PARTY_NOTICES.txt`）。
**片手（左手）FBX** を入れ、対の手（右）は実行時に negative X scale で mirror する（`ControllerModelRunner` + shader `_Cull` Off）。

`BG2VR.Unity`（`6000.0.61f1`）の `Assets/BG2VRModels/` に配置（DL 済み）:
- `Player hand.fbx`（左手・ローダは GameObject ルートをアセット名 `Player hand` で引く＝`BundledControllerModels.HandModelName`）
- 色を別 PNG で使う場合はそれも配置し `BG2VRBundleBuilder.ModelAssets` に追加（FBX 埋め込みテクスチャは依存として自動同梱される）

import 設定（`Player hand.fbx`）:
- **Rig > Animation Type = None**（静的表示・指固定）
- **Scale Factor は既定のままでよい**（globalScale=1 import で実寸が小さいので、コード側 `HandModelBaseScale=1.0`（`ControllerModelRunner`）で手元実寸に合致。実寸の微調整は F10 の `手拡大率`）
- マテリアルは import 時の自動 lit を使わない（コードで unlit に再マテリアル）。テクスチャがあれば sRGB ON

bake は **`BG2VRBundleBuilder.ModelAssets` に `Player hand.fbx` を追加済み**なので、コントローラと同じ `BG2VR/Build Controller Model Bundle`（`-executeMethod BG2VRBundleBuilder.BuildModels`）で `bg2vr_models` を再 bake → `BG2VR/Resources/bg2vr_models` へ上書き。
shader に `_Cull` を追加したため **`bg2vr_shaders` も再 bake** する（`BG2VRBundleBuilder.Build`）。

### カラオケ右手サイリウム（`Saliyum_Pink.fbx`）の bake

カラオケ ミニゲーム中に右手へサイリウムを出す（`HandModelKind.GlowStick`）。**自作モデル＝テクスチャ無し・
5 パーツ（Button/Core/Handle/Ring/Tube）／5 Phong マテリアル**。タンバリンと同じ**色駆動プロップ**で、
各パーツの DiffuseColor を `ControllerModelRunner.AssignColorDrivenMaterials` が runtime で unlit へ転写する。

`BG2VR.Unity`（`6000.0.61f1`）の `Assets/BG2VRModels/` に `Saliyum_Pink.fbx` を配置（DL/作成済み）。

import 設定（`Saliyum_Pink.fbx`）:
- **Rig > Animation Type = None**（静的表示）。
- **マテリアル取込は残す**（`materialImportMode = None` を**適用しない**＝カメラ FBX と違う点。None だと
  submesh の `.color`/`_BaseColor` が読めず色駆動が成立しない）。`BG2VRBundleBuilder.BuildModels` の None
  ループ対象はカメラ FBX のみ。
- AssetBundle タグの手動設定は不要（`AssetBundleBuild` が明示 `assetNames` で `ModelAssets` を指定する）。

bake は `BG2VRBundleBuilder.ModelAssets` に `Saliyum_Pink.fbx` を追加済みなので、コントローラと同じ
`-executeMethod BG2VRBundleBuilder.BuildModels` で再 bake → `BG2VR/Resources/bg2vr_models` へ上書き。
bake ログの `color-driven submesh material 'Mat_*' ... color=...` で各パーツ色が灰
`(0.500, 0.500, 0.500)` や near-black に落ちていないか確認すること（落ちている＝色が読めていない）。
スケールは `asset 'Saliyum_Pink' ... native bounds size=...` を見て `ControllerModelRunner.GlowStickBaseScale`
を確定（実寸 ~0.2m）。実機微調整は F10 `KaraokeGlowScale`。
