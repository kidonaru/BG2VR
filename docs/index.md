<script setup>
import VueTweet from 'vue-tweet'
</script>

# はじめに

**BG2VR** は『Bunny Garden 2』(Steam) を VR 化する非公式の BepInEx プラグインです。
OpenXR 対応 HMD で一人称 VR としてプレイできるようにします。ゲーム本体には手を入れず、
BepInEx + Harmony で VR レンダリング・入力・UI 投影を後付けします。

> ⚠️ **非公式・自己責任**でご利用ください。ゲーム本体のアップデートで動作しなくなる場合があります。

## 紹介動画

<ClientOnly>
  <VueTweet tweet-url="https://x.com/kidonaru/status/2068638040030163116">
    <template #error>
      動画が表示できない場合は <a href="https://x.com/kidonaru/status/2068638040030163116" target="_blank" rel="noreferrer">X で見る</a>。
    </template>
  </VueTweet>
</ClientOnly>

## 動作要件

- **Bunny Garden 2**（Steam）
- **BepInEx 5**（Unity Mono build。BepInEx 5 上の FixMod と共存可）
- **OpenXR 対応 VR HMD**（Meta Quest + Link / Air Link / Virtual Desktop など）

## 次に読む

- [インストール](/guide/installation) — 導入手順
- [機能一覧](/guide/features) — 提供する VR 機能
- [操作方法](/guide/controls) — コントローラ割当・ジェスチャ
