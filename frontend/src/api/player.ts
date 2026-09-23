/**
 * 玩家档案与历史战绩查询 API 模块 (src/api/player.ts)
 * 遵循 Wails v3 前后端绑定唯一事实来源铁律：统一走 callGo 进行强类型直调与平滑降级
 */

import type { PlayerProfileAnalysis } from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'
import { callGo } from './client'

export const playerApi = {
  /**
   * 规范化 Dota 账号 ID（支持好友码或 Steam ID 转 32 位数字 ID）
   */
  async normalizeAccountId(input: string): Promise<string> {
    if (!input || !input.trim()) {
      throw new Error('请输入有效的玩家 ID 或 Steam 好友代码')
    }
    const clean = input.trim()
    return callGo(
      () => AppBindings.NormalizeDotaAccountId(clean),
      ['main.App.NormalizeDotaAccountId', 'services.PlayerService.NormalizeAccountId'],
      clean,
    )
  },

  /**
   * 查询并生成玩家完整画像（胜率、绝活英雄、近期状态、雷达画像标签）
   */
  async getPlayerProfile(accountIdInput: string): Promise<PlayerProfileAnalysis> {
    if (!accountIdInput || !accountIdInput.trim()) {
      throw new Error('玩家账号 ID 不能为空')
    }
    const clean = accountIdInput.trim()
    return callGo(
      () => AppBindings.GetPlayerProfile(clean),
      ['main.App.GetPlayerProfile', 'services.PlayerService.GetPlayerProfile'],
      clean,
    )
  },

  /**
   * 获取单场比赛详细原始数据
   */
  async getMatchDetails(matchId: string): Promise<string> {
    return callGo(
      () => AppBindings.GetMatchDetails(matchId),
      ['main.App.GetMatchDetails', 'services.PlayerService.GetMatchDetails'],
      matchId,
    )
  },

  /**
   * 获取指定玩家近期对局历史列表原始数据
   */
  async getPlayerRecentMatches(accountId: string): Promise<string> {
    return callGo(
      () => AppBindings.GetPlayerRecentMatches(accountId),
      ['main.App.GetPlayerRecentMatches', 'services.PlayerService.GetPlayerRecentMatches'],
      accountId,
    )
  },
}
