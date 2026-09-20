import { createApp } from 'vue'
import App from './App.vue'
import { initWailsBridge } from './services/wailsBridge'
import './index.css'

initWailsBridge()

createApp(App).mount('#app')
