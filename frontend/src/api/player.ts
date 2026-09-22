/**
 * 玩家档案与历史战绩查询 API 模块 (src/api/player.ts)
 * 封装 Dota 玩家画像分析、比赛详情及近期对局查询
 */

import type { PlayerProfileAnalysis } from './types'
import { invokeWails } from './client'

export const playerApi = {
  /**
   * 规范化 Dota 账号 ID（支持好友码或 Steam ID 转 32 位数字 ID）
   */
  async normalizeAccountId(input: string): Promise<string> {
    if (!input || !input.trim()) {
      throw new Error('请输入有效的玩家 ID 或 Steam 好友代码')
    }
    return invokeWails<string>(
      [
        'main.App.NormalizeDotaAccountId',
        'services.PlayerService.NormalizeAccountId',
        'app.App.NormalizeDotaAccountId',
      ],
      input.trim(),
    )
  },

  /**
   * 查询并生成玩家完整画像（胜率、绝活英雄、近期状态、雷达画像标签）
   */
  async getPlayerProfile(accountIdInput: string): Promise<PlayerProfileAnalysis> {
    if (!accountIdInput || !accountIdInput.trim()) {
      throw new Error('玩家账号 ID 不能为空')
    }
    return invokeWails<PlayerProfileAnalysis>(
      [
        'main.App.GetPlayerProfile',
        'services.PlayerService.GetPlayerProfile',
        'app.App.GetPlayerProfile',
      ],
      accountIdInput.trim(),
    )
  },

  /**
   * 获取单场比赛详细原始数据
   */
  async getMatchDetails(matchId: string): Promise<string> {
    return invokeWails<string>(
      [
        'main.App.GetMatchDetails',
        'services.PlayerService.GetMatchDetails',
        'app.App.GetMatchDetails',
      ],
      matchId,
    )
  },

  /**
   * 获取指定玩家近期对局历史列表原始数据
   */
  async getPlayerRecentMatches(accountId: string): Promise<string> {
    return invokeWails<string>(
      [
        'main.App.GetPlayerRecentMatches',
        'services.PlayerService.GetPlayerRecentMatches',
        'app.App.GetPlayerRecentMatches',
      ],
      accountId,
    )
  },
}
