// Dota 2 GSI (Game State Integration) 配置文件生成与安装助手

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
`;

/**
 * 触发浏览器/客户端一键下载 GSI 配置文件
 */
export function downloadGsiConfigFile(): void {
  const blob = new Blob([DEFAULT_GSI_CONFIG], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'gamestate_integration_aegis.cfg';
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

/**
 * 复制配置内容到操作系统剪贴板
 */
export async function copyGsiConfigToClipboard(): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(DEFAULT_GSI_CONFIG);
    return true;
  } catch {
    return false;
  }
}
