/**
 * 战局分析与五维战术评分计算 Composable (src/views/analysis/composables/useAnalysisStats.ts)
 */

import type { Ref } from 'vue'
import { computed } from 'vue'

export interface AnalysisStatsOverview {
  total: number
  wins: number
  losses: number
  winRate: string
  avgKills: string
  avgDeaths: string
  avgAssists: string
  kdaRatio: string
  avgGpm: number
  avgTowerDmg: number
}

export function useAnalysisStats(recentMatches: Ref<any[]>) {
  /**
   * 战绩核心统计概况计算
   */
  const statsOverview = computed<AnalysisStatsOverview>(() => {
    const list = recentMatches.value
    const total = list.length
    if (total === 0) {
      return {
        total: 0,
        wins: 0,
        losses: 0,
        winRate: '0.0',
        avgKills: '0.0',
        avgDeaths: '0.0',
        avgAssists: '0.0',
        kdaRatio: '0.0',
        avgGpm: 0,
        avgTowerDmg: 0,
      }
    }

    let wins = 0
    let totalKills = 0
    let totalDeaths = 0
    let totalAssists = 0
    let totalGpm = 0
    let totalTowerDmg = 0

    for (const m of list) {
      const isWin = m.radiant_win === (m.player_slot < 128)
      if (isWin) wins++
      totalKills += Number(m.kills || 0)
      totalDeaths += Number(m.deaths || 0)
      totalAssists += Number(m.assists || 0)
      totalGpm += Number(m.gold_per_min || 0)
      totalTowerDmg += Number(m.tower_damage || 0)
    }

    const losses = total - wins
    const winRate = ((wins / total) * 100).toFixed(1)
    const avgKills = (totalKills / total).toFixed(1)
    const avgDeaths = (totalDeaths / total).toFixed(1)
    const avgAssists = (totalAssists / total).toFixed(1)
    const kdaRatio = ((totalKills + totalAssists) / Math.max(1, totalDeaths)).toFixed(2)
    const avgGpm = Math.round(totalGpm / total)
    const avgTowerDmg = Math.round(totalTowerDmg / total)

    return {
      total,
      wins,
      losses,
      winRate,
      avgKills,
      avgDeaths,
      avgAssists,
      kdaRatio,
      avgGpm,
      avgTowerDmg,
    }
  })

  /**
   * 五维战术评分 (0 ~ 100)
   */
  const radarDimensions = computed(() => {
    const s = statsOverview.value
    if (s.total === 0) {
      return [
        { name: '参战', score: 50 },
        { name: '发育', score: 50 },
        { name: '击杀', score: 50 },
        { name: '生存', score: 50 },
        { name: '推进', score: 50 },
      ]
    }

    const partScore = Math.min(99, Math.max(25, Math.round((Number(s.avgAssists) / 14) * 85 + 10)))
    const gpmVal = s.avgGpm > 0 ? s.avgGpm : 500
    const farmScore = Math.min(99, Math.max(25, Math.round((gpmVal / 650) * 85 + 5)))
    const killScore = Math.min(99, Math.max(25, Math.round((Number(s.avgKills) / 9) * 85 + 10)))
    const dVal = Number(s.avgDeaths)
    const survScore = Math.min(99, Math.max(25, Math.round(100 - (dVal * 7.5))))
    const tdVal = s.avgTowerDmg > 0 ? s.avgTowerDmg : 2000
    const pushScore = Math.min(99, Math.max(25, Math.round((tdVal / 2800) * 80 + 15)))

    return [
      { name: '参战', score: partScore },
      { name: '发育', score: farmScore },
      { name: '击杀', score: killScore },
      { name: '生存', score: survScore },
      { name: '推进', score: pushScore },
    ]
  })

  return {
    statsOverview,
    radarDimensions,
  }
}
