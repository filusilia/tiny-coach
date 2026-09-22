/**
 * 前后端契约强类型定义 (src/api/types.ts)
 * 严格与 Go 端 internal/models/ 保持 1:1 映射
 * 遵循小驼峰命名规范，严禁出现下划线字段
 */

// ==============================================================================
// 1. 系统与存储模型 (System & Storage DTO)
// ==============================================================================

export interface CacheDirectoryResponse {
  /** 当前生效的缓存根目录绝对路径 */
  cacheDir: string
  /** 系统权威默认缓存目录 (如 E:\documents\DotaAICoach) */
  defaultDir: string
  /** 当前是否由用户手动自定义了存储目录 */
  isCustom: boolean
}

export interface DiskModelFile {
  /** 模型文件名 (例如 qwen2.5-1.5b-instruct.gguf) */
  name: string
  /** 模型文件全量绝对路径 */
  path: string
  /** 文件字节大小 (单位: bytes) */
  sizeBytes: number
  /** 格式化人类可读大小 (例如 "1.24 GB") */
  sizeDisplay: string
}

// ==============================================================================
// 2. 玩家画像与战绩分析模型 (Player Profile DTO)
// ==============================================================================

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
  matches?: number
  /** 获胜场次 */
  wins?: number
  /** 胜率百分比 (0-100) */
  winRate?: number
  /** 场均击杀 */
  avgKills?: number
  /** 场均阵亡 */
  avgDeaths?: number
  /** 场均助攻 */
  avgAssists?: number
  // 历史兼容字段
  games?: number
  win?: number
  winrate?: number
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
  avgKills?: number
  /** 场均阵亡 */
  avgDeaths?: number
  /** 场均助攻 */
  avgAssists?: number
  /** 招牌/常用英雄表现 */
  topHeroes?: HeroPerformanceDTO[]
  /** 近期高频选用英雄表现列表 */
  recentHeroes: HeroPerformanceDTO[]
  /** 自然语言战术风格总结 */
  tacticalSummary?: string
  /** 缓存写入/更新时间戳 */
  lastUpdated?: string
  // 历史过渡兼容字段
  personaname?: string
  avatarUrl?: string
  totalGames?: number
  winGames?: number
  winrate?: number
  tags?: string[]
  fromCache?: boolean
  cachedAt?: number
}

// ==============================================================================
// 3. AI 推理与大模型服务模型 (AI DTO)
// ==============================================================================

export type AiProviderMode = 'local' | 'cloud' | 'custom'

export interface AiEndpointTestResultDTO {
  /** 连通测试是否成功 */
  success: boolean
  /** 网络往返延迟耗时 (毫秒) */
  latencyMs: number
  /** 原始响应内容 */
  rawResponse?: string
  /** 错误信息 */
  errorMessage?: string
  /** 响应的模型标识名 */
  modelUsed?: string
}

export interface BattleContextDTO {
  heroId?: number
  heroName?: string
  heroLevel?: number
  healthPercent?: number
  manaPercent?: number
  gold?: number
  clockTime?: string
  kda?: string
  items?: string[]
  abilities?: string[]
  userNotes?: string
  playerProfileSnippet?: string
}

export interface MatchReviewResultDTO {
  overview: string
  keyTurningPoints: string
  itemizationAnalysis: string
  nextMatchActions: string[]
  fullText: string
  modelUsed: string
}

// ==============================================================================
// 4. Dota 2 GSI 实时数据快照模型 (GSI DTO)
// ==============================================================================

export interface GsiPlayerState {
  steamId: string
  name: string
  activity: string
  kills: number
  deaths: number
  assists: number
  lastHits: number
  denies: number
  killStreak: number
  gold: number
  goldReliable: number
  goldUnreliable: number
  goldPerMin: number
  xpPerMin: number
}

export interface GsiHeroState {
  id: number
  name: string
  level: number
  alive: boolean
  respawnSeconds: number
  buybackCost: number
  buybackCooldown: number
  health: number
  maxHealth: number
  healthPercent: number
  mana: number
  maxMana: number
  manaPercent: number
  hasAghsScepter: boolean
  hasAghsShard: boolean
}

export interface GsiMapState {
  matchId: string
  gameTime: number
  clockTime: number
  daytime: boolean
  nightstalkerNight: boolean
  radiantScore: number
  direScore: number
  gameState: string
  winTeam: string
}

export interface GsiItemSlot {
  name: string
  purchaser: number
  passive: boolean
  canCast: boolean
  cooldown: number
  charges?: number
}

export interface GsiAbilitySlot {
  name: string
  level: number
  canCast: boolean
  passive: boolean
  abilityActive: boolean
  cooldown: number
  ultimate: boolean
}

export interface GsiSnapshot {
  provider?: {
    name: string
    appid: number
    version: number
    timestamp: number
  }
  map?: GsiMapState
  player?: GsiPlayerState
  hero?: GsiHeroState
  abilities?: Record<string, GsiAbilitySlot>
  items?: Record<string, GsiItemSlot>
}
