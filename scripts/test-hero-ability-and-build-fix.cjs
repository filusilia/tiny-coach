/**
 * 英雄技能 HTML 标签清理、Tip 占位符解析、神杖标识与推荐出装大图标测试套件
 * 运行方式: node scripts/test-hero-ability-and-build-fix.cjs
 */

const fs = require('fs')
const path = require('path')

const repoRoot = path.resolve(__dirname, '..')
let passed = 0
let failed = 0

function assert(condition, message) {
  if (condition) {
    console.log(`[PASS] ${message}`)
    passed++
  } else {
    console.error(`[FAIL] ${message}`)
    failed++
  }
}

console.log('====================================================')
console.log('   神盾教练：英雄技能清洗、神杖标识与出装大图标测试套件   ')
console.log('====================================================\n')

// 1. 读取官方全量 127 英雄技能数据
const heroesJsonPath = path.join(repoRoot, 'frontend', 'public', 'assets', 'data', 'heroes-details-official.json')
assert(fs.existsSync(heroesJsonPath), 'heroes-details-official.json 必须存在')
const rawData = JSON.parse(fs.readFileSync(heroesJsonPath, 'utf8'))
const heroes = rawData.heroes || []
assert(heroes.length >= 124, `官方英雄数据数量应不少于 124，实际: ${heroes.length}`)

// 2. 模拟 abilityMeta.ts 中的 getAbilityLevelValues 逻辑
function cleanInlineText(raw) {
  if (!raw || typeof raw !== 'string') return ''
  return raw
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<\/p>/gi, '\n\n')
    .replace(/<\/?(font|[biup]|strong|div|span|h[1-6]|em|small|big)[^>]*>/gi, '')
    .replace(/<[^>]+>/g, '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, '\'')
    .replace(/&ndash;/g, '–')
    .replace(/&mdash;/g, '—')
    .replace(/\s+/g, ' ')
    .trim()
}

function formatSingleValue(raw) {
  const num = typeof raw === 'number' ? raw : Number.parseFloat(String(raw))
  if (!Number.isFinite(num)) return String(raw)
  return String(Number(num.toFixed(3)))
}

function getAbilityLevelValues(ability) {
  const specials = ability?.special_values
  if (!Array.isArray(specials)) return []

  const result = []
  for (const sv of specials) {
    const rawLabel = (sv.heading_loc || '').trim()
    if (!rawLabel) continue

    const cleanedLabel = cleanInlineText(rawLabel).replace(/[：:]\s*$/, '').trim()
    if (!cleanedLabel) continue

    const values = (sv.values_float?.length
      ? sv.values_float
      : sv.values_int?.length
        ? sv.values_int
        : sv.values)
    if (!values || values.length === 0) continue

    const numeric = values.map(v => typeof v === 'number' ? v : Number.parseFloat(String(v)))
    if (numeric.every(n => Number.isFinite(n) && n === 0)) continue

    const percent = sv.is_percentage === true
    const formatted = numeric.map(n => `${formatSingleValue(n)}${percent ? '%' : ''}`)
    const display = formatted.every(v => v === formatted[0]) ? formatted[0] : formatted.join(' / ')

    result.push({
      label: cleanedLabel,
      display,
      isPercentage: percent,
    })
  }
  return result
}

// 模拟 abilityTextResolver.ts 中的 resolveAbilityText 逻辑
const PLACEHOLDER_REGEX = /%(\w+)%|\{([sdfg]?:?\w+)\}|\{\{(\w+)\}\}/g
function formatValues(values) {
  if (!values || values.length === 0) return ''
  const first = values[0]
  const allSame = values.every(v => v === first)
  if (allSame) return String(first)
  return values.join(' / ')
}

function getValueFromSpecial(sv, upgradeKind) {
  if (!sv) return null
  let list = null
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

function resolveKeyInAbility(key, ability, upgradeKind, hero) {
  const specials = ability.special_values || []
  const lk = key.toLowerCase()

  const exact = specials.find(s => s.name.toLowerCase() === lk)
  if (exact) {
    const val = getValueFromSpecial(exact, upgradeKind)
    if (val !== null) return { value: val, method: 'exact' }
  }

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

  for (const prefix of prefixes) {
    const prefixed = prefix + lk
    const match = specials.find(s => s.name.toLowerCase() === prefixed)
    if (match) {
      const val = getValueFromSpecial(match, upgradeKind)
      if (val !== null) return { value: val, method: 'alias_prefix_add' }
    }
  }

  if (lk === 'base_magic_resistance') {
    const bearMatch = specials.find(s => s.name.toLowerCase() === 'bear_magic_resistance')
    if (bearMatch) {
      const val = getValueFromSpecial(bearMatch, upgradeKind)
      if (val !== null) return { value: val, method: 'alias_entity_prefix' }
    }
  }

  if (lk === 'castpoint_tooltip') {
    if (ability.cast_points && ability.cast_points.length > 0) return { value: formatValues(ability.cast_points), method: 'formula' }
  }
  if (lk === 'damage' && ability.damages && ability.damages.length > 0) return { value: formatValues(ability.damages), method: 'formula' }
  if (lk === 'duration' && ability.durations && ability.durations.length > 0) return { value: formatValues(ability.durations), method: 'formula' }
  if (lk === 'cast_range' && ability.cast_ranges && ability.cast_ranges.length > 0) return { value: formatValues(ability.cast_ranges), method: 'formula' }
  if (lk === 'cooldown' && ability.cooldowns && ability.cooldowns.length > 0) return { value: formatValues(ability.cooldowns), method: 'formula' }

  return null
}

function resolveAbilityText(template, ability, upgradeKind = 'none', hero) {
  if (!template || typeof template !== 'string') return ''
  let text = template
  text = text.replace(PLACEHOLDER_REGEX, (_rawMatch, p1, p2, p3) => {
    const rawKey = p1 || p2 || p3
    if (!rawKey) return ''
    const cleanKey = rawKey.replace(/^[sdfg]:/, '')
    const res = resolveKeyInAbility(cleanKey, ability, upgradeKind, hero)
    if (res !== null) return res.value
    return '数值待同步'
  })
  text = text.replace(/%%/g, '%')
  text = text.replace(/<br\s*\/?>/gi, '\n')
  text = text.replace(/<[^>]+>/g, '')
  return text.trim()
}

// ----------------------------------------------------------------------
// 1. 蓝胖（食人魔魔法师）针对性断言
// ----------------------------------------------------------------------
console.log('--- 1. 检查蓝胖（食人魔魔法师）技能属性与文本 ---')
const ogre = heroes.find(h => h.id === 84)
assert(!!ogre, '英雄食人魔魔法师 (ID 84) 必须存在')

// 1.1 检查火焰爆轰的 label 清洗
const fireblast = ogre.abilities.find(a => a.name === 'ogre_magi_fireblast')
assert(!!fireblast, '火焰爆轰技能必须存在')
const fireblastValues = getAbilityLevelValues(fireblast)
const stunEntry = fireblastValues.find(v => v.label.includes('眩晕'))
assert(!!stunEntry, '火焰爆轰各等级数值中必须包含眩晕条目')
assert(stunEntry.label === '眩晕时间', `眩晕条目 label 必须完全清洗掉 <font> 标签，实际: "${stunEntry.label}"`)
assert(!stunEntry.label.includes('<'), `label 中严禁出现 '<'，实际: "${stunEntry.label}"`)

// 1.2 检查未精通的火焰爆轰的 label 清洗与神杖从属
const unrefined = ogre.abilities.find(a => a.name === 'ogre_magi_unrefined_fireblast')
assert(!!unrefined, '未精通的火焰爆轰技能必须存在')
const unrefinedValues = getAbilityLevelValues(unrefined)
const unrefinedStun = unrefinedValues.find(v => v.label.includes('眩晕'))
assert(unrefinedStun.label === '眩晕时间', `未精通火焰爆轰的眩晕条目必须清洗为“眩晕时间”，实际: "${unrefinedStun.label}"`)
assert(unrefined.ability_is_granted_by_scepter === true, '未精通的火焰爆轰必须由阿哈利姆神杖赋予 (ability_is_granted_by_scepter=true)')
assert(!unrefined.ability_is_granted_by_shard, '未精通的火焰爆轰绝非由魔晶赋予')

// 1.3 检查蓝胖所有技能 notes_loc 占位符解析
const fireblastNote0 = resolveAbilityText(fireblast.notes_loc[0], fireblast, 'none', ogre)
assert(fireblastNote0.includes('0.6秒的间隔'), `火焰爆轰 note 0 中的 %multicast_delay% 必须成功解析为 0.6，实际: "${fireblastNote0}"`)
assert(!fireblastNote0.includes('%multicast_delay%'), 'note 0 严禁残留 %multicast_delay% 替换符')

const ignite = ogre.abilities.find(a => a.name === 'ogre_magi_ignite')
const igniteNote0 = resolveAbilityText(ignite.notes_loc[0], ignite, 'none', ogre)
assert(igniteNote0.includes('1400范围'), `引燃 note 0 中的 %ignite_multicast_aoe% 必须成功解析为 1400，实际: "${igniteNote0}"`)
assert(!igniteNote0.includes('%ignite_multicast_aoe%'), '引燃 note 0 严禁残留 %ignite_multicast_aoe% 替换符')

const bloodlust = ogre.abilities.find(a => a.name === 'ogre_magi_bloodlust')
const bloodlustNote0 = resolveAbilityText(bloodlust.notes_loc[0], bloodlust, 'none', ogre)
assert(bloodlustNote0.includes('700范围'), `嗜血术 note 0 中的 %multicast_bloodlust_aoe% 必须成功解析为 700，实际: "${bloodlustNote0}"`)
assert(!bloodlustNote0.includes('%multicast_bloodlust_aoe%'), '嗜血术 note 0 严禁残留 %multicast_bloodlust_aoe% 替换符')

const smash = ogre.abilities.find(a => a.name === 'ogre_magi_smash')
const smashNote0 = resolveAbilityText(smash.notes_loc[0], smash, 'none', ogre)
assert(smashNote0.includes('1000范围'), `烈火护盾 note 0 中的 %multicast_fireshield_aoe% 必须成功解析为 1000，实际: "${smashNote0}"`)
assert(!smashNote0.includes('%multicast_fireshield_aoe%'), '烈火护盾 note 0 严禁残留 %multicast_fireshield_aoe% 替换符')

// ----------------------------------------------------------------------
// 2. 全量 127 英雄属性标签 HTML 零残留测试
// ----------------------------------------------------------------------
console.log('\n--- 2. 全量 127 英雄技能各等级数值 label 零 HTML 标签检验 ---')
let totalLabelsChecked = 0
let htmlLabelsFound = 0

heroes.forEach(h => {
  h.abilities.forEach(a => {
    const vals = getAbilityLevelValues(a)
    vals.forEach(v => {
      totalLabelsChecked++
      if (/<[^>]+>/.test(v.label) || v.label.includes('<font')) {
        htmlLabelsFound++
        console.error(`[LEAK] 英雄 ${h.name_loc} 技能 ${a.name_loc} label 残留 HTML: "${v.label}"`)
      }
    })
  })
})

assert(htmlLabelsFound === 0, `全量 ${totalLabelsChecked} 个技能属性标签中残留 HTML 标签数量必须为 0，实际: ${htmlLabelsFound}`)

// ----------------------------------------------------------------------
// 3. AbilityShowcase.vue 前端组件契约断言
// ----------------------------------------------------------------------
console.log('\n--- 3. 检查 AbilityShowcase.vue 前端组件契约 ---')
const showcaseVuePath = path.join(repoRoot, 'frontend', 'src', 'views', 'heroes', 'components', 'AbilityShowcase.vue')
assert(fs.existsSync(showcaseVuePath), 'AbilityShowcase.vue 必须存在')
const showcaseContent = fs.readFileSync(showcaseVuePath, 'utf8')

assert(showcaseContent.includes('神杖提供新技能'), 'AbilityShowcase.vue 必须包含【神杖提供新技能】徽章')
assert(showcaseContent.includes('魔晶提供新技能'), 'AbilityShowcase.vue 必须包含【魔晶提供新技能】徽章')
assert(showcaseContent.includes('activeSelectedAbility.ability_is_granted_by_scepter'), 'AbilityShowcase.vue 必须判定 ability_is_granted_by_scepter')

// 验证 cleanInlineText(v.label)
assert(showcaseContent.includes('cleanInlineText(v.label)'), 'AbilityShowcase.vue 必须使用 cleanInlineText 保护 v.label')

// 验证 currentAbilityNotes 调用 resolveAbilityText
assert(showcaseContent.includes('resolveAbilityText(\n                      note,'), 'AbilityShowcase.vue 必须对 currentAbilityNotes 中的 note 调用 resolveAbilityText')

// ----------------------------------------------------------------------
// 4. HeroBuildPanel.vue 出装图标放大与紧凑间距断言
// ----------------------------------------------------------------------
console.log('\n--- 4. 检查 HeroBuildPanel.vue 出装图标尺寸与间距 ---')
const buildPanelVuePath = path.join(repoRoot, 'frontend', 'src', 'views', 'heroes', 'components', 'HeroBuildPanel.vue')
assert(fs.existsSync(buildPanelVuePath), 'HeroBuildPanel.vue 必须存在')
const buildPanelContent = fs.readFileSync(buildPanelVuePath, 'utf8')

assert(buildPanelContent.includes('h-9 w-12'), 'HeroBuildPanel.vue 物品图标容器必须放大至 h-9 w-12 (36x48px)')
assert(buildPanelContent.includes('gap-1.5'), 'HeroBuildPanel.vue 物品容器必须保持 gap-1.5 紧凑间距')
assert(!buildPanelContent.includes('h-4.5 w-6.5'), 'HeroBuildPanel.vue 必须淘汰旧的 h-4.5 w-6.5 微型图标')

console.log('\n====================================================')
console.log(`测试结果统计: ${passed} 项通过，${failed} 项失败`)
console.log('====================================================')

if (failed > 0) {
  process.exit(1)
}
