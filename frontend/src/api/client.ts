/**
 * Wails v3 IPC 强类型统一调用客户端 (src/api/client.ts)
 * 职责：
 * 1. 优先使用 Wails 代码生成器导出的强类型绑定方法直连 Go 后端
 * 2. 在方法未导出或编译命名空间差异时，按候选 FQN 自动逐级降级重试
 * 3. 区分“方法不存在”与“业务执行异常”，避免异常被误吞
 */

import { Call } from '@wailsio/runtime'
import { ApiError, isCancelError, isMethodMissing } from './errors'

export { ApiError, isCancelError, isMethodMissing }

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
}

/**
 * 自动扩充候选方法列表，保证无论编译环境如何均能 100% 匹配 Go 后端导出的绑定
 * 优先级：传入候选 -> main.App.<MethodName> -> aegis-coach/internal/services.<Service>.<MethodName> -> services.<Service>.<MethodName>
 */
export function expandMethodCandidates(candidates: string[]): string[] {
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
      `services.PlayerService.${methodName}`,
      `services.GsiService.${methodName}`,
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
 * 统一 Go 调用核心入口
 * 优先执行 AppBindings 强类型方法；若因绑定缺失报错，则平滑降级按候选 FQN 逐级尝试 Call.ByName
 * 若为业务错误或用户主动取消，则直接抛出原始异常，绝不盲目降级
 *
 * @param bindingFn 强类型绑定函数包装器，例如 () => AppBindings.GetCacheDirectory()
 * @param fallbackCandidates 降级备用 FQN 列表，例如 ['main.App.GetCacheDirectory']
 * @param args 传递给后端的参数（用于降级时的 ByName 调用）
 */
export async function callGo<T>(
  bindingFn: () => Promise<T>,
  fallbackCandidates?: string[],
  ...args: unknown[]
): Promise<T> {
  try {
    return await bindingFn()
  } catch (bindErr: unknown) {
    if (isCancelError(bindErr)) {
      throw bindErr
    }
    // 只有在方法不存在/未绑定时才尝试降级；若属于业务报错直接向外抛出
    if (!isMethodMissing(bindErr)) {
      throw bindErr
    }

    if (!fallbackCandidates || fallbackCandidates.length === 0) {
      throw bindErr
    }

    console.warn(`[Wails IPC] 强类型绑定未命中或异常，执行降级寻址:`, fallbackCandidates[0])
    return await invokeWails<T>(fallbackCandidates, ...args)
  }
}

/**
 * 降级与动态调用 Wails 后端绑定方法
 * @param methodCandidates 方法名候选列表（按优先级依次尝试）
 * @param args 传递给后端的参数
 */
export async function invokeWails<T>(
  methodCandidates: string[],
  ...args: unknown[]
): Promise<T> {
  const win = typeof window !== 'undefined' ? (window as WailsRuntimeWindow) : null
  const allCandidates = expandMethodCandidates(methodCandidates)
  // 参数安全清洗：将 undefined 转换为 ""，防止 Go 反射参数缺失报错
  const cleanArgs = (args || []).map(arg => (arg === undefined ? '' : arg))

  let lastError: unknown = null

  // 1. 优先通过 Wails v3 官方 @wailsio/runtime Call.ByName 逐个候选尝试
  for (const method of allCandidates) {
    try {
      if (Call && typeof Call.ByName === 'function') {
        const result = (await Call.ByName(method, ...cleanArgs)) as T
        return result
      }
    } catch (err: unknown) {
      if (isCancelError(err)) {
        throw err
      }
      lastError = err
      if (isMethodMissing(err)) {
        continue
      }
      // 业务逻辑错误或运行时执行异常直接抛出
      console.warn(`[Wails IPC] 调用 '${method}' 业务报错:`, err)
      throw new ApiError(method, String((err as any)?.message || err), err)
    }
  }

  // 2. 兼容全局 window.wails?.Call 机制
  const wailsCall = win?.wails?.Call
  if (wailsCall) {
    for (const method of allCandidates) {
      try {
        if (typeof (wailsCall as any).ByName === 'function') {
          const result = (await (wailsCall as any).ByName(method, ...cleanArgs)) as T
          return result
        }
        if (typeof (wailsCall as any).Call === 'function') {
          const result = (await (wailsCall as any).Call({ methodName: method, args: cleanArgs })) as T
          return result
        }
        if (typeof wailsCall === 'function') {
          const result = (await (wailsCall as any)({ methodName: method, args: cleanArgs })) as T
          return result
        }
      } catch (err: unknown) {
        if (isCancelError(err)) {
          throw err
        }
        lastError = err
        if (isMethodMissing(err)) {
          continue
        }
        console.warn(`[Wails IPC] 调用 '${method}' 运行时报错:`, err)
        throw new ApiError(method, String((err as any)?.message || err), err)
      }
    }
  }

  // 3. 浏览器开发环境沙箱无桌面端环境提示
  if (!win?.wails && (typeof window !== 'undefined')) {
    console.warn(`[Mock Mode] Wails 桌面运行时未就绪，模拟调用: ${methodCandidates[0]}`, args)
    return null as unknown as T
  }

  throw new ApiError(
    methodCandidates[0],
    `未能在运行时找到对应的后端方法 (已尝试: ${allCandidates.slice(0, 4).join(', ')}...)`,
    lastError,
  )
}
