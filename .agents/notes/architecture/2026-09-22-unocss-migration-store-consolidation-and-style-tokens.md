# Agent Note: UnoCSS 现代化迁移、Store 双轨治理合并与全局样式 Token 参数化集中系统

Status: implemented
Date: 2026-09-22
Class: architecture

## 问题背景与因果机制分析

1. **样式引擎冗余与未启用的 UnoCSS**：
   - 原前端构建依赖 `tailwindcss: ^4.3.3` 与 `@tailwindcss/postcss: ^4.3.3`，通过 Vite 的 PostCSS 管道在每次构建时编译体积庞大的 CSS；
   - 仓库内虽已有 `uno.config.ts`，但未在 `vite.config.ts` 中注册 UnoCSS 插件，亦未在入口 `main.ts` 中加载虚拟模块 `virtual:uno.css`；
   - 依赖与编译链路存在双轨冗余，且 TailwindCSS v4 在与轻量化电竞桌面端架构（Wails v3）协同方面加载较慢、不易于利用 UnoCSS 的轻量极速与灵活的动态原子能力。

2. **状态层双轨二义性（`src/store` 与 `src/stores`）**：
   - 历史上为了向下兼容，项目同时保留了 `src/store/index.ts`（旧桥接单例）与标准模块化 Pinia `src/stores/`（`app.ts`, `battle.ts`, `hero.ts`, `item.ts`, `player.ts`）；
   - 共有 4 个组件与服务模块（`GsiSimulator.vue`, `TitleBar.vue`, `aiCoachService.ts`, `gsiRuleEngine.ts`）仍在通过旧桥接层访问，导致开发者调用状态时容易产生目录混淆与类型二义性。

3. **全局样式硬编码与缺乏集中参数化系统**：
   - `src/index.css` 耦合了 Tailwind 指令（`@import 'tailwindcss'`, `@theme`），无法作为通用、独立、集中的 CSS 变量源；
   - 各业务页面中虽然大量使用原子类，但缺少统一的 CSS Custom Properties（CSS Tokens）定义，无法在统一的中心样式表中一处调整全局主色调、主字号、主边框。

## 决策方案

1. **全面迁移至 UnoCSS 并剥离 TailwindCSS**：
   - 移除 `package.json` 中的 `tailwindcss`, `@tailwindcss/postcss`, `postcss`, `autoprefixer` 依赖；
   - 删除 `frontend/postcss.config.js` 与 `frontend/tailwind.config.js`；
   - 在 `frontend/vite.config.ts` 中注册 `UnoCSS()` 插件；
   - 在 `frontend/uno.config.ts` 中配置完善的 `presetUno` / `presetWind4` 与 `presetRemToPx`，并将 theme 颜色与字号全面映射至 CSS Tokens。

2. **状态层单轨化与彻底合并**：
   - 将原 `src/store/` 彻底归拢至 `src/stores/`；
   - 将 `GsiSimulator.vue`, `TitleBar.vue`, `aiCoachService.ts`, `gsiRuleEngine.ts` 的引用规范化为直接引用模块化 Store；
   - 在 `src/stores/index.ts` 中暴露标准入口，并完全删除废弃的 `src/store/` 目录。

3. **全局样式集中参数化管理（`src/style/index.css`）**：
   - 将 `src/index.css` 重构并移动至 `src/style/index.css`；
   - 在 `src/style/index.css` 的 `:root` 中统一定义所有基础参数：
     - 主题颜色 Tokens：`--color-dota-bg`, `--color-dota-surface`, `--color-dota-panel`, `--color-dota-card`, `--color-dota-hover`, `--color-dota-border`, `--color-dota-gold`, `--color-dota-cyan`, `--color-dota-red`, `--color-dota-green`, `--color-dota-text`, `--color-dota-muted`, `--color-dota-subtle`；
     - 字号梯度 Tokens：`--font-size-2xs` (9px), `--font-size-xs` (11px), `--font-size-sm` (12px), `--font-size-base` (13px), `--font-size-md` (14px), `--font-size-lg` (16px), `--font-size-xl` (18px), `--font-size-2xl` (24px)；
     - 主字号定义：`--font-size-main: var(--font-size-md)`；
     - 主字体族与等宽字体族定义；
     - 基础电竞穿透与滚动条规则；
   - 在 `main.ts` 中导入 `virtual:uno.css` 与 `./style/index.css`。

## 曾考虑的替代方案

1. **继续保留 Tailwind CSS v4 并仅在局部引入 UnoCSS**：
   - *最强理由*：无需调整现有的 `@import 'tailwindcss'`，改动风险最小。
   - *否决原因*：双套 CSS 生成引擎在构建时存在 class 规则碰撞、重复解析开销，且增加了打包体积与开发心智负担，违背了 Wails v3 极轻电竞架构原则。

2. **保留 `src/store/` 别名继续重定向**：
   - *最强理由*：无需改动现存 4 个调用旧 store 的文件。
   - *否决原因*：目录并存是工程腐化的温床，新老成员极易误引，趁此架构梳理彻底消除单复数二义性是最彻底的规范化方案。

## 后果与验证计划

1. **正面收益**：
   - 前端构建打包彻底脱离 PostCSS 冗余管线，UnoCSS 按需极速编译；
   - 颜色与字体大小完全由 `src/style/index.css` 的 CSS 变量单一事实源控制，修改变量即可实时全局联动；
   - 状态管理目录唯一明确，杜绝二义性。
2. **验证计划**：
   - 执行 `pnpm --filter frontend build` 确保全量构建 0 警告 0 报错；
   - 执行 `npm run verify-notes` 确保决策笔记符合规范；
   - 检查所有页面类名编译出的 CSS 包含预期变量引用与视觉展现。
