/**
 * 客户端路由管理器 (src/router/index.ts)
 * 遵循 Wails 桌面环境铁律：使用 createWebHashHistory 避免本地单文件部署刷新 404
 */

import type { RouteRecordRaw } from 'vue-router'
import { createRouter, createWebHashHistory } from 'vue-router'
import AnalysisView from '../views/AnalysisView.vue'
import CoachView from '../views/CoachView.vue'
import HeroesView from '../views/HeroesView.vue'
import ItemsView from '../views/ItemsView.vue'
import SettingsView from '../views/SettingsView.vue'

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    redirect: '/coach',
  },
  {
    path: '/coach',
    name: 'Coach',
    component: CoachView,
    meta: { tab: 'coach', title: '实时教练' },
  },
  {
    path: '/analysis',
    name: 'Analysis',
    component: AnalysisView,
    meta: { tab: 'analysis', title: '战局复盘' },
  },
  {
    path: '/heroes',
    name: 'Heroes',
    component: HeroesView,
    meta: { tab: 'heroes', title: '英雄图鉴' },
  },
  {
    path: '/items',
    name: 'Items',
    component: ItemsView,
    meta: { tab: 'items', title: '物品合成' },
  },
  {
    path: '/settings',
    name: 'Settings',
    component: SettingsView,
    meta: { tab: 'settings', title: '系统设置' },
  },
  {
    path: '/overlay',
    name: 'Overlay',
    component: () => import('../views/OverlayView.vue'),
    meta: { isOverlay: true, title: '经济雷达悬浮窗' },
  },
]

export const router = createRouter({
  history: createWebHashHistory(),
  routes,
})

router.beforeEach((to, _from, next) => {
  if (typeof window !== 'undefined') {
    const search = window.location.search || ''
    const hash = window.location.hash || ''
    const href = window.location.href || ''
    const params = new URLSearchParams(search)

    const isOverlayMode = params.get('window') === 'overlay' ||
      params.get('mode') === 'overlay' ||
      search.includes('window=overlay') ||
      search.includes('mode=overlay') ||
      hash.includes('window=overlay') ||
      hash.includes('mode=overlay') ||
      hash.includes('/overlay') ||
      href.includes('mode=overlay') ||
      window.name === 'game-overlay'

    if (isOverlayMode && to.path !== '/overlay') {
      return next('/overlay')
    }
    if (!isOverlayMode && to.path === '/overlay') {
      // 主窗口绝不内嵌渲染悬浮窗，杜绝主窗口层级混淆与无法脱出误解
      return next('/coach')
    }
  }
  next()
})
