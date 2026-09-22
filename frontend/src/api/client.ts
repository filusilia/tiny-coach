/**
 * Wails v3 IPC 强类型底层调用封装客户端 (src/api/client.ts)
 * 职责：
 * 1. 统一屏蔽 Wails v3 的跨进程通信细节与方法寻址差异常态
 * 2. 具备多候选服务名自动回退重试能力 (例如 services.SystemService vs app.App)
 * 3. 统一入参序列化校验与统一错误格式化
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

interface WailsRuntimeWindow extends Window {
  wails?: {
    Call?: {
      Call?: (options: { methodName?: string, methodID?: number, args?: unknown[] }) => Promise<unknown>
      ByName?: (method: string, ...args: unknown[]) => Promise<unknown>
      ByID?: (id: string | number, ...args: unknown[]) => Promise<unknown>
      (options: { methodName?: string, methodID?: number, args?: unknown[] }): Promise<unknown>
    } | ((options: { methodName?: string, methodID?: number, args?: unknown[] }) => Promise<unknown>)
    Dialogs?: {
      OpenFile?: (options: any) => Promise<string | string[]>
      SaveFile?: (options: any) => Promise<string>
    }
    Events?: {
      On?: (eventName: string, callback: (payload: unknown) => void) => () => void
      Off?: (eventName: string) => void
      Emit?: (eventName: string, ...args: unknown[]) => void
    }
  }
  go?: Record<string, unknown>
}

/**
 * 自动扩充候选方法列表，保证无论编译环境如何均能 100% 匹配 Go 后端导出的绑定
 * 优先级：传入候选 -> main.App.<MethodName> -> aegis-coach/internal/services.<Service>.<MethodName> -> services.<Service>.<MethodName>
 */
function expandMethodCandidates(candidates: string[]): string[] {
  const result: string[] = []
  const seen = new Set<string>()

  for (const c of candidates) {
    if (!seen.has(c)) {
      seen.add(c)
      result.push(c)
    }

    const parts = c.split('.')
    const methodName = parts[parts.length - 1]
    if (!methodName) continue

    const mainFqn = `main.App.${methodName}`
    if (!seen.has(mainFqn)) {
      seen.add(mainFqn)
      result.push(mainFqn)
    }

    const expandedList = [
      `aegis-coach/internal/services.SystemService.${methodName}`,
      `aegis-coach/internal/services.OverlayService.${methodName}`,
      `aegis-coach/internal/services.AiService.${methodName}`,
      `aegis-coach/internal/services.PlayerService.${methodName}`,
      `aegis-coach/internal/services.GsiService.${methodName}`,
      `aegis-coach/internal/app.App.${methodName}`,
      `services.SystemService.${methodName}`,
      `services.OverlayService.${methodName}`,
      `services.AiService.${methodName}`,
      `app.App.${methodName}`,
    ]

    for (const exp of expandedList) {
      if (!seen.has(exp)) {
        seen.add(exp)
        result.push(exp)
      }
    }
  }

  return result
}

/**
 * 安全调用 Wails 后端导出的绑定方法
 * @param methodCandidates 方法名候选列表（按优先级依次尝试）
 * @param args 传递给后端的参数
 */
export async function invokeWails<T>(
  methodCandidates: string[],
  ...args: unknown[]
): Promise<T> {
  const win = window as WailsRuntimeWindow
  const allCandidates = expandMethodCandidates(methodCandidates)
  // 参数安全清洗：将 undefined 转换为 ""，确保数组完整性，防止 Go 反射参数缺失报错
  const cleanArgs = (args || []).map(arg => (arg === undefined ? '' : arg))

  let lastError: unknown = null

  // 1. 优先通过 Wails v3 Call.Call / Call.ByName / Call 逐个候选尝试
  const wailsCall = win.wails?.Call
  if (wailsCall) {
    for (const method of allCandidates) {
      try {
        // 1.1 优先使用 Wails v3 官方推荐的 ByName 快速寻址通道
        if (typeof (wailsCall as any).ByName === 'function') {
          const result = (await (wailsCall as any).ByName(method, ...cleanArgs)) as T
          return result
        }
        // 1.2 兼容对象化 Call 签名
        if (typeof (wailsCall as any).Call === 'function') {
          const result = (await (wailsCall as any).Call({ methodName: method, args: cleanArgs })) as T
          return result
        }
        // 1.3 兼容直接函数调用
        if (typeof wailsCall === 'function') {
          const result = (await (wailsCall as any)({ methodName: method, args: cleanArgs })) as T
          return result
        }
      } catch (err: unknown) {
        lastError = err
        const errMsg = String(err)
        const lowerMsg = errMsg.toLowerCase()
        const isMethodMissing
          = lowerMsg.includes('unknown bound method')
            || lowerMsg.includes('is not a function')
            || lowerMsg.includes('referenceerror')
            || lowerMsg.includes('method not found')
            || lowerMsg.includes('not found')
            || lowerMsg.includes('undefined')
            || lowerMsg.includes('no method')
            || lowerMsg.includes('cannot read properties of undefined')
        if (isMethodMissing) {
          continue
        }
        // 若为真正的后端业务逻辑错误或运行时执行异常，直接向外抛出真实原因
        console.warn(`[Wails IPC] 调用 '${method}' 业务或运行时报错:`, err)
        throw new ApiError(method, errMsg, err)
      }
    }
  }

  // 2. 尝试从 window.go 命名空间树中定位函数执行
  if (win.go) {
    for (const method of allCandidates) {
      try {
        const parts = method.split('.')
        let target: unknown = win.go
        for (const part of parts) {
          if (target && typeof target === 'object' && part in target) {
            target = (target as Record<string, unknown>)[part]
          } else {
            target = undefined
            break
          }
        }
        if (typeof target === 'function') {
          return (await target(...args)) as T
        }
      } catch (err) {
        lastError = err
        const errMsg = String(err)
        if (errMsg.includes('not found') || errMsg.includes('undefined')) {
          continue
        }
        throw new ApiError(method, errMsg, err)
      }
    }
  }

  // 3. 开发环境独立浏览器预览提示与保底
  if (!win.wails && !win.go) {
    console.warn(`[Mock Mode] Wails 桌面运行时未就绪，模拟调用: ${methodCandidates[0]}`, args)
    return null as unknown as T
  }

  throw new ApiError(
    methodCandidates[0],
    `未能在运行时找到对应的后端方法 (已尝试: ${allCandidates.slice(0, 4).join(', ')}...)`,
    lastError,
  )
}
