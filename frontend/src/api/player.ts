/**
 * 玩家档案与历史战绩查询 API 模块 (src/api/player.ts)
 * 遵循 Wails v3 前后端绑定唯一事实来源铁律：优先直调 AppBindings
 */

import type { PlayerProfileAnalysis } from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'
import { invokeWails } from './client'

export const playerApi = {
  /**
   * 规范化 Dota 账号 ID（支持好友码或 Steam ID 转 32 位数字 ID）
   */
  async normalizeAccountId(input: string): Promise<string> {
    if (!input || !input.trim()) {
      throw new Error('请输入有效的玩家 ID 或 Steam 好友代码')
    }
    const clean = input.trim()

    try {
      const res = await AppBindings.NormalizeDotaAccountId(clean)
      if (res) return res
    } catch (bindErr) {
      console.warn('[playerApi] AppBindings.NormalizeDotaAccountId 异常，尝试降级:', bindErr)
    }

    return invokeWails<string>(
      [
        'main.App.NormalizeDotaAccountId',
        'services.PlayerService.NormalizeAccountId',
        'app.App.NormalizeDotaAccountId',
      ],
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

    try {
      const res = await AppBindings.GetPlayerProfile(clean)
      if (res) return res as PlayerProfileAnalysis
    } catch (bindErr) {
      console.warn('[playerApi] AppBindings.GetPlayerProfile 异常，尝试降级:', bindErr)
    }

    return invokeWails<PlayerProfileAnalysis>(
      [
        'main.App.GetPlayerProfile',
        'services.PlayerService.GetPlayerProfile',
        'app.App.GetPlayerProfile',
      ],
      clean,
    )
  },

  /**
   * 获取单场比赛详细原始数据
   */
  async getMatchDetails(matchId: string): Promise<string> {
    try {
      const res = await AppBindings.GetMatchDetails(matchId)
      if (typeof res === 'string') return res
    } catch (bindErr) {
      console.warn('[playerApi] AppBindings.GetMatchDetails 异常，尝试降级:', bindErr)
    }

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
    try {
      const res = await AppBindings.GetPlayerRecentMatches(accountId)
      if (typeof res === 'string') return res
    } catch (bindErr) {
      console.warn('[playerApi] AppBindings.GetPlayerRecentMatches 异常，尝试降级:', bindErr)
    }

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
