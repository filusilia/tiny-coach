import { createApp } from 'vue'
import App from './App.vue'
import { router } from './router'
import { pinia } from './stores'
import '@wailsio/runtime'
import 'virtual:uno.css'
import './style/index.css'

const app = createApp(App)
app.use(pinia)
app.use(router)
app.mount('#app')
