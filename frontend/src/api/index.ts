/**
 * 前端 API 统一导出索引 (src/api/index.ts)
 * 作为前端调用 Go 后端服务的唯一入口，严禁前端组件直接绕过本层
 */

export { aiApi } from './ai'
export * from './client'
export * from './errors'
export { gsiApi } from './gsi'
export { overlayApi } from './overlay'
export { playerApi } from './player'
export { systemApi } from './system'
export * from './types'
