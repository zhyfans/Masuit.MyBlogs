﻿using System.Collections.Frozen;
using FreeRedis;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Extensions.Firewall;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.RegularExpressions;
using Masuit.MyBlogs.Core.Models;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 基本父控制器
/// </summary>
[ApiExplorerSettings(IgnoreApi = true), ServiceFilter(typeof(FirewallAttribute))]
public class BaseController : Controller
{
    public IUserInfoService UserInfoService { get; set; }

    public ILinksService LinksService { get; set; }

    public IAdvertisementService AdsService { get; set; }

    public IVariablesService VariablesService { get; set; }

    public IRedisClient RedisHelper { get; set; }

    public IFirewallService FirewallService { get; set; }

    protected UserInfoDto CurrentUser => HttpContext.Session.Get<UserInfoDto>(SessionKey.UserInfo) ?? new UserInfoDto();

    /// <summary>
    /// 客户端的真实IP
    /// </summary>
    protected IPAddress ClientIP => HttpContext.Connection.RemoteIpAddress;

    /// <summary>
    /// 普通访客是否token合法
    /// </summary>
    private bool VisitorTokenValid => Request.Cookies.ContainsKey("FullAccessToken") && Request.Cookies["Email"].MDString(AppConfig.BaiduAK).Equals(Request.Cookies["FullAccessToken"]);

    private int[] HideCategories => Request.GetHideCategories();

    private bool SafeMode => !Request.Cookies.ContainsKey("Nsfw") || Request.Cookies["Nsfw"] == "1";

    /// <summary>
    /// 响应数据
    /// </summary>
    /// <param name="data">数据</param>
    /// <param name="success">响应状态</param>
    /// <param name="message">响应消息</param>
    /// <param name="isLogin">登录状态</param>
    /// <param name="code">http响应码</param>
    /// <returns></returns>
    internal ActionResult ResultData(object data, bool success = true, string message = "", bool isLogin = true, HttpStatusCode code = HttpStatusCode.OK)
    {
        return Ok(new
        {
            IsLogin = isLogin,
            Success = success,
            Message = message,
            Data = data,
            code
        });
    }

    protected string ReplaceVariables(string text, int depth = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var location = Request.Location();
        var template = Template.Create(text)
            .Set("clientip", ClientIP.ToString())
            .Set("location", location.Address)
            .Set("network", location.Network)
            .Set("domain", Request.Host.Host)
            .Set("path", Request.Path.ToUriComponent());
        if (text.Contains("{{browser}}") || text.Contains("{{os}}"))
        {
            var agent = UserAgent.Parse(Request.Headers[HeaderNames.UserAgent] + "");
            template.Set("browser", agent.Browser + " " + agent.BrowserVersion).Set("os", agent.Platform);
        }

        var pattern = @"\{\{[\w._-]+\}\}";
        var keys = Regex.Matches(template.Render(), pattern).Select(m => m.Value.Trim('{', '}')).ToArray();
        if (keys.Length > 0)
        {
            var dic = VariablesService.GetQueryFromCache(v => keys.Contains(v.Key)).ToFrozenDictionary(v => v.Key, v => v.Value);
            foreach (var (key, value) in dic)
            {
                string valve = value;
                if (Regex.IsMatch(valve, pattern) && depth < 32)
                {
                    valve = ReplaceVariables(valve, depth++);
                }

                template.Set(key, valve);
            }
        }

        return template.Render();
    }

    public override Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ViewBag.Desc = CommonHelper.SystemSettings["Description"];
        var user = context.HttpContext.Session.Get<UserInfoDto>(SessionKey.UserInfo);
#if DEBUG
        if (HttpContext.Connection.RemoteIpAddress.IsPrivateIP())
        {
            user = UserInfoService.GetByUsername("masuit").ToDto();
            context.HttpContext.Session.Set(SessionKey.UserInfo, user);
        }
#endif
        if (CommonHelper.SystemSettings.GetOrAdd("CloseSite", "false") == "true" && user?.IsAdmin != true)
        {
            context.Result = RedirectToAction("ComingSoon", "Error");
            return Task.CompletedTask;
        }

        if (Request.Method == HttpMethods.Post && !Request.Path.Value.Contains("get", StringComparison.InvariantCultureIgnoreCase) && CommonHelper.SystemSettings.GetOrAdd("DataReadonly", "false") == "true" && !context.Filters.Any(m => m.ToString().Contains(nameof(MyAuthorizeAttribute))))
        {
            context.Result = ResultData("网站当前处于数据写保护状态，无法提交任何数据，如有疑问请联系网站管理员！", false, "网站当前处于数据写保护状态，无法提交任何数据，如有疑问请联系网站管理员！", user != null, HttpStatusCode.BadRequest);
            return Task.CompletedTask;
        }

        if (user == null && Request.Cookies.ContainsKey("username") && Request.Cookies.ContainsKey("password")) //执行自动登录
        {
            var name = Request.Cookies["username"];
            var pwd = Request.Cookies["password"];
            var userInfo = UserInfoService.Login(name, pwd.DesDecrypt(AppConfig.BaiduAK));
            if (userInfo != null)
            {
                Response.Cookies.Append("username", name, new CookieOptions
                {
                    Expires = DateTime.Now.AddYears(1),
                    SameSite = SameSiteMode.Lax
                });
                Response.Cookies.Append("password", pwd, new CookieOptions
                {
                    Expires = DateTime.Now.AddYears(1),
                    SameSite = SameSiteMode.Lax
                });
                context.HttpContext.Session.Set(SessionKey.UserInfo, userInfo);
            }
        }

        if (ModelState.IsValid) return next();
        var errmsgs = ModelState.SelectMany(kv => kv.Value.Errors.Select(e => e.ErrorMessage)).Select((s, i) => $"{i + 1}. {s}");
        context.Result = true switch
        {
            _ when Request.HasJsonContentType() || Request.Method == HttpMethods.Post => ResultData(errmsgs, false, "数据校验失败，错误信息：" + errmsgs.Join(" | "), user != null, HttpStatusCode.BadRequest),
            _ => base.BadRequest("参数错误：" + errmsgs.Join(" | "))
        };
        return Task.CompletedTask;
    }

    /// <summary>
    /// 验证邮箱验证码
    /// </summary>
    /// <param name="mailSender"></param>
    /// <param name="email">邮箱地址</param>
    /// <param name="code">验证码</param>
    /// <returns></returns>
    internal async Task<string> ValidateEmailCode(IEmailBlocklistService mailSender, string email, string code)
    {
        if (CurrentUser.IsAdmin)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(Request.Cookies["ValidateKey"]))
        {
            if (string.IsNullOrEmpty(code))
            {
                return "请输入验证码！";
            }
            if (await RedisHelper.GetAsync("code:" + email) != code)
            {
                return "验证码错误！";
            }
        }
        else if (Request.Cookies["ValidateKey"].DesDecrypt(AppConfig.BaiduAK) != email)
        {
            Response.Cookies.Delete("Email");
            Response.Cookies.Delete("NickName");
            Response.Cookies.Delete("ValidateKey");
            return "邮箱验证信息已失效，请刷新页面后重新评论！";
        }

        if (mailSender.Any(e => e.Email == email))
        {
            Response.Cookies.Delete("Email");
            Response.Cookies.Delete("NickName");
            Response.Cookies.Delete("ValidateKey");
            return "邮箱地址错误，请刷新页面后重新使用有效的邮箱地址！";
        }

        await RedisHelper.DelAsync("code:" + email);
        return string.Empty;
    }

    internal void WriteEmailKeyCookie(string email)
    {
        Response.Cookies.Append("Email", email, new CookieOptions()
        {
            Expires = DateTimeOffset.Now.AddYears(1),
            SameSite = SameSiteMode.Lax
        });
        Response.Cookies.Append("ValidateKey", email.DesEncrypt(AppConfig.BaiduAK), new CookieOptions()
        {
            Expires = DateTimeOffset.Now.AddYears(1),
            SameSite = SameSiteMode.Lax
        });
    }

    protected Expression<Func<Post, bool>> PostBaseWhere()
    {
        if (CurrentUser.IsAdmin || Request.IsRobot())
        {
            return _ => true;
        }

        var ip = ClientIP.ToString();
        Expression<Func<Post, bool>> where = p => p.Status == Status.Published && p.LimitMode != RegionLimitMode.OnlyForSearchEngine;
        where = where.AndIf(HideCategories.Length > 0, p => !HideCategories.Contains(p.CategoryId)).AndIf(SafeMode, p => !p.IsNsfw);
        if (VisitorTokenValid || CommonHelper.IPWhiteList.Contains(ip))
        {
            return where;
        }

        var ipLocation = Request.Location();
        var location = ipLocation + ipLocation.Coodinate + "|" + Request.Headers[HeaderNames.Referer] + "|" + Request.Headers[HeaderNames.UserAgent] + "|" + Request.Headers.Where(x => x.Key.StartsWith("cf-")).Select(x => x.Value).Join("|");
        if (Request.Cookies.TryGetValue(SessionKey.RawIP, out var rawip) && ip != rawip)
        {
            var s = rawip.Base64Decrypt();
            if (ip != s)
            {
                location += "|" + s.GetIPLocation();
            }
        }

        return where.And(p => p.LimitMode == null || p.LimitMode == RegionLimitMode.All ? true :
            p.LimitMode == RegionLimitMode.AllowRegion ? Regex.IsMatch(location, p.Regions, RegexOptions.IgnoreCase) :
            p.LimitMode == RegionLimitMode.ForbidRegion ? !Regex.IsMatch(location, p.Regions, RegexOptions.IgnoreCase) :
            p.LimitMode == RegionLimitMode.AllowRegionExceptForbidRegion ? Regex.IsMatch(location, p.Regions, RegexOptions.IgnoreCase) && !Regex.IsMatch(location, p.ExceptRegions, RegexOptions.IgnoreCase) : !Regex.IsMatch(location, p.Regions, RegexOptions.IgnoreCase) || Regex.IsMatch(location, p.ExceptRegions, RegexOptions.IgnoreCase));
    }

    protected void CheckPermission(Post post)
    {
        var ip = ClientIP.ToString();
        if (CurrentUser.IsAdmin || VisitorTokenValid || Request.IsRobot() || CommonHelper.IPWhiteList.Contains(ip))
        {
            return;
        }

        var ipLocation = Request.Location();
        var location = ipLocation + ipLocation.Coodinate + "|" + Request.Headers[HeaderNames.Referer] + "|" + Request.Headers[HeaderNames.UserAgent] + "|" + Request.Headers.Where(x => x.Key.StartsWith("cf-")).Select(x => x.Value).Join("|");
        if (Request.Cookies.TryGetValue(SessionKey.RawIP, out var rawip) && ip != rawip)
        {
            var s = rawip.Base64Decrypt();
            if (ip != s)
            {
                location += "|" + s.GetIPLocation();
            }
        }

        switch (post.LimitMode)
        {
            case RegionLimitMode.OnlyForSearchEngine:
                Disallow(post);
                break;

            case RegionLimitMode.AllowRegion:
                if (!Regex.IsMatch(location, post.Regions, RegexOptions.IgnoreCase))
                {
                    Disallow(post);
                }

                break;

            case RegionLimitMode.ForbidRegion:
                if (Regex.IsMatch(location, post.Regions, RegexOptions.IgnoreCase))
                {
                    Disallow(post);
                }

                break;

            case RegionLimitMode.AllowRegionExceptForbidRegion:
                if (Regex.IsMatch(location, post.ExceptRegions, RegexOptions.IgnoreCase))
                {
                    Disallow(post);
                }

                goto case RegionLimitMode.AllowRegion;
            case RegionLimitMode.ForbidRegionExceptAllowRegion:
                if (Regex.IsMatch(location, post.ExceptRegions, RegexOptions.IgnoreCase))
                {
                    break;
                }

                goto case RegionLimitMode.ForbidRegion;
        }

        if (HideCategories.Contains(post.CategoryId) || (SafeMode && post.IsNsfw))
        {
            throw new NotFoundException("文章未找到");
        }
    }

    private void Disallow(Post post)
    {
        var remark = "无权限查看该文章";
        var ip = ClientIP.ToString();
        if (Request.Cookies.TryGetValue(SessionKey.RawIP, out var rawip) && ip != rawip.Base64Decrypt())
        {
            remark += "，发生了IP切换，原始IP：" + rawip.Base64Decrypt();
        }

        FirewallService.AddIntercept(new IpInterceptLog()
        {
            IP = ip,
            RequestUrl = $"//{Request.Host}/{post.Id}",
            Referer = Request.Headers[HeaderNames.Referer],
            Time = DateTime.Now,
            UserAgent = Request.Headers[HeaderNames.UserAgent],
            Remark = remark,
            Address = Request.Location(),
            HttpVersion = Request.Protocol,
            Headers = new
            {
                Request.Protocol,
                Request.Headers
            }.ToJsonString()
        });
        throw new NotFoundException("文章未找到");
    }
}