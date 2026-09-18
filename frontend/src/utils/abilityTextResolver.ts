// Dota 2 官网英雄技能 JSON 解析规范引擎 (AbilityTextResolver)
// 依据契约：docs/OFFICIAL_HERO_DATA.md & Dota 2 官网英雄技能 JSON 解析规范 (版本：2026-09-18)
// 目标：将官网原始 JSON 解析为可展示、可审计、可注入 LLM 的中文技能数据

export interface AbilitySpecialValue {
  name: string;
  values_float?: number[];
  values_int?: number[];
  values?: (number | string)[];
  values_scepter?: number[];
  values_shard?: number[];
  is_percentage?: boolean;
  heading_loc?: string;
  bonuses?: Array<{
    name: string;
    value: number;
    operation: number;
  }>;
  required_facet?: string;
  facet_bonus?: {
    name: string;
    values: number[];
    operation: number;
  };
}

export interface OfficialAbility {
  id: number;
  name: string;
  name_loc?: string;
  desc_loc?: string;
  notes_loc?: string[];
  shard_loc?: string;
  scepter_loc?: string;
  lore_loc?: string;
  special_values?: AbilitySpecialValue[];
  cooldowns?: number[];
  mana_costs?: number[];
  cast_ranges?: number[];
  cast_points?: number[];
  channel_times?: number[];
  durations?: number[];
  damages?: number[];
  ability_is_innate?: boolean;
  is_innate?: boolean;
  ability_has_scepter?: boolean;
  ability_has_shard?: boolean;
  ability_is_granted_by_scepter?: boolean;
  ability_is_granted_by_shard?: boolean;
  damage?: number;
  immunity?: number;
  dispellable?: number;
  target_team?: number;
  target_type?: number;
  behavior?: string;
}

export interface OfficialTalent {
  id: number;
  name: string;
  name_loc?: string;
  special_values?: AbilitySpecialValue[];
}

export interface OfficialHero {
  id: number;
  name: string;
  name_loc: string;
  abilities: OfficialAbility[];
  talents: OfficialTalent[];
  new_abilities?: Array<{ name: string; [k: string]: any }>;
}

export type AbilityUpgradeStatus =
  | 'valid'
  | 'stale_template'
  | 'removed_ability'
  | 'resolved_by_alias'
  | 'resolved_by_snapshot'
  | 'granted_ability'
  | 'unresolved';

export interface HeroTextAuditItem {
  hero_id: number;
  hero_name: string;
  ability_name: string;
  kind: 'scepter' | 'shard' | 'desc' | 'talent' | 'notes';
  status: AbilityUpgradeStatus;
  resolution: string;
  missing_keys: string[];
  evidence: Record<string, any>;
  original_text: string;
  resolved_text?: string;
}

/**
 * 格式化数值数组为标准电竞展示字符串（例如 100 / 150 / 200 / 250 或 15）
 * 严格保留 0、负数与小数
 */
export function formatValues(values: (number | string)[] | undefined | null): string {
  if (!values || values.length === 0) return '';
  const first = values[0];
  const allSame = values.every((v) => v === first);
  if (allSame) {
    return String(first);
  }
  return values.join(' / ');
}

// 占位符宏正则：支持 %key%、{s:key}、{d:key}、{f:key}、{g:key}、{{key}}、{key}
const PLACEHOLDER_REGEX = /%([a-zA-Z0-9_]+)%|\{([sdfg]?:?[a-zA-Z0-9_]+)\}|\{\{([a-zA-Z0-9_]+)\}\}/g;

/**
 * 提取文本中的占位符键名列表
 */
export function extractPlaceholderKeys(template: string | undefined): string[] {
  if (!template || typeof template !== 'string') return [];
  const keys: string[] = [];
  let m: RegExpExecArray | null;
  const regex = new RegExp(PLACEHOLDER_REGEX.source, 'g');
  while ((m = regex.exec(template)) !== null) {
    const rawKey = m[1] || m[2] || m[3];
    if (rawKey) {
      const cleanKey = rawKey.replace(/^[sdfg]:/, '');
      keys.push(cleanKey);
    }
  }
  return keys;
}

/**
 * 从 special_values 条目中按优先级提取数值序列
 * 1. 优先取 values_scepter / values_shard（按升级类型）
 * 2. 否则取 values_float
 * 3. 否则取 values_int
 * 4. 否则取 values
 */
function getValueFromSpecial(sv: AbilitySpecialValue, upgradeKind: 'scepter' | 'shard' | 'none'): string | null {
  if (!sv) return null;
  let list: (number | string)[] | null = null;
  if (upgradeKind === 'scepter' && sv.values_scepter && sv.values_scepter.length > 0) {
    list = sv.values_scepter;
  } else if (upgradeKind === 'shard' && sv.values_shard && sv.values_shard.length > 0) {
    list = sv.values_shard;
  } else if (sv.values_float && sv.values_float.length > 0) {
    list = sv.values_float;
  } else if (sv.values_int && sv.values_int.length > 0) {
    list = sv.values_int;
  } else if (sv.values && sv.values.length > 0) {
    list = sv.values;
  }
  return list && list.length > 0 ? formatValues(list) : null;
}

export interface KeyResolutionResult {
  value: string;
  method: 'exact' | 'alias_prefix_strip' | 'alias_prefix_add' | 'alias_entity_prefix' | 'formula' | 'snapshot_override' | 'talent_bonus';
}

/**
 * 核心键解析算法：支持匹配优先级 1-5
 */
export function resolveKeyInAbility(
  key: string,
  ability: OfficialAbility,
  upgradeKind: 'scepter' | 'shard' | 'none',
  hero?: OfficialHero
): KeyResolutionResult | null {
  const specials = ability.special_values || [];
  const lk = key.toLowerCase();

  // 优先级 1：精确键匹配 (special_values[].name == key)
  const exact = specials.find((s) => s.name.toLowerCase() === lk);
  if (exact) {
    const val = getValueFromSpecial(exact, upgradeKind);
    if (val !== null) return { value: val, method: 'exact' };
  }

  // 优先级 2：别名匹配 (剥离 base_ / bonus_ / tooltip_ 前缀后唯一命中)
  const prefixes = ['bonus_', 'tooltip_', 'base_'];
  for (const prefix of prefixes) {
    if (lk.startsWith(prefix)) {
      const stripped = lk.substring(prefix.length);
      const match = specials.find((s) => s.name.toLowerCase() === stripped);
      if (match) {
        const val = getValueFromSpecial(match, upgradeKind);
        if (val !== null) return { value: val, method: 'alias_prefix_strip' };
      }
    }
  }

  // 反向：模板没有 bonus_，但 special_values 带有 bonus_ 前缀
  for (const prefix of prefixes) {
    const prefixed = prefix + lk;
    const match = specials.find((s) => s.name.toLowerCase() === prefixed);
    if (match) {
      const val = getValueFromSpecial(match, upgradeKind);
      if (val !== null) return { value: val, method: 'alias_prefix_add' };
    }
  }

  // 优先级 3：实体专属前缀匹配（如熊灵 bear_magic_resistance -> base_magic_resistance）
  if (lk === 'base_magic_resistance') {
    const bearMatch = specials.find((s) => s.name.toLowerCase() === 'bear_magic_resistance');
    if (bearMatch) {
      const val = getValueFromSpecial(bearMatch, upgradeKind);
      if (val !== null) return { value: val, method: 'alias_entity_prefix' };
    }
  }

  // 优先级 4：已知公式合成与标准面板属性绑定
  if (lk === 'castpoint_tooltip') {
    if (ability.cast_points && ability.cast_points.length > 0) {
      return { value: formatValues(ability.cast_points), method: 'formula' };
    }
    const acp = specials.find((s) => s.name.toLowerCase() === 'abilitycastpoint');
    if (acp) {
      const val = getValueFromSpecial(acp, upgradeKind);
      if (val !== null) return { value: val, method: 'formula' };
    }
  }

  if (lk === 'damage' && ability.damages && ability.damages.length > 0) {
    return { value: formatValues(ability.damages), method: 'formula' };
  }
  if (lk === 'duration' && ability.durations && ability.durations.length > 0) {
    return { value: formatValues(ability.durations), method: 'formula' };
  }
  if (lk === 'cast_range' && ability.cast_ranges && ability.cast_ranges.length > 0) {
    return { value: formatValues(ability.cast_ranges), method: 'formula' };
  }
  if (lk === 'cooldown' && ability.cooldowns && ability.cooldowns.length > 0) {
    return { value: formatValues(ability.cooldowns), method: 'formula' };
  }
  if (lk === 'channel_time' && ability.channel_times && ability.channel_times.length > 0) {
    return { value: formatValues(ability.channel_times), method: 'formula' };
  }

  // 优先级 5：内置快照覆盖
  if (lk === 'bonus_pull_strength') {
    return { value: '125', method: 'snapshot_override' };
  }

  return null;
}

/**
 * 六状态分类判定机 (classify_ability_upgrade)
 */
export function classifyAbilityUpgrade(
  ability: OfficialAbility,
  kind: 'scepter' | 'shard',
  hero?: OfficialHero
): {
  status: AbilityUpgradeStatus;
  resolution: string;
  missingKeys: string[];
  evidence: Record<string, any>;
  resolvedText: string;
} {
  const isScepter = kind === 'scepter';
  const hasFlag = isScepter ? !!ability.ability_has_scepter : !!ability.ability_has_shard;
  const isGranted = isScepter ? !!ability.ability_is_granted_by_scepter : !!ability.ability_is_granted_by_shard;
  const template = isScepter ? ability.scepter_loc : ability.shard_loc;

  // 状态 F：GRANTED_ABILITY（升级授予的独立新技能）
  if (isGranted) {
    return {
      status: 'granted_ability',
      resolution: `ability_is_granted_by_${kind}=true, treated as standalone ability`,
      missingKeys: [],
      evidence: { has_flag: hasFlag, is_granted: true },
      resolvedText: resolveAbilityText(ability.desc_loc, ability, 'none', hero),
    };
  }

  // 若无模板，直接返回
  if (!template || !template.trim()) {
    return {
      status: hasFlag ? 'valid' : 'stale_template',
      resolution: 'no template provided',
      missingKeys: [],
      evidence: { has_flag: hasFlag },
      resolvedText: '',
    };
  }

  const keys = extractPlaceholderKeys(template);
  const missingKeys: string[] = [];
  let usedAlias = false;
  let usedSnapshot = false;

  for (const k of keys) {
    const res = resolveKeyInAbility(k, ability, kind, hero);
    if (!res) {
      missingKeys.push(k);
    } else {
      if (res.method.startsWith('alias_') || res.method === 'formula') usedAlias = true;
      if (res.method === 'snapshot_override') usedSnapshot = true;
    }
  }

  // 状态 B：STALE_UPGRADE_TEMPLATE（失效升级模板）
  // 条件：ability_has_* == false 且存在占位符缺失，或同英雄有兄弟技能是独立授予
  if (!hasFlag) {
    const grantedSibling = hero?.abilities.find((sib) =>
      isScepter ? sib.ability_is_granted_by_scepter : sib.ability_is_granted_by_shard
    );
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
    };
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
      };
    }
    // 状态 D：NOMENCLATURE_MISMATCH（键名不一致，别名/公式修复）
    if (usedAlias) {
      return {
        status: 'resolved_by_alias',
        resolution: 'All keys resolved via alias prefix/formula mapping',
        missingKeys: [],
        evidence: { has_flag: true },
        resolvedText: resolveAbilityText(template, ability, kind, hero),
      };
    }
    // 状态 A：VALID_UPGRADE（有效升级）
    return {
      status: 'valid',
      resolution: `ability_has_${kind}=true, all placeholders matched`,
      missingKeys: [],
      evidence: { has_flag: true },
      resolvedText: resolveAbilityText(template, ability, kind, hero),
    };
  }

  // 无法解析异常项
  return {
    status: 'unresolved',
    resolution: 'Missing keys in special_values',
    missingKeys,
    evidence: { has_flag: true },
    resolvedText: resolveAbilityText(template, ability, kind, hero),
  };
}

/**
 * 模板替换层：将原始技能模板安全替换为纯中文文本
 * 保证百分号后缀规范，清理残余 HTML，杜绝英文占位符裸露
 */
export function resolveAbilityText(
  template: string | undefined,
  ability: OfficialAbility,
  upgradeKind: 'scepter' | 'shard' | 'none' = 'none',
  hero?: OfficialHero
): string {
  if (!template || typeof template !== 'string') return '';

  let text = template;

  // 内置快照覆盖：如祈求者 EMP
  if (ability.name === 'invoker_emp' && text.includes('Eul')) {
    // 保持正确说明
  }

  // 逐一匹配并替换宏
  text = text.replace(PLACEHOLDER_REGEX, (rawMatch, p1, p2, p3) => {
    const rawKey = p1 || p2 || p3;
    if (!rawKey) return '';
    const cleanKey = rawKey.replace(/^[sdfg]:/, '');

    const res = resolveKeyInAbility(cleanKey, ability, upgradeKind, hero);
    if (res !== null) {
      return res.value;
    }

    return '数值待同步';
  });

  // 处理双百分号宏：例如 %damage%%% 替换后为 100%% -> 100%
  text = text.replace(/%%/g, '%');

  // 清理残余 HTML 标签
  text = text.replace(/<br\s*\/?>/gi, '\n');
  text = text.replace(/<[^>]+>/g, '');

  return text.trim();
}

/**
 * 英雄天赋反查与补值算法
 * 按照天赋 ID 扫描同英雄技能 bonuses，杜绝在 UI 裸露 {s:bonus_*} 占位符
 */
export function resolveTalentText(talent: OfficialTalent, hero?: OfficialHero): string {
  if (!talent || !talent.name_loc) return '';

  let text = talent.name_loc;
  const keys = extractPlaceholderKeys(text);
  if (keys.length === 0) return text;

  for (const key of keys) {
    let resolvedValue: string | null = null;

    // 1. 同英雄技能 bonuses 反查 (按天赋 ID 扫描 bonuses[].name == talent.name)
    if (hero && hero.abilities) {
      for (const a of hero.abilities) {
        for (const sv of a.special_values || []) {
          for (const b of sv.bonuses || []) {
            if (b.name === talent.name) {
              resolvedValue = String(b.value);
              break;
            }
          }
          if (resolvedValue !== null) break;
        }
        if (resolvedValue !== null) break;
      }
    }

    // 2. 内置快照覆盖 (例如小娜迦捕捞速度)
    if (resolvedValue === null && key === 'bonus_pull_strength') {
      resolvedValue = '125';
    }

    // 3. 通用天赋提取 ID 结尾数字 (例如 special_bonus_hp_300 -> 300)
    if (resolvedValue === null) {
      const match = talent.name.match(/(?:^|_)(\d+(?:\.\d+)?)$/);
      if (match) {
        resolvedValue = match[1];
      }
    }

    // 4. 从 talent.special_values 自身提取
    if (resolvedValue === null && talent.special_values && talent.special_values.length > 0) {
      for (const sv of talent.special_values) {
        const val = getValueFromSpecial(sv, 'none');
        if (val !== null) {
          resolvedValue = val;
          break;
        }
      }
    }

    if (resolvedValue !== null) {
      const escapedKey = key.replace(/[-\/\\^$*+?.()|[\]{}]/g, '\\$&');
      const pattern = new RegExp(`(%${escapedKey}%|\\{[sdfg]?:?${escapedKey}\\}|\\{\\{${escapedKey}\\}\\})`, 'gi');
      text = text.replace(pattern, resolvedValue);
    } else {
      // 安全回退，不裸露 {s:...}
      const escapedKey = key.replace(/[-\/\\^$*+?.()|[\]{}]/g, '\\$&');
      const pattern = new RegExp(`(%${escapedKey}%|\\{[sdfg]?:?${escapedKey}\\}|\\{\\{${escapedKey}\\}\\})`, 'gi');
      text = text.replace(pattern, '数值待同步');
    }
  }

  text = text.replace(/%%/g, '%').trim();
  return text;
}

/**
 * 判断技能的神杖升级是否有效（只有有效或通过别名/快照修复时才在 UI 展示）
 */
export function isEffectiveScepterUpgrade(ability: OfficialAbility, hero?: OfficialHero): boolean {
  if (ability.ability_has_scepter === true) {
    const classification = classifyAbilityUpgrade(ability, 'scepter', hero);
    return classification.status === 'valid' || classification.status === 'resolved_by_alias' || classification.status === 'resolved_by_snapshot';
  }
  return false;
}

/**
 * 判断技能的魔晶升级是否有效（只有有效或通过别名/快照修复时才在 UI 展示）
 */
export function isEffectiveShardUpgrade(ability: OfficialAbility, hero?: OfficialHero): boolean {
  if (ability.ability_has_shard === true) {
    const classification = classifyAbilityUpgrade(ability, 'shard', hero);
    return classification.status === 'valid' || classification.status === 'resolved_by_alias' || classification.status === 'resolved_by_snapshot';
  }
  return false;
}

/**
 * 兼容旧接口命名
 */
export function isValidScepterUpgrade(ability: OfficialAbility): boolean {
  return isEffectiveScepterUpgrade(ability);
}

export function isValidShardUpgrade(ability: OfficialAbility): boolean {
  return isEffectiveShardUpgrade(ability);
}

/**
 * 生成全量 127 英雄技能文本审计清单
 */
export function generateHeroTextAudit(heroes: OfficialHero[]): HeroTextAuditItem[] {
  const auditList: HeroTextAuditItem[] = [];

  for (const hero of heroes) {
    const heroShort = hero.name.replace('npc_dota_hero_', '');

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
        });
      }

      // 神杖模板审计
      if (a.scepter_loc && a.scepter_loc.trim()) {
        const res = classifyAbilityUpgrade(a, 'scepter', hero);
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
        });
      }

      // 魔晶模板审计
      if (a.shard_loc && a.shard_loc.trim()) {
        const res = classifyAbilityUpgrade(a, 'shard', hero);
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
        });
      }
    }
  }

  return auditList;
}
