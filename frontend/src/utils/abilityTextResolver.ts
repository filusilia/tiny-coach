// Dota 2 官网英雄技能 JSON 解析规范引擎 (AbilityTextResolver)
// 依据契约：docs/OFFICIAL_HERO_DATA.md & Dota 2 官网英雄技能 JSON 解析规范 (版本：2026-09-18)
// 目标：将官网原始 JSON 解析为可展示、可审计、可注入 LLM 的中文技能数据

/**
 * 技能特有数值加成条目实体
 * 解析自 Valve 官方 NPC 脚本或官网 JSON，用于动态替换技能描述中的宏变量
 */
export interface AbilitySpecialValue {
  /** 变量名标识符 (如 "damage", "radius", "duration") */
  name: string
  /** 浮点数值列表 */
  values_float?: number[]
  /** 整型数值列表 */
  values_int?: number[]
  /** 混合数值列表 */
  values?: (number | string)[]
  /** 神杖强化后的专有数值列表 */
  values_scepter?: number[]
  /** 魔晶强化后的专有数值列表 */
  values_shard?: number[]
  /** 该数值是否为百分比形式 */
  is_percentage?: boolean
  /** 本地化标题文本 (如 "伤害：", "作用范围：") */
  heading_loc?: string
  /** 天赋或神杖对该数值的操作修饰器列表 */
  bonuses?: Array<{
    name: string
    value: number
    operation: number
  }>
  /** 所属命石要求 (7.36+ Facets 命石机制) */
  required_facet?: string
  /** 命石对该特殊变量的数值加成 */
  facet_bonus?: {
    name: string
    values: number[]
    operation: number
  }
}

/**
 * 官方标准技能接口
 * 包含主动、被动、先天技能以及阿哈利姆神杖与魔晶描述模板
 */
export interface OfficialAbility {
  /** 技能数字 ID */
  id: number
  /** 技能英文内部名 (如 "antimage_blink") */
  name: string
  /** 技能本地化中文名称 (如 "闪烁") */
  name_loc?: string
  /** 技能本地化描述模板文本 (含待解析宏变量占位符) */
  desc_loc?: string
  /** 技能机制注解说明列表 */
  notes_loc?: string[]
  /** 阿哈利姆魔晶升级说明模板 */
  shard_loc?: string
  /** 阿哈利姆神杖升级说明模板 */
  scepter_loc?: string
  /** 技能背景故事传奇 (Lore) */
  lore_loc?: string
  /** 技能动态特殊变量数值列表 */
  special_values?: AbilitySpecialValue[]
  /** 各等级冷却时间列表 (秒) */
  cooldowns?: number[]
  /** 各等级魔法消耗数值列表 */
  mana_costs?: number[]
  /** 各等级施法距离列表 (码) */
  cast_ranges?: number[]
  /** 各等级施法前摇点列表 (秒) */
  cast_points?: number[]
  /** 各等级持续施法时间列表 (秒) */
  channel_times?: number[]
  /** 各等级持续生效时间列表 (秒) */
  durations?: number[]
  /** 各等级伤害数值列表 */
  damages?: number[]
  /** 官方字段: 是否为先天技能 (7.36+) */
  ability_is_innate?: boolean
  /** 兼容字段: 是否为先天技能 */
  is_innate?: boolean
  /** 是否拥有阿哈利姆神杖升级 */
  ability_has_scepter?: boolean
  /** 是否拥有阿哈利姆魔晶升级 */
  ability_has_shard?: boolean
  /** 是否为购买阿哈利姆神杖后额外赋予的新技能 */
  ability_is_granted_by_scepter?: boolean
  /** 是否为购买阿哈利姆魔晶后额外赋予的新技能 */
  ability_is_granted_by_shard?: boolean
  /** 伤害类型 (1: 物理, 2: 魔法, 4: 纯粹) */
  damage?: number
  /** 技能免疫穿透状态 */
  immunity?: number
  /** 驱散类型状态 */
  dispellable?: number
  /** 目标阵营限制 */
  target_team?: number
  /** 目标类别限制 */
  target_type?: number
  /** 施法行为掩码 */
  behavior?: string
}

/**
 * 官方天赋实体
 */
export interface OfficialTalent {
  /** 天赋数字 ID */
  id: number
  /** 天赋英文内部名 (如 "special_bonus_unique_antimage_1") */
  name: string
  /** 天赋本地化中文文本 (含宏占位符) */
  name_loc?: string
  /** 天赋专属数值条目列表 */
  special_values?: AbilitySpecialValue[]
}

/**
 * 官方英雄全量元数据实体
 */
export interface OfficialHero {
  /** 英雄数字 ID */
  id: number
  /** 英雄英文名 (如 "npc_dota_hero_antimage") */
  name: string
  /** 英雄中文名 (如 "敌法师") */
  name_loc: string
  /** 英雄普通技能与先天技能列表 */
  abilities: OfficialAbility[]
  /** 英雄 8 个天赋树节点列表 */
  talents: OfficialTalent[]
  /** 7.36+ 新增技能列表 */
  new_abilities?: Array<{ name: string, [k: string]: any }>
  /** 操作上手复杂度 (1: 简单, 2: 中等, 3: 极高) */
  complexity?: number
  /** 英雄简要定位介绍 */
  hype_loc?: string
  /** 英雄完整背景传记故事 */
  bio_loc?: string
  /** 基础力量 */
  str_base?: number
  /** 力量成长 */
  str_gain?: number
  /** 基础敏捷 */
  agi_base?: number
  /** 敏捷成长 */
  agi_gain?: number
  /** 基础智力 */
  int_base?: number
  /** 智力成长 */
  int_gain?: number
  /** 基础攻击速率系数 */
  attack_rate?: number
  /** 基础攻击范围 */
  attack_range?: number
  /** 基础魔法抗性 */
  magic_resistance?: number
  /** 转身速率 */
  turn_rate?: number
  /** 白天基础视野 */
  sight_range_day?: number
  /** 夜晚基础视野 */
  sight_range_night?: number
  /** 剪裁正方形头像路径 */
  crops_img?: string
  [key: string]: any
}

/**
 * 神杖/魔晶升级六状态分类判定枚举
 * - valid: 有效升级 (ability_has_* 为 true 且所有占位符成功解析)
 * - stale_template: 失效废弃模板 (ability_has_* 为 false 且存在占位符缺失)
 * - removed_ability: 技能已被删除/重做
 * - resolved_by_alias: 通过前缀剥离或公式别名成功解析
 * - resolved_by_snapshot: 通过内置权威快照修复官网数据源缺陷
 * - granted_ability: 购买后额外授予的全新独立技能
 * - unresolved: 无法解析的残缺项
 */
export type AbilityUpgradeStatus
  = | 'valid'
    | 'stale_template'
    | 'removed_ability'
    | 'resolved_by_alias'
    | 'resolved_by_snapshot'
    | 'granted_ability'
    | 'unresolved'

/**
 * 英雄技能文本审计清单条目实体
 * 用于自动化测试与官方数据规范合规性校验
 */
export interface HeroTextAuditItem {
  /** 英雄 ID */
  hero_id: number
  /** 英雄简称 */
  hero_name: string
  /** 技能名称 */
  ability_name: string
  /** 审计类型 ('scepter' | 'shard' | 'desc' | 'talent' | 'notes') */
  kind: 'scepter' | 'shard' | 'desc' | 'talent' | 'notes'
  /** 六状态分类 */
  status: AbilityUpgradeStatus
  /** 裁定原因或解析方案说明 */
  resolution: string
  /** 缺失或无法匹配的宏占位符键名列表 */
  missing_keys: string[]
  /** 审计裁定佐证数据字典 */
  evidence: Record<string, any>
  /** 原始未解析文本 */
  original_text: string
  /** 最终解析生成的纯文本 */
  resolved_text?: string
}

/**
 * 格式化数值数组为标准电竞展示字符串（例如 "100 / 150 / 200 / 250" 或各级相同输出 "15"）
 * 严格保留 0、负数与小数，杜绝将 0 误判为 falsy 导致丢失
 * @param values 官方数值数组
 * @returns 格式化后的字符串
 */
export function formatValues(values: (number | string)[] | undefined | null): string {
  if (!values || values.length === 0) return ''
  const first = values[0]
  const allSame = values.every(v => v === first)
  if (allSame) {
    return String(first)
  }
  return values.join(' / ')
}

/** 占位符宏正则：支持 %key%、{s:key}、{d:key}、{f:key}、{g:key}、{{key}}、{key} */
const PLACEHOLDER_REGEX = /%(\w+)%|\{([sdfg]?:?\w+)\}|\{\{(\w+)\}\}/g

/**
 * 提取技能或天赋文本模板中的所有待替换宏变量键名列表
 * @param template 原始包含宏占位符的文本模板
 * @returns 清洗前缀后的变量名数组 (如 ["damage", "radius"])
 */
export function extractPlaceholderKeys(template: string | undefined): string[] {
  if (!template || typeof template !== 'string') return []
  const keys: string[] = []
  let m: RegExpExecArray | null
  const regex = new RegExp(PLACEHOLDER_REGEX.source, 'g')
  while ((m = regex.exec(template)) !== null) {
    const rawKey = m[1] || m[2] || m[3]
    if (rawKey) {
      const cleanKey = rawKey.replace(/^[sdfg]:/, '')
      keys.push(cleanKey)
    }
  }
  return keys
}

/**
 * 从 special_values 条目中按严格优先级提取目标数值序列
 * 1. 优先提取当前升级类型专属的 values_scepter / values_shard
 * 2. 其次提取 values_float 浮点数组
 * 3. 再次提取 values_int 整型数组
 * 4. 保底提取 values 通用数组
 * @param sv 技能特有数值条目
 * @param upgradeKind 强化类型 ('scepter' | 'shard' | 'none')
 * @returns 格式化后的数值文本，无有效数据返回 null
 */
function getValueFromSpecial(sv: AbilitySpecialValue, upgradeKind: 'scepter' | 'shard' | 'none'): string | null {
  if (!sv) return null
  let list: (number | string)[] | null = null
  if (upgradeKind === 'scepter' && sv.values_scepter && sv.values_scepter.length > 0) {
    list = sv.values_scepter
  } else if (upgradeKind === 'shard' && sv.values_shard && sv.values_shard.length > 0) {
    list = sv.values_shard
  } else if (sv.values_float && sv.values_float.length > 0) {
    list = sv.values_float
  } else if (sv.values_int && sv.values_int.length > 0) {
    list = sv.values_int
  } else if (sv.values && sv.values.length > 0) {
    list = sv.values
  }
  return list && list.length > 0 ? formatValues(list) : null
}

/**
 * 单个占位符宏解析结果实体
 */
export interface KeyResolutionResult {
  /** 最终解析替换的实际数值字符串 */
  value: string
  /**
   * 采用的解析策略途径：
   * - exact: 键名 100% 精确匹配
   * - alias_prefix_strip: 剥离 bonus_/tooltip_/base_ 前缀匹配
   * - alias_prefix_add: 反向补充 bonus_ 前缀匹配
   * - alias_entity_prefix: 专属实体前缀映射 (如熊灵 bear_)
   * - formula: 面板属性字段直读 (如 cast_ranges, cooldowns)
   * - snapshot_override: 权威离线快照兜底修复
   * - talent_bonus: 技能关联天赋 bonus 提取
   */
  method: 'exact' | 'alias_prefix_strip' | 'alias_prefix_add' | 'alias_entity_prefix' | 'formula' | 'snapshot_override' | 'talent_bonus'
}

/**
 * 核心键解析算法 (5 级优先级判定)
 * 严格按照官方规范执行：
 * 优先级 1: 精确键匹配 (special_values[].name == key)
 * 优先级 2: 别名前缀剥离与反向补充匹配 (bonus_ / tooltip_ / base_)
 * 优先级 3: 召唤物/专属实体前缀映射 (如 bear_magic_resistance)
 * 优先级 4: 技能面板原生字段公式直读 (damage, cooldown, cast_range 等)
 * 优先级 5: 内置权威快照兜底
 * @param key 占位符宏键名
 * @param ability 官方技能数据实体
 * @param upgradeKind 强化类型 ('scepter' | 'shard' | 'none')
 * @param _hero 所属英雄全量实体 (可选)
 * @returns 解析结果对象，未命中返回 null
 */
export function resolveKeyInAbility(
  key: string,
  ability: OfficialAbility,
  upgradeKind: 'scepter' | 'shard' | 'none',
  _hero?: OfficialHero,
): KeyResolutionResult | null {
  const specials = ability.special_values || []
  const lk = key.toLowerCase()

  // 优先级 1：精确键匹配 (special_values[].name == key)
  const exact = specials.find(s => s.name.toLowerCase() === lk)
  if (exact) {
    const val = getValueFromSpecial(exact, upgradeKind)
    if (val !== null) return { value: val, method: 'exact' }
  }

  // 优先级 2：别名匹配 (剥离 base_ / bonus_ / tooltip_ 前缀后唯一命中)
  const prefixes = ['bonus_', 'tooltip_', 'base_']
  for (const prefix of prefixes) {
    if (lk.startsWith(prefix)) {
      const stripped = lk.substring(prefix.length)
      const match = specials.find(s => s.name.toLowerCase() === stripped)
      if (match) {
        const val = getValueFromSpecial(match, upgradeKind)
        if (val !== null) return { value: val, method: 'alias_prefix_strip' }
      }
    }
  }

  // 反向：模板没有 bonus_，但 special_values 带有 bonus_ 前缀
  for (const prefix of prefixes) {
    const prefixed = prefix + lk
    const match = specials.find(s => s.name.toLowerCase() === prefixed)
    if (match) {
      const val = getValueFromSpecial(match, upgradeKind)
      if (val !== null) return { value: val, method: 'alias_prefix_add' }
    }
  }

  // 优先级 3：实体专属前缀匹配（如熊灵 bear_magic_resistance -> base_magic_resistance）
  if (lk === 'base_magic_resistance') {
    const bearMatch = specials.find(s => s.name.toLowerCase() === 'bear_magic_resistance')
    if (bearMatch) {
      const val = getValueFromSpecial(bearMatch, upgradeKind)
      if (val !== null) return { value: val, method: 'alias_entity_prefix' }
    }
  }

  // 优先级 4：已知公式合成与标准面板属性绑定
  if (lk === 'castpoint_tooltip') {
    if (ability.cast_points && ability.cast_points.length > 0) {
      return { value: formatValues(ability.cast_points), method: 'formula' }
    }
    const acp = specials.find(s => s.name.toLowerCase() === 'abilitycastpoint')
    if (acp) {
      const val = getValueFromSpecial(acp, upgradeKind)
      if (val !== null) return { value: val, method: 'formula' }
    }
  }

  if (lk === 'damage' && ability.damages && ability.damages.length > 0) {
    return { value: formatValues(ability.damages), method: 'formula' }
  }
  if (lk === 'duration' && ability.durations && ability.durations.length > 0) {
    return { value: formatValues(ability.durations), method: 'formula' }
  }
  if (lk === 'cast_range' && ability.cast_ranges && ability.cast_ranges.length > 0) {
    return { value: formatValues(ability.cast_ranges), method: 'formula' }
  }
  if (lk === 'cooldown' && ability.cooldowns && ability.cooldowns.length > 0) {
    return { value: formatValues(ability.cooldowns), method: 'formula' }
  }
  if (lk === 'channel_time' && ability.channel_times && ability.channel_times.length > 0) {
    return { value: formatValues(ability.channel_times), method: 'formula' }
  }

  // 优先级 5：内置快照覆盖
  if (lk === 'bonus_pull_strength') {
    return { value: '125', method: 'snapshot_override' }
  }

  return null
}

/**
 * 六状态分类判定机 (classify_ability_upgrade)
 * 严格按照官方规范执行裁决：
 * - 状态 F (granted_ability): ability_is_granted_by_* 为 true，作为独立授予技能处理
 * - 状态 B (stale_template): ability_has_* 为 false 且存在占位符缺失，跳过废弃模板
 * - 状态 E (resolved_by_snapshot): 存在官方数据源缺陷，通过权威快照修复
 * - 状态 D (resolved_by_alias): 占位符通过前缀剥离或公式映射成功闭环
 * - 状态 A (valid): 所有占位符完美匹配
 * - 异常 (unresolved): 仍有缺失键，回退安全兜底
 * @param ability 技能实体
 * @param kind 强化类型 ('scepter' | 'shard')
 * @param hero 所属英雄实体 (可选)
 * @returns 六状态分类结果与解析后文本
 */
export function classifyAbilityUpgrade(
  ability: OfficialAbility,
  kind: 'scepter' | 'shard',
  hero?: OfficialHero,
): {
  status: AbilityUpgradeStatus
  resolution: string
  missingKeys: string[]
  evidence: Record<string, any>
  resolvedText: string
} {
  const isScepter = kind === 'scepter'
  const hasFlag = isScepter ? !!ability.ability_has_scepter : !!ability.ability_has_shard
  const isGranted = isScepter ? !!ability.ability_is_granted_by_scepter : !!ability.ability_is_granted_by_shard
  const template = isScepter ? ability.scepter_loc : ability.shard_loc

  // 状态 F：GRANTED_ABILITY（升级授予的独立新技能）
  if (isGranted) {
    return {
      status: 'granted_ability',
      resolution: `ability_is_granted_by_${kind}=true, treated as standalone ability`,
      missingKeys: [],
      evidence: { has_flag: hasFlag, is_granted: true },
      resolvedText: resolveAbilityText(ability.desc_loc, ability, 'none', hero),
    }
  }

  // 若无模板，直接返回
  if (!template || !template.trim()) {
    return {
      status: hasFlag ? 'valid' : 'stale_template',
      resolution: 'no template provided',
      missingKeys: [],
      evidence: { has_flag: hasFlag },
      resolvedText: '',
    }
  }

  const keys = extractPlaceholderKeys(template)
  const missingKeys: string[] = []
  let usedAlias = false
  let usedSnapshot = false

  for (const k of keys) {
    const res = resolveKeyInAbility(k, ability, kind, hero)
    if (!res) {
      missingKeys.push(k)
    } else {
      if (res.method.startsWith('alias_') || res.method === 'formula') usedAlias = true
      if (res.method === 'snapshot_override') usedSnapshot = true
    }
  }

  // 状态 B：STALE_UPGRADE_TEMPLATE（失效升级模板）
  // 条件：ability_has_* == false 且存在占位符缺失，或同英雄有兄弟技能是独立授予
  if (!hasFlag) {
    const grantedSibling = hero?.abilities.find(sib =>
      isScepter ? sib.ability_is_granted_by_scepter : sib.ability_is_granted_by_shard,
    )
    return {
      status: 'stale_template',
      resolution: `ability_has_${kind}=false, obsolete template skipped`,
      missingKeys,
      evidence: {
        has_flag: false,
        has_granted_sibling: !!grantedSibling,
        granted_sibling_name: grantedSibling?.name || null,
      },
      resolvedText: '',
    }
  }

  // 若无缺失键
  if (missingKeys.length === 0) {
    // 状态 E：DATA_SOURCE_DEFECT（官网数据源缺陷，快照修复）
    if (usedSnapshot) {
      return {
        status: 'resolved_by_snapshot',
        resolution: 'Official data defect resolved by built-in snapshot',
        missingKeys: [],
        evidence: { has_flag: true },
        resolvedText: resolveAbilityText(template, ability, kind, hero),
      }
    }
    // 状态 D：NOMENCLATURE_MISMATCH（键名不一致，别名/公式修复）
    if (usedAlias) {
      return {
        status: 'resolved_by_alias',
        resolution: 'All keys resolved via alias prefix/formula mapping',
        missingKeys: [],
        evidence: { has_flag: true },
        resolvedText: resolveAbilityText(template, ability, kind, hero),
      }
    }
    // 状态 A：VALID_UPGRADE（有效升级）
    return {
      status: 'valid',
      resolution: `ability_has_${kind}=true, all placeholders matched`,
      missingKeys: [],
      evidence: { has_flag: true },
      resolvedText: resolveAbilityText(template, ability, kind, hero),
    }
  }

  // 无法解析异常项
  return {
    status: 'unresolved',
    resolution: 'Missing keys in special_values',
    missingKeys,
    evidence: { has_flag: true },
    resolvedText: resolveAbilityText(template, ability, kind, hero),
  }
}

/**
 * 模板替换层：将原始技能模板安全替换为纯中文文本
 * 保证百分号后缀规范 (将 %% 处理为 %)，清理残余 HTML 标签，杜绝英文宏变量在 UI 中裸露
 * @param template 原始描述模板
 * @param ability 技能数据对象
 * @param upgradeKind 升级类别
 * @param hero 所属英雄对象
 * @returns 最终清洗后的纯中文技能文本
 */
export function resolveAbilityText(
  template: string | undefined,
  ability: OfficialAbility,
  upgradeKind: 'scepter' | 'shard' | 'none' = 'none',
  hero?: OfficialHero,
): string {
  if (!template || typeof template !== 'string') return ''

  let text = template

  // 内置快照覆盖：如祈求者 EMP
  if (ability.name === 'invoker_emp' && text.includes('Eul')) {
    // 保持正确说明
  }

  // 逐一匹配并替换宏
  text = text.replace(PLACEHOLDER_REGEX, (_rawMatch, p1, p2, p3) => {
    const rawKey = p1 || p2 || p3
    if (!rawKey) return ''
    const cleanKey = rawKey.replace(/^[sdfg]:/, '')

    const res = resolveKeyInAbility(cleanKey, ability, upgradeKind, hero)
    if (res !== null) {
      return res.value
    }

    return '数值待同步'
  })

  // 处理双百分号宏：例如 %damage%%% 替换后为 100%% -> 100%
  text = text.replace(/%%/g, '%')

  // 清理残余 HTML 标签
  text = text.replace(/<br\s*\/?>/gi, '\n')
  text = text.replace(/<[^>]+>/g, '')

  return text.trim()
}

/**
 * 英雄天赋反查与补值算法
 * 按照天赋 ID 扫描同英雄所有技能下的 bonuses 数组，杜绝在 UI 裸露 {s:bonus_*} 占位符
 * @param talent 天赋数据实体
 * @param hero 所属英雄实体
 * @returns 替换完成的天赋名称描述文本
 */
export function resolveTalentText(talent?: OfficialTalent | null, hero?: OfficialHero): string {
  if (!talent || !talent.name_loc) return ''

  let text = talent.name_loc
  const keys = extractPlaceholderKeys(text)
  if (keys.length === 0) return text

  for (const key of keys) {
    let resolvedValue: string | null = null

    // 1. 同英雄技能 bonuses 反查 (按天赋 ID 扫描 bonuses[].name == talent.name)
    if (hero && hero.abilities) {
      for (const a of hero.abilities) {
        for (const sv of a.special_values || []) {
          for (const b of sv.bonuses || []) {
            if (b.name === talent.name) {
              resolvedValue = String(b.value)
              break
            }
          }
          if (resolvedValue !== null) break
        }
        if (resolvedValue !== null) break
      }
    }

    // 2. 内置快照覆盖 (例如小娜迦捕捞速度)
    if (resolvedValue === null && key === 'bonus_pull_strength') {
      resolvedValue = '125'
    }

    // 3. 通用天赋提取 ID 结尾数字 (例如 special_bonus_hp_300 -> 300)
    if (resolvedValue === null) {
      const match = talent.name.match(/(?:^|_)(\d+(?:\.\d+)?)$/)
      if (match) {
        resolvedValue = match[1]
      }
    }

    // 4. 从 talent.special_values 自身提取
    if (resolvedValue === null && talent.special_values && talent.special_values.length > 0) {
      for (const sv of talent.special_values) {
        const val = getValueFromSpecial(sv, 'none')
        if (val !== null) {
          resolvedValue = val
          break
        }
      }
    }

    if (resolvedValue !== null) {
      const escapedKey = key.replace(/[-/\\^$*+?.()|[\]{}]/g, '\\$&')
      const pattern = new RegExp(`(%${escapedKey}%|\\{[sdfg]?:?${escapedKey}\\}|\\{\\{${escapedKey}\\}\\})`, 'gi')
      text = text.replace(pattern, resolvedValue)
    } else {
      // 安全回退，不裸露 {s:...}
      const escapedKey = key.replace(/[-/\\^$*+?.()|[\]{}]/g, '\\$&')
      const pattern = new RegExp(`(%${escapedKey}%|\\{[sdfg]?:?${escapedKey}\\}|\\{\\{${escapedKey}\\}\\})`, 'gi')
      text = text.replace(pattern, '数值待同步')
    }
  }

  text = text.replace(/%%/g, '%').trim()
  return text
}

/**
 * 判断技能的神杖升级是否有效（只有有效或通过别名/快照修复时才在 UI 展示）
 * @param ability 技能数据实体
 * @param hero 所属英雄实体 (可选)
 * @returns 是否为有效升级
 */
export function isEffectiveScepterUpgrade(ability: OfficialAbility, hero?: OfficialHero): boolean {
  if (ability.ability_has_scepter === true) {
    const classification = classifyAbilityUpgrade(ability, 'scepter', hero)
    return classification.status === 'valid' || classification.status === 'resolved_by_alias' || classification.status === 'resolved_by_snapshot'
  }
  return false
}

/**
 * 判断技能的魔晶升级是否有效（只有有效或通过别名/快照修复时才在 UI 展示）
 * @param ability 技能数据实体
 * @param hero 所属英雄实体 (可选)
 * @returns 是否为有效升级
 */
export function isEffectiveShardUpgrade(ability: OfficialAbility, hero?: OfficialHero): boolean {
  if (ability.ability_has_shard === true) {
    const classification = classifyAbilityUpgrade(ability, 'shard', hero)
    return classification.status === 'valid' || classification.status === 'resolved_by_alias' || classification.status === 'resolved_by_snapshot'
  }
  return false
}

/**
 * 兼容旧接口命名: 判断是否为有效神杖升级
 */
export function isValidScepterUpgrade(ability: OfficialAbility): boolean {
  return isEffectiveScepterUpgrade(ability)
}

/**
 * 兼容旧接口命名: 判断是否为有效魔晶升级
 */
export function isValidShardUpgrade(ability: OfficialAbility): boolean {
  return isEffectiveShardUpgrade(ability)
}

/**
 * 生成全量 127 英雄技能文本审计清单
 * @param heroes 全量英雄数据列表
 * @returns 审计条目数组
 */
export function generateHeroTextAudit(heroes: OfficialHero[]): HeroTextAuditItem[] {
  const auditList: HeroTextAuditItem[] = []

  for (const hero of heroes) {
    const heroShort = hero.name.replace('npc_dota_hero_', '')

    for (const a of hero.abilities) {
      // 独立授予技能
      if (a.ability_is_granted_by_scepter || a.ability_is_granted_by_shard) {
        auditList.push({
          hero_id: hero.id,
          hero_name: heroShort,
          ability_name: a.name,
          kind: a.ability_is_granted_by_scepter ? 'scepter' : 'shard',
          status: 'granted_ability',
          resolution: `ability_is_granted_by_* == true, treated as standalone ability`,
          missing_keys: [],
          evidence: {
            granted_by_scepter: !!a.ability_is_granted_by_scepter,
            granted_by_shard: !!a.ability_is_granted_by_shard,
          },
          original_text: a.desc_loc || '',
        })
      }

      // 神杖模板审计
      if (a.scepter_loc && a.scepter_loc.trim()) {
        const res = classifyAbilityUpgrade(a, 'scepter', hero)
        auditList.push({
          hero_id: hero.id,
          hero_name: heroShort,
          ability_name: a.name,
          kind: 'scepter',
          status: res.status,
          resolution: res.resolution,
          missing_keys: res.missingKeys,
          evidence: res.evidence,
          original_text: a.scepter_loc,
          resolved_text: res.resolvedText,
        })
      }

      // 魔晶模板审计
      if (a.shard_loc && a.shard_loc.trim()) {
        const res = classifyAbilityUpgrade(a, 'shard', hero)
        auditList.push({
          hero_id: hero.id,
          hero_name: heroShort,
          ability_name: a.name,
          kind: 'shard',
          status: res.status,
          resolution: res.resolution,
          missing_keys: res.missingKeys,
          evidence: res.evidence,
          original_text: a.shard_loc,
          resolved_text: res.resolvedText,
        })
      }
    }
  }

  return auditList
}
