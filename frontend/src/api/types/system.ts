/**
 * 系统与存储模型契约定义 (src/api/types/system.ts)
 */

export interface CacheDirectoryResponse {
  /** 当前生效的缓存根目录绝对路径 */
  cacheDir: string
  /** 系统权威默认缓存目录 (如 E:\documents\DotaAICoach) */
  defaultDir: string
  /** 当前是否由用户手动自定义了存储目录 */
  isCustom: boolean
}

export interface DiskModelFile {
  /** 模型文件名 (例如 qwen2.5-1.5b-instruct.gguf) */
  name: string
  /** 模型文件全量绝对路径 */
  path: string
  /** 文件字节大小 (单位: bytes) */
  sizeBytes: number
  /** 格式化人类可读大小 (例如 "1.24 GB") */
  sizeDisplay: string
}
