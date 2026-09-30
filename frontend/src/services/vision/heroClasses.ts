/**
 * YOLO 英雄检测类别常数与权威映射字典 (src/services/vision/heroClasses.ts)
 * 职责：
 * 1. 严格按 hero_classes.txt 顺序定义 127 个目标检测类别
 * 2. 100% 闭环映射到本项目数字英雄 ID 与标准中文名称体系
 * 3. 映射率达到 100%，杜绝任何未定义或别名缺失
 */

import { HERO_CHINESE_NAMES, HERO_SHORT_NAMES } from '@/data/heroNames'

/**
 * 训练模型对应的 127 个英雄类别标识（严格与 hero_classes.txt 一致）
 */
export const YOLO_HERO_CLASSES: string[] = [
  'abaddon',
  'abyssal_underlord',
  'alchemist',
  'ancient_apparition',
  'antimage',
  'arc_warden',
  'axe',
  'bane',
  'batrider',
  'beastmaster',
  'bloodseeker',
  'bounty_hunter',
  'brewmaster',
  'bristleback',
  'broodmother',
  'centaur',
  'chaos_knight',
  'chen',
  'clinkz',
  'crystal_maiden',
  'dark_seer',
  'dark_willow',
  'dawnbreaker',
  'dazzle',
  'death_prophet',
  'disruptor',
  'doom_bringer',
  'dragon_knight',
  'drow_ranger',
  'earth_spirit',
  'earthshaker',
  'elder_titan',
  'ember_spirit',
  'enchantress',
  'enigma',
  'faceless_void',
  'furion',
  'grimstroke',
  'gyrocopter',
  'hoodwink',
  'huskar',
  'invoker',
  'jakiro',
  'juggernaut',
  'keeper_of_the_light',
  'kez',
  'kunkka',
  'largo',
  'legion_commander',
  'leshrac',
  'lich',
  'life_stealer',
  'lina',
  'lion',
  'lone_druid',
  'luna',
  'lycan',
  'magnataur',
  'marci',
  'mars',
  'medusa',
  'meepo',
  'mirana',
  'monkey_king',
  'morphling',
  'muerta',
  'naga_siren',
  'necrolyte',
  'nevermore',
  'night_stalker',
  'nyx_assassin',
  'obsidian_destroyer',
  'ogre_magi',
  'omniknight',
  'oracle',
  'pangolier',
  'phantom_assassin',
  'phantom_lancer',
  'phoenix',
  'primal_beast',
  'puck',
  'pudge',
  'pugna',
  'queenofpain',
  'rattletrap',
  'razor',
  'riki',
  'ringmaster',
  'rubick',
  'sand_king',
  'shadow_demon',
  'shadow_shaman',
  'shredder',
  'silencer',
  'skeleton_king',
  'skywrath_mage',
  'slardar',
  'slark',
  'snapfire',
  'sniper',
  'spectre',
  'spirit_breaker',
  'storm_spirit',
  'sven',
  'techies',
  'templar_assassin',
  'terrorblade',
  'tidehunter',
  'tinker',
  'tiny',
  'treant',
  'troll_warlord',
  'tusk',
  'undying',
  'ursa',
  'vengefulspirit',
  'venomancer',
  'viper',
  'visage',
  'void_spirit',
  'warlock',
  'weaver',
  'windrunner',
  'winter_wyvern',
  'wisp',
  'witch_doctor',
  'zuus',
]

/**
 * 训练名与本地英雄 ID 映射别名兜底表
 * 针对 Dota2 历史遗留内部名称差异提供 100% 确定性校正
 */
const CLASS_ALIAS_TO_ID: Record<string, number> = {
  // 黑鸟: 内部名为 obsidian_destroyer，官方展示名为 outworld_destroyer
  obsidian_destroyer: 76,
  // 发条技师: 内部名为 rattletrap，官方展示名为 clockwerk
  rattletrap: 51,
}

// 预先建立反向短名索引 (shortName -> id)
const SHORT_NAME_TO_ID: Record<string, number> = {}
for (const [idStr, shortName] of Object.entries(HERO_SHORT_NAMES)) {
  SHORT_NAME_TO_ID[shortName] = Number(idStr)
}

/**
 * 将 YOLO 类别名解析为完整的英雄业务元数据
 * @param className YOLO 识别出的英文类名 (如 'zuus', 'rattletrap', 'antimage')
 */
export function resolveHeroFromClass(className: string): {
  heroId: number
  heroName: string
  shortName: string
} {
  const normName = className.trim().toLowerCase()
  let heroId = CLASS_ALIAS_TO_ID[normName] || SHORT_NAME_TO_ID[normName] || 0

  if (heroId === 0) {
    // 兜底模糊匹配
    const foundEntry = Object.entries(HERO_SHORT_NAMES).find(
      ([, sName]) => sName.toLowerCase() === normName,
    )
    if (foundEntry) {
      heroId = Number(foundEntry[0])
    }
  }

  const heroName = HERO_CHINESE_NAMES[heroId] || className
  const shortName = HERO_SHORT_NAMES[heroId] || className

  return {
    heroId,
    heroName,
    shortName,
  }
}
