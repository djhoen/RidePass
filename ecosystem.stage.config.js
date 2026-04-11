module.exports = {
    apps: [
        {
            name: "stage-vueapp",
            script: "vueapp/server.js",
            max_memory_restart: "150M",
            watch: false,
            env: {
                NODE_ENV: "staging",
                PORT: 8080
            }
        },
        {
            name: "stage-taskrunner",
            script: "dotnet",
            args: "TaskRunner/bin/Release/net7.0/TaskRunner.dll",
            max_memory_restart: "150M",
            watch: false
        },
        {
            name: "stage-webapi",
            script: "dotnet",
            args: "webapi/bin/Release/net7.0/webapi.dll",
            max_memory_restart: "350M",
            watch: false,
            env: {
                ASPNETCORE_ENVIRONMENT: "Staging",
                ASPNETCORE_URLS: "http://0.0.0.0:7293",
                ISSUER: "https://stage.yourdomain.com/",
                SIGNING_KEY: "YOUR_STAGING_SIGNING_KEY",
                STRIPE_SKEY: "sk_test_YOUR_STRIPE_TEST_KEY",
                AWS_ACCESS_KEY_ID: "YOUR_ACCESS_KEY",
                AWS_SECRET_ACCESS_KEY: "YOUR_SECRET_KEY"
            }
        }
    ],
    deploy: {
        staging: {
            user: "YOUR_USER",
            host: "YOUR_STAGING_SERVER_IP",
            ref: "origin/stage",
            repo: "YOUR_REPO_URL",
            path: "/var/www/staging",
            "post-deploy": "cd vueapp && npm install && npm run build && cd .. && dotnet publish webapi -c Release && dotnet publish TaskRunner -c Release && pm2 startOrRestart ecosystem.stage.config.js"
        }
    }
}
