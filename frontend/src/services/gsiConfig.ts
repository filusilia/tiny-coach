/**
 * Dota 2 官方标准 GSI (Game State Integration) 配置文件模板
 * 规范配置 Valve GSI HTTP POST 服务的监听端口 (4444) 与各战局要素采样位
 * 包含地图时间、选手状态、英雄属性、技能状态、物品栏及建筑生命
 */
export const DEFAULT_GSI_CONFIG = `"AegisCoach Integration"
{
    "uri"           "http://127.0.0.1:4444/"
    "timeout"       "5.0"
    "buffer"        "0.1"
    "throttle"      "0.1"
    "heartbeat"     "30.0"
    "data"
    {
        "provider"      "1"
        "map"           "1"
        "player"        "1"
        "hero"          "1"
        "abilities"     "1"
        "items"         "1"
        "draft"         "1"
        "wearables"     "1"
        "buildings"     "1"
    }
}
`

/**
 * 触发客户端浏览器一键生成并下载 gamestate_integration_aegis.cfg 配置文件
 * 引导用户将此文件放置于 Dota 2 的 game/dota/cfg/gamestate_integration 目录下
 */
export function downloadGsiConfigFile(): void {
  const blob = new Blob([DEFAULT_GSI_CONFIG], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = 'gamestate_integration_aegis.cfg'
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}

/**
 * 将官方 GSI 配置文本内容一键写入操作系统剪贴板
 * @returns 复制是否成功
 */
export async function copyGsiConfigToClipboard(): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(DEFAULT_GSI_CONFIG)
    return true
  } catch {
    return false
  }
}
