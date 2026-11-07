namespace webapi.Storage
{
    /// <summary>
    /// Where LocalFilesystemImageStorage keeps PRIVATE images (rider gate photos). Never web-served,
    /// and it has to survive a deploy.
    ///
    /// What a deploy keeps (scripts/deploy.sh for production, .github/workflows/deploy-stage-action.yml
    /// for stage): the whole deploy path (/var/www/production, /var/www/staging) is emptied with
    /// `find $DEPLOY_PATH -mindepth 1 -delete` and re-extracted; only webapi/publish/wwwroot is moved
    /// aside and restored. The API runs under pm2 with cwd = {DEPLOY_PATH}/webapi/publish, so anything
    /// under the content root other than wwwroot (e.g. App_Data) is wiped, and wwwroot is served by
    /// UseStaticFiles, so neither can hold private images.
    ///
    /// Resolution:
    /// 1. Storage:PrivateImageRoot (env Storage__PrivateImageRoot) when set.
    /// 2. A published deploy (content root ".../webapi/publish"): {HOME}/ridepass-private-images/{deploy dir
    ///    name}, e.g. /home/deploy/ridepass-private-images/production and .../staging. Outside the deploy
    ///    path (so no deploy touches it), owned by the user pm2 runs the API as (so it can be created
    ///    without root, unlike a sibling under /var/www), and one folder per environment on a shared host.
    /// 3. Development: {content root}/App_Data/private-images (git-ignored).
    /// The folder is created at startup; a root inside the web root is refused.
    /// </summary>
    public static class PrivateImageRoot
    {
        public static string Resolve(IConfiguration config, IWebHostEnvironment env)
        {
            var configured = config["Storage:PrivateImageRoot"];
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

            var content = Path.GetFullPath(env.ContentRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.Equals(Path.GetFileName(content), "publish", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(home))
            {
                // {DEPLOY_PATH}/webapi/publish -> {DEPLOY_PATH}
                var deployRoot = Path.GetDirectoryName(Path.GetDirectoryName(content));
                var envName = string.IsNullOrWhiteSpace(deployRoot) ? env.EnvironmentName : Path.GetFileName(deployRoot);
                return Path.Combine(home, "ridepass-private-images", envName.ToLowerInvariant());
            }
            return Path.Combine(content, "App_Data", "private-images");
        }

        /// <summary>Resolves, refuses a root inside the web root, and creates the folder.</summary>
        public static string EnsureCreated(IConfiguration config, IWebHostEnvironment env)
        {
            var root = Resolve(config, env);
            var webRoot = Path.GetFullPath(string.IsNullOrEmpty(env.WebRootPath)
                ? Path.Combine(env.ContentRootPath, "wwwroot") : env.WebRootPath);
            if ((root + Path.DirectorySeparatorChar).StartsWith(webRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Storage:PrivateImageRoot ({root}) is inside the web root ({webRoot}); private images would be public.");
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
