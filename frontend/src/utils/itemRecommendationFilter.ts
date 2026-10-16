/**
 * 装备出装推荐过滤与角色分路匹配工具 (src/utils/itemRecommendationFilter.ts)
 * 职责：
 * 1. 过滤局内已购买或已升级的装备，避免重复推荐；
 * 2. 识别玩家分路定位 (辅助 vs 核心)，辅助分路严禁推荐输出装，核心分路优先输出与克制。
 */

/** 装备进阶衍生与合成同族映射表 (拥有任意高级或同族装备即视为已拥有) */
const ITEM_UPGRADE_FAMILIES: Record<string, string[]> = {
  // 辅助与保命通用
  force_staff: ['item_force_staff', 'item_hurricane_pike'],
  hurricane_pike: ['item_hurricane_pike'],
  ghost: ['item_ghost', 'item_ethereal_blade'],
  glimmer_cape: ['item_glimmer_cape'],
  spirit_vessel: ['item_spirit_vessel'],
  urn_of_shadows: ['item_urn_of_shadows', 'item_spirit_vessel'],
  cyclone: ['item_cyclone', 'item_wind_waker'],
  rod_of_atos: ['item_rod_of_atos', 'item_gleipnir'],
  heavens_halberd: ['item_heavens_halberd'],
  pipe: ['item_pipe'],
  aeon_disk: ['item_aeon_disk'],
  solar_crest: ['item_solar_crest'],
  pavise: ['item_pavise', 'item_solar_crest'],
  lotus_orb: ['item_lotus_orb'],
  sheepstick: ['item_sheepstick'],
  mekansm: ['item_mekansm', 'item_guardian_greaves'],
  guardian_greaves: ['item_guardian_greaves'],
  blink: ['item_blink', 'item_overwhelming_blink', 'item_swift_blink', 'item_arcane_blink'],

  // 核心输出克制
  black_king_bar: ['item_black_king_bar'],
  monkey_king_bar: ['item_monkey_king_bar'],
  silver_edge: ['item_silver_edge'],
  invis_sword: ['item_invis_sword', 'item_silver_edge'],
  skadi: ['item_skadi'],
  nullifier: ['item_nullifier'],
  bloodthorn: ['item_bloodthorn'],
  orchid: ['item_orchid', 'item_bloodthorn'],
  diffusal_blade: ['item_diffusal_blade', 'item_disperser'],
  basher: ['item_basher', 'item_abyssal_blade'],
}

/** 核心大哥输出专属装备 key 集合 (辅助分路严禁推荐) */
export const CORE_ONLY_ITEM_KEYS = new Set([
  'monkey_king_bar',
  'silver_edge',
  'skadi',
  'nullifier',
  'bloodthorn',
  'bfury',
  'desolator',
  'greater_crit',
  'butterfly',
  'radiance',
  'satanic',
  'mjollnir',
  'armlet',
  'mask_of_madness',
  'yasha',
  'manta',
])

/** 辅助专属团队/救人装 key 集合 (核心大哥一般不推荐此类出装) */
export const SUPPORT_ONLY_ITEM_KEYS = new Set([
  'glimmer_cape',
  'spirit_vessel',
  'ghost',
  'heavens_halberd',
  'pipe',
  'solar_crest',
  'holy_locket',
])

/**
 * 判定指定装备是否已在玩家物品栏、背包、储藏处或中立槽位中存在（包含进阶版）
 */
export function isItemOwned(itemKey: string, ownedItemNames: string[]): boolean {
  if (!ownedItemNames || ownedItemNames.length === 0) return false
  const lowerKey = itemKey.toLowerCase().replace(/^item_/, '')
  const family = ITEM_UPGRADE_FAMILIES[lowerKey]
  if (family) {
    if (family.some(target => ownedItemNames.some(owned => owned.toLowerCase() === target || owned.toLowerCase().includes(target)))) {
      return true
    }
  }
  // 通用前缀匹配与类名完全匹配
  const standardName = `item_${lowerKey}`
  return ownedItemNames.some(owned => {
    const o = owned.toLowerCase()
    return o === standardName || o === lowerKey || o.includes(lowerKey)
  })
}

export interface FilterableItem {
  itemKey: string
  itemName?: string
  reason?: string
  role?: string
}

/**
 * 依据分路定位与当前拥有的装备过滤推荐出装列表
 * @param items 原始推荐出装数组
 * @param ownedGsiNames GSI 采集到的已持有装备类名数组
 * @param isSupport 是否为辅助分路
 */
export function filterRoleAndOwnedRecommendations<T extends FilterableItem>(
  items: T[],
  ownedGsiNames: string[],
  isSupport: boolean,
): T[] {
  if (!items || items.length === 0) return []

  return items.filter(item => {
    const key = item.itemKey.toLowerCase().replace(/^item_/, '')

    // 1. 过滤已拥有或已升级的高阶装备
    if (isItemOwned(key, ownedGsiNames)) {
      return false
    }

    // 2. 辅助角色保护：如果是辅助，严禁推荐输出装
    if (isSupport) {
      if (item.role === 'core') return false
      if (CORE_ONLY_ITEM_KEYS.has(key)) return false
    } else {
      // 核心大哥角色：过滤纯辅助团队装
      if (item.role === 'support') return false
      if (SUPPORT_ONLY_ITEM_KEYS.has(key)) return false
    }

    return true
  })
}
