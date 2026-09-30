/**
 * AI 战术推理与大语言模型 API 模块 (src/api/ai.ts)
 *
 * 绑定唯一事实来源（铁律 9）：直接 import 生成绑定并调用，不做任何方法名猜测。
 */

import type {
  AiEndpointTestResultDTO,
  BattleContextDTO,
  MatchReviewResultDTO,
} from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'
import { BattleContextDTO as BattleContextDTOModel } from '../../bindings/aegis-coach/internal/models/models'

export const aiApi = {
  /**
   * 测试 AI 端点连通性及可用模型
   */
  async testAiEndpoint(
    mode: string,
    endpoint: string,
    apiKey: string,
    model: string,
  ): Promise<AiEndpointTestResultDTO | null> {
    return AppBindings.TestAiEndpoint(mode, endpoint, apiKey, model)
  },

  /**
   * 获取云端/兼容 OpenAI 接口的模型列表
   */
  async fetchCloudModels(baseUrl: string, apiKey: string): Promise<string[]> {
    const res = await AppBindings.FetchCloudModels(baseUrl, apiKey)
    return Array.isArray(res) ? res : []
  },

  /**
   * 战局实时战术教练提问
   *
   * 说明：Go 侧 BattleContextDTO.clockTime 声明为 string，而战局上下文内部以秒数(number)保存，
   * 故此处显式归一化为字符串并借助生成绑定的构造函数补齐必填字段默认值，
   * 避免使用 `as any` 绕过类型检查（曾因此掩盖契约不一致）。
   */
  async askCoach(
    question: string,
    battleContext: BattleContextDTO,
    mode: string,
    endpoint: string,
    apiKey: string,
    model: string,
  ): Promise<string> {
    const ctx = new BattleContextDTOModel({
      ...battleContext,
      clockTime: battleContext.clockTime === undefined ? '' : String(battleContext.clockTime),
    })
    return AppBindings.AskCoach(question, ctx, mode, endpoint, apiKey, model)
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
  ): Promise<MatchReviewResultDTO | null> {
    return AppBindings.GenerateMatchReview(matchId, matchDataJson, playerSlot, mode, endpoint, apiKey, model)
  },

  /**
   * 拉取本地大模型 (走 Go 原生服务，无 CORS 限制)
   */
  async pullLocalModel(modelName: string, endpoint: string): Promise<string> {
    return AppBindings.PullLocalModel(modelName, endpoint)
  },
}
