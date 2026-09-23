/**
 * 战局 HUD 与实时状态模型契约定义 (src/api/types/battle.ts)
 */

export interface AbilityHudItem {
  /** 技能英文内部标识符 (如 "antimage_mana_void") */
  name: string
  /** 当前已加点等级 (0 表示尚未学习) */
  level: number
  /** 当前剩余冷却时间 (秒，0 表示就绪) */
  cooldown: number
  /** 当前是否满足施法条件 (魔法充足且非被禁魔/被击晕) */
  canCast: boolean
  /** 是否为纯被动技能 */
  passive: boolean
}

export interface ItemHudItem {
  /** 物品槽位标识 (如 "slot0" ~ "slot5", "backpack0" ~ "backpack2", "neutral0") */
  slot: string
  /** 物品内部标识符 (如 "item_black_king_bar") */
  name: string
  /** 物品当前剩余主动冷却时间 (秒) */
  cooldown: number
  /** 是否可主动释放/使用 */
  canCast: boolean
}

export interface ActiveBattleContext {
  /** 是否与 Dota 2 客户端建立 GSI 通信连接 */
  connected: boolean
  /** 比赛对局时钟秒数 (负数表示号角吹响前的准备阶段) */
  clockTime: number
  /** 引擎底层绝对时钟时间 (秒) */
  gameTime: number
  /** 当前比赛状态 (如 "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS") */
  gameState: string
  /** 玩家当前操纵英雄的官方数字 ID */
  heroId: number
  /** 英雄本地化中文名称 (如 "敌法师") */
  heroName: string
  /** 英雄英文内部类名 (如 "npc_dota_hero_antimage") */
  heroInternalName: string
  /** 英雄当前等级 (1 ~ 30) */
  heroLevel: number
  /** 英雄当前生命值 */
  health: number
  /** 英雄最大生命值 */
  maxHealth: number
  /** 英雄当前生命百分比 (0 ~ 100) */
  healthPercent: number
  /** 英雄当前魔法值 */
  mana: number
  /** 英雄最大魔法值 */
  maxMana: number
  /** 英雄当前魔法百分比 (0 ~ 100) */
  manaPercent: number
  /** 英雄是否存活 */
  isAlive: boolean
  /** 复活倒计时秒数 */
  respawnTime: number
  /** 买活所需金钱 */
  buybackCost: number
  /** 买活剩余冷却时间 (秒) */
  buybackCooldown: number
  /** 当前手头可靠+不可靠金钱总和 */
  gold: number
  /** 当前每分钟金钱 GPM */
  gpm: number
  /** 当前每分钟经验 XPM */
  xpm: number
  /** 正补数 */
  lastHits: number
  /** 反补数 */
  denies: number
  /** 击杀 / 阵亡 / 助攻 */
  kda: string
  /** 是否白天 */
  isDaytime: boolean
  /** 当前技能栏快照 */
  abilities: AbilityHudItem[]
  /** 当前物品栏快照 */
  items: ItemHudItem[]
}
