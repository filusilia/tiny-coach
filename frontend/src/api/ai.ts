/**
 * AI 战术推理与大语言模型 API 模块 (src/api/ai.ts)
 * 遵循 Wails v3 前后端绑定唯一事实来源铁律：统一走 callGo 进行强类型直调与平滑降级
 */

import type {
  AiEndpointTestResultDTO,
  BattleContextDTO,
  MatchReviewResultDTO,
} from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'
import { callGo } from './client'

export const aiApi = {
  /**
   * 测试 AI 端点连通性及可用模型
   */
  async testAiEndpoint(
    mode: string,
    endpoint: string,
    apiKey: string,
    model: string,
  ): Promise<AiEndpointTestResultDTO> {
    return callGo(
      () => AppBindings.TestAiEndpoint(mode, endpoint, apiKey, model),
      ['main.App.TestAiEndpoint', 'services.AiService.TestAiEndpoint'],
      mode,
      endpoint,
      apiKey,
      model,
    )
  },

  /**
   * 获取云端/兼容 OpenAI 接口的模型列表
   */
  async fetchCloudModels(baseUrl: string, apiKey: string): Promise<string[]> {
    const res = await callGo(
      () => AppBindings.FetchCloudModels(baseUrl, apiKey),
      ['main.App.FetchCloudModels', 'services.AiService.FetchCloudModels'],
      baseUrl,
      apiKey,
    )
    return Array.isArray(res) ? res : []
  },

  /**
   * 战局实时战术教练提问
   */
  async askCoach(
    question: string,
    battleContext: BattleContextDTO,
    mode: string,
    endpoint: string,
    apiKey: string,
    model: string,
  ): Promise<string> {
    return callGo(
      () => AppBindings.AskCoach(question, battleContext as any, mode, endpoint, apiKey, model),
      ['main.App.AskCoach', 'services.AiService.AskCoach'],
      question,
      battleContext,
      mode,
      endpoint,
      apiKey,
      model,
    )
  },

  /**
   * 生成单场比赛 AI 深度复盘报告
   */
  async generateMatchReview(
    matchId: string,
    matchDataJson: string,
    playerSlot: number,
    mode: string,
    endpoint: string,
    apiKey: string,
    model: string,
  ): Promise<MatchReviewResultDTO> {
    return callGo(
      () => AppBindings.GenerateMatchReview(matchId, matchDataJson, playerSlot, mode, endpoint, apiKey, model),
      ['main.App.GenerateMatchReview', 'services.AiService.GenerateMatchReview'],
      matchId,
      matchDataJson,
      playerSlot,
      mode,
      endpoint,
      apiKey,
      model,
    )
  },
}
