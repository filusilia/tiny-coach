/**
 * 玩家档案与历史战绩查询 API 模块 (src/api/player.ts)
 *
 * 绑定唯一事实来源（铁律 9）：直接 import 生成绑定并调用，不做任何方法名猜测。
 * 外部数据（OpenDota）一律经 Go 侧 PlayerService 统一出口，本层不直连第三方。
 */

import type { HeroMatchupRecommendation, PlayerProfileAnalysis } from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'

export const playerApi = {
  /**
   * 规范化 Dota 账号 ID（支持好友码或 Steam ID 转 32 位数字 ID）
   */
  async normalizeAccountId(input: string): Promise<string> {
    if (!input || !input.trim()) {
      throw new Error('请输入有效的玩家 ID 或 Steam 好友代码')
    }
    return AppBindings.NormalizeDotaAccountId(input.trim())
  },

  /**
   * 查询并生成玩家完整画像（胜率、绝活英雄、近期状态、雷达画像标签）
   */
  async getPlayerProfile(accountIdInput: string): Promise<PlayerProfileAnalysis | null> {
    if (!accountIdInput || !accountIdInput.trim()) {
      throw new Error('玩家账号 ID 不能为空')
    }
    return AppBindings.GetPlayerProfile(accountIdInput.trim())
  },

  /**
   * 获取单场比赛详细原始数据 (JSON 字符串)
   */
  async getMatchDetails(matchId: string): Promise<string> {
    return AppBindings.GetMatchDetails(matchId)
  },

  /**
   * 获取指定玩家近期对局历史列表原始数据 (JSON 字符串)
   */
  async getPlayerRecentMatches(accountId: string): Promise<string> {
    return AppBindings.GetPlayerRecentMatches(accountId)
  },

  /**
   * 获取玩家公开个人主页原始数据 (JSON 字符串：昵称、头像、天梯段位等)
   *
   * 统一出口约束：前端严禁直连 OpenDota，本方法经 Go 侧 PlayerService 代理。
   */
  async getPlayerCard(accountId: string): Promise<string> {
    return AppBindings.GetPlayerCard(accountId)
  },

  /**
   * 基于对局敌我阵容的大数据针对性出装推荐
   * 遵循铁律 1/6/10：经由 Go 后端统一网关与 SingleFlight 缓存调取，前端直连严禁。
   */
  async getHeroMatchupRecommendations(heroId: number, enemyHeroIds: number[] = []): Promise<HeroMatchupRecommendation | null> {
    if (!heroId || heroId <= 0) return null
    return (await AppBindings.GetHeroMatchupRecommendations(heroId, enemyHeroIds)) as HeroMatchupRecommendation | null
  },
}
