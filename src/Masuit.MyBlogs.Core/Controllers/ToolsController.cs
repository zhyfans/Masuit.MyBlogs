﻿using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Models.ViewModel;
using Masuit.Tools;
using Masuit.Tools.Core.Validator;
using Masuit.Tools.Models;
using MaxMind.GeoIP2.Exceptions;
using MaxMind.GeoIP2.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Polly;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TimeZoneConverter;

#if DEBUG
#endif

namespace Masuit.MyBlogs.Core.Controllers
{
    /// <summary>
    /// 黑科技
    /// </summary>
    [Route("tools")]
    public class ToolsController : BaseController
    {
        private readonly HttpClient _httpClient;

        public ToolsController(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }

        /// <summary>
        /// 获取ip地址详细信息
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        [Route("ip"), Route("ip/{ip?}", Order = 1), ResponseCache(Duration = 600, VaryByQueryKeys = new[] { "ip" }, VaryByHeader = "Cookie")]
        public ActionResult GetIpInfo([IsIPAddress] string ip)
        {
            if (string.IsNullOrEmpty(ip))
            {
                ip = ClientIP;
            }

            if (ip.IsPrivateIP())
            {
                return Ok("内网IP");
            }

            ViewBag.IP = ip;
            var cityInfo = Policy<CityResponse>.Handle<AddressNotFoundException>().Fallback(() => new CityResponse()).Execute(() => CommonHelper.MaxmindReader.City(ip));
            var address = new IpInfo()
            {
                CityInfo = cityInfo,
                Address = $"{ip.GetIPLocation()}（UTC{TZConvert.GetTimeZoneInfo(cityInfo.Location.TimeZone ?? "Asia/Shanghai").BaseUtcOffset.Hours:+#;-#;0}）",
                Asn = ip.GetIPAsn()
            };
            if (Request.Method.Equals(HttpMethods.Get))
            {
                return View(address);
            }

            return Json(address);
        }

        /// <summary>
        /// 根据经纬度获取详细地理信息
        /// </summary>
        /// <param name="lat"></param>
        /// <param name="lng"></param>
        /// <returns></returns>
        [HttpGet("pos"), ResponseCache(Duration = 600, VaryByQueryKeys = new[] { "lat", "lng" }, VaryByHeader = "Cookie")]
        public async Task<ActionResult> Position(string lat, string lng)
        {
            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lng))
            {
                var ip = ClientIP;
#if DEBUG
                var r = new Random();
                ip = $"{r.Next(210)}.{r.Next(255)}.{r.Next(255)}.{r.Next(255)}";
#endif
                var location = Policy<CityResponse>.Handle<AddressNotFoundException>().Fallback(() => new CityResponse()).Execute(() => CommonHelper.MaxmindReader.City(ip));
                var address = new PhysicsAddress()
                {
                    Status = 0,
                    AddressResult = new AddressResult()
                    {
                        FormattedAddress = ip.GetIPLocation(),
                        Location = new Location()
                        {
                            Lng = location.Location.Longitude ?? 0,
                            Lat = location.Location.Latitude ?? 0
                        }
                    }
                };
                return View(address);
            }

            var s = await _httpClient.GetStringAsync($"http://api.map.baidu.com/geocoder/v2/?location={lat},{lng}&output=json&pois=1&ak={AppConfig.BaiduAK}", new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).ContinueWith(t =>
             {
                 if (t.IsCompletedSuccessfully)
                 {
                     return JsonConvert.DeserializeObject<PhysicsAddress>(t.Result);
                 }

                 return new PhysicsAddress();
             });

            return View(s);
        }

        /// <summary>
        /// 详细地理信息转经纬度
        /// </summary>
        /// <param name="addr"></param>
        /// <returns></returns>
        [Route("addr"), ResponseCache(Duration = 600, VaryByQueryKeys = new[] { "addr" }, VaryByHeader = "Cookie")]
        public async Task<ActionResult> Address(string addr)
        {
            if (string.IsNullOrEmpty(addr))
            {
                var ip = ClientIP;
#if DEBUG
                Random r = new Random();
                ip = $"{r.Next(210)}.{r.Next(255)}.{r.Next(255)}.{r.Next(255)}";
#endif
                var location = Policy<CityResponse>.Handle<AddressNotFoundException>().Fallback(() => new CityResponse()).Execute(() => CommonHelper.MaxmindReader.City(ip));
                var address = new PhysicsAddress()
                {
                    Status = 0,
                    AddressResult = new AddressResult()
                    {
                        FormattedAddress = ip.GetIPLocation(),
                        Location = new Location()
                        {
                            Lng = location.Location.Longitude ?? 0,
                            Lat = location.Location.Latitude ?? 0
                        }
                    }
                };
                ViewBag.Address = address.AddressResult.FormattedAddress;
                if (Request.Method.Equals(HttpMethods.Get))
                {
                    return View(address.AddressResult.Location);
                }

                return Json(address.AddressResult.Location);
            }

            ViewBag.Address = addr;
            var physicsAddress = await _httpClient.GetStringAsync($"http://api.map.baidu.com/geocoder/v2/?output=json&address={addr}&ak={AppConfig.BaiduAK}", new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).ContinueWith(t =>
             {
                 if (t.IsCompletedSuccessfully)
                 {
                     return JsonConvert.DeserializeObject<PhysicsAddress>(t.Result);
                 }

                 return new PhysicsAddress();
             });
            if (Request.Method.Equals(HttpMethods.Get))
            {
                return View(physicsAddress?.AddressResult?.Location);
            }

            return Json(physicsAddress?.AddressResult?.Location);
        }
    }
}