import type { HeroStat } from '../types'

/**
 * 英雄 1 级实战综合基础面板计算结果实体
 */
export interface Level1StatsResult {
  /** 1 级实战生命值上限 */
  hp: number
  /** 1 级生命自然恢复速度 (HP/s) */
  hpRegen: number
  /** 1 级实战魔法值上限 */
  mana: number
  /** 1 级魔法自然恢复速度 (MP/s) */
  manaRegen: number
  /** 1 级综合物理护甲值 */
  armor: number
  /** 1 级综合攻击力下限 */
  attackMin: number
  /** 1 级综合攻击力上限 */
  attackMax: number
  /** 1 级基础魔法抗性百分比 (大部分为 25%) */
  magicResist: number
  /** 1 级基础移动速度 (码/秒) */
  moveSpeed: number
}

/**
 * 计算英雄 1 级综合基础实战属性面板
 * 严格遵循 Dota 2 官方 7.36+ 权威换算公式与开工铁律：
 * - 生命上限 = 120 + round(基础力量 * 22)
 * - 生命恢复 = 基础生命恢复 + 基础力量 * 0.1
 * - 魔法上限 = 75 + round(基础智力 * 12)
 * - 魔法恢复 = 基础魔法恢复 + 基础智力 * 0.05
 * - 综合护甲 = 基础护甲底数 + 基础敏捷 / 6.0
 * - 攻击加成:
 *   - 力量英雄: +基础力量
 *   - 敏捷英雄: +基础敏捷
 *   - 智力英雄: +基础智力
 *   - 全属性(Universal)英雄: +floor((基础力量 + 基础敏捷 + 基础智力) * 0.7)
 * @param hero 英雄数据实体
 * @returns 1 级综合面板属性对象
 */
export function calculateLevel1Stats(hero: HeroStat): Level1StatsResult {
  const hp = 120 + Math.round(hero.base_str * 22)
  const hpRegen = +(hero.base_health_regen + hero.base_str * 0.1).toFixed(1)
  const mana = 75 + Math.round(hero.base_int * 12)
  const manaRegen = +(hero.base_mana_regen + hero.base_int * 0.05).toFixed(1)
  const armor = +(hero.base_armor + hero.base_agi / 6.0).toFixed(1)

  let bonusDmg = 0
  if (hero.primary_attr === 'str') {
    bonusDmg = hero.base_str
  } else if (hero.primary_attr === 'agi') {
    bonusDmg = hero.base_agi
  } else if (hero.primary_attr === 'int') {
    bonusDmg = hero.base_int
  } else if (hero.primary_attr === 'all') {
    bonusDmg = Math.floor((hero.base_str + hero.base_agi + hero.base_int) * 0.7)
  }

  const attackMin = hero.base_attack_min + bonusDmg
  const attackMax = hero.base_attack_max + bonusDmg

  return {
    hp,
    hpRegen,
    mana,
    manaRegen,
    armor,
    attackMin,
    attackMax,
    magicResist: hero.base_mr || 25,
    moveSpeed: hero.move_speed,
  }
}
