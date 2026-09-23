/**
 * API 与 IPC 错误类及状态判断工具 (src/api/errors.ts)
 * 统一承载跨进程通信异常判定、取消语义识别与方法寻址缺失判定
 */

export class ApiError extends Error {
  public readonly method: string
  public readonly rawError: unknown

  constructor(method: string, message: string, rawError?: unknown) {
    super(`[API Error] 调用 '${method}' 失败: ${message}`)
    this.name = 'ApiError'
    this.method = method
    this.rawError = rawError
  }
}

/**
 * 校验异常是否属于用户主动取消原生对话框等正常中断操作
 */
export function isCancelError(e: unknown): boolean {
  if (!e) return false
  const anyErr = e as any
  const msg = String(anyErr?.message || e || '').toLowerCase()
  return (
    anyErr?.name === 'AbortError'
    || anyErr?.code === 1223
    || anyErr?.code === -2147023673
    || anyErr?.code === '0x800704c7'
    || /cancel|取消|0x800704c7|abort/i.test(msg)
  )
}

/**
 * 校验异常是否属于 Wails 后端方法未导出或方法名未找到
 * 用于精确触发 FQN 候选降级，避免将真正的后端业务执行异常误判为方法缺失
 */
export function isMethodMissing(e: unknown): boolean {
  if (!e) return false
  const errMsg = String((e as any)?.message || e || '').toLowerCase()
  return (
    errMsg.includes('unknown bound method')
    || errMsg.includes('is not a function')
    || errMsg.includes('referenceerror')
    || errMsg.includes('method not found')
    || errMsg.includes('not found')
    || errMsg.includes('undefined')
    || errMsg.includes('no method')
    || errMsg.includes('cannot read properties of undefined')
  )
}
