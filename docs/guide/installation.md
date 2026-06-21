# インストール

BG2VR は **BepInEx 5（Unity Mono）** の上で動く drop-in プラグインです。
配布 zip には BepInEx 本体は含まれないため、まず BepInEx を用意してから BG2VR を重ねます。
以下はすべて **`BUNNY GARDEN 2.exe` があるインストールフォルダ**
（例: `...\steamapps\common\BUNNY GARDEN 2\`）で作業します。

::: tip FixMod と共存できます
本プラグインは BepInEx 5（Unity Mono）で動作するため、同じ BepInEx 5 で動く FixMod と同居できます。
FixMod は既定が BepInEx 5 なので、その環境にそのまま追加できます。
（FixMod を BepInEx 6 ビルドで使っている場合は、両方を 1 つの BepInEx に載せるため FixMod 側も
BepInEx 5 ビルドに揃えてください。）

FixMod: [kazumasa200/BunnyGarden2FixMod](https://github.com/kazumasa200/BunnyGarden2FixMod)
:::

## 1. BepInEx 5 を用意する

- **導入済みの場合**（FixMod の既定ビルド等）… そのまま使えます（手順 2 へ）。同じ BepInEx 5 上の FixMod と同居するので退避は不要です。
- **未導入の場合**… [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) の win-x64 版（`BepInEx_win_x64_5.4.23.5.zip`）をダウンロードし、ゲームを終了した状態でインストールフォルダに解凍します。`BepInEx`・`winhttp.dll` が `BUNNY GARDEN 2.exe` と同じ階層に並ぶのが正しい配置です。

## 2. BG2VR を導入する

1. [BG2VR Releases](https://github.com/kidonaru/BG2VR/releases) から最新の `BG2VR-vX.Y.Z.zip` をダウンロードします。
2. ゲームを終了した状態で zip を解凍し、インストールフォルダに `BepInEx` と `BUNNY GARDEN 2_Data` を配置します。
   - `BepInEx\plugins\BG2VR\` … VR 本体 DLL
   - `BUNNY GARDEN 2_Data\Plugins\x86_64\UnityGraphicsHelper.dll` … D3D12 ネイティブ

## 3. D3D12 起動オプションを設定する（必須）

Steam ライブラリで **BUNNY GARDEN 2 → プロパティ → 起動オプション** に **`-force-d3d12`** を追加します。
出荷構成は D3D12 で、NVIDIA ドライバ由来のフリーズを構造的に回避します。

![Steam の起動オプションに -force-d3d12 を追加した画面](/images/ss_01.png)

## 4. 起動する

VR HMD を接続してゲームを起動します。設定はゲーム内 **F10** パネル、または `BepInEx\config\BG2VR.cfg`
から変更できます（[設定](/guide/configuration) 参照）。
