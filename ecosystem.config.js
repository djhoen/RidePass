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
            args: "./TaskRunner.dll",
            cwd: "./TaskRunner/publish",
            max_memory_restart: "150M",
            watch: false,
            env: {
                DOTNET_ENVIRONMENT: "Production"
            }
        },
        {
            name: "prod-webapi",
            script: "dotnet",
            args: "./webapi.dll",
            cwd: "./webapi/publish",
            max_memory_restart: "350M",
            watch: false,
            env: {
                ASPNETCORE_ENVIRONMENT: "Production",
                ASPNETCORE_URLS: "http://127.0.0.1:7293"
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
            "post-deploy": "cd vueapp && npm install && npm run build && cd .. && dotnet publish webapi/webapi.csproj -c Release -o webapi/publish && dotnet publish TaskRunner/TaskRunner.csproj -c Release -o TaskRunner/publish && dotnet publish RidePass.Migrator/RidePass.Migrator.csproj -c Release -o RidePass.Migrator/publish && set -a && source /etc/ridepass/production.env && set +a && dotnet RidePass.Migrator/publish/RidePass.Migrator.dll && pm2 startOrRestart ecosystem.config.js --update-env"
        }
    }
}
