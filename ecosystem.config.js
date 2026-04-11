module.exports = {
    apps: [
        {
            name: "prod-vueapp",
            script: "vueapp/server.js",
            max_memory_restart: "150M",
            watch: false,
            env: {
                NODE_ENV: "production",
                PORT: 8080
            }
        },
        {
            name: "prod-taskrunner",
            script: "dotnet",
            args: "TaskRunner/bin/Release/net7.0/TaskRunner.dll",
            max_memory_restart: "150M",
            watch: false
        },
        {
            name: "prod-webapi",
            script: "dotnet",
            args: "webapi/bin/Release/net7.0/webapi.dll",
            max_memory_restart: "350M",
            watch: false,
            env: {
                ASPNETCORE_ENVIRONMENT: "Production",
                ASPNETCORE_URLS: "http://0.0.0.0:7293",
                ISSUER: "https://yourdomain.com/",
                SIGNING_KEY: "YOUR_PRODUCTION_SIGNING_KEY",
                STRIPE_SKEY: "sk_live_YOUR_STRIPE_SECRET_KEY",
                AWS_ACCESS_KEY_ID: "YOUR_ACCESS_KEY",
                AWS_SECRET_ACCESS_KEY: "YOUR_SECRET_KEY"
            }
        }
    ],
    deploy: {
        production: {
            user: "YOUR_USER",
            host: "YOUR_SERVER_IP",
            ref: "origin/main",
            repo: "YOUR_REPO_URL",
            path: "/var/www/production",
            "post-deploy": "cd vueapp && npm install && npm run build && cd .. && dotnet publish webapi -c Release && dotnet publish TaskRunner -c Release && pm2 startOrRestart ecosystem.config.js"
        }
    }
}
