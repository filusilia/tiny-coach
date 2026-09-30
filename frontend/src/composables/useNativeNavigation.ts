/**
 * 原生桌面导航与菜单/托盘事件集成 Composable (src/composables/useNativeNavigation.ts)
 * 职责：
 * 1. 监听 Go 后端托盘与窗口菜单派发的 "navigate_to" 事件
 * 2. 驱动 Vue Router 跳转并同步 Tab 状态
 * 3. 组件销毁或卸载时完整清理事件监听
 */

import { onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAppStore } from '@/stores/app.ts'

interface WailsRuntimeWindow extends Window {
  wails?: {
    Events?: {
      On?: (eventName: string, callback: (payload: unknown) => void) => () => void
      Off?: (eventName: string) => void
    }
  }
}

export function useNativeNavigation() {
  const router = useRouter()
  const appStore = useAppStore()
  let unlistenNav: (() => void) | null = null
  let unlistenOverlay: (() => void) | null = null

  function handleNavigation(targetRoute: string) {
    if (!targetRoute) return

    const route = targetRoute.toLowerCase()
    if (route.includes('settings')) {
      appStore.setCurrentTab('settings')
      router.push('/settings').catch(() => {})
    } else if (route.includes('coach')) {
      appStore.setCurrentTab('coach')
      router.push('/coach').catch(() => {})
    } else if (route.includes('analysis')) {
      appStore.setCurrentTab('analysis')
      router.push('/analysis').catch(() => {})
    } else if (route.includes('heroes')) {
      appStore.setCurrentTab('heroes')
      router.push('/heroes').catch(() => {})
    } else if (route.includes('items')) {
      appStore.setCurrentTab('items')
      router.push('/items').catch(() => {})
    }
  }

  let onUnmountedCleanup: (() => void) | null = null

  onMounted(() => {
    const win = window as WailsRuntimeWindow
    if (win.wails?.Events?.On) {
      // 1. 监听原生菜单与托盘导航事件
      const cleanupNav = win.wails.Events.On('navigate_to', (eventPayload: unknown) => {
        const route = typeof eventPayload === 'string'
          ? eventPayload
          : (eventPayload as { data?: string })?.data ?? ''
        handleNavigation(route)
      })
      if (typeof cleanupNav === 'function') {
        unlistenNav = cleanupNav
      }

      // 2. 监听悬浮窗显隐切换原生事件 (Alt+F2 或系统菜单触发时的双向状态同步)
      const cleanupOverlay = win.wails.Events.On('overlay_state_change', (eventPayload: unknown) => {
        let active = false
        if (typeof eventPayload === 'boolean') {
          active = eventPayload
        } else if (eventPayload && typeof eventPayload === 'object') {
          const raw = (eventPayload as { data?: unknown }).data ?? eventPayload
          if (typeof raw === 'boolean') {
            active = raw
          } else if (Array.isArray(raw) && raw.length > 0 && typeof raw[0] === 'boolean') {
            active = raw[0]
          }
        }
        appStore.isOverlayRunning = active
        appStore.isOverlayOpen = active
      })
      if (typeof cleanupOverlay === 'function') {
        unlistenOverlay = cleanupOverlay
      }
    }

    // 3. 跨窗口 localStorage 状态双重保活
    const handleStorage = (e: StorageEvent) => {
      if (e.key === 'aegis_overlay_status' && e.newValue) {
        try {
          const parsed = JSON.parse(e.newValue)
          if (typeof parsed.running === 'boolean') {
            appStore.isOverlayRunning = parsed.running
            appStore.isOverlayOpen = parsed.running
          }
        } catch {}
      }
    }
    window.addEventListener('storage', handleStorage)

    // 4. 窗口获得焦点时重新校准悬浮窗真实运行状态
    const handleFocus = () => {
      appStore.checkOverlayStatus()
    }
    window.addEventListener('focus', handleFocus)

    onUnmountedCleanup = () => {
      window.removeEventListener('storage', handleStorage)
      window.removeEventListener('focus', handleFocus)
    }
  })

  onUnmounted(() => {
    if (onUnmountedCleanup) {
      onUnmountedCleanup()
      onUnmountedCleanup = null
    }
    if (unlistenNav) {
      unlistenNav()
      unlistenNav = null
    }
    if (unlistenOverlay) {
      unlistenOverlay()
      unlistenOverlay = null
    }
    const win = window as WailsRuntimeWindow
    if (win.wails?.Events?.Off) {
      win.wails.Events.Off('navigate_to')
      win.wails.Events.Off('overlay_state_change')
    }
  })

  return {
    handleNavigation,
  }
}
