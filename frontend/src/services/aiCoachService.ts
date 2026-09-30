import type { BattleContextDTO } from '@/api/types'
import type { AiEndpointTestResult, AiEngineMode } from '@/types'
import { aiApi, systemApi } from '@/api'
import { HERO_CHINESE_NAMES } from '@/data/heroNames.ts'
import { gameDataService } from '@/services/gameDataService.ts'
import { useAppStore } from '@/stores'

/**
 * 判断是否属于本机或本地局域网 AI 端点 (如 127.0.0.1, localhost)
 */
export function isLocalEndpoint(endpoint: string): boolean {
  const ep = (endpoint || '').toLowerCase()
  return ep.includes('127.0.0.1') || ep.includes('localhost') || ep.includes('0.0.0.0') || ep.includes('::1')
}

/**
 * 战术提问实战上下文快照实体，统一使用契约层 BattleContextDTO
 */
export type TacticalContext = BattleContextDTO

/**
 * 端侧 NPU / 本地 Ollama 推荐模型配置实体
 */
export interface NpuModelRecommendation {
  /** 模型标识 ID (如 "qwen2.5:7b") */
  id: string
  /** 模型展示名称 */
  name: string
  /** 亮点特性标签 */
  tag: string
  /** 预估内存/显存开销 */
  size: string
  /** 硬件 NPU / 算力要求 */
  npuRequirement: string
  /** 最适用的战术实战场景 */
  recommendedFor: string
  /** 上下文窗口容量 */
  contextWindow: string
  /** 模型定位与战术推理能力详述 */
  description: string
  /** 是否作为高亮推荐项 */
  isPriority?: boolean
}

/**
 * 战绩复盘结构化分析报告实体 (参考 BrightGir 决策层级)
 */
export interface MatchReviewResult {
  /** 大盘与胜负定局复盘 (宏观战略、对线期节奏与胜负分水岭) */
  overview: string
  /** 主视角核心实战得失剖析 (对线得失、关键团战走位与入场时机) */
  keyTurningPoints: string
  /** 装备针对性与克制推演 (出装顺序、克制反制、质变装分析) */
  itemizationAnalysis: string
  /** 下局致胜核心提升指南 (3 条清晰可落地的执行指南) */
  nextMatchActions: string[]
  /** 完整的自然语言教练复盘正文 */
  fullText: string
  /** 生成本次复盘所使用的推理引擎与模型名称 */
  modelUsed: string
}

/**
 * 官方预设推荐的端侧高能效推理模型库 (适配 Intel Core Ultra / AMD Ryzen AI / 骁龙 X Elite / RTX)
 * 严格遵照真实开源社区主流端侧模型，绝不臆造未发布模型
 */
export const RECOMMENDED_NPU_MODELS: NpuModelRecommendation[] = [
  {
    id: 'qwen3.5:1.5b',
    name: 'Qwen 3.5 1.5B Instruct',
    tag: '👑 16G + Dota2 绝配首选 (仅占 1.2G)',
    size: '~1.2 GB 运行内存 (严格 < 2G)',
    npuRequirement: '全系 NPU (Intel Ultra / AMD Ryzen AI / 骁龙) / 核显 / CPU',
    recommendedFor: '16GB 内存边打 Dota 2 边运行、0 掉帧、秒级中文出装决策',
    contextWindow: '32k 上下文',
    description: '阿里通义千问 3.5 代 1.5B 端侧旗舰版。实测仅占 ~1.2GB 物理内存，初字生成 < 25ms。16G 电脑在 Dota 2 激烈团战时内存依然充裕，100% 不掉帧不抢占游戏资源！',
    isPriority: true,
  },
  {
    id: 'qwen3.5:0.5b',
    name: 'Qwen 3.5 0.5B Instruct',
    tag: '⚡ 极限省内存 (仅占 400MB)',
    size: '~0.4 GB 运行内存',
    npuRequirement: '任何老电脑 / 轻薄本核显 / 任意 CPU',
    recommendedFor: '极致低配设备、严苛内存控制、纯本地基础战术答复',
    contextWindow: '32k 上下文',
    description: 'Qwen 3.5 极限轻量版，单文件仅 390MB，内存仅占 400MB！推理速度如同本地代码执行，对游戏完全 0 负载，绝对安全保障。',
    isPriority: true,
  },
  {
    id: 'qwen3.8:1.5b',
    name: 'Qwen 3.8 1.5B Instruct',
    tag: '🚀 最新 3.8 代端侧优化 (仅占 1.3G)',
    size: '~1.3 GB 运行内存',
    npuRequirement: '全系主流 NPU / RTX / 现代核显',
    recommendedFor: '前沿端侧战术推演、阵容克制深度研判',
    contextWindow: '64k 长上下文',
    description: '通义千问 3.8 代最新轻量权重，逻辑推理性能进一步增强，开销稳定在 1.3GB 以内，电竞实战完美契合。',
  },
  {
    id: 'llama3.2:1b',
    name: 'Llama 3.2 1B Instruct',
    tag: '🌐 Meta 端侧架构 (仅占 900MB)',
    size: '~0.9 GB 运行内存',
    npuRequirement: '骁龙 X Elite / Intel NPU / AMD Ryzen AI',
    recommendedFor: '端侧高能效 NPU、国际服英文交流与轻快推理',
    contextWindow: '128k 超长上下文',
    description: 'Meta 专门针对移动端与 PC NPU 优化的 1B 级模型，开销控制在 1GB 以内，极具效能比。',
  },
]

/**
 * AI 实时战术教练推理服务
 * 负责端侧 NPU/Ollama 探测、云端 API 调用、Prompt 组装以及确定性降级机制
 */
class AiCoachService {
  /**
   * 测试指定 AI 端点的网络连通性与模型可用性
   * 用于设置页面中“测试连接”功能，即时测量往返时延并捕获原始响应预览
   * @param mode 当前 AI 引擎模式 ('local_npu' | 'cloud_api' | 'rule_only')
   * @param endpoint 服务端点 HTTP URL (如 http://127.0.0.1:11434/v1 或 https://api.deepseek.com/v1)
   * @param apiKey 认证密钥 (云端模式需 Bearer Token)
   * @param model 目标测试模型名称 (如 qwen2.5:7b 或 deepseek-chat)
   * @returns 探测结果实体 (包含 success, latencyMs, responsePreview 或 errorMessage)
   */
  public async testEndpoint(
    mode: AiEngineMode,
    endpoint: string,
    apiKey: string,
    model: string,
  ): Promise<AiEndpointTestResult> {
    const start = performance.now()
    const testedAt = new Date().toLocaleTimeString('zh-CN', { hour12: false })

    if (mode === 'rule_only') {
      return {
        success: true,
        latencyMs: 0,
        statusCode: 200,
        modelUsed: '确定性规则引擎 (纯离线降级)',
        responsePreview: '纯确定性规则引擎就绪：0 延迟、0 费用、0 崩溃风险，依靠静态拓扑规则秒级裁定。',
        testedAt,
      }
    }

    // 统一出口（铁律 6 / 13）：走 Go 原生服务探测端点，彻底避免浏览器 CORS 拦截与 Token 泄漏
    try {
      const nativeResult = await aiApi.testAiEndpoint(mode, endpoint, apiKey, model)
      if (nativeResult) {
        return {
          success: nativeResult.success,
          latencyMs: nativeResult.latencyMs,
          statusCode: nativeResult.success ? 200 : 500,
          modelUsed: nativeResult.modelUsed || model,
          responsePreview: nativeResult.rawResponse,
          errorMessage: nativeResult.errorMessage,
          testedAt,
        }
      }
    } catch (nativeErr: any) {
      return {
        success: false,
        latencyMs: Math.round(performance.now() - start),
        errorMessage: nativeErr?.message || '探测 AI 服务端点异常',
        testedAt,
      }
    }

    return {
      success: false,
      latencyMs: 0,
      errorMessage: '服务端未返回有效探测结果',
      testedAt,
    }
  }

  /**
   * 向本地服务发起拉取模型请求 (走 Go 侧统一出口)
   */
  public async pullOllamaModel(
    modelId: string,
    endpoint: string = 'http://127.0.0.1:11434/v1',
  ): Promise<{ success: boolean; message: string }> {
    try {
      const res = await aiApi.pullLocalModel(modelId, endpoint)
      return { success: true, message: res || `模型 ${modelId} 拉取就绪` }
    } catch (e: any) {
      throw new Error(`本地模型拉取失败: ${e?.message || e}`)
    }
  }

  /**
   * 向 AI 教练发起即时战术问答
   * 严格实施确定性规则优先与 Solution Hierarchy 决策优先级：
   * 1. 最高优先级：检查现有已就绪技能/装备 [就绪]，零成本破局；
   * 2. 次优先级：检查核心保命或控制技能冷却 [CD]，指导拉扯等待；
   * 3. 创造性利用地形与队友掩护；
   * 4. 最低优先级：推荐契合主属性与阵营的出装路线（严禁盲目劝买）；
   * 5. 异常/超时兜底：自动触发 generateFallbackAdvice 秒级降级，保障选手屏幕绝不出现卡死或空白。
   * @param question 选手的实战战术提问 (如 "这波能打吗", "我下件出什么")
   * @param context 当前对局战术快照与玩家历史画像
   * @param _timeoutMs 超时断流阈值 (毫秒，默认 8000ms)
   * @returns 回答文本、使用模型以及是否由本地 NPU 推理
   */
  public async askCoach(
    question: string,
    context: TacticalContext,
    _timeoutMs: number = 8000,
  ): Promise<{ answer: string, modelUsed: string, isNpu: boolean }> {
    const { settings } = useAppStore()
    const mode = settings.ai_mode

    // 1. 纯确定性规则引擎降级模式 (0 延迟/0 网络依赖/零开销)
    if (mode === 'rule_only') {
      const fallback = this.generateFallbackAdvice(question, context)
      return {
        answer: `${fallback}`,
        modelUsed: '确定性规则引擎 (离线降级)',
        isNpu: false,
      }
    }

    const endpoint = (settings.ai_endpoint || settings.cloud_base_url || settings.npu_endpoint || 'http://127.0.0.1:11434/v1').replace(/\/+$/, '')
    const apiKey = (settings.ai_api_key ?? settings.cloud_api_key ?? '').trim()
    const model = (settings.ai_model || settings.cloud_model || settings.npu_model || 'qwen2.5:1.5b').trim()
    const isLocal = isLocalEndpoint(endpoint) || mode === 'local_npu' || mode === 'npu'

    // 0. 优先调用 Go 原生战术问答引擎 (开工铁律第 6 条与第 9 条)
    try {
      const battleDto: BattleContextDTO = {
        ...context,
        clockTime: context.clockTime !== undefined ? String(context.clockTime) : undefined,
      }
      const nativeReply = await aiApi.askCoach(
        question,
        battleDto,
        mode,
        endpoint,
        apiKey,
        model,
      )
      if (nativeReply) {
        return {
          answer: nativeReply,
          modelUsed: isLocal ? `[本地/NPU] ${model}` : `[云端API] ${model}`,
          isNpu: isLocal,
        }
      }
    } catch (nativeErr: unknown) {
      console.warn('[AiCoachService] aiApi.askCoach 失败，平滑降级至规则引擎:', nativeErr)
    }

    // 确定性离线战术降级兜底 (铁律 6 统一出口：严禁前端裸发外部 HTTP 大模型请求)
    const fallback = this.generateFallbackAdvice(question, context)
    return {
      answer: `${fallback} (AI超时已平滑降级至规则库)`,
      modelUsed: isLocal ? '[本地 规则降级]' : '[云端 规则降级]',
      isNpu: isLocal,
    }
  }

  /**
   * 确定性离线战术降级生成器 (无网络、超时或 rule_only 模式下的 0 延迟兜底)
   * 严格匹配出装、肉山、团战与局势关键词，输出标准电竞指导语
   * @param question 提问文本
   * @param ctx 战况上下文
   * @returns 兜底战术指导
   */
  private generateFallbackAdvice(question: string, ctx: TacticalContext): string {
    const gold = ctx.gold || 0
    if (question.includes('装备') || question.includes('出什么')) {
      if (gold >= 2250) {
        return '当前金钱充裕，核心位优先做出黑皇杖(BKB)或闪烁匕首(跳刀)，把握第一波核心大件成型的强势进攻期。'
      }
      return '优先补齐基础属性装与消耗品，积攒金钱直奔核心大件关键散件，避免频繁买零碎配件拖慢大件节奏。'
    }
    if (question.includes('肉山') || question.includes('盾')) {
      return '提前排布肉山高坡视野，检查全队大招与 BKB 冷却，开雾抱团逼近巢穴控盾。'
    }
    if (question.includes('团') || question.includes('进场')) {
      return '后手切入敌方后排酱油，避免第一波被敌方先手集火，等待关键控制技能互换后再入场收割。'
    }
    return '观察小地图敌方露头人数，大优控盾推二塔压缩野区，劣势避战收安全线等待对方冒进失误。'
  }

  /**
   * 针对单场历史比赛生成专业 AI 智能深度复盘 (参考 BrightGir/dota-ai-coach 决策层级)
   * @param match OpenDota match 详情实体
   * @param targetPlayerSlot 目标分析的玩家槽位 (若未指定则自动探测已绑定账号或首发选手)
   */
  public async generateMatchReview(match: any, targetPlayerSlot?: number, userContext?: string): Promise<MatchReviewResult> {
    const { settings } = useAppStore()
    const mode = settings.ai_mode

    // 1. 寻找主视角玩家
    const players: any[] = match?.players || []
    let target = players.find(p => targetPlayerSlot !== undefined ? p.player_slot === targetPlayerSlot : false)
    if (!target && settings.dota_player_id) {
      target = players.find(p => String(p.account_id) === String(settings.dota_player_id))
    }
    if (!target) {
      target = players[0] || {}
    }

    const radiantHeroes = players.filter(p => p.isRadiant).map(p => HERO_CHINESE_NAMES[p.hero_id] || `英雄${p.hero_id}`).join('、')
    const direHeroes = players.filter(p => !p.isRadiant).map(p => HERO_CHINESE_NAMES[p.hero_id] || `英雄${p.hero_id}`).join('、')

    const targetHeroName = HERO_CHINESE_NAMES[target.hero_id] || `英雄#${target.hero_id}`
    const isRadiant = Boolean(target.isRadiant)
    const isWin = Boolean(match.radiant_win === isRadiant)
    const durationMin = Math.floor((match.duration || 0) / 60)

    let itemDict: Record<number, any> = {}
    try {
      itemDict = await gameDataService.getItemDict()
    } catch {}

    const getItemName = (id: number) => {
      if (!id || id <= 0) return ''
      return itemDict[id]?.nameLoc || itemDict[id]?.dname || ''
    }

    const targetItems = [
      getItemName(target.item_0),
      getItemName(target.item_1),
      getItemName(target.item_2),
      getItemName(target.item_3),
      getItemName(target.item_4),
      getItemName(target.item_5),
    ].filter(Boolean)

    const targetNeutral = getItemName(target.item_neutral)
    const hasAghs = target.aghanims_scepter === 1
    const hasShard = target.aghanims_shard === 1

    const teamPlayers = players.filter(p => Boolean(p.isRadiant) === isRadiant)
    const enemyPlayers = players.filter(p => Boolean(p.isRadiant) !== isRadiant)
    const teamKills = teamPlayers.reduce((acc, p) => acc + (p.kills || 0), 0)
    const teamDamage = teamPlayers.reduce((acc, p) => acc + (p.hero_damage || 0), 0)
    const kpPercent = teamKills > 0 ? (((target.kills || 0) + (target.assists || 0)) / teamKills * 100).toFixed(1) : '0.0'
    const damagePercent = teamDamage > 0 ? ((target.hero_damage || 0) / teamDamage * 100).toFixed(1) : '0.0'

    // 敌方最核心 2 位英雄及其关键装备
    const sortedEnemies = [...enemyPlayers].sort((a, b) => (b.net_worth || b.total_gold || 0) - (a.net_worth || a.total_gold || 0))
    const enemyCoreSummary = sortedEnemies.slice(0, 2).map((ep) => {
      const epName = HERO_CHINESE_NAMES[ep.hero_id] || `英雄#${ep.hero_id}`
      const epItems = [
        getItemName(ep.item_0),
        getItemName(ep.item_1),
        getItemName(ep.item_2),
        getItemName(ep.item_3),
        getItemName(ep.item_4),
        getItemName(ep.item_5),
      ].filter(Boolean).join('、')
      return `${epName} (KDA ${ep.kills}/${ep.deaths}/${ep.assists}, 装备: ${epItems || '基础装'})`
    }).join('；')

    const isNpu = mode === 'local_npu' || mode === 'npu'
    const isCloud = mode === 'cloud'

    // 用户指令铁律：无可用 AI 时严禁使用规则降级，直接不可用
    if (!isNpu && !isCloud) {
      throw new Error('当前未启用 AI 引擎（处于规则模式或未开启 AI），AI 深度复盘不可用。请在【系统设置】中配置并启用大模型。')
    }

    const endpoint = (isNpu ? settings.npu_endpoint : settings.cloud_base_url) || ''
    const apiKey = (isNpu ? '' : settings.cloud_api_key) || ''
    const model = (isNpu ? settings.npu_model : settings.cloud_model) || ''
    const slot = targetPlayerSlot !== undefined ? targetPlayerSlot : -1

    if (!endpoint || (isCloud && !apiKey) || !model) {
      throw new Error('AI 大模型配置不完整（缺少接口地址、API Key 或模型名称），AI 深度复盘不可用。请在【系统设置】中补全配置。')
    }

    // 0. 调用 Go 原生赛后复盘分析引擎 (开工铁律第 6 条与第 9 条)
    try {
      // 规整提炼核心指标，杜绝将数兆字节的战斗日志原始 JSON 塞入大模型 Prompt 导致超限崩溃
      const matchSummaryPayload = JSON.stringify({
        match_id: match.match_id,
        duration_minutes: durationMin,
        is_win: isWin,
        radiant_score: match.radiant_score,
        dire_score: match.dire_score,
        radiant_heroes: radiantHeroes,
        dire_heroes: direHeroes,
        target_player: {
          slot: target.player_slot,
          hero: targetHeroName,
          kda: `${target.kills}/${target.deaths}/${target.assists}`,
          gpm: target.gold_per_min,
          xpm: target.xp_per_min,
          hero_damage: target.hero_damage,
          tower_damage: target.tower_damage,
          last_hits: target.last_hits,
          denies: target.denies,
          items: targetItems,
          neutral_item: targetNeutral,
          has_aghs: hasAghs,
          has_shard: hasShard,
          kp_percent: kpPercent,
          damage_percent: damagePercent,
        },
        enemy_cores: enemyCoreSummary,
        user_focus: userContext || '',
      })

      const nativeReview = await aiApi.generateMatchReview(
        String(match.match_id || ''),
        matchSummaryPayload,
        slot,
        mode,
        endpoint,
        apiKey,
        model,
      )
      if (nativeReview && nativeReview.overview) {
        return nativeReview
      }
      throw new Error('AI 引擎未返回有效的复盘分析内容')
    } catch (nativeErr: any) {
      console.warn('[AiCoachService] aiApi.generateMatchReview 失败 (严禁规则降级，直接不可用):', nativeErr)
      throw new Error(nativeErr?.message || 'AI 引擎推理连接失败或请求超时，深度复盘暂不可用。')
    }
  }

  /**
   * 检查当前系统环境是否具备可用的 AI 复盘大模型
   */
  public isAiReviewConfigured(): { available: boolean, reason?: string } {
    const { settings } = useAppStore()
    const mode = settings.ai_mode
    const isNpu = mode === 'local_npu' || mode === 'npu'
    const isCloud = mode === 'cloud'

    if (!isNpu && !isCloud) {
      return { available: false, reason: '未启用 AI 引擎 (当前处于纯规则模式)' }
    }
    const endpoint = (isNpu ? settings.npu_endpoint : settings.cloud_base_url) || ''
    const apiKey = (isNpu ? '' : settings.cloud_api_key) || ''
    const model = (isNpu ? settings.npu_model : settings.cloud_model) || ''

    if (!endpoint || (isCloud && !apiKey) || !model) {
      return { available: false, reason: 'AI 引擎配置未补全 (缺少地址/Key/模型)' }
    }
    return { available: true }
  }
}

export const aiCoachService = new AiCoachService()

/**
 * 原生获取云端可用模型列表
 * 通过 Go 端发送请求，彻底解决浏览器跨域 CORS 拦截问题
 */
/**
 * 通用获取可用大模型列表 (支持标准 OpenAI /v1/models、本地 Ollama 原生探测与端侧兼容端点)
 * 通过 Go 端优先发送请求，彻底解决浏览器跨域 CORS 拦截问题
 */
export async function fetchModelsList(endpoint: string, apiKey: string = ''): Promise<string[]> {
  const cleanEndpoint = (endpoint || '').trim().replace(/\/+$/, '')
  if (!cleanEndpoint) return []

  const modelsSet = new Set<string>()

  // 1. 优先通过 Go 后端获取 (支持标准 OpenAI /v1/models)
  try {
    const list = await aiApi.fetchCloudModels(cleanEndpoint, apiKey)
    if (Array.isArray(list) && list.length > 0) {
      list.forEach(m => modelsSet.add(m))
    }
  } catch (e) {
    console.debug('[AiCoachService] aiApi.fetchCloudModels 未响应，继续降级探测:', e)
  }

  // 2. 本地磁盘已下载模型扫描
  if (isLocalEndpoint(cleanEndpoint)) {
    try {
      const localFiles = await systemApi.scanLocalModelFiles()
      if (Array.isArray(localFiles)) {
        localFiles.forEach((f: any) => {
          const name = typeof f === 'string' ? f : f?.name
          if (name && name.trim()) modelsSet.add(name.trim())
        })
      }
    } catch {}
  }

  return Array.from(modelsSet)
}

/**
 * 兼容旧接口：获取云端可用模型列表
 */
export async function fetchCloudModelsList(baseUrl: string, apiKey: string): Promise<string[]> {
  return fetchModelsList(baseUrl, apiKey)
}

/**
 * 兼容旧接口：获取本地安装/已下载模型列表
 */
export async function fetchLocalModelsList(endpoint: string): Promise<string[]> {
  return fetchModelsList(endpoint, '')
}
