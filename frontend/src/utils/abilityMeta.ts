/**
 * 官方技能元信息解码器
 *
 * 负责把官网 datafeed 的原始枚举字段解码为「技能详情」面板可直接展示的中文结构化字段。
 * 所有口径严格对齐 [docs/OFFICIAL_HERO_DATA.md](../docs/OFFICIAL_HERO_DATA.md)：
 *
 * | 字段 | 口径 |
 * |---|---|
 * | `behavior` | 64 位标志字符串，**必须**用 BigInt 按位解码。用 Int32 会溢出成负数，导致技能类型全部错判 |
 * | `damage` | 1 物理、2 魔法、3/4 纯粹，0 不适用 |
 * | `immunity` | 1/3 是，2/4 否，5 对友军无视、对敌人无效，0 未指定 |
 * | `dispellable` | 1 仅强驱散、2 是、3 否，0 未指定 |
 *
 * 铁律约束：未知枚举一律输出「资料待同步」，**绝不默认成「否」**（零伪造原则）。
 */

import type { OfficialAbility } from './abilityTextResolver'
import { cleanInlineText } from './htmlCleaner'

/** 施法行为位标志定义（仅收录官网「技能详情」会展示的已知低位） */
interface BehaviorFlag {
  /** 位值（BigInt，避免 64 位溢出） */
  bit: bigint
  /** 英文内部标识 */
  key: string
  /** 中文展示标签 */
  label: string
  /** 是否为「主技能类型」（互斥，取优先级最高的一个） */
  primary: boolean
}

const BEHAVIOR_FLAGS: BehaviorFlag[] = [
  { bit: 1n << 1n, key: 'PASSIVE', label: '被动', primary: true },
  { bit: 1n << 9n, key: 'TOGGLE', label: '切换', primary: true },
  { bit: 1n << 3n, key: 'UNIT_TARGET', label: '单位目标', primary: true },
  { bit: 1n << 4n, key: 'POINT', label: '点目标', primary: true },
  { bit: 1n << 2n, key: 'NO_TARGET', label: '无目标', primary: true },
  { bit: 1n << 5n, key: 'AOE', label: '范围', primary: false },
  { bit: 1n << 7n, key: 'CHANNELLED', label: '持续施法', primary: false },
  { bit: 1n << 11n, key: 'IMMEDIATE', label: '瞬间施法', primary: false },
  { bit: 1n << 12n, key: 'AUTOCAST', label: '自动施放', primary: false },
]

/** 未知枚举的统一兜底文案（不允许沉默成「否」） */
const UNKNOWN_LABEL = '资料待同步'

/** 技能类型解码结果 */
export interface AbilityBehaviorInfo {
  /** 主技能类型（被动 / 切换 / 单位目标 / 点目标 / 无目标） */
  primary: string
  /** 附加施法特性（持续施法 / 范围 / 瞬间施法 / 自动施放） */
  modifiers: string[]
}

/**
 * 将官网 behavior 标志位解析为 BigInt
 * @param behavior 官网原始值（十进制字符串或数字）
 */
function toBehaviorBigInt(behavior: string | number | undefined | null): bigint {
  if (behavior === undefined || behavior === null || behavior === '') return 0n
  try {
    return BigInt(behavior)
  } catch {
    return 0n
  }
}

/**
 * 解码技能施法行为（技能类型）
 *
 * 例：力丸「闪烁突袭」behavior=134742024 → 单位目标；
 * 「绝杀秘技」behavior=176 → 点目标 · 范围 · 持续施法。
 *
 * @param behavior 官网原始 behavior 值
 */
export function decodeAbilityBehavior(
  behavior?: string | number | null,
): AbilityBehaviorInfo {
  const value = toBehaviorBigInt(behavior)
  const primaryHits: string[] = []
  const modifiers: string[] = []

  for (const flag of BEHAVIOR_FLAGS) {
    if ((value & flag.bit) === flag.bit) {
      if (flag.primary) primaryHits.push(flag.label)
      else modifiers.push(flag.label)
    }
  }

  return {
    primary: primaryHits[0] || UNKNOWN_LABEL,
    modifiers,
  }
}

/**
 * 技能类型的完整展示文案（主类型 + 附加特性）
 * @param behavior 官网原始 behavior 值
 */
export function formatAbilityBehaviorText(
  behavior?: string | number | null,
): string {
  const info = decodeAbilityBehavior(behavior)
  return [info.primary, ...info.modifiers].join(' · ')
}

/**
 * 伤害类型映射（官网面板口径）
 * @param damage 官网 damage 枚举
 */
export function getDamageTypeLabel(damage?: number): string {
  switch (damage) {
    case 1:
      return '物理伤害'
    case 2:
      return '魔法伤害'
    case 3:
    case 4:
      return '纯粹伤害'
    case 0:
      return '不适用'
    default:
      return UNKNOWN_LABEL
  }
}

/**
 * 是否无视减益免疫（黑皇杖 BKB 穿透）
 * @param immunity 官网 immunity 枚举
 */
export function getImmunityLabel(immunity?: number): string {
  switch (immunity) {
    case 1:
    case 3:
      return '是'
    case 2:
    case 4:
      return '否'
    case 5:
      return '对友军无视、对敌人无效'
    case 0:
      return '不适用'
    default:
      return UNKNOWN_LABEL
  }
}

/**
 * 是否可被驱散
 * @param dispellable 官网 dispellable 枚举
 */
export function getDispellableLabel(dispellable?: number): string {
  switch (dispellable) {
    case 1:
      return '仅强驱散'
    case 2:
      return '是'
    case 3:
      return '否'
    case 0:
      return '不适用'
    default:
      return UNKNOWN_LABEL
  }
}

/** 单条各等级数值条目 */
export interface AbilityLevelValue {
  /** 中文属性标签（如「额外伤害」「减速持续时间」） */
  label: string
  /** 已格式化的各等级数值文案（如「25 / 35 / 45 / 55」） */
  display: string
  /** 是否为百分比数值 */
  isPercentage: boolean
}

/**
 * 数值格式化：去掉浮点尾随零，按官网口径去掉「：」后缀标签
 * @param raw 原始数值
 */
function formatSingleValue(raw: number | string): string {
  const num = typeof raw === 'number' ? raw : Number.parseFloat(String(raw))
  if (!Number.isFinite(num)) return String(raw)
  return String(Number(num.toFixed(3)))
}

/**
 * 提取技能各等级数值列表
 *
 * 只取官网自带中文标签（`heading_loc` 非空）的 special_values，
 * 无中文标签的内部键（如 `AbilityCastRange`）不展示，避免英文污染 UI。
 * 全 0 的条目（官网占位行）与各等级完全相同的数值会被折叠为单值。
 *
 * @param ability 官方技能实体
 */
export function getAbilityLevelValues(
  ability?: OfficialAbility | null,
): AbilityLevelValue[] {
  const specials = ability?.special_values
  if (!Array.isArray(specials)) return []

  const result: AbilityLevelValue[] = []
  for (const sv of specials) {
    const rawLabel = (sv.heading_loc || '').trim()
    if (!rawLabel) continue

    const cleanedLabel = cleanInlineText(rawLabel).replace(/[：:]\s*$/, '').trim()
    if (!cleanedLabel) continue

    const values = (sv.values_float?.length
      ? sv.values_float
      : sv.values_int?.length
        ? sv.values_int
        : sv.values) as Array<number | string> | undefined
    if (!values || values.length === 0) continue

    const numeric = values.map(v =>
      typeof v === 'number' ? v : Number.parseFloat(String(v)),
    )
    // 全 0 行是官网占位数据，不展示
    if (numeric.every(n => Number.isFinite(n) && n === 0)) continue

    const percent = sv.is_percentage === true
    const formatted = numeric.map(n => `${formatSingleValue(n)}${percent ? '%' : ''}`)
    // 各等级数值完全一致时折叠为单值（对齐官网「减速持续时间 0.75」的展示口径）
    const display = formatted.every(v => v === formatted[0])
      ? formatted[0]
      : formatted.join(' / ')

    result.push({
      label: cleanedLabel,
      display,
      isPercentage: percent,
    })
  }
  return result
}
