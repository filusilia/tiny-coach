/**
 * 玩家画像与战绩分析模型契约定义 (src/api/types/player.ts)
 * 严格与 Go 端 internal/models/player.go 保持 1:1 映射
 * 遵循零历史包袱原则，已清理已废弃的历史兼容器字段
 */

export interface PlayerHeroWinrate {
  /** 英雄数字 ID */
  heroId: number
  /** 英雄名称 */
  heroName: string
  /** 总场次 */
  games: number
  /** 胜场 */
  win: number
  /** 胜率百分比 (0-100) */
  winrate: number
}

export interface RecentMatchSummary {
  /** 比赛数字 ID */
  matchId: string
  /** 所选英雄数字 ID */
  heroId: number
  /** 所选英雄名称 */
  heroName: string
  /** 击杀数 */
  kills: number
  /** 阵亡数 */
  deaths: number
  /** 助攻数 */
  assists: number
  /** 对局时长 (分钟) */
  durationMinutes: number
  /** 本场是否获胜 */
  isWin: boolean
  /** 比赛开始 UNIX 时间戳 (秒) */
  startTime: number
}

export interface HeroPerformanceDTO {
  /** 英雄数字 ID */
  heroId: number
  /** 英雄本地化名称 */
  heroName: string
  /** 出场场次 */
  matches: number
  /** 获胜场次 */
  wins: number
  /** 胜率百分比 (0-100) */
  winRate: number
  /** 场均击杀 */
  avgKills: number
  /** 场均阵亡 */
  avgDeaths: number
  /** 场均助攻 */
  avgAssists: number
}

export interface PlayerProfileAnalysis {
  /** 统一规范后的 32 位 Dota 账号数字 ID */
  accountId: string
  /** 历史已录入总场次 */
  totalMatches: number
  /** 历史总胜场 */
  wins: number
  /** 历史总负场 */
  losses: number
  /** 历史总体胜率百分比 (0-100) */
  winRate: number
  /** 场均击杀 */
  avgKills: number
  /** 场均阵亡 */
  avgDeaths: number
  /** 场均助攻 */
  avgAssists: number
  /** 招牌/常用英雄表现 */
  topHeroes: HeroPerformanceDTO[]
  /** 近期高频选用英雄表现列表 */
  recentHeroes: HeroPerformanceDTO[]
  /** 自然语言战术风格总结 */
  tacticalSummary: string
  /** 缓存写入/更新时间戳 */
  lastUpdated: string
}

export interface OpenDotaPlayer {
  player_slot: number
  account_id: number
  hero_id: number
  personaname?: string
  name?: string
  level: number
  kills: number
  deaths: number
  assists: number
  kda?: number
  gold_per_min: number
  xp_per_min: number
  hero_damage: number
  tower_damage: number
  hero_healing?: number
  last_hits: number
  denies: number
  net_worth?: number
  total_gold?: number
  item_0?: number
  item_1?: number
  item_2?: number
  item_3?: number
  item_4?: number
  item_5?: number
  backpack_0?: number
  backpack_1?: number
  backpack_2?: number
  item_neutral?: number
  aghanims_scepter?: number
  aghanims_shard?: number
  buyback_count?: number
  sen_placed?: number
  obs_placed?: number
  purchase_ward_sen?: number
  purchase_ward_obs?: number
  purchase_gem?: number
  camps_stacked?: number
  rune_pickups?: number
  roshans_killed?: number
  firstblood_claimed?: number
  ability_upgrades_arr?: number[]
  benchmarks?: Record<string, { raw?: number, pct?: number }>
}

export interface OpenDotaPickBan {
  is_pick: boolean
  hero_id: number
  team: number // 0: Radiant, 1: Dire
  order: number
}

export interface OpenDotaMatchDetail {
  match_id: number
  duration: number
  radiant_win: boolean
  radiant_score: number
  dire_score: number
  start_time: number
  game_mode: number
  lobby_type: number
  players: OpenDotaPlayer[]
  radiant_gold_adv?: number[]
  radiant_xp_adv?: number[]
  picks_bans?: OpenDotaPickBan[]
  barracks_status_radiant?: number
  barracks_status_dire?: number
  tower_status_radiant?: number
  tower_status_dire?: number
  first_blood_time?: number
  region?: number
  leagueid?: number
}

export interface OpenDotaPlayerProfile {
  profile: {
    personaname: string
    avatar: string
    avatarfull: string
  }
  rank_tier?: number
  leaderboard_rank?: number | null
}

export interface ItemMeta {
  key: string
  nameLoc: string
  dname: string
}

export interface ThreatHeroSummary {
  heroId: number
  heroName: string
  gamesPlayed: number
  winRate: number
  threatLevel: 'extreme' | 'high' | 'normal'
  threatDesc: string
}

export interface RecommendedItem {
  itemId: number
  itemKey: string
  itemName: string
  category: 'start' | 'early' | 'mid' | 'late' | 'counter'
  popularity: number
  reason: string
  winRate?: number
}

export interface HeroMatchupRecommendation {
  heroId: number
  heroName: string
  threatHeroes: ThreatHeroSummary[]
  startItems: RecommendedItem[]
  earlyItems: RecommendedItem[]
  midItems: RecommendedItem[]
  lateItems: RecommendedItem[]
  counterItems: RecommendedItem[]
  source: 'opendota' | 'cache' | 'offline_rule'
  updatedAt: string
}
