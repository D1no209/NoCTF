import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'
import './style.css'
import App from './App.vue'
import router from './router'
import { i18n } from './i18n'

createApp(App).use(createPinia()).use(router).use(i18n).use(VueQueryPlugin).mount('#app')
