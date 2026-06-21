import { defineConfig } from 'vitepress';

export default defineConfig({
  lang: 'ja-JP',
  title: 'BG2VR',
  description: 'Bunny Garden 2 を VR 化する BepInEx プラグインのユーザーガイド',
  base: '/BG2VR/',
  themeConfig: {
    nav: [
      { text: 'ガイド', link: '/' },
      { text: '設定リファレンス', link: '/configs' },
    ],
    sidebar: [
      {
        text: 'ガイド',
        items: [
          { text: 'はじめに', link: '/' },
          { text: 'インストール', link: '/guide/installation' },
          { text: '機能一覧', link: '/guide/features' },
          { text: '操作方法', link: '/guide/controls' },
          { text: '設定', link: '/guide/configuration' },
          { text: 'ソースからビルド', link: '/guide/build' },
        ],
      },
      {
        text: 'リファレンス',
        items: [{ text: '設定リファレンス', link: '/configs' }],
      },
    ],
    search: { provider: 'local' },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/kidonaru/BG2VR' },
    ],
  },
});
