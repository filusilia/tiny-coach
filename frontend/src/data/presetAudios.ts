/**
 * 项目原生音色离线预置高频战术语音 Fast-Path 字典与匹配引擎
 * 采用本项目专属 MeloTTS 引擎离线预录的 4 大类极简事实语音（无形容词修饰，无连杀播报，总计仅 ~0.70MB），
 * 提供零延迟秒开播放路径（0ms 响应、0% CPU 占用、音色与本项目 MeloTTS 绝对一致），未命中时无缝回退至本地 MeloTTS 实时合成。
 */

export interface PresetAudioMeta {
  key: string
  file: string
  category: 'map_resource' | 'countdown' | 'sys_events' | 'levelup'
  title: string
}

/**
 * 预置音频基础路径 (由 Go 静态中间件与 Vite 插件统一代理映射到 /audio/preset/)
 */
export const PRESET_AUDIO_BASE_PATH = '/audio/preset/'

/**
 * 精确标题/关键字到音频文件的快速哈希索引
 */
export const EXACT_PRESET_MAP: Record<string, string> = {
  // 1. 地图资源刷新 (map_resource) - 极简纯事实
  '赏金神符即将刷新': 'preset_bounty.mp3',
  '赏金神符刷新': 'preset_bounty.mp3',
  '赏金神符': 'preset_bounty.mp3',

  '智慧神符即将刷新': 'preset_wisdom.mp3',
  '智慧神符争夺': 'preset_wisdom.mp3',
  '智慧神符刷新': 'preset_wisdom.mp3',
  '智慧神符': 'preset_wisdom.mp3',

  '莲花池即将刷新': 'preset_lotus.mp3',
  '莲花池刷新': 'preset_lotus.mp3',
  '莲花池争夺': 'preset_lotus.mp3',
  '莲花池': 'preset_lotus.mp3',

  '强化神符即将刷新': 'preset_rune_river.mp3',
  '河道强化神符预警': 'preset_rune_river.mp3',
  '河道强化神符刷新': 'preset_rune_river.mp3',
  '河道强化神符': 'preset_rune_river.mp3',

  '狂石系统启动': 'preset_kuangshi.mp3',

  '中立装备已掉落': 'preset_neutral.mp3',
  '中立装备掉落': 'preset_neutral.mp3',
  '中立装备': 'preset_neutral.mp3',

  // 2. 战略节点倒计时 (countdown) - 极简纯事实
  '1:00 狂石系统启动！': 'preset_cd_1.mp3',
  '一分钟，狂石系统启动': 'preset_cd_1.mp3',
  '3:00 莲花池刷新！': 'preset_cd_3.mp3',
  '三分钟，莲花池刷新': 'preset_cd_3.mp3',
  '6:00 功能符刷新！': 'preset_cd_6.mp3',
  '六分钟，强化神符刷新': 'preset_cd_6.mp3',
  '7:00 智慧符刷新！注意安全！': 'preset_cd_7.mp3',
  '七分钟，智慧神符刷新': 'preset_cd_7.mp3',
  '8:00 功能符刷新！': 'preset_cd_8.mp3',
  '八分钟，强化神符刷新': 'preset_cd_8.mp3',
  '11:00 功能符刷新！': 'preset_cd_11.mp3',
  '十一分钟，强化神符刷新': 'preset_cd_11.mp3',
  '12:00 功能符刷新！': 'preset_cd_12.mp3',
  '十二分钟，强化神符刷新': 'preset_cd_12.mp3',
  '14:00 智慧符刷新！': 'preset_cd_14.mp3',
  '十四分钟，智慧神符刷新': 'preset_cd_14.mp3',
  '15:00 T2中立装备！打造装备！': 'preset_cd_15.mp3',
  '十五分钟，中立装备掉落': 'preset_cd_15.mp3',
  '16:00 功能符刷新！': 'preset_cd_16.mp3',
  '十六分钟，强化神符刷新': 'preset_cd_16.mp3',
  '18:00 功能符刷新！': 'preset_cd_18.mp3',
  '十八分钟，强化神符刷新': 'preset_cd_18.mp3',
  '20:00 关键节奏窗口！': 'preset_cd_20.mp3',
  '二十分钟，痛苦魔方刷新': 'preset_cd_20.mp3',
  '25:00 T3中立装备！': 'preset_cd_25.mp3',
  '二十五分钟，中立装备掉落': 'preset_cd_25.mp3',
  '35:00 T4中立装备！后期大件的补强！': 'preset_cd_35.mp3',
  '三十五分钟，中立装备掉落': 'preset_cd_35.mp3',
  '60:00 T5终极中立装备！决定胜负！': 'preset_cd_60.mp3',
  '六十分钟，中立装备掉落': 'preset_cd_60.mp3',
  '注意拉野或者屯野': 'preset_cd_laye.mp3',

  // 3. 系统战场事件 (sys_events) - 极简纯事实
  '比赛开启 · 悬浮窗就绪': 'preset_sys_start.mp3',
  '比赛开始': 'preset_sys_start.mp3',
  '比赛开启': 'preset_sys_start.mp3',

  '我方团灭': 'preset_sys_teamwipe.mp3',
  '己方团灭': 'preset_sys_teamwipe.mp3',
  '全军覆没': 'preset_sys_teamwipe.mp3',

  '敌方团灭': 'preset_sys_enemywipe.mp3',
  '敌方全灭': 'preset_sys_enemywipe.mp3',

  '肉山即将刷新': 'preset_sys_roshan_window.mp3',
  '肉山巢穴即将刷新': 'preset_sys_roshan_window.mp3',
  '肉山已被击杀': 'preset_sys_roshan_kill.mp3',
  '肉山击杀': 'preset_sys_roshan_kill.mp3',

  '防御符文开启': 'preset_sys_tower_defense.mp3',
  '防御符文已开启': 'preset_sys_tower_defense.mp3',
  '开启塔防': 'preset_sys_tower_defense.mp3',

  '英雄已买活': 'preset_sys_buyback.mp3',
  '英雄阵亡 · 具备买活': 'preset_sys_buyback.mp3',
  '英雄买活': 'preset_sys_buyback.mp3',

  // 4. 等级质变 (levelup) - 纯事实，无任何修饰
  '英雄升到6级': 'preset_level_6.mp3',
  '升到6级': 'preset_level_6.mp3',
  '升到 6 级 · 核心质变': 'preset_level_6.mp3',
  '升到 6 级': 'preset_level_6.mp3',

  '英雄升到12级': 'preset_level_12.mp3',
  '升到12级': 'preset_level_12.mp3',
  '升到 12 级 · 二级大招': 'preset_level_12.mp3',
  '升到 12 级': 'preset_level_12.mp3',

  '英雄升到15级': 'preset_level_15.mp3',
  '升到15级': 'preset_level_15.mp3',
  '升到 15 级 · 关键天赋': 'preset_level_15.mp3',
  '升到 15 级': 'preset_level_15.mp3',

  '英雄升到18级': 'preset_level_18.mp3',
  '升到18级': 'preset_level_18.mp3',
  '升到 18 级 · 满级大招': 'preset_level_18.mp3',
  '升到 18 级': 'preset_level_18.mp3',

  '英雄升到25级': 'preset_level_25.mp3',
  '升到25级': 'preset_level_25.mp3',
  '升到 25 级 · 终极天赋': 'preset_level_25.mp3',
  '升到 25 级': 'preset_level_25.mp3',

  '英雄升到30级': 'preset_level_30.mp3',
  '升到30级': 'preset_level_30.mp3',
  '升到 30 级 · 全天赋解锁': 'preset_level_30.mp3',
  '升到 30 级': 'preset_level_30.mp3',
}

/**
 * 匹配项目原生预置离线语音 Fast-Path
 * 优先执行 O(1) 精确字典命中，若未命中则通过模式规则智能判定
 * @param text 告警标题或全文
 * @returns 预置音频完整相对 URL (如 '/audio/preset/preset_bounty.mp3')，未命中则返回 null
 */
export function findPresetAudio(text: string): string | null {
  if (!text) return null
  const clean = text.trim()

  // 1. 精确哈希匹配
  if (EXACT_PRESET_MAP[clean]) {
    return `${PRESET_AUDIO_BASE_PATH}${EXACT_PRESET_MAP[clean]}`
  }

  // 2. 针对常见告警标题前缀匹配
  const titlePart = clean.split(/[。·\n:：]/)[0]?.trim()
  if (titlePart && EXACT_PRESET_MAP[titlePart]) {
    return `${PRESET_AUDIO_BASE_PATH}${EXACT_PRESET_MAP[titlePart]}`
  }

  // 3. 智能模式规则匹配 (无连杀多杀，纯客观事实)

  // (1) 等级质变里程碑 (6, 12, 15, 18, 25, 30)
  if (/(?:升到|达到)\s*30\s*级/i.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_level_30.mp3`
  }
  if (/(?:升到|达到)\s*25\s*级/i.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_level_25.mp3`
  }
  if (/(?:升到|达到)\s*18\s*级/i.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_level_18.mp3`
  }
  if (/(?:升到|达到)\s*15\s*级/i.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_level_15.mp3`
  }
  if (/(?:升到|达到)\s*12\s*级/i.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_level_12.mp3`
  }
  if (/(?:升到|达到)\s*6\s*级/i.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_level_6.mp3`
  }

  // (2) 地图资源刷新 (赏金/智慧/莲花/功能符/中立装备/狂石)
  if (/(?:赏金神?符|金币符)/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_bounty.mp3`
  }
  if (/智慧神?符/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_wisdom.mp3`
  }
  if (/莲花池?/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_lotus.mp3`
  }
  if (/河道强化神符|强化神符|功能符/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_rune_river.mp3`
  }
  if (/中立装备|中立物品/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_neutral.mp3`
  }
  if (/狂石/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_kuangshi.mp3`
  }

  // (3) 系统战场事件 (比赛开始/团灭/敌方全灭/肉山/塔防/买活)
  if (/比赛(?:开始|开启)|对局正式开始/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_sys_start.mp3`
  }
  if (/敌方(?:团灭|全灭)|对面全灭/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_sys_enemywipe.mp3`
  }
  if (/(?:我方|己方)团灭|全军覆没|全死了/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_sys_teamwipe.mp3`
  }
  if (/肉山.*(?:击杀|抢盾|没了)|抢盾/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_sys_roshan_kill.mp3`
  }
  if (/肉山巢穴即将刷新|肉山即将刷新|肉山刷新/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_sys_roshan_window.mp3`
  }
  if (/防御符文|开塔防|塔防/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_sys_tower_defense.mp3`
  }
  if (/买活/.test(clean)) {
    return `${PRESET_AUDIO_BASE_PATH}preset_sys_buyback.mp3`
  }

  // (4) 关键分钟倒计时
  const cdMatch = clean.match(/(\d+):00/)
  if (cdMatch) {
    const min = parseInt(cdMatch[1], 10)
    const cdFile = `preset_cd_${min}.mp3`
    if (Object.values(EXACT_PRESET_MAP).includes(cdFile)) {
      return `${PRESET_AUDIO_BASE_PATH}${cdFile}`
    }
  }

  return null
}
