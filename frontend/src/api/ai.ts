/**
 * AI 战术推理与大语言模型 API 模块 (src/api/ai.ts)
 * 封装端点连通性测试、远端模型探测、实时战况问答、赛后智能复盘与模型拉取
 */

import type {
  AiEndpointTestResultDTO,
  BattleContextDTO,
  MatchReviewResultDTO,
} from './types'
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
