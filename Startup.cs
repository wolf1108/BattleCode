using Microsoft.Owin;
using Owin;
using Microsoft.Owin.Security.Cookies;
//using Microsoft.Owin.Security.Facebook;
using Microsoft.AspNet.Identity;

[assembly: OwinStartup(typeof(BattleCode.Startup))]  // ← 注意這裡！

namespace BattleCode
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {

            app.MapSignalR(); // 啟用 SignalR
            ConfigureAuth(app);

        }

        public void ConfigureAuth(IAppBuilder app)
        {
            // 1. 主要登入 cookie
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = "ApplicationCookie",
                LoginPath = new PathString("/Home/Login")
            });

            // 2. 必須先註冊 SignInAsAuthenticationType
            app.UseExternalSignInCookie(DefaultAuthenticationTypes.ExternalCookie);

            // 3. 再自訂 ExternalCookie（SameSite、Secure）
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = DefaultAuthenticationTypes.ExternalCookie,
                CookieName = ".AspNet.ExternalCookie",
                CookiePath = "/",
                CookieSecure = CookieSecureOption.SameAsRequest,
                CookieSameSite = Microsoft.Owin.SameSiteMode.None
            });

            // Facebook
            //app.UseFacebookAuthentication(new FacebookAuthenticationOptions
            //{
            //    AppId = "你的 Facebook AppId",
            //    AppSecret = "你的 Facebook AppSecret"
            //});
        }
    }
}
