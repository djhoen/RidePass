import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
    plugins: [vue()],
    server: {
        port: 3000,
        open: true
    },
    preview: {
        port: 8080
    },
    resolve: {
        alias: {
            '@': '/src'
        }
    }
})
