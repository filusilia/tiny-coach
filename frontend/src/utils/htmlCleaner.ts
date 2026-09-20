/**
 * Dota 2 官方数据 HTML 标签清洗与实体解码器
 * 彻底消除 bio_loc、hype_loc、desc_loc 中的 <font>、<b>、<br> 等网页标签与未解码实体
 */

/**
 * 完整清洗 HTML 标签并保留自然段落换行
 * 适用于英雄背景故事传记 (bio_loc)、技能详细机制描述 (desc_loc) 等富文本字段
 * @param raw 包含 HTML 标签或转义实体的原始字符串
 * @returns 格式化后的纯文本段落
 */
export function cleanHtml(raw: string | undefined | null): string {
  if (!raw || typeof raw !== 'string') return ''

  return raw
    // 1. 将换行标签规范化为纯文本换行
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<\/p>/gi, '\n\n')
    // 2. 清除常见格式化标签
    .replace(/<\/?(font|[biup]|strong|div|span|h[1-6]|em|small|big)[^>]*>/gi, '')
    // 3. 清理任何残余 HTML 标签
    .replace(/<[^>]+>/g, '')
    // 4. 解码常见 HTML 实体
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, '\'')
    .replace(/&ndash;/g, '–')
    .replace(/&mdash;/g, '—')
    // 5. 规范化空白字符与多余空行
    .replace(/\r\n/g, '\n')
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim()
}

/**
 * 单行压缩清洗工具
 * 去除全部 HTML 标签，并将多余连续空白缩减为单个空格
 * 适用于英雄简短定位介绍 (hype_loc)、卡片标题与 Tooltip 浮窗预览
 * @param raw 原始文本
 * @returns 单行紧凑纯文本
 */
export function cleanInlineText(raw: string | undefined | null): string {
  if (!raw || typeof raw !== 'string') return ''
  return cleanHtml(raw).replace(/\s+/g, ' ').trim()
}
