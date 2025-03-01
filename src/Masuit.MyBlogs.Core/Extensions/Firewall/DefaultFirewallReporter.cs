﻿using System.Net;
using Masuit.Tools.Core;

namespace Masuit.MyBlogs.Core.Extensions.Firewall;

public sealed class DefaultFirewallReporter(DataContext dataContext) : IFirewallReporter
{
    public string ReporterName { get; set; }

    /// <summary>
    /// 上报IP
    /// </summary>
    /// <param name="ip"></param>
    public void Report(IPAddress ip)
    {
        var s = ip.ToString();
        if (dataContext.IpReportLogs.AnyWithNoLock(e => e.IP == s))
        {
            return;
        }
        dataContext.IpReportLogs.Add(new IpReportLog
        {
            IP = s,
            Time = DateTime.Now
        });
        dataContext.SaveChanges();
    }

    /// <summary>
    /// 上报IP
    /// </summary>
    /// <param name="ip"></param>
    /// <returns></returns>
    public async Task<bool> ReportAsync(IPAddress ip)
    {
        var s = ip.ToString();
        if (await dataContext.IpReportLogs.AnyWithNoLockAsync(e => e.IP == s))
        {
            return false;
        }
        await dataContext.IpReportLogs.AddAsync(new IpReportLog
        {
            IP = s,
            Time = DateTime.Now
        });
        return await dataContext.SaveChangesAsync() > 0;
    }
}