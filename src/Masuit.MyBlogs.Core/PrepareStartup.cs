﻿using Hangfire;
using Hangfire.Dashboard;
using Masuit.LuceneEFCore.SearchEngine;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Extensions.Hangfire;
using Masuit.Tools.Mime;
using Masuit.Tools.Win32;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.IO;
using Microsoft.Net.Http.Headers;
using Polly;
using SixLabors.ImageSharp.Web.Caching;
using SixLabors.ImageSharp.Web.Commands;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Processors;
using SixLabors.ImageSharp.Web.Providers;
using StackExchange.Profiling;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;
using Windows = Masuit.Tools.Win32.Windows;

namespace Masuit.MyBlogs.Core
{
    public static class PrepareStartup
    {
        /// <summary>
        /// 初始化系统设置参数
        /// </summary>
        /// <param name="app"></param>
        internal static void InitSettings(this IApplicationBuilder app)
        {
            var dic = app.ApplicationServices.GetRequiredService<DataContext>().SystemSetting.ToDictionary(s => s.Name, s => s.Value);
            CommonHelper.SystemSettings.AddOrUpdate(dic);
        }

        internal static void UseLuceneSearch(this IApplicationBuilder app, IHostEnvironment env, IHangfireBackJob hangfire, LuceneIndexerOptions luceneIndexerOptions)
        {
            var are = new AutoResetEvent(false);
            Task.Run(() =>
            {
                Console.WriteLine("正在导入自定义词库...");
                double time = HiPerfTimer.Execute(() =>
                {
                    var db = app.ApplicationServices.GetRequiredService<DataContext>();
                    var set = db.Post.Select(p => $"{p.Title},{p.Label},{p.Keyword}").AsParallel().SelectMany(s => Regex.Split(s, @"\p{P}(?<!\.|#)|\p{Z}|\p{S}")).Where(s => s.Length > 1).ToHashSet();
                    var lines = File.ReadAllLines(Path.Combine(env.ContentRootPath, "App_Data", "CustomKeywords.txt")).Union(set);
                    KeywordsManager.AddWords(lines);
                    KeywordsManager.AddSynonyms(File.ReadAllLines(Path.Combine(env.ContentRootPath, "App_Data", "CustomSynonym.txt")).Where(s => s.Contains(' ')).Select(s =>
                    {
                        var arr = Regex.Split(s, "\\s");
                        return (arr[0], arr[1]);
                    }));
                });
                Console.WriteLine($"导入自定义词库完成，耗时{time}s");
                Masuit.Tools.Win32.Windows.ClearMemorySilent();
                are.Set();
            });

            string lucenePath = Path.Combine(env.ContentRootPath, luceneIndexerOptions.Path);
            if (!Directory.Exists(lucenePath) || Directory.GetFiles(lucenePath).Length < 1)
            {
                are.WaitOne();
                Console.WriteLine("索引库不存在，开始自动创建Lucene索引库...");
                hangfire.CreateLuceneIndex();
                Console.WriteLine("索引库创建完成！");
            }
        }

        public static void SetupHangfire(this IApplicationBuilder app)
        {
            app.UseHangfireDashboard("/taskcenter", new DashboardOptions()
            {
                Authorization = new[]
                {
                    new MyRestrictiveAuthorizationFilter()
                }
            }); //配置hangfire
            HangfireJobInit.Start(); //初始化定时任务
        }

        public static void SetupHttpsRedirection(this IApplicationBuilder app, IConfiguration config)
        {
            if (bool.Parse(config["Https:Enabled"]))
            {
                app.UseHttpsRedirection();
            }

            var options = new RewriteOptions().Add(c =>
            {
                if (c.HttpContext.Request.Path.Equals("/tag") && c.HttpContext.Request.Query.ContainsKey("tag"))
                {
                    c.Result = RuleResult.EndResponse;
                    c.HttpContext.Response.Redirect("/tag/" + HttpUtility.UrlEncode(c.HttpContext.Request.Query["tag"]), true);
                }

                if ((c.HttpContext.Request.Path.Equals("/search") || c.HttpContext.Request.Path.Equals("/s")) && c.HttpContext.Request.Query.ContainsKey("wd"))
                {
                    c.Result = RuleResult.EndResponse;
                    c.HttpContext.Response.Redirect("/search/" + HttpUtility.UrlEncode(c.HttpContext.Request.Query["wd"]).Replace("+", "%20"), true);
                }
            }).AddRewrite(@"\w+/_blazor(.*)", "_blazor$1", false);
            switch (config["UseRewriter"])
            {
                case "NonWww":
                    options.AddRedirectToNonWww(301); // URL重写
                    break;

                case "WWW":
                    options.AddRedirectToWww(301); // URL重写
                    break;
            }

            app.UseRewriter(options);
        }

        public static void SetupMiniProfile(this IServiceCollection services)
        {
            services.AddMiniProfiler(options =>
            {
                options.RouteBasePath = "/profiler";
                options.EnableServerTimingHeader = true;
                options.ResultsAuthorize = req => req.HttpContext.Session.Get<UserInfoDto>(SessionKey.UserInfo)?.IsAdmin == true;
                options.ResultsListAuthorize = options.ResultsAuthorize;
                options.IgnoredPaths.AddRange("/Assets/", "/Content/", "/fonts/", "/images/", "/ng-views/", "/Scripts/", "/static/", "/template/", "/cloud10.png", "/favicon.ico", "/_blazor");
                options.PopupRenderPosition = RenderPosition.BottomLeft;
                options.PopupShowTimeWithChildren = true;
                options.PopupShowTrivial = true;
            }).AddEntityFramework();
        }

        public static void ConfigureOptions(this IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options => options.MinimumSameSitePolicy = SameSiteMode.Lax); //配置Cookie策略
            services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 104857600); //配置请求长度
            services.Configure<ForwardedHeadersOptions>(options => // X-Forwarded-For
            {
                options.ForwardLimit = null;
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardedForHeaderName = AppConfig.TrueClientIPHeader;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });
            services.Configure<StaticFileOptions>(options =>
            {
                options.OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers[HeaderNames.CacheControl] = "public,no-cache";
                    context.Context.Response.Headers[HeaderNames.Expires] = DateTime.Now.AddDays(7).ToString("R");
                };
                options.ContentTypeProvider = new FileExtensionContentTypeProvider(MimeMapper.MimeTypes);
                options.HttpsCompression = HttpsCompressionMode.Compress;
            }); // 配置静态资源文件类型和缓存
        }

        public static IServiceCollection SetupHttpClients(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpClient("").AddTransientHttpErrorPolicy(builder => builder.Or<TaskCanceledException>().Or<OperationCanceledException>().Or<TimeoutException>().OrResult(res => !res.IsSuccessStatusCode).RetryAsync(5)).ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                if (bool.TryParse(config["HttpClientProxy:Enabled"], out var b) && b)
                {
                    handler.Proxy = new WebProxy(config["HttpClientProxy:Uri"], true); // 使用自定义代理
                }
                else
                {
                    handler.Proxy = WebRequest.GetSystemWebProxy(); // 使用系统代理
                }

                return handler;
            }); //注入HttpClient
            services.AddHttpClient<ImagebedClient>().AddTransientHttpErrorPolicy(builder => builder.Or<TaskCanceledException>().Or<OperationCanceledException>().Or<TimeoutException>().OrResult(res => !res.IsSuccessStatusCode).RetryAsync(3)); //注入HttpClient
            return services;
        }

        public static IServiceCollection SetupImageSharp(this IServiceCollection services)
        {
            services.AddImageSharp(options =>
            {
                options.MemoryStreamManager = new RecyclableMemoryStreamManager();
                options.BrowserMaxAge = TimeSpan.FromDays(7);
                options.CacheMaxAge = TimeSpan.FromDays(365);
                options.Configuration = SixLabors.ImageSharp.Configuration.Default;
            }).SetRequestParser<QueryCollectionRequestParser>().Configure<PhysicalFileSystemCacheOptions>(options =>
            {
                options.CacheRootPath = null;
                options.CacheFolder = "static/image_cache";
            }).SetCache<PhysicalFileSystemCache>().SetCacheKey<UriRelativeLowerInvariantCacheKey>().SetCacheHash<SHA256CacheHash>().Configure<PhysicalFileSystemProviderOptions>(options => options.ProviderRootPath = null).AddProvider<PhysicalFileSystemProvider>().AddProcessor<ResizeWebProcessor>().AddProcessor<FormatWebProcessor>().AddProcessor<BackgroundColorWebProcessor>().AddProcessor<QualityWebProcessor>().AddProcessor<AutoOrientWebProcessor>();
            return services;
        }
    }

    /// <summary>
    /// hangfire授权拦截器
    /// </summary>
    public class MyRestrictiveAuthorizationFilter : IDashboardAuthorizationFilter
    {
        /// <summary>
        /// 授权校验
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool Authorize(DashboardContext context)
        {
#if DEBUG
            return true;
#endif
            var user = context.GetHttpContext().Session.Get<UserInfoDto>(SessionKey.UserInfo) ?? new UserInfoDto();
            return user.IsAdmin;
        }
    }
}