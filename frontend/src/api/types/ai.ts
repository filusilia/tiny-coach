/**
 * AI 推理与大模型服务模型契约定义 (src/api/types/ai.ts)
 */

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
  clockTime?: number | string
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
