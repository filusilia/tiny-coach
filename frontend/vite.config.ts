import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import vue from '@vitejs/plugin-vue'
import wails from '@wailsio/runtime/plugins/vite'
import UnoCSS from 'unocss/vite'
import { defineConfig, type Plugin } from 'vite'

function serveExternalAssets(): Plugin {
  return {
    name: 'serve-external-assets',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const rawUrl = req.url?.split('?')[0] || ''
        if (rawUrl.startsWith('/models/') || rawUrl.startsWith('/ort/')) {
          const rootDir = fileURLToPath(new URL('..', import.meta.url))
          const targetFile = path.join(rootDir, rawUrl.replace(/^\//, ''))
          if (fs.existsSync(targetFile) && fs.statSync(targetFile).isFile()) {
            if (targetFile.endsWith('.wasm')) {
              res.setHeader('Content-Type', 'application/wasm')
            } else if (targetFile.endsWith('.mjs') || targetFile.endsWith('.js')) {
              res.setHeader('Content-Type', 'application/javascript')
            }
            return fs.createReadStream(targetFile).pipe(res)
          }
        }
        next()
      })
    },
  }
}

export default defineConfig({
  base: './',
  plugins: [
    serveExternalAssets(),
    wails('./bindings'),
    UnoCSS(),
    vue(),
  ],
  clearScreen: false,
  server: {
    port: Number(process.env.WAILS_VITE_PORT) || 9245,
    strictPort: true,
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      'onnxruntime-web': fileURLToPath(new URL('./node_modules/onnxruntime-web/dist/ort.min.mjs', import.meta.url)),
    },
  },
})
