/**
 * 窗口模式判定工具 (src/utils/windowMode.ts)
 * 用于确定性同步判定当前窗口是否为独立桌面置顶悬浮窗模式
 */

export function checkIsOverlayWindow(): boolean {
  if (typeof window === 'undefined') return false
  const search = window.location.search || ''
  const hash = window.location.hash || ''
  const href = window.location.href || ''
  const params = new URLSearchParams(search)

  return params.get('window') === 'overlay' ||
    params.get('mode') === 'overlay' ||
    search.includes('window=overlay') ||
    search.includes('mode=overlay') ||
    hash.includes('window=overlay') ||
    hash.includes('mode=overlay') ||
    hash.includes('/overlay') ||
    href.includes('mode=overlay') ||
    window.name === 'game-overlay'
}
