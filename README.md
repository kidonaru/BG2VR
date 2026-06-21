# BG2VR

**Bunny Garden 2 を VR 化する BepInEx プラグイン**（非公式・自己責任でご利用ください）

OpenXR 対応 HMD で『Bunny Garden 2』を一人称 VR としてプレイできるようにする companion MOD です。
ゲーム本体には手を入れず、BepInEx + Harmony で VR レンダリング・入力・UI 投影を後付けします。

> ⚠️ バージョンは 0.x（開発中）です。互換性は保証されません。

## 主な機能

- **VR レンダリング** — 両眼立体視 + HMD ヘッドトラッキング（OpenXR backend）
- **シームレスな遷移** — シーン遷移・環境切替で視界を保ち、遷移絵柄も VR 空間に表示
- **Comfort 設定** — World Scale / 目の高さ / 酔いカメラ抑制 / フレームペーシング（リプロジェクション由来のちらつき対策）
- **ワールド UI 投影** — ゲーム UI を VR 空間の曲面パネルに投影し、コントローラのレーザーで操作（位置・距離・サイズ・曲面を調整可能、固定位置の保存も）
- **手元モデル** — コントローラ実機モデル / アニメ調ハンドモデルを表示・切替
- **ジェスチャ & ゲームパッド入力** — ゲームパッド準拠のボタン割当、カラオケの振り入力（タンバリン / サイリウム）、カルタのスティック操作など
- **空間音声** — Steam Audio (HRTF) によるキャストボイスの 3D 化
- **視線追従** — 顔 / 虹彩の HMD 追従（Head / Eye Look-At）
- **ミニゲーム対応** — チェキ・手押し相撲・あ〜ん・カラオケ等の VR 操作対応

## 動作要件

- **Bunny Garden 2**（Steam）
- **BepInEx 6**（BepInEx Unity Mono build）
- **Doorstop 4.5.0**（D3D12 に必須。4.3.0 等の古い版はクラッシュします）
- **OpenXR 対応 VR HMD**（Meta Quest + Link / Air Link / Virtual Desktop など）

## インストール

1. [Releases](https://github.com/kidonaru/BG2VR/releases) から最新の `BG2VR-vX.Y.Z.zip` をダウンロード。
2. ゲームを終了する（DLL がロックされるため）。
3. zip を `BUNNY GARDEN 2` のインストールフォルダ（例: `...\steamapps\common\BUNNY GARDEN 2\`）に解凍する。
   - `BepInEx\plugins\BG2VR\` … VR 本体 DLL（自動配置）
   - `BUNNY GARDEN 2_Data\Plugins\x86_64\UnityGraphicsHelper.dll` … D3D12 ネイティブ（自動配置）
4. ゲームを起動し、VR HMD を接続する。

> 前提として **BepInEx 6 + Doorstop 4.5.0** を先に導入しておく必要があります。

設定はゲーム内 F10 パネル、または `BepInEx\config\com.BG2VR.cfg` から変更できます。

## ソースからビルド

ゲーム本体（Managed DLL）が必要なため、Bunny Garden 2 をインストール済みの環境でビルドします。

```bash
# submodule（VR コア fork + UniverseLib）込みで取得
git clone --recursive https://github.com/kidonaru/BG2VR.git
cd BG2VR

# VR コア fork → BG2VR の順にビルド（fork を先に）
dotnet build UnityVRMod/src/UnityVRMod.csproj -c BIE_Unity_Mono_OpenXR_Release
dotnet build BG2VR.csproj -c Release -p:VrModBackend=OpenXR -p:VrModConfig=UnityVRMod.BepInEx.Mono
```

- ゲーム参照 DLL は初回ビルド時にゲームの `*_Data/Managed` から自動 populate されます（別パスは `-p:UnityManagedDir=...` で指定）。
- リリース用の drop-in zip 生成は `tools/release.sh`（`--dry-run` で検証可）。

## ライセンス

- 本プラグインは **GPL v3** です（[LICENSE](LICENSE) 参照）。
- VR コアは [UnityVRMod](https://github.com/kidonaru/UnityVRMod) の fork（GPL v3）を同梱しています。
- 同梱サードパーティアセット（コントローラ / ハンド / 各種 3D モデル・Steam Audio）の帰属は [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt) を参照。
