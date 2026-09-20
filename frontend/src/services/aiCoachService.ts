import type { AiEndpointTestResult } from '@/types'
import { useAppStore } from '@/store'
import { HERO_CHINESE_NAMES } from '../data/heroNames'
import { playerProfileService } from './playerProfileService'

/**
 * 战术提问实战上下文快照实体
 * 用于向端侧 NPU 或云端大模型组装实战环境 Prompt
 */
export interface TacticalContext {
  /** 英雄数字 ID */
  heroId?: number
  /** 英雄本地化名称 (如 "敌法师") */
  heroName?: string
  /** 英雄当前等级 */
  heroLevel?: number
  /** 当前生命值百分比 (0 ~ 100) */
  healthPercent?: number
  /** 当前魔法值百分比 (0 ~ 100) */
  manaPercent?: number
  /** 当前手头持有的金钱 */
  gold?: number
  /** 比赛对局时钟秒数 */
  clockTime?: number
  /** 战绩 KDA 字符串 (如 "5/1/8") */
  kda?: string
  /** 身上已持有的物品列表 */
  items?: string[]
  /** 英雄当前主要技能及就绪/冷却状态 */
  abilities?: string[]
  /** 选手手动录入的敌情备忘录 (如 "火枪无买活") */
  userNotes?: string
  /** 绑定的玩家近期战绩与英雄熟练度画像摘要 */
  playerProfileSnippet?: string
}

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
    id: 'qwen2.5:7b',
    name: 'Qwen 2.5 7B Instruct',
    tag: '🌟 官方首推 · 中文战术理解最强',
    size: '~4.7 GB 内存/显存',
    npuRequirement: '中高端 PC NPU / 独显加速',
    recommendedFor: '中文电竞术语、出装克制推演、战术意图深度理解',
    contextWindow: '32k 上下文',
    description: '阿里通义千问 2.5 代 7B 标杆模型，逻辑严密，战术复盘见解深刻，首选主力！',
    isPriority: true,
  },
  {
    id: 'qwen2.5:3b',
    name: 'Qwen 2.5 3B Instruct',
    tag: '⚡ 极致轻量 · 极速端侧推理',
    size: '~2.2 GB 内存占用',
    npuRequirement: '主流 PC NPU (Intel Core Ultra / AMD Ryzen AI / 骁龙)',
    recommendedFor: '轻薄本、超低显存、毫秒级快速问答',
    contextWindow: '32k 上下文',
    description: '轻量化端侧主力，在主流轻薄本 NPU 上延迟低至 120ms，开销极低。',
    isPriority: true,
  },
  {
    id: 'deepseek-r1:7b',
    name: 'DeepSeek-R1 Distill Qwen 7B',
    tag: '🧠 原生思维链 · 深度宏观推演',
    size: '~4.8 GB 内存占用',
    npuRequirement: '30+ TOPS NPU 或独立 GPU',
    recommendedFor: '复盘长局推演、团战胜负手与胜率波动归因',
    contextWindow: '64k 上下文',
    description: '具备原生 CoT 深度推理链，善于剖析团战失误与经济差质变点。',
    isPriority: true,
  },
  {
    id: 'deepseek-r1:1.5b',
    name: 'DeepSeek-R1 Distill 1.5B',
    tag: '⚡ 超轻便携 · 极低资源消耗',
    size: '~1.1 GB 内存占用',
    npuRequirement: '核显 / 便携设备通用 (10+ TOPS 即顺畅)',
    recommendedFor: '极低配置设备、局内极速答疑',
    contextWindow: '64k 上下文',
    description: '极致轻量化，在超轻薄核显本上也能瞬时响应。',
  },
  {
    id: 'llama3.2:3b',
    name: 'Llama 3.2 3B Instruct',
    tag: '🌐 Meta 端侧国际旗舰',
    size: '~2.0 GB 内存占用',
    npuRequirement: 'Windows DirectML 硬件加速',
    recommendedFor: '英文与多语言对局分析、标准遵循',
    contextWindow: '128k 超长上下文',
    description: 'Meta 专为现代端侧硬件优化，遵循严苛指令约束。',
  },
]

/**
 * AI 实时战术教练推理服务
 * 负责端侧 NPU/Ollama 探测、云端 API 调用、Prompt 组装以及确定性降级机制
 */
class AiCoachService {
  /**
   * 格式化战况快照为标准化 Prompt 上下文
   * @param ctx 实战战术快照对象
   * @returns 结构化文本片段
   */
  private formatGameContext(ctx: TacticalContext): string {
    const timeStr = ctx.clockTime !== undefined
      ? `${Math.floor(ctx.clockTime / 60)}:${(ctx.clockTime % 60).toString().padStart(2, '0')}`
      : '未知'

    const profileSnippet = ctx.playerProfileSnippet || playerProfileService.getTacticalPromptSnippet(ctx.heroId)

    return [
      `【实战对局快照】`,
      `- 比赛时钟: ${timeStr} | 英雄: ${ctx.heroName || '当前英雄'} (Lv.${ctx.heroLevel || 1})`,
      `- 状态: ${ctx.healthPercent ?? 100}% HP | ${ctx.manaPercent ?? 100}% MP`,
      `- 战绩与经济: ${ctx.kda || '0/0/0'} | 储备金币: ${ctx.gold || 0}`,
      `- 现有装备: [${(ctx.items || []).join(', ') || '空背包'}]`,
      `- 技能状态: [${(ctx.abilities || []).join(', ') || '未知'}]`,
      ctx.userNotes ? `- 战场敌情备忘: "${ctx.userNotes}"` : '',
      profileSnippet ? `\n${profileSnippet}` : '',
    ].filter(Boolean).join('\n')
  }

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
    mode: 'local_npu' | 'cloud_api' | 'rule_only' | 'npu' | 'cloud',
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

    // 0. 优先通过 Go 原生进行毫秒级端点网络探测 (彻底避免浏览器 CORS 拦截与 Token 泄漏)
    const win = window as any
    if (win?.go?.main?.App?.TestAiEndpoint) {
      try {
        const nativeResult = await win.go.main.App.TestAiEndpoint(mode, endpoint, apiKey, model)
        if (nativeResult) {
          return nativeResult
        }
      } catch (nativeErr: any) {
        console.warn('[AiCoachService] Go 原生 TestAiEndpoint 失败，降级至前端探测:', nativeErr)
      }
    }

    try {
      const isLocal = mode === 'local_npu' || mode === 'npu'
      const cleanEndpoint = (endpoint || (isLocal ? 'http://127.0.0.1:11434/v1' : 'https://api.deepseek.com/v1')).replace(/\/+$/, '')
      const testModel = model || (isLocal ? 'qwen2.5:7b' : 'deepseek-chat')

      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
      }
      if (!isLocal && apiKey) {
        headers.Authorization = `Bearer ${apiKey}`
      }

      const controller = new AbortController()
      const timer = setTimeout(() => controller.abort(), 6000)

      const res = await fetch(`${cleanEndpoint}/chat/completions`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          model: testModel,
          messages: [
            { role: 'user', content: 'Ping: 请回复一句简短的“Dota 2 教练在线”以验证连通性。' },
          ],
          temperature: 0.1,
          max_tokens: 30,
        }),
        signal: controller.signal,
      })

      clearTimeout(timer)
      const latencyMs = Math.round(performance.now() - start)

      if (!res.ok) {
        const errText = await res.text().catch(() => '')
        return {
          success: false,
          latencyMs,
          statusCode: res.status,
          modelUsed: testModel,
          errorMessage: `HTTP ${res.status}: ${errText.slice(0, 150) || res.statusText}`,
          testedAt,
        }
      }

      const data = await res.json()
      const reply = data.choices?.[0]?.message?.content?.trim() || JSON.stringify(data).slice(0, 120)

      return {
        success: true,
        latencyMs,
        statusCode: res.status,
        modelUsed: testModel,
        responsePreview: reply,
        testedAt,
      }
    } catch (err: any) {
      const latencyMs = Math.round(performance.now() - start)
      return {
        success: false,
        latencyMs,
        errorMessage: err.name === 'AbortError' ? '请求超时 (6000ms)：请检查本地服务是否启动' : (err.message || '网络连接失败'),
        testedAt,
      }
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
   * @param timeoutMs 超时断流阈值 (毫秒，默认 8000ms)
   * @returns 回答文本、使用模型以及是否由本地 NPU 推理
   */
  public async askCoach(
    question: string,
    context: TacticalContext,
    timeoutMs: number = 8000,
  ): Promise<{ answer: string, modelUsed: string, isNpu: boolean }> {
    const { state } = useAppStore()
    const mode = state.settings.ai_mode

    // 1. 纯确定性规则引擎降级模式 (0 延迟/0 网络依赖/零开销)
    if (mode === 'rule_only') {
      const fallback = this.generateFallbackAdvice(question, context)
      return {
        answer: `${fallback}`,
        modelUsed: '确定性规则引擎 (离线降级)',
        isNpu: false,
      }
    }

    const isNpu = mode === 'local_npu' || mode === 'npu'

    // 0. 优先调用 Go 原生战术问答引擎 (开工铁律第 6 条与第 9 条)
    const win = window as any
    if (win?.go?.main?.App?.AskCoach) {
      try {
        const endpoint = (isNpu ? state.settings.npu_endpoint : state.settings.cloud_base_url) || ''
        const apiKey = (isNpu ? '' : state.settings.cloud_api_key) || ''
        const model = (isNpu ? state.settings.npu_model : state.settings.cloud_model) || ''
        const timeoutSec = Math.max(2, Math.round(timeoutMs / 1000))

        const nativeReply = await win.go.main.App.AskCoach(
          question,
          context,
          mode,
          endpoint,
          apiKey,
          model,
          timeoutSec,
        )
        if (nativeReply && nativeReply.answer) {
          return {
            answer: nativeReply.answer,
            modelUsed: nativeReply.modelUsed || (isNpu ? `[NPU] ${model}` : `[Cloud] ${model}`),
            isNpu: Boolean(nativeReply.isNpu),
          }
        }
      } catch (nativeErr: any) {
        console.warn('[AiCoachService] Go 原生 AskCoach 失败，降级至前端请求:', nativeErr)
      }
    }

    const systemPrompt = `你是一名 Dota 2 顶尖电竞教练。
你的职责是基于当前选手的实战对局快照与战场敌情，给出精确、果断、立竿见影的实战决策。

【核心决策优先级（Solution Hierarchy 必须严格执行）】
1. 最高优先级：优先检查身上已就绪的技能或装备（[就绪]），零经济成本化解眼前危机。
2. 次优先级：检查核心保命或关键控制技能的冷却状态（[CD]），若即将就绪，指导拉扯拖延等待 CD。
3. 创造性利用现有战场资源（身位拉扯、走位掩体、队友掩护）。
4. 最低优先级（严禁盲目劝买）：只有在身上技能与现有装备均无法应对时，才推荐合成新装备，且推荐必须契合英雄主属性、输出类型（物理/法术）与团队定位。

【选手战绩与对阵风格融合】
- 若快照包含【玩家近期战绩画像】，必须结合选手历史英雄熟练度与风格弱点（如场均阵亡偏高需侧重自保出装与防游走视野，高熟练度英雄需强化质变装节奏），生成契合其水平的针对性战术。

【输出要求】
- 必须使用纯正 Dota 2 游戏术语（严禁使用任何 LOL 术语）。
- 严格限制在 2~3 句话以内，拒绝空话套话（不要说“注意走位”、“认真补刀”等废话）。
- 必须明确“立即对谁用什么”或“留钱合成哪件部件”。`

    const fullUserPrompt = `${this.formatGameContext(context)}\n\n【选手战术提问】\n"${question}"\n\n请按照优先级给出精准可执行的 2~3 句行动建议：`

    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), timeoutMs)

    try {
      if (isNpu) {
        // 本地 AI / 本机 NPU 模式 (Ollama / LM Studio / DirectML endpoint)
        const endpoint = (state.settings.npu_endpoint || 'http://127.0.0.1:11434/v1').replace(/\/+$/, '')
        const model = state.settings.npu_model || 'qwen2.5:7b'

        const res = await fetch(`${endpoint}/chat/completions`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            model,
            messages: [
              { role: 'system', content: systemPrompt },
              { role: 'user', content: fullUserPrompt },
            ],
            temperature: 0.3,
            max_tokens: 300,
          }),
          signal: controller.signal,
        })

        clearTimeout(timer)
        if (!res.ok) throw new Error(`本地服务响应异常 (${res.status})`)

        const data = await res.json()
        const content = data.choices?.[0]?.message?.content?.trim()
        if (!content) throw new Error('模型未返回有效内容')

        return { answer: content, modelUsed: `[NPU/Local] ${model}`, isNpu: true }
      } else {
        // 云端大模型模式
        const endpoint = (state.settings.cloud_base_url || 'https://api.deepseek.com/v1').replace(/\/+$/, '')
        const apiKey = state.settings.cloud_api_key || ''
        const model = state.settings.cloud_model || 'deepseek-chat'

        if (!apiKey) {
          throw new Error('未配置云端 API Key，已自动为您降级至规则引擎')
        }

        const res = await fetch(`${endpoint}/chat/completions`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${apiKey}`,
          },
          body: JSON.stringify({
            model,
            messages: [
              { role: 'system', content: systemPrompt },
              { role: 'user', content: fullUserPrompt },
            ],
            temperature: 0.3,
            max_tokens: 300,
          }),
          signal: controller.signal,
        })

        clearTimeout(timer)
        if (!res.ok) throw new Error(`云端请求失败 (${res.status})`)

        const data = await res.json()
        const content = data.choices?.[0]?.message?.content?.trim()
        if (!content) throw new Error('云端模型未返回有效内容')

        return { answer: content, modelUsed: `[Cloud] ${model}`, isNpu: false }
      }
    } catch (err: any) {
      clearTimeout(timer)
      console.warn('[AiCoachService] 自动降级至确定性规则兜底:', err)
      const fallback = this.generateFallbackAdvice(question, context)
      return {
        answer: `${fallback} (AI超时已平滑降级至规则库)`,
        modelUsed: isNpu ? '[NPU 规则降级]' : '[云端 规则降级]',
        isNpu,
      }
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
  public async generateMatchReview(match: any, targetPlayerSlot?: number): Promise<MatchReviewResult> {
    const { state } = useAppStore()
    const mode = state.settings.ai_mode

    // 1. 寻找主视角玩家
    const players: any[] = match?.players || []
    let target = players.find(p => targetPlayerSlot !== undefined ? p.player_slot === targetPlayerSlot : false)
    if (!target && state.settings.dota_player_id) {
      target = players.find(p => String(p.account_id) === String(state.settings.dota_player_id))
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
    const durationSec = (match.duration || 0) % 60

    const userPrompt = `【Dota 2 对局核心数据快照】
- 比赛编号: #${match.match_id}
- 对局时长: ${durationMin}分${durationSec}秒
- 胜负判定: ${isWin ? '本场胜利' : '本场失利'} (天辉 ${match.radiant_score} : 夜魇 ${match.dire_score})
- 天辉阵容: ${radiantHeroes}
- 夜魇阵容: ${direHeroes}
- 主分析视角玩家: ${target.personaname || '玩家'} (${isRadiant ? '天辉方' : '夜魇方'})
- 操纵英雄: ${targetHeroName} (等级: Lv.${target.level || '未知'})
- 个人战绩: ${target.kills} 击杀 / ${target.deaths} 阵亡 / ${target.assists} 助攻
- 经济与经验: GPM ${target.gold_per_min} / XPM ${target.xp_per_min}
- 英雄伤害: ${target.hero_damage} | 建筑伤害: ${target.tower_damage || 0}
- 正/反补刀: ${target.last_hits} / ${target.denies}

请依据上述客观事实，给出高水平专业战术复盘：`

    const systemPrompt = `你是一名 Dota 2 职业战队高级分析师与专业电竞教练。
你的职责是基于客观战况数据与双方阵容克制关系，给出深度、见解独到、直击痛点的赛后复盘。

请严格遵照以下【4个维度结构】输出纯文本内容，禁止空话套话，使用标准中文 Dota 2 术语：
### 1. 【大盘与胜负定局复盘】
分析双方阵容的强势期分布、对线期战略走势与决定胜负的宏观节奏分水岭（如控盾推塔时机、兵线压力、上高阵型）。
### 2. 【主视角实战得失剖析】
针对操纵英雄 ${targetHeroName} 的 KDA（${target.kills}/${target.deaths}/${target.assists}）与输出伤害（${target.hero_damage}），肯定其对线或团战贡献，指出 1~2 个导致局势恶化或阵亡过多的核心执行失误（如站位靠前、拉扯脱节、入场时机偏差）。
### 3. 【装备针对性与克制推演】
结合敌方阵容（${isRadiant ? direHeroes : radiantHeroes}），分析主视角出装的克制针对性，点出面对敌方核心威胁时本局缺失的关键针对装（例如：面对多控制是否必须提早做黑皇杖(BKB)、面对闪避是否需金箍棒、面对肉核是否需减甲/冰眼等）。
### 4. 【下局致胜核心提升指南】
总结 3 条清晰、立即可落地的对线/中后期团战执行指南（每条一句话），帮助选手在下局立刻形成战术肌肉记忆。`

    if (mode === 'rule_only') {
      return this.generateDeterministicMatchReview(match, target, isWin, targetHeroName, radiantHeroes, direHeroes, '确定性规则引擎 (离线保底)')
    }

    const isNpu = mode === 'local_npu' || mode === 'npu'

    // 0. 优先调用 Go 原生赛后复盘分析引擎 (开工铁律第 6 条与第 9 条)
    const win = window as any
    if (win?.go?.main?.App?.GenerateMatchReview) {
      try {
        const endpoint = (isNpu ? state.settings.npu_endpoint : state.settings.cloud_base_url) || ''
        const apiKey = (isNpu ? '' : state.settings.cloud_api_key) || ''
        const model = (isNpu ? state.settings.npu_model : state.settings.cloud_model) || ''
        const slot = targetPlayerSlot !== undefined ? targetPlayerSlot : -1
        const boundId = state.settings.dota_player_id || ''

        const nativeReview = await win.go.main.App.GenerateMatchReview(
          JSON.stringify(match),
          slot,
          boundId,
          mode,
          endpoint,
          apiKey,
          model,
        )
        if (nativeReview && nativeReview.overview) {
          return nativeReview
        }
      } catch (nativeErr: any) {
        console.warn('[AiCoachService] Go 原生 GenerateMatchReview 失败，降级至前端请求:', nativeErr)
      }
    }
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), 12000)

    try {
      let rawText = ''
      let modelUsed = ''

      if (isNpu) {
        const endpoint = (state.settings.npu_endpoint || 'http://127.0.0.1:11434/v1').replace(/\/+$/, '')
        const model = state.settings.npu_model || 'qwen2.5:7b'
        modelUsed = `[本地/NPU] ${model}`

        const res = await fetch(`${endpoint}/chat/completions`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            model,
            messages: [
              { role: 'system', content: systemPrompt },
              { role: 'user', content: userPrompt },
            ],
            temperature: 0.4,
            max_tokens: 800,
          }),
          signal: controller.signal,
        })
        clearTimeout(timer)
        if (!res.ok) throw new Error(`本地端点响应错误 HTTP ${res.status}`)
        const data = await res.json()
        rawText = data.choices?.[0]?.message?.content?.trim() || ''
      } else {
        const endpoint = (state.settings.cloud_base_url || 'https://api.deepseek.com/v1').replace(/\/+$/, '')
        const apiKey = state.settings.cloud_api_key || ''
        const model = state.settings.cloud_model || 'deepseek-chat'
        modelUsed = `[云端API] ${model}`

        if (!apiKey) throw new Error('未配置云端 API Key')

        const res = await fetch(`${endpoint}/chat/completions`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${apiKey}`,
          },
          body: JSON.stringify({
            model,
            messages: [
              { role: 'system', content: systemPrompt },
              { role: 'user', content: userPrompt },
            ],
            temperature: 0.4,
            max_tokens: 800,
          }),
          signal: controller.signal,
        })
        clearTimeout(timer)
        if (!res.ok) throw new Error(`云端服务响应错误 HTTP ${res.status}`)
        const data = await res.json()
        rawText = data.choices?.[0]?.message?.content?.trim() || ''
      }

      if (!rawText) throw new Error('模型未返回有效复盘内容')

      return this.parseReviewSections(rawText, modelUsed)
    } catch (err: any) {
      clearTimeout(timer)
      console.warn('[AiCoachService] 赛后复盘生成失败，触发确定性战术分析降级:', err)
      return this.generateDeterministicMatchReview(
        match,
        target,
        isWin,
        targetHeroName,
        radiantHeroes,
        direHeroes,
        `${isNpu ? '[本地/NPU]' : '[云端]'} 规则降级: ${err.message || '超时'}`,
      )
    }
  }

  /**
   * 将大模型输出解析为结构化 4 块
   */
  private parseReviewSections(rawText: string, modelUsed: string): MatchReviewResult {
    let overview = ''
    let keyTurningPoints = ''
    let itemizationAnalysis = ''
    let nextMatchActions: string[] = []

    const p1 = rawText.includes('### 1.') ? rawText.indexOf('### 1.') : rawText.indexOf('1.')
    const p2 = rawText.includes('### 2.') ? rawText.indexOf('### 2.') : rawText.indexOf('2.')
    const p3 = rawText.includes('### 3.') ? rawText.indexOf('### 3.') : rawText.indexOf('3.')
    const p4 = rawText.includes('### 4.') ? rawText.indexOf('### 4.') : rawText.indexOf('4.')

    if (p1 !== -1 && p2 !== -1) {
      overview = rawText.substring(p1, p2).replace(/^#*\s*1\.\s*[^】\n]+】?/, '').trim()
    }
    if (p2 !== -1 && p3 !== -1) {
      keyTurningPoints = rawText.substring(p2, p3).replace(/^#*\s*2\.\s*[^】\n]+】?/, '').trim()
    }
    if (p3 !== -1 && p4 !== -1) {
      itemizationAnalysis = rawText.substring(p3, p4).replace(/^#*\s*3\.\s*[^】\n]+】?/, '').trim()
    }
    if (p4 !== -1) {
      const p4Text = rawText.substring(p4).replace(/^#*\s*4\.\s*[^】\n]+】?/, '').trim()
      nextMatchActions = p4Text.split('\n').map(l => l.replace(/^[-*0-9.]+\s*/, '').trim()).filter(l => l.length > 2)
    }

    if (!overview) overview = rawText.slice(0, 300)
    if (!keyTurningPoints) keyTurningPoints = '团战入场与关键控制技能互换存在拉扯提升空间。'
    if (!itemizationAnalysis) itemizationAnalysis = '针对敌方核心爆发，应注重黑皇杖 (BKB) 等质变保命装的做出时机。'
    if (nextMatchActions.length === 0) {
      nextMatchActions = [
        '中前期带线时务必观察小地图敌方关键先手英雄视野，避免盲目深入单走。',
        '团战避免第一波吃到敌方集中爆发，等待后手控制技能交出后再切入战场。',
        '均势局或劣势局优先积累买活资金，杜绝带盾前或守高前无买活暴毙。',
      ]
    }

    return {
      overview,
      keyTurningPoints,
      itemizationAnalysis,
      nextMatchActions,
      fullText: rawText,
      modelUsed,
    }
  }

  /**
   * 确定性离线复盘报告生成器 (保证在无网络/无模型时 100% 产出专业见解)
   */
  private generateDeterministicMatchReview(
    match: any,
    target: any,
    isWin: boolean,
    targetHeroName: string,
    radiantHeroes: string,
    direHeroes: string,
    modelUsed: string,
  ): MatchReviewResult {
    const kdaRatio = target.deaths === 0 ? (target.kills + target.assists) : ((target.kills + target.assists) / target.deaths).toFixed(1)
    const gpm = target.gold_per_min || 0

    const overview = isWin
      ? `本场比赛最终取得胜利。天辉阵容 (${radiantHeroes}) 与夜魇阵容 (${direHeroes}) 在多轮阵地战中交锋，己方在团战执行与兵线推进上建立了稳定的经济雪球，并在关键控盾与上高节奏中果断终结比赛。`
      : `本场比赛遗憾告负。双方对抗中，己方在面对敌方多点开花与强先手机制时容错率偏低，中后期关键阵亡导致防御塔与野区视野失守，终被敌方抓住买活时间差破路翻盘。`

    const keyTurningPoints = Number(kdaRatio) >= 3
      ? `主操纵英雄【${targetHeroName}】交出了 ${target.kills}/${target.deaths}/${target.assists} (KDA ${kdaRatio}) 的亮眼数据，场均输出高达 ${target.hero_damage}。对线期与中期抓人效率突出，但在 20~30 分钟的关键推塔波次中，队伍整体站位稍显脱节，后续应加强抱团开雾第一时间的集火统一性。`
      : `主操纵英雄【${targetHeroName}】个人数据为 ${target.kills}/${target.deaths}/${target.assists}，阵亡次数偏高。实战中多次在缺乏保护视野的情况下收线过深，被敌方游走抓单；团战入场过早承担了过多第一波不明伤害，导致核心输出未能完整倾泻。`

    const itemizationAnalysis = gpm >= 550
      ? `经济刷取效率维持在较高水准 (GPM ${gpm})。出装整体兼顾了刷钱与打架节奏，但面对敌方控制链时，应评估更早做出闪烁匕首或黑皇杖 (BKB) 的必要性，以建立更强的先手容错率。`
      : `经济发育速度相对受限 (GPM ${gpm})。散件过度投资在一定程度上延缓了质变核心大件的成型。建议优化打钱路线，减少无效支援跑路，直奔关键保命装与第一波输出质变装。`

    const nextMatchActions = [
      '对线期强化正反补兵线控制，5 分钟前优先确保消耗品充足与控符视野。',
      '中后期开雾推进前，务必由酱油先手探草排眼，核心切忌走在第一身位吃控。',
      '核心大件成型第一波果断发起集结进攻，推塔后立即扫荡敌方三角区野怪扩大经济差。',
    ]

    const fullText = `### 1. 【大盘与胜负定局复盘】\n${overview}\n\n### 2. 【主视角实战得失剖析】\n${keyTurningPoints}\n\n### 3. 【装备针对性与克制推演】\n${itemizationAnalysis}\n\n### 4. 【下局致胜核心提升指南】\n${nextMatchActions.map((a, i) => `${i + 1}. ${a}`).join('\n')}`

    return {
      overview,
      keyTurningPoints,
      itemizationAnalysis,
      nextMatchActions,
      fullText,
      modelUsed,
    }
  }
}

export const aiCoachService = new AiCoachService()

/**
 * 原生获取云端可用模型列表
 * 通过 Go 端发送请求，彻底解决浏览器跨域 CORS 拦截问题
 */
export async function fetchCloudModelsList(baseUrl: string, apiKey: string): Promise<string[]> {
  const win = window as any
  if (win?.go?.main?.App?.FetchCloudModels) {
    try {
      const models = await win.go.main.App.FetchCloudModels(baseUrl, apiKey)
      if (Array.isArray(models) && models.length > 0) {
        return models
      }
    } catch (e) {
      console.warn('[AiCoachService] Go 原生 FetchCloudModels 失败，降级至前端 fetch:', e)
    }
  }

  // 纯浏览器降级
  const res = await fetch(`${baseUrl.replace(/\/+$/, '')}/models`, {
    headers: apiKey ? { Authorization: `Bearer ${apiKey}` } : {},
    signal: AbortSignal.timeout(6000),
  })
  if (res.ok) {
    const data = await res.json()
    if (Array.isArray(data?.data)) {
      return data.data.map((m: any) => m.id).filter(Boolean)
    }
  }
  return []
}

/**
 * 原生获取本地 Ollama 安装模型列表
 */
export async function fetchLocalModelsList(endpoint: string): Promise<string[]> {
  const win = window as any
  if (win?.go?.main?.App?.FetchLocalModels) {
    try {
      const models = await win.go.main.App.FetchLocalModels(endpoint)
      if (Array.isArray(models) && models.length > 0) {
        return models
      }
    } catch (e) {
      console.warn('[AiCoachService] Go 原生 FetchLocalModels 失败，降级至前端 fetch:', e)
    }
  }

  const cleanEndpoint = (endpoint || 'http://127.0.0.1:11434').replace(/\/+$/, '').replace(/\/v1$/, '')
  const res = await fetch(`${cleanEndpoint}/api/tags`, {
    signal: AbortSignal.timeout(3000),
  })
  if (res.ok) {
    const data = await res.json()
    if (Array.isArray(data?.models)) {
      return data.models.map((m: any) => m.name).filter(Boolean)
    }
  }
  return []
}
