import type { BroadcastMode, ReminderCategoryConfig, ReminderCategoryKey } from '../types'

/**
 * 战术提醒播报档位定义列表
 * - 0: 完全关闭该分类的告警（不进入战术流流水，不发声）
 * - 1: 仅文字播报（在 HUD 与战术流水中呈现卡片气泡，静音）
 * - 2: 语音+文字播报（呈现战术卡片并调用 MeloTTS 超保真语音朗读）
 */
export const BROADCAST_MODES = [
  { mode: 0 as const, label: '关闭', shortLabel: '关', desc: '完全关闭提醒与语音' },
  { mode: 1 as const, label: '仅文字播报', shortLabel: '仅文字', desc: '仅在战术流展示文字气泡，不发声' },
  { mode: 2 as const, label: '语音+文字播报', shortLabel: '语音+文字', desc: '文字气泡 + MeloTTS 超保真离线朗读' },
]

/**
 * 宏观战术播报策略预设体系
 */
export interface BroadcastStrategyPreset {
  id: 'all_coach' | 'focus_ranked' | 'silent_hud'
  name: string
  tag: string
  description: string
  badgeClass: string
}

export const BROADCAST_STRATEGY_PRESETS: BroadcastStrategyPreset[] = [
  {
    id: 'all_coach',
    name: '全能战术解说',
    tag: '大局观提升 · 推荐',
    description: '关键战略节拍（肉山/魔方/神符）与危机变量（买活/终极技能/关键装成型）全量人声语音 + 屏幕战术卡片',
    badgeClass: 'text-amber-400 border-amber-500/40 bg-amber-500/15',
  },
  {
    id: 'focus_ranked',
    name: '专注天梯防打扰',
    tag: '纯净排位 · 开黑连麦',
    description: '仅在生死核心大局（肉山刷新倒计时、买活危机、敌方质变跳刀/BKB）语音预警，其余事件静音文字呈现，绝不干扰游戏环境音与开麦',
    badgeClass: 'text-cyan-400 border-cyan-500/40 bg-cyan-500/15',
  },
  {
    id: 'silent_hud',
    name: '纯净战术流',
    tag: '完全静音 · 沉浸视觉',
    description: '彻底关闭所有人声发音，仅在战况面板与局内置顶悬浮窗中展现高对比度战术气泡与倒计时',
    badgeClass: 'text-slate-400 border-slate-700 bg-slate-900/80',
  },
]

/**
 * 官方预设 9 大战术与大局提醒分类配置表
 * 严格遵照“宏观战略优先，杜绝微观琐碎噪音”设计原则：
 * 包含：宏观局势节奏、肉山控盾、痛苦魔方、神符视野掌控、买活储备与防守、关键装成型与克制、AI 研判指导、核心技能就绪、阵亡复活时机
 */
export const DEFAULT_REMINDER_CATEGORIES: ReminderCategoryConfig[] = [
  // 全局大局与核心战略变量（核心优先，去噪音，去微观琐碎）
  {
    key: 'macro_tempo',
    label: '宏观局势节奏',
    triggerCondition: '推进/带线/压制窗口',
    defaultMode: 2, // 语音+文字
    mode: 2,
    description: '对线期破塔转线、抱团控图压制、上高与撤退大局把控',
    group: 'macro',
  },
  {
    key: 'roshan_tactics',
    label: '肉山控盾',
    triggerCondition: '肉山刷新与盾消窗口',
    defaultMode: 2, // 语音+文字
    mode: 2,
    description: '肉山进出刷新窗口排布视野、带盾上高时机、防偷盾警戒',
    group: 'macro',
  },
  {
    key: 'tormentor_tactics',
    label: '痛苦魔方',
    triggerCondition: '魔方刷新前 15 秒 (20/30分钟)',
    defaultMode: 2, // 语音+文字
    mode: 2,
    description: '20分钟痛苦魔方刷新集结、团队获取关键魔晶战术节点',
    group: 'macro',
  },
  {
    key: 'rune_control',
    label: '神符视野掌控',
    triggerCondition: '河道/智慧神符刷新前 15 秒',
    defaultMode: 2, // 语音+文字
    mode: 2,
    description: '2/4/6分钟河道关键强化神符推线控符、7分钟智慧神符经验争夺',
    group: 'macro',
  },
  {
    key: 'buyback_strategy',
    label: '买活储备与防守',
    triggerCondition: '买活资金告急 / 关键阵亡守高',
    defaultMode: 2, // 语音+文字
    mode: 2,
    description: '核心买活备用金阈值监控、高地危机买活反打关键变量裁定',
    group: 'macro',
  },
  {
    key: 'item_timing_threat',
    label: '关键装成型与克制',
    triggerCondition: '闪烁匕首/黑皇杖等质变装成型',
    defaultMode: 2, // 语音+文字
    mode: 2,
    description: '己方强势装（跳刀/BKB）成型第一波出击、敌方核心大件针对性反制出装',
    group: 'tactical',
  },
  {
    key: 'tactical_ai_ask',
    label: 'AI 研判指导',
    triggerCondition: '对局提问 / 实时战况深度解答',
    defaultMode: 2, // 语音+文字
    mode: 2,
    description: '基于当前装备/技能/局势提供 2~3 句即时可执行战术指导',
    group: 'tactical',
  },
  {
    key: 'ability_ready',
    label: '核心技能就绪',
    triggerCondition: '核心大招 / 黑皇杖冷却完毕',
    defaultMode: 1, // 仅文字
    mode: 1,
    description: '核心终极技能或黑皇杖冷却完毕，提示战术强势期',
    group: 'tactical',
  },
  {
    key: 'death_respawn',
    label: '阵亡复活时机',
    triggerCondition: '自身阵亡读秒 / 买活窗口期',
    defaultMode: 1, // 仅文字
    mode: 1,
    description: '自身或核心阵亡状态、复活时间差与局势应对',
    group: 'tactical',
  },
]

/**
 * 提醒分类出厂默认播报模式映射字典
 */
export const DEFAULT_REMINDER_MODES: Record<ReminderCategoryKey, BroadcastMode> = {
  macro_tempo: 2,
  roshan_tactics: 2,
  tormentor_tactics: 2,
  rune_control: 2,
  buyback_strategy: 2,
  item_timing_threat: 2,
  tactical_ai_ask: 2,
  ability_ready: 1,
  death_respawn: 1,
}
