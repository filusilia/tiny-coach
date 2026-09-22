import antfu from '@antfu/eslint-config'

export default antfu({
  // 忽略自动生成目录与构建产物
  ignores: [
    'dist/**',
    'build/**',
    'bindings/**',
  ],
  // 启用各种格式支持
  formatters: {
    css: true,
    html: true,
    markdown: true,
    prettierOptions: {
      endOfLine: 'lf',
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
    'no-console': 'off',
    'unused-imports/no-unused-imports': 'error',
    'node/prefer-global/process': 'off',
    'antfu/if-newline': 'off',
    'vue/block-order': ['error', {
      order: ['script', 'template', 'style'],
    }],
    'vue/max-attributes-per-line': 'off',
    'vue/no-v-html': 'off',
    'style/brace-style': ['error', '1tbs', { allowSingleLine: true }],
    'style/linebreak-style': ['error', 'unix'],
  },
})
