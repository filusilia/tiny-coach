import antfu from '@antfu/eslint-config'

export default antfu({
  // 忽略自动生成目录与构建产物
  ignores: [
    '**/dist/**',
    '**/build/**',
    '**/bindings/**',
    'node_modules/**',
  ],
  // 启用各种格式支持
  formatters: {
    css: true,
    html: true,
    markdown: true,
    prettierOptions: {
      endOfLine: 'lf',
      printWidth: 300,
    },
  },
  unocss: true,
  vue: true,
  typescript: true,
  jsonc: true,
  yaml: true,
  markdown: true,
  test: true,
  // 自定义规则
  rules: {
    // ── 项目特定规则 ──────────────────────────────────────────
    'no-console': 'off', // 允许使用 console（调试需要）
    'unused-imports/no-unused-imports': 'error', // 禁止未使用的导入
    'node/prefer-global/process': 'off', // 允许使用 process 全局变量
    'antfu/if-newline': 'off', // 允许 if 语句不强制换行
    'vue/block-order': ['error', { order: ['script', 'template', 'style'] }], // 强制 Vue SFC 块顺序
    'vue/max-attributes-per-line': 'off', // 不限制每行属性数量
    'vue/no-v-html': 'off', // 允许使用 v-html
    'style/brace-style': ['error', '1tbs', { allowSingleLine: true }], // 大括号风格
    'style/linebreak-style': ['error', 'unix'], // 强制 Unix 换行符

    // ── ⭐ 关闭所有"因超宽而换行"的规则 ──────────────────────
    // 行长度限制
    'max-len': 'off', // 关闭 ESLint 核心 max-len 规则
    '@stylistic/max-len': 'off', // 关闭 Stylistic max-len 规则

    // 运算符换行
    '@stylistic/operator-linebreak': 'off', // 关闭运算符前后强制换行

    // 函数/调用换行
    '@stylistic/function-call-argument-newline': 'off', // 关闭函数参数强制换行
    '@stylistic/function-paren-newline': 'off', // 关闭函数括号内强制换行
    '@stylistic/implicit-arrow-linebreak': 'off', // 关闭箭头函数隐式换行

    // 数组/对象换行
    '@stylistic/array-element-newline': 'off', // 关闭数组元素强制换行
    '@stylistic/array-bracket-newline': 'off', // 关闭数组括号强制换行
    '@stylistic/object-property-newline': 'off', // 关闭对象属性强制换行
    '@stylistic/object-curly-newline': 'off', // 关闭对象花括号强制换行

    // 链式调用换行
    '@stylistic/newline-per-chained-call': 'off', // 关闭链式调用强制换行
    '@stylistic/member-delimiter-style': 'off', // 关闭成员分隔符样式

    // Vue 模板折行
    'vue/singleline-html-element-content-newline': 'off', // 关闭单行元素内容换行
    'vue/html-closing-bracket-newline': 'off', // 关闭 HTML 闭合括号换行
    'vue/html-indent': 'off', // 关闭 HTML 缩进检查
    'vue/html-self-closing': 'off', // 关闭自闭合标签检查
  },
})
