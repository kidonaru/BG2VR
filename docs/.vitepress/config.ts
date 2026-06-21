import { defineConfig } from 'vitepress';

// GitHub Pages のサブパス。favicon 等 head のリソースは base を自動付与されないため共有する
const base = '/BG2VR/';

export default defineConfig({
  lang: 'ja-JP',
  title: 'BG2VR',
  description: 'Bunny Garden 2 を VR 化する BepInEx プラグインのユーザーガイド',
  base,
  head: [['link', { rel: 'icon', href: `${base}favicon.ico` }]],
  themeConfig: {
    logo: '/favicon.png',
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
