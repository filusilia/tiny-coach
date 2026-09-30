import { reactive } from 'vue'
import { playerApi } from '@/api'
import { HERO_CHINESE_NAMES } from '@/data/heroNames.ts'

/**
 * OpenDota 单场比赛摘要实体
 * 映射自 OpenDota REST API: /api/players/{account_id}/matches
 */
export interface OpenDotaMatchSummary {
  /** 比赛唯一数字 ID */
  match_id: number
  /** 玩家槽位编号 (0~4 为天辉 Radiant, 128~132 为夜魇 Dire) */
  player_slot: number
  /** 是否天辉阵营胜利 */
  radiant_win: boolean
  /** 比赛持续时长 (秒) */
  duration: number
  /** 比赛模式代码 (如 22 为 All Pick 天梯排名模式) */
  game_mode: number
  /** 房间类型代码 (如 7 为 Ranked 天梯) */
  lobby_type: number
  /** 使用的英雄数字 ID */
  hero_id: number
  /** 比赛开始的 Unix 时间戳 (秒) */
  start_time: number
  /** 游戏版本代码 */
  version?: number
  /** 个人击杀数 */
  kills: number
  /** 个人阵亡数 */
  deaths: number
  /** 个人助攻数 */
  assists: number
  /** 房间平均天梯段位星级 */
  average_rank?: number
  /** 组队开黑人数 (单排为 1) */
  party_size?: number
}

/**
 * 玩家单个英雄实战综合表现实体
 */
export interface HeroPerformance {
  /** 英雄数字 ID */
  heroId: number
  /** 英雄本地化中文名称 */
  heroName: string
  /** 近期出战总场次 */
  matches: number
  /** 近期获胜场次 */
  wins: number
  /** 胜率百分比 (0 ~ 100) */
  winRate: number
  /** 场均击杀数 */
  avgKills: number
  /** 场均阵亡数 */
  avgDeaths: number
  /** 场均助攻数 */
  avgAssists: number
}

/**
 * 玩家近期综合技战术画像实体
 * 严格遵照合规红线脱敏，仅输出宏观统计特征
 */
export interface PlayerProfileAnalysis {
  /** 玩家 32 位 Dota 2 数字账号 ID */
  accountId: string
  /** 统计样本总场次数 (默认 20 场) */
  totalMatches: number
  /** 胜场总数 */
  wins: number
  /** 负场总数 */
  losses: number
  /** 总体胜率百分比 (0 ~ 100) */
  winRate: number
  /** 场均击杀 (K) */
  avgKills: number
  /** 场均阵亡 (D) */
  avgDeaths: number
  /** 场均助攻 (A) */
  avgAssists: number
  /** 胜率与场次最高的 Top 5 招牌英雄池 */
  topHeroes: HeroPerformance[]
  /** 近期使用的全量英雄表现列表 */
  recentHeroes: HeroPerformance[]
  /** 当前对局操纵英雄的专属画像切片 (若有) */
  currentHeroProfile?: HeroPerformance
  /** 自然语言总结的技战术风格概括 */
  tacticalSummary: string
  /** 画像计算更新时间戳 */
  lastUpdated: string
}

/** 本地 localStorage 存储玩家 ID 的键名 */
const STORAGE_KEY_PLAYER_ID = 'aegis_dota2_account_id'
/** 本地战绩画像缓存键名 */
const CACHE_KEY_PROFILE = 'aegis_player_profile_cache'
/** 缓存有效时长 (10 分钟)，避免频繁刷新触发 OpenDota API 429 限流 */
const CACHE_TTL_MS = 10 * 60 * 1000

/**
 * 64 位 Steam ID 转 32 位 Dota 2 Account ID
 * 严格依据 Valve 账号转换公式: 32位 ID = SteamID64 - 76561197960265728
 * 支持直接输入 32 位 ID，自动兼容识别
 * @param steamId64 原始 64 位 Steam 社区 ID 或 32 位好友 ID
 * @returns 32 位数字字符串，解析失败返回 null
 */
export function convertSteamId64ToAccountId(steamId64: string): string | null {
  try {
    const raw = steamId64.trim()
    if (!raw) return null
    if (raw.length < 15) {
      // 已经是 32 位 Account ID
      return raw
    }
    const big = BigInt(raw)
    const base = BigInt('76561197960265728')
    if (big > base) {
      return (big - base).toString()
    }
    return raw
  } catch {
    return null
  }
}

/**
 * Dota 2 玩家数字 ID 规范化提取器
 *
 * 统一出口约束（铁律 6 / 10）：规范化规则（全角数字、零宽字符/Emoji 清洗、Steam3、
 * SteamID64 换算、OpenDota/Dotabuff/Steam 链接解析、昵称模糊搜索）**只在 Go 侧
 * PlayerService.NormalizeAccountId 实现一份**。
 *
 * 历史上此处曾有一份约 80 行的 TS 平行实现作为"降级"，它不仅与 Go 侧规则各自演化
 * （修复一处必然漏改另一处），还直连 OpenDota 搜索接口，与统一调度/防限流要求冲突。
 * 故彻底移除，本函数仅做空值守卫与错误归一化。
 *
 * @param input 用户输入的任意玩家文本（ID、好友码、链接或昵称）
 * @returns 规范化的 32 位数字账号 ID；无法解析或后端不可用时返回 null
 */
export async function normalizeDotaAccountId(input: string): Promise<string | null> {
  if (!input || typeof input !== 'string' || !input.trim()) return null
  try {
    const normalized = await playerApi.normalizeAccountId(input)
    return normalized && normalized.trim() ? normalized.trim() : null
  } catch (e) {
    console.warn('[PlayerProfile] 规范化玩家 ID 失败:', e)
    return null
  }
}

/**
 * 玩家对战档案与技战术风格画像服务
 * 负责绑定玩家账号、拉取公开战绩、计算 KDA 与英雄池胜率、生成 AI 战术提示片段
 */
class PlayerProfileService {
  /** 响应式状态容器 */
  public state = reactive<{
    accountId: string
    loading: boolean
    error: string | null
    profile: PlayerProfileAnalysis | null
  }>({
    accountId: '',
    loading: false,
    error: null,
    profile: null,
  })

  constructor() {
    this.loadSavedAccountId()
  }

  /**
   * 从 localStorage 加载此前绑定的玩家 ID 及有效缓存
   */
  private loadSavedAccountId() {
    if (typeof window === 'undefined' || !window.localStorage) return
    try {
      const saved = localStorage.getItem(STORAGE_KEY_PLAYER_ID)
      if (saved) {
        this.state.accountId = saved
      }
      const cached = localStorage.getItem(CACHE_KEY_PROFILE)
      if (cached) {
        const parsed = JSON.parse(cached)
        if (parsed && Date.now() - parsed.timestamp < CACHE_TTL_MS) {
          this.state.profile = parsed.data
        }
      }
    } catch (e) {
      console.warn('读取本地玩家战绩缓存失败:', e)
    }
  }

  /**
   * 设置并持久化玩家 32 位 Dota 2 账号 ID
   * @param id 玩家数字 ID
   */
  public setAccountId(id: string) {
    const cleanId = id.trim()
    this.state.accountId = cleanId
    if (typeof window !== 'undefined' && window.localStorage) {
      localStorage.setItem(STORAGE_KEY_PLAYER_ID, cleanId)
    }
  }

  /**
   * 拉取指定玩家近 20 场公开比赛历史并提炼画像
   *
   * 统一出口约束（铁律 6 / 10）：战绩与画像聚合**只在 Go 侧 PlayerService 实现一份**，
   * 本方法仅负责调用绑定并把结果映射为前端视图模型。
   * 历史上此处曾在 Go 调用失败时用前端 fetch 重发同一请求并自行聚合画像，
   * 既造成双发流量（触碰防限流要求），也让 Go 侧的静默契约缺陷长期无人察觉。
   *
   * @param accountIdInput 可选目标玩家 ID，未传则读取当前绑定账号
   * @returns 提炼后的战绩画像实体，失败返回 null
   */
  public async fetchRecentMatches(accountIdInput?: string): Promise<PlayerProfileAnalysis | null> {
    const rawInput = (accountIdInput || this.state.accountId).trim()
    const targetId = (await normalizeDotaAccountId(rawInput)) || rawInput
    if (!targetId) {
      this.state.error = '未配置有效的 Dota 2 玩家数字 ID'
      return null
    }

    this.state.loading = true
    this.state.error = null

    try {
      // Go 侧带 10 分钟 TTL 内存缓存与磁盘持久化，且为 OpenDota 唯一出口
      const nativeProfile = await playerApi.getPlayerProfile(targetId)
      if (!nativeProfile || !nativeProfile.accountId) {
        throw new Error('未检索到该玩家近期公开战绩 (请确认游戏内是否已开启「公开比赛数据」)')
      }

      const rawHeroes = nativeProfile.topHeroes.length > 0
        ? nativeProfile.topHeroes
        : nativeProfile.recentHeroes

      const topHeroes: HeroPerformance[] = rawHeroes.map((h) => {
        const matches = h.matches ?? 0
        const wins = h.wins ?? 0
        const winRate = h.winRate ?? (matches > 0 ? Math.round((wins / matches) * 100) : 0)
        return {
          heroId: h.heroId,
          heroName: h.heroName || HERO_CHINESE_NAMES[h.heroId] || `英雄 #${h.heroId}`,
          matches,
          wins,
          winRate,
          avgKills: Number(h.avgKills || 0),
          avgDeaths: Number(h.avgDeaths || 0),
          avgAssists: Number(h.avgAssists || 0),
        }
      })

      const totalMatches = nativeProfile.totalMatches || 20
      const wins = nativeProfile.wins || 0
      const losses = nativeProfile.losses ?? Math.max(0, totalMatches - wins)
      const winRate = nativeProfile.winRate ?? (totalMatches > 0 ? Math.round((wins / totalMatches) * 100) : 0)
      const summary = nativeProfile.tacticalSummary || '天梯实力选手'

      const mappedProfile: PlayerProfileAnalysis = {
        accountId: nativeProfile.accountId,
        totalMatches,
        wins,
        losses,
        winRate,
        avgKills: Number(nativeProfile.avgKills || 0),
        avgDeaths: Number(nativeProfile.avgDeaths || 0),
        avgAssists: Number(nativeProfile.avgAssists || 0),
        topHeroes: topHeroes.slice(0, 5),
        recentHeroes: topHeroes,
        tacticalSummary: summary,
        lastUpdated: nativeProfile.lastUpdated || new Date().toLocaleTimeString('zh-CN', { hour12: false }),
      }

      this.state.profile = mappedProfile
      if (typeof window !== 'undefined' && window.localStorage) {
        localStorage.setItem(
          CACHE_KEY_PROFILE,
          JSON.stringify({ timestamp: Date.now(), data: mappedProfile }),
        )
      }
      return mappedProfile
    } catch (err: any) {
      console.warn('[PlayerProfile] 拉取玩家战绩画像失败:', err)
      this.state.error = err?.message || '获取近期比赛失败'
      return null
    } finally {
      this.state.loading = false
    }
  }

  /**
   * 根据当前局所选英雄，提炼并输出用于注入 AI Coach Prompt 的脱敏画像片段
   * 若玩家使用当前英雄出场较多但场均阵亡偏高，指导 AI 强化视野与保命防 gank 建议；
   * 若胜率极高，指导 AI 提供进阶带线压制与主动开雾战略。
   * @param currentHeroId 当前操纵英雄数字 ID
   * @returns 注入 Prompt 的结构化画像文本，未就绪时返回空字符串
   */
  public getTacticalPromptSnippet(currentHeroId?: number): string {
    const p = this.state.profile
    if (!p) return ''

    const lines: string[] = [
      `【玩家近期战绩画像 (OpenDota 脱敏特征)】`,
      `- 近 20 场整体战绩: ${p.wins}胜 ${p.losses}负 (胜率 ${p.winRate}%) | 场均战绩: ${p.avgKills}K / ${p.avgDeaths}D / ${p.avgAssists}A`,
      `- 常用英雄池: ${p.topHeroes.map(h => `${h.heroName}(${h.matches}场/胜率${h.winRate}%)`).join(', ') || '多英雄轮换'}`,
    ]

    if (currentHeroId) {
      const matchHero = p.recentHeroes.find(h => h.heroId === currentHeroId)
      if (matchHero) {
        lines.push(
          `- 当前英雄 [${matchHero.heroName}] 近期熟练度: 近 20 场出战 ${matchHero.matches} 次，胜率 ${matchHero.winRate}% (场均 ${matchHero.avgKills}/${matchHero.avgDeaths}/${matchHero.avgAssists})`,
        )
        if (matchHero.avgDeaths >= 5.0) {
          lines.push(`- 战术风格提示: 玩家使用该英雄近期场均阵亡偏高(${matchHero.avgDeaths}次)，需重点强化防 gank 视野感知与自保装(BKB/跳刀/推推)优先级。`)
        } else if (matchHero.winRate >= 70) {
          lines.push(`- 战术风格提示: 玩家对该英雄熟练度极高，可更多建议进阶带线牵制、压制控盾与终结比赛路线。`)
        }
      } else {
        lines.push(`- 当前英雄 [${HERO_CHINESE_NAMES[currentHeroId] || '当前英雄'}] 近期未有出战记录，建议战术提示偏向基础连招次序与稳健出装。`)
      }
    }

    return lines.join('\n')
  }
}

export const playerProfileService = new PlayerProfileService()

/**
 * 原生获取单场历史比赛全量复盘详情
 * 统一出口约束（铁律 6 / 10）：优先读取 Go 端本地磁盘不可变缓存，未命中则走 Go 原生抓取并落盘
 */
export async function fetchMatchDetails(matchId: string): Promise<any> {
  const cleanId = matchId.trim()
  if (!cleanId) throw new Error('比赛 ID 不能为空')

  const rawJson = await playerApi.getMatchDetails(cleanId)
  if (rawJson) {
    return JSON.parse(rawJson)
  }
  throw new Error('未能获取比赛详情数据')
}

/**
 * 原生拉取玩家近期比赛简报
 * 统一出口约束（铁律 6 / 10）：走 Go 原生单例接口，享有后端防限流与缓存保护
 */
export async function fetchPlayerRecentMatchesList(accountId: string, _limit: number = 100): Promise<any[]> {
  const cleanId = accountId.trim()
  if (!cleanId) return []

  try {
    const rawJson = await playerApi.getPlayerRecentMatches(cleanId)
    if (rawJson) {
      const parsed = JSON.parse(rawJson)
      if (Array.isArray(parsed) && parsed.length > 0) return parsed
    }
  } catch (e) {
    console.warn('[PlayerProfile] playerApi.getPlayerRecentMatches 失败:', e)
  }

  return []
}
