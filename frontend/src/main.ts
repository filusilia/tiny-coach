import { createApp } from 'vue'
import App from './App.vue'
import { router } from './router'
import { initWailsBridge } from './services/wailsBridge'
import { pinia } from './stores'
import 'virtual:uno.css'
import './style/index.css'

initWailsBridge()

const app = createApp(App)
app.use(pinia)
app.use(router)
app.mount('#app')
