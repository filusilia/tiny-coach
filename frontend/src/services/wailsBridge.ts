/**
 * Wails v3 原生桥接与向下兼容适配器
 * 统一承载 window.wails (Wails v3) 与 window.go.main.App (历史绑定) 的调用转换
 */

export function initWailsBridge() {
  const win = window as any

  // 1. 初始化 window.go 命名空间
  if (!win.go) {
    win.go = {}
  }
  if (!win.go.main) {
    win.go.main = {}
  }

  // 2. 为 window.go.main.App 提供 Proxy，无缝映射到 Wails v3 绑定调用
  if (!win.go.main.App) {
    win.go.main.App = new Proxy(
      {},
      {
        get(_target, prop: string) {
          return async (...args: any[]) => {
            const callObj = win.wails?.Call
            if (callObj) {
              const fqns = [
                `main.App.${prop}`,
                `services.SystemService.${prop}`,
                `services.OverlayService.${prop}`,
                `services.AiService.${prop}`,
                `services.PlayerService.${prop}`,
                `services.GsiService.${prop}`,
                `aegis-coach/internal/services.SystemService.${prop}`,
                `aegis-coach/internal/services.OverlayService.${prop}`,
                `aegis-coach/internal/services.AiService.${prop}`,
                `aegis-coach/internal/services.PlayerService.${prop}`,
                `aegis-coach/internal/services.GsiService.${prop}`,
                `app.App.${prop}`,
              ]
              for (const fqn of fqns) {
                try {
                  if (typeof callObj.Call === 'function') {
                    return await callObj.Call({ methodName: fqn, args })
                  }
                  if (typeof callObj === 'function') {
                    return await callObj({ methodName: fqn, args })
                  }
                  if (typeof callObj.ByName === 'function') {
                    return await callObj.ByName(fqn, ...args)
                  }
                } catch {
                  // 继续尝试下一个 FQN
                }
              }
              if (typeof callObj.ByID === 'function') {
                return callObj.ByID(prop, ...args)
              }
            }
            // 开发预览模式下的控制台提示
            console.debug(`[WailsBridge] 调用 main.App.${prop}`, args)
            return null
          }
        },
      },
    )
  }

  // 3. 为 window.runtime 提供向下兼容垫片
  if (!win.runtime) {
    win.runtime = {
      EventsOn: (eventName: string, callback: (...args: any[]) => void) => {
        if (win.wails?.Events?.On) {
          return win.wails.Events.On(eventName, (ev: any) => {
            callback(ev?.data !== undefined ? ev.data : ev)
          })
        }
        return () => {}
      },
      EventsOff: (eventName: string) => {
        if (win.wails?.Events?.Off) {
          win.wails.Events.Off(eventName)
        }
      },
      EventsEmit: (eventName: string, ...args: any[]) => {
        if (win.wails?.Events?.Emit) {
          return win.wails.Events.Emit(eventName, ...args)
        }
      },
    }
  }
}
