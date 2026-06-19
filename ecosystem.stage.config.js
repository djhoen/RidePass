// pm2 process definitions for the STAGING droplet. Mirrors ecosystem.config.js
// (production) but with stage-* app names and ASPNETCORE/DOTNET = Staging.
//
// Secrets/config are NOT here. The stage deploy does
//   set -a; source /etc/ridepass/staging.env; set +a
//   pm2 startOrRestart ecosystem.stage.config.js --update-env
// so every line of staging.env becomes a process env var (same pattern as prod).
//
// Ports match prod (8080 / 7293) on the assumption stage runs on its own droplet.
// If you ever colocate stage on the prod droplet, change these to avoid collisions.
module.exports = {
    apps: [
        {
            name: "stage-vueapp",
            script: "vueapp/server.js",
            max_memory_restart: "150M",
            watch: false,
            env: {
                NODE_ENV: "production",
                PORT: 8080
            }
        },
        {
            name: "stage-taskrunner",
            script: "dotnet",
            args: "./TaskRunner.dll",
            cwd: "./TaskRunner/publish",
            max_memory_restart: "150M",
            watch: false,
            env: {
                DOTNET_ENVIRONMENT: "Staging"
            }
        },
        {
            name: "stage-webapi",
            script: "dotnet",
            args: "./webapi.dll",
            cwd: "./webapi/publish",
            max_memory_restart: "350M",
            watch: false,
            env: {
                ASPNETCORE_ENVIRONMENT: "Staging",
                ASPNETCORE_URLS: "http://127.0.0.1:7293"
            }
        }
    ]
}
