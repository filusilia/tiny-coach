/**
 * 物品与装备文本权威汉化与属性解析器 (src/utils/itemTextResolver.ts)
 * 职责：
 * 1. 彻底消除 Dota 2 装备/中立物品属性与技能描述中的残存英文提示 (如 Health, Mana, Agility, Damage, Active, Passive)
 * 2. 规范化技能卡片 (Spellcard) 分段与微标签
 * 3. 过滤官方与常量数据中的 0 增益无效占位
 */

import { cleanHtml } from '@/utils/htmlCleaner.ts'

export interface ItemSkillSection {
  type: '主动' | '被动' | '开关' | '使用' | '升级' | '通用'
  title: string
  body: string
}

/** 常见装备英文属性至中文术语对照表 */
const ATTRIBUTE_TRANSLATION_MAP: [RegExp, string][] = [
  [/\bHealth Regen\b/gi, '生命恢复'],
  [/\bHP Regen\b/gi, '生命恢复'],
  [/\bMana Regen\b/gi, '魔法恢复'],
  [/\bAll Attributes\b/gi, '全属性'],
  [/\bAttack Damage\b/gi, '攻击力'],
  [/\bDamage\b/gi, '攻击力'],
  [/\bHealth\b/gi, '生命值'],
  [/\bMana\b/gi, '魔法值'],
  [/\bStrength\b/gi, '力量'],
  [/\bAgility\b/gi, '敏捷'],
  [/\bIntelligence\b/gi, '智力'],
  [/\bAttack Speed\b/gi, '攻击速度'],
  [/\bArmor\b/gi, '护甲'],
  [/\bMovement Speed\b/gi, '移动速度'],
  [/\bMove Speed\b/gi, '移动速度'],
  [/\bCast Range\b/gi, '施法距离'],
  [/\bAttack Range\b/gi, '攻击距离'],
  [/\bMagic Resistance\b/gi, '魔法抗性'],
  [/\bMagic Resist\b/gi, '魔法抗性'],
  [/\bSpell Amplification\b/gi, '技能增强'],
  [/\bSpell Amp\b/gi, '技能增强'],
  [/\bStatus Resistance\b/gi, '状态抗性'],
  [/\bEvasion\b/gi, '闪避'],
  [/\bCooldown Reduction\b/gi, '冷却时间减少'],
  [/\bGPM BONUS\b/gi, '每分钟金钱'],
  [/\bSECONDARY STATS\b/gi, '次属性'],
  [/\bBonus Health\b/gi, '生命值加成'],
  [/\bBonus Mana\b/gi, '魔法值加成'],
  [/\bBonus Damage\b/gi, '攻击力加成'],
  [/\bBonus Armor\b/gi, '护甲加成'],
  [/\bBonus Attack Speed\b/gi, '攻击速度加成'],
  [/\bLifesteal\b/gi, '吸血'],
  [/\bSpell Lifesteal\b/gi, '技能吸血'],
  [/\bNight Vision\b/gi, '夜间视野'],
  [/\bVision\b/gi, '视野'],
  [/\bProjectile Speed\b/gi, '弹道速度'],
]

/** 技能正文/描述内部英文标签替换 */
const SKILL_BODY_TRANSLATION_MAP: [RegExp, string][] = [
  [/^Active:\s*/gim, '主动：'],
  [/^Passive:\s*/gim, '被动：'],
  [/^Toggle:\s*/gim, '开关：'],
  [/^Use:\s*/gim, '使用：'],
  [/^Upgrade:\s*/gim, '升级：'],
  [/\bCooldown:\s*/gi, '冷却时间：'],
  [/\bMana Cost:\s*/gi, '魔法消耗：'],
  [/\bRadius:\s*/gi, '作用范围：'],
  [/\bDuration:\s*/gi, '持续时间：'],
  [/\bRange:\s*/gi, '施法距离：'],
]

/**
 * 翻译并汉化属性单行文本
 */
export function translateAttributeLine(line: string): string {
  let res = line
  for (const [pattern, zh] of ATTRIBUTE_TRANSLATION_MAP) {
    res = res.replace(pattern, zh)
  }
  // 优化格式：去除多余空格与规范化冒号
  res = res.replace(/[:：]\s*/g, '：').replace(/\s+/g, ' ').trim()
  if (res.startsWith('+') && !res.includes('：') && !res.includes(':')) {
    // 类似于 "+ 10 力量" -> "+力量：10" 或 "+10 力量"
    const match = res.match(/^\+\s*([\d./%-]+)\s+([^\d./%\s-].*)$/)
    if (match) {
      res = `+${match[2]}：${match[1]}`
    }
  }
  return res
}

/**
 * 清洗并解析属性加成行列表 (汉化英文并杜绝 "+生命恢复：0" 等无用零值)
 */
export function parseAttributes(attribStr?: string | null): string[] {
  if (!attribStr) return []
  const clean = cleanHtml(attribStr)
  return clean
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter((line) => {
      if (!line) return false
      // 过滤加成为 0 的条目 (如 "+生命恢复：0", "+生命：0", "+智力: 0", "+闪避：0%")
      if (/[：:\s]\s*0%?$/.test(line)) return false
      if (/^[+＋]?0%?$/.test(line)) return false
      return true
    })
    .map(line => translateAttributeLine(line))
}

/**
 * 将官方与常量表中的结构化 attrib 数组格式化为标准中文属性多行文本
 */
export function buildAttributesFromConstant(attribList: any[]): string {
  if (!Array.isArray(attribList) || attribList.length === 0) return ''
  const lines: string[] = []

  for (const a of attribList) {
    const valStr = String(a.value || '').trim()
    if (!valStr || valStr === '0' || valStr === '0.0') continue

    if (a.display) {
      lines.push(a.display.replace('{value}', valStr))
    } else if (a.header || a.footer) {
      lines.push(`${a.header || ''} ${valStr} ${a.footer || ''}`.trim())
    } else if (a.key) {
      // 依据 key 兜底汉化
      const keyMap: Record<string, string> = {
        bonus_health: '+生命值',
        bonus_mana: '+魔法值',
        bonus_health_regen: '+生命恢复',
        bonus_mana_regen: '+魔法恢复',
        mana_regen: '+魔法恢复',
        bonus_all_stats: '+全属性',
        bonus_strength: '+力量',
        bonus_agility: '+敏捷',
        bonus_intellect: '+智力',
        bonus_damage: '+攻击力',
        damage: '+攻击力',
        bonus_attack_speed: '+攻击速度',
        bonus_armor: '+护甲',
        armor: '+护甲',
        bonus_movement_speed: '+移动速度',
        movement_speed: '+移动速度',
        bonus_spell_amp: '+技能增强',
        bonus_magic_resistance: '+魔法抗性',
        magic_resistance: '+魔法抗性',
        shield: '+吸收护盾',
        bonus_primary_stat: '+主属性',
        bonus_secondary_stat: '+次属性',
      }
      const label = keyMap[a.key] || a.key
      lines.push(`${label}：${valStr}`)
    }
  }

  return lines.join('\n')
}

/**
 * 将物品技能描述拆分为独立微标签展示的技能区块 (支持主动技能/被动技能/开关/使用)
 */
export function parseItemSkills(desc?: string | null): ItemSkillSection[] {
  if (!desc || !desc.trim()) return []
  const sections: ItemSkillSection[] = []
  const parts = desc.split(/(?=<h1>)/i)

  for (const part of parts) {
    const trimmed = part.trim()
    if (!trimmed) continue
    const h1Match = trimmed.match(/^<h1>(.*?)<\/h1>([\s\S]*)$/i)
    if (h1Match) {
      const fullTitle = h1Match[1].trim()
      let body = cleanHtml(h1Match[2])
      for (const [pattern, zh] of SKILL_BODY_TRANSLATION_MAP) {
        body = body.replace(pattern, zh)
      }

      let type: ItemSkillSection['type'] = '通用'
      let title = fullTitle

      const prefixes = ['主动', '被动', '开关', '使用', '升级', 'Active', 'Passive', 'Toggle', 'Use', 'Upgrade']
      for (const p of prefixes) {
        if (fullTitle.toLowerCase().startsWith(p.toLowerCase())) {
          if (/主动|active/i.test(p)) type = '主动'
          else if (/被动|passive/i.test(p)) type = '被动'
          else if (/开关|toggle/i.test(p)) type = '开关'
          else if (/使用|use/i.test(p)) type = '使用'
          else if (/升级|upgrade/i.test(p)) type = '升级'

          const rem = fullTitle.slice(p.length).replace(/^[：:\s-]+/, '').trim()
          title = rem || fullTitle
          break
        }
      }
      sections.push({ type, title, body })
    } else {
      let body = cleanHtml(trimmed)
      for (const [pattern, zh] of SKILL_BODY_TRANSLATION_MAP) {
        body = body.replace(pattern, zh)
      }
      if (body) {
        let type: ItemSkillSection['type'] = '通用'
        let title = '效果'
        if (/^(?:主动|[Aa]ctive)[：:\s]/.test(body)) {
          type = '主动'
          title = '主动技能'
          body = body.replace(/^(?:主动|[Aa]ctive)[：:\s]+/, '')
        } else if (/^(?:被动|[Pp]assive)[：:\s]/.test(body)) {
          type = '被动'
          title = '被动技能'
          body = body.replace(/^(?:被动|[Pp]assive)[：:\s]+/, '')
        }
        sections.push({ type, title, body })
      }
    }
  }
  return sections
}

/** 过滤无效零值判定辅助函数 (杜绝 "0s" 和 "0" 虚假展示) */
export function hasValidCd(cd: any): boolean {
  if (cd === undefined || cd === null || cd === false) return false
  const s = String(cd).trim()
  if (!s || s === '0' || s === '0.0') return false
  const parts = s.split(/[\s/]/).map(p => Number(p.trim())).filter(n => !Number.isNaN(n))
  if (parts.length > 0 && parts.every(n => n === 0)) return false
  return true
}

export function hasValidMc(mc: any): boolean {
  if (mc === undefined || mc === null || mc === false) return false
  const s = String(mc).trim()
  if (!s || s === '0' || s === '0.0') return false
  const parts = s.split(/[\s/]/).map(p => Number(p.trim())).filter(n => !Number.isNaN(n))
  if (parts.length > 0 && parts.every(n => n === 0)) return false
  return true
}

/** 常用装备机制提示完整汉化词典 */
const ITEM_NOTES_TRANSLATIONS: [RegExp, string][] = [
  [/Self-casting will cause you to teleport in the direction of your team's fountain\./gi, '对自身施法将朝己方泉水方向瞬间移动。'],
  [/Effects of multiple quelling blades do not stack\./gi, '多把压制之刃的效果无法叠加。'],
  [/Uses at most one charge per damage instance\./gi, '每次受到伤害最多消耗 1 点能量。'],
  [/Multiple sources of damage block do not stack\./gi, '多个伤害格挡来源的效果无法叠加。'],
  [/When the Guard buff is applied to ranged heroes, Guard blocks 25 less damage\./gi, '当护卫增益施加给远程英雄时，格挡伤害减少 25 点。'],
  [/Guard does not affect units that have been affected by Guard within Crimson Guard's cooldown\./gi, '在赤红甲冷却时间内已受过护卫效果的单位不受影响。'],
  [/Damage Return is calculated before any kind of reduction\./gi, '伤害反弹在计算任何减免前结算。'],
  [/Returned damage type is the same as it was received\./gi, '反弹的伤害类型与受到的伤害类型相同。'],
  [/Damage done by this item is considered reflected damage\. Reflected damage does not provide any lifesteal and cannot be reflected\./gi, '该物品造成的伤害属于反弹伤害。反弹伤害无法享受任何吸血效果，且无法被再次反弹。'],
  [/Stacks cannot be gained for 3s after you take damage from a player-controlled unit or Roshan\./gi, '受到玩家控制单位或肉山的伤害后 3 秒内无法叠加层数。'],
  [/Self-damage and HP Loss damage cannot remove stacks\./gi, '自残伤害和生命移除无法扣除层数。'],
  [/Movement speed buff does not stack\./gi, '移动速度加成无法叠加。'],
  [/Movement speed buff is only provided if you have reached maximum stacks at least once while the item is equipped\./gi, '仅当装备该物品期间曾达到过最大层数时，才提供移动速度加成。'],
  [/Ghost form ends if you become Debuff Immune and will have no effect if you are already Debuff Immune\./gi, '获得减益免疫时虚无形态立即结束，若已处于减益免疫状态则无效果。'],
  [/Shares cooldown with Ghost Scepter and its upgrades\./gi, '与幽魂权杖及其升级装备共享冷却时间。'],
  [/Putrefaction Aura does not redirect Health Restoration from enemy heroes that are at full health\./gi, '腐败光环不会从满血的敌方英雄身上重定向生命恢复。'],
  [/If Divine Rapier is dropped and picked up by an enemy of its original owner, it cannot be dropped again except by death\./gi, '若圣剑掉落并被敌方捡起，除携带者死亡外无法再次丢弃。'],
  [/Critical Strike does not work against buildings\./gi, '暴击效果对建筑物无效。'],
  [/Phantom Critical does not work against buildings\./gi, '幻影暴击对建筑物无效。'],
  [/Counts as a Critical Strike for purposes of stacking with other sources of Critical Strike\./gi, '在与其他暴击来源叠加时视作暴击。'],
  [/Does not stack with other sources of Bash\./gi, '无法与其他重击来源叠加。'],
  [/The following heroes cannot trigger Bash on this item: Spirit Breaker, Faceless Void, Slardar, and Kez while in Sai mode\./gi, '以下英雄无法触发该物品的重击：裂魂人、虚空假面、斯拉达以及处于铁尺形态的凯。'],
  [/Does not grant Bash to Clones and Tempest Doubles\./gi, '克隆体与风暴双雄无法继承重击。'],
  [/The stun is melee range\./gi, '眩晕效果仅限近战距离。'],
  [/Cleave damage goes through spell immunity\./gi, '分裂伤害无视技能免疫。'],
  [/If multiple sources of Cleave are present, each Cleave's damage is applied separately\./gi, '若存在多个分裂来源，每个分裂的伤害独立结算。'],
  [/Has a 0\.1 second cast time during which you are invulnerable\./gi, '拥有 0.1 秒施法时间，期间自身处于无敌状态。'],
  [/Many effects are removed upon using Manta\./gi, '使用分身斧时可驱散大量负面状态。'],
  [/Yasha-based movement speed bonuses from multiple items do not stack\./gi, '多个基于夜叉的移动速度加成无法叠加。'],
  [/Yasha based movement speed bonuses from multiple items do not stack\./gi, '多个基于夜叉的移动速度加成无法叠加。'],
  [/Attack Range bonuses from Dragon Lance and its upgrades do not stack\./gi, '魔龙枪及其升级装备的攻击距离加成无法叠加。'],
  [/The strength change will affect both maximum and current HP, but you cannot die from the change\./gi, '力量变化会同时影响生命上限与当前生命值，但该变化不会导致死亡。'],
  [/The strength change occurs over 0\.6 seconds\./gi, '力量变化在 0.6 秒内逐步生效。'],
  [/Activating or deactivating Unholy Strength does not interrupt channeling\./gi, '开启或关闭邪恶之力不会打断持续施法。'],
  [/Has a 0\.3 second fade time\./gi, '拥有 0.3 秒渐隐时间。'],
  [/If the invisibility ends without attacking, the bonus damage is lost\./gi, '若隐身结束时未进行攻击，额外伤害加成将失效。'],
  [/Static Charge cannot trigger more than once per second\./gi, '静电超荷每秒最多触发一次。'],
  [/Static Charge's shock deals magical damage centered on hero with the Static Charge\./gi, '静电超荷的电击造成以携带英雄为中心的魔法伤害。'],
  [/Static Charge's targets cannot be more than 900 range away\./gi, '静电超荷的目标距离不能超过 900 码。'],
  [/Static Charge procs will not bounce to heroes that are invisible or hidden by Fog of War\./gi, '静电超荷不会弹跳至隐身或处于战争迷雾中的英雄。'],
  [/Cannot dominate more than one unit at a time\. If a new unit is dominated, the old one will die\./gi, '同一时间最多支配 1 个单位。支配新单位时原单位将死亡。'],
  [/Can also Dominate enemy lane creeps and summoned units\./gi, '也可以支配敌方线上小兵和召唤单位。'],
  [/Selling Helm of the Dominator will cause dominated units to die\./gi, '出售支配头盔将导致已支配的单位死亡。'],
  [/Selling will cause dominated units to die\./gi, '出售装备将导致已支配的单位死亡。'],
  [/AoE bonuses from multiple Chasm Stones or its upgrades do not stack\./gi, '多个深渊之石及其升级装备的作用范围加成无法叠加。'],
  [/Armor reduction works on buildings\./gi, '减甲效果对建筑物生效。'],
  [/Armor reduction does not stack with its components, Desolator, or Stygian Desolator\./gi, '减甲效果无法与配件、暗灭或冥府暗灭叠加。'],
  [/Does not stack with other diffusal blades\./gi, '无法与其他净魂之刃叠加。'],
  [/Ethereal units take -30% bonus magic damage\./gi, '虚化单位承受额外 30% 魔法伤害。'],
  [/If this mana is not used before the duration ends, the extra mana is lost\./gi, '若在持续时间结束前未使用该魔法值，额外魔法将消失。'],
  [/Does not work on Meepo clones\./gi, '对米波克隆体无效。'],
  [/Does not stack with its upgrades, Desolator, or Stygian Desolator\./gi, '无法与其升级装备、暗灭或冥府暗灭叠加。'],
  [/Does not stack with its upgrades\./gi, '无法与其升级装备叠加。'],
  [/Shares cooldown with Pavise\./gi, '与长林法杖/统御头巾共享冷却时间。'],
  [/Spell Weakness does not stack with Bloodstone's Spell Weakness Aura\./gi, '法术脆弱无法与血精石的法术脆弱光环叠加。'],
  [/Mend does not affect units that have been affected by Mend or Mekansm Restore within Guardian Greaves' cooldown\./gi, '在卫士胫甲冷却时间内已受到过润泽或梅肯斯姆恢复的单位不受影响。'],
  [/Has a 4 second cooldown on Trees\./gi, '砍树时冷却时间为 4 秒。'],
  [/Multiple instances of Aquila Aura do not stack\./gi, '多个天鹰光环无法叠加。'],
  [/Takes 1\.25s of the 3\.5s for the hero to fully fade into invisibility\./gi, '需要 1.25 秒才能完全进入隐身状态。'],
  [/Does not trigger spell block or spell reflect\./gi, '不会触发法术否定或法术反弹。'],
  [/Grants two-thirds value to each attribute for Universal Heroes/gi, '对全才英雄提供三分之二的全属性加成。'],
  [/Grants 1\/3rd of the value to each attribute for Universal Heroes\./gi, '对全才英雄提供三分之一的全属性加成。'],
  [/Grants \+24 to each attribute for Universal Heroes\./gi, '对全才英雄提供 +24 全属性。'],
  [/Universal heroes get 0\.4 bonus to all attributes\./gi, '全才英雄获得 0.4 倍全属性加成。'],
  [/16% chance for a Bounty Rune\./gi, '16% 概率挖出赏金神符。'],
  [/28% chance each for a Flask, an Enchanted Mango, or a kobold\./gi, '各 28% 概率挖出治疗药膏、魔丸或狗头人。'],
  [/The same Trusty Shovel cannot find the same reward twice in a row\./gi, '同一把可靠铁铲无法连续两次挖出相同奖励。'],
  [/Does not stack with Movement Speed bonuses from boots/gi, '无法与鞋类物品的移动速度加成叠加。'],
  [/Will not ignite enemies that are more than 2000 range away\./gi, '不会点燃距离超过 2000 码的敌人。'],
  [/Damage is non-lethal and classified as HP Loss\./gi, '造成的伤害非致命，归类为生命移除。'],
  [/Damage taken when returning is non-lethal and disables Blink Dagger and other items that are muted upon taking damage\./gi, '返回时受到的伤害非致命，但会打断跳刀等受创禁用的物品。'],
  [/Only considers abilities that are visible and have a cooldown/gi, '仅对可见且具有冷却时间的技能生效。'],
  [/Player controlled units includes heroes and any creep summoned or converted by them\./gi, '玩家控制单位包括英雄及其召唤或转换的小兵。'],
  [/Does not stack with other manabreak abilities\./gi, '无法与其他消魔类技能叠加。'],
  [/Can be cast on invulnerable units\./gi, '可以对无敌单位施放。'],
  [/Refresher Shard is shareable\./gi, '刷新球碎片可以给予队友使用。'],
  [/Does not heal from Pure or Reflected damage\./gi, '纯粹伤害或反弹伤害无法提供生命偷取。'],
  [/Disabled while on a courier\./gi, '在信使身上时效果失效。'],
  [/Stacks multiplicatively with other sources of magic resistance\./gi, '与其他魔法抗性来源按乘法叠加。'],
  [/Empty vessels gain 2 charges\./gi, '空瓶将获得 2 点能量。'],
  [/Only the closest Spirit Vessel to the dying hero will gain a charge\./gi, '仅距离死亡英雄最近的魂之灵龛可获得能量。'],
  [/If the allied unit is attacked by an enemy hero or Roshan, the effect is lost\./gi, '若友方单位受到敌方英雄或肉山的攻击，该效果立即消失。'],
  [/While dormant, provides vision in 400 radius\. Vision lingers for 0\.5s after latching\./gi, '休眠期间提供 400 码范围视野。附着后视野残留 0.5 秒。'],
]

/**
 * 清洗并汉化物品机制提示文本 (杜绝生硬英文残留)
 */
export function translateItemNotes(notes?: string | null): string {
  if (!notes || !notes.trim()) return ''
  let cleaned = cleanHtml(notes)
  for (const [pattern, zh] of ITEM_NOTES_TRANSLATIONS) {
    cleaned = cleaned.replace(pattern, zh)
  }
  return cleaned
}
