/**
 * 图标本地缓存与官方 CDN 兜底解析工具 (src/utils/assetUrl.ts)
 * 架构规范：
 * 1. 默认优先从本地缓存路由 (/cache/items/, /cache/heroes/, /cache/abilities/) 流式读取
 * 2. 本地不存在或未命中时，后端自动透明拉取 CDN 并落盘，或前端 @error 兜底回退至官方 CDN
 * 3. 严格防范破图与循环 error 事件
 */

const STEAM_CDN_BASE = 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react'
const BLANK_IMAGE_PLACEHOLDER = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64"><rect width="64" height="64" fill="%231e293b"/></svg>'

/**
 * 清理英雄英文名（去除 npc_dota_hero_ 前缀）
 */
export function cleanHeroName(rawName: string): string {
  if (!rawName) return ''
  return rawName.replace(/^npc_dota_hero_/, '').trim()
}

/**
 * 清理物品键名（去除 item_ 前缀）
 */
export function cleanItemKey(rawKey: string): string {
  if (!rawKey) return ''
  return rawKey.replace(/^item_/, '').trim()
}

/**
 * 获取物品官方 CDN URL 兜底
 */
export function getItemCdnUrl(rawKey: string): string {
  const clean = cleanItemKey(rawKey)
  if (!clean) return BLANK_IMAGE_PLACEHOLDER
  if (clean === 'recipe' || clean.startsWith('recipe_')) {
    return `${STEAM_CDN_BASE}/items/recipe.png`
  }
  return `${STEAM_CDN_BASE}/items/${clean}.png`
}

/**
 * 获取物品图标 URL (本地缓存优先，落盘兜底)
 */
export function getItemIconUrl(rawKey: string): string {
  const clean = cleanItemKey(rawKey)
  if (!clean) return BLANK_IMAGE_PLACEHOLDER
  if (clean === 'recipe' || clean.startsWith('recipe_')) {
    return '/cache/items/recipe.png'
  }
  return `/cache/items/${clean}.png`
}

/**
 * 获取英雄头像官方 CDN URL 兜底
 */
export function getHeroCdnUrl(rawName: string): string {
  const clean = cleanHeroName(rawName)
  if (!clean) return BLANK_IMAGE_PLACEHOLDER
  return `${STEAM_CDN_BASE}/heroes/${clean}.png`
}

/**
 * 获取英雄头像图标 URL (本地缓存优先，落盘兜底)
 */
export function getHeroIconUrl(rawName: string): string {
  const clean = cleanHeroName(rawName)
  if (!clean) return BLANK_IMAGE_PLACEHOLDER
  return `/cache/heroes/${clean}.png`
}

/**
 * 获取英雄技能官方 CDN URL 兜底
 */
export function getAbilityCdnUrl(rawAbilityName: string): string {
  if (!rawAbilityName) return BLANK_IMAGE_PLACEHOLDER
  return `${STEAM_CDN_BASE}/abilities/${rawAbilityName.trim()}.png`
}

/**
 * 获取英雄技能图标 URL (本地缓存优先，落盘兜底)
 */
export function getAbilityIconUrl(rawAbilityName: string): string {
  if (!rawAbilityName) return BLANK_IMAGE_PLACEHOLDER
  return `/cache/abilities/${rawAbilityName.trim()}.png`
}

/**
 * 通用图片加载失败降级事件处理函数
 * 逻辑：初次失败降级为 CDN，若 CDN 亦失败则降级为占位色块，杜绝死循环
 */
export function handleImageFallback(event: Event, fallbackUrl: string): void {
  const target = event.target as HTMLImageElement | null
  if (!target) return

  const currentSrc = target.src || ''
  if (!target.dataset.triedFallback && fallbackUrl && currentSrc !== fallbackUrl) {
    target.dataset.triedFallback = 'true'
    target.src = fallbackUrl
  } else {
    target.src = BLANK_IMAGE_PLACEHOLDER
  }
}
