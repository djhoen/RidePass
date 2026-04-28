import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
    plugins: [vue()],
    server: {
        port: 3000,
        open: true,
        host: '0.0.0.0',
        // Leading dot = allow this domain and any subdomain (so acme.ridepass.local, foothills.ridepass.local, etc. work)
        allowedHosts: ['.ridepass.local', 'localhost']
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
