/**
 * 前端 API 统一导出索引 (src/api/index.ts)
 * 作为前端调用 Go 后端服务的唯一入口，严禁前端组件直接绕过本层。
 *
 * 注意：本层不再提供任何方法寻址/降级胶水（原 src/api/client.ts 已删除）。
 * 各模块直接 import frontend/bindings 下由 `wails3 generate bindings` 生成的强类型函数，
 * 绑定名唯一事实来源由生成器与 vue-tsc 类型门禁共同保障（铁律 1 / 9）。
 */

export { aiApi } from './ai'
export { gsiApi } from './gsi'
export { overlayApi } from './overlay'
export { playerApi } from './player'
export { systemApi } from './system'
export { ttsApi } from './tts'
export { visionApi } from './vision'
export * from './types'
