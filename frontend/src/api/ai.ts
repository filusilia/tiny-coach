/**
 * AI 战术推理与大语言模型 API 模块 (src/api/ai.ts)
 * 遵循 Wails v3 前后端绑定唯一事实来源铁律：优先直调 AppBindings
 */

import type {
  AiEndpointTestResultDTO,
  BattleContextDTO,
  MatchReviewResultDTO,
} from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'
import { invokeWails } from './client'

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
    try {
      const res = await AppBindings.TestAiEndpoint(mode, endpoint, apiKey, model)
      if (res) return res as AiEndpointTestResultDTO
    } catch (bindErr) {
      console.warn('[aiApi] AppBindings.TestAiEndpoint 异常，尝试降级:', bindErr)
    }

    return invokeWails<AiEndpointTestResultDTO>(
      [
        'main.App.TestAiEndpoint',
        'services.AiService.TestAiEndpoint',
        'app.App.TestAiEndpoint',
      ],
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
    try {
      const res = await AppBindings.FetchCloudModels(baseUrl, apiKey)
      if (Array.isArray(res)) return res
    } catch (bindErr) {
      console.warn('[aiApi] AppBindings.FetchCloudModels 异常，尝试降级:', bindErr)
    }

    const res = await invokeWails<string[]>(
      [
        'main.App.FetchCloudModels',
        'services.AiService.FetchCloudModels',
        'app.App.FetchCloudModels',
      ],
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
    try {
      const res = await AppBindings.AskCoach(question, battleContext as any, mode, endpoint, apiKey, model)
      if (typeof res === 'string') return res
    } catch (bindErr) {
      console.warn('[aiApi] AppBindings.AskCoach 异常，尝试降级:', bindErr)
    }

    return invokeWails<string>(
      [
        'main.App.AskCoach',
        'services.AiService.AskCoach',
        'app.App.AskCoach',
      ],
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
    try {
      const res = await AppBindings.GenerateMatchReview(matchId, matchDataJson, playerSlot, mode, endpoint, apiKey, model)
      if (res) return res as MatchReviewResultDTO
    } catch (bindErr) {
      console.warn('[aiApi] AppBindings.GenerateMatchReview 异常，尝试降级:', bindErr)
    }

    return invokeWails<MatchReviewResultDTO>(
      [
        'main.App.GenerateMatchReview',
        'services.AiService.GenerateMatchReview',
        'app.App.GenerateMatchReview',
      ],
      matchId,
      matchDataJson,
      playerSlot,
      mode,
      endpoint,
      apiKey,
      model,
    )
  },

  /**
   * 向本地 Ollama 服务发起模型拉取/下载
   */
  async pullLocalModel(modelName: string, endpoint: string = ''): Promise<string> {
    try {
      const res = await AppBindings.PullLocalModel(modelName, endpoint)
      if (typeof res === 'string') return res
    } catch (bindErr) {
      console.warn('[aiApi] AppBindings.PullLocalModel 异常，尝试降级:', bindErr)
    }

    return invokeWails<string>(
      [
        'main.App.PullLocalModel',
        'services.AiService.PullLocalModel',
        'app.App.PullLocalModel',
      ],
      modelName,
      endpoint,
    )
  },
}
