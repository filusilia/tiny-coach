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
