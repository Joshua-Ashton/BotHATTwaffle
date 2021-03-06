﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BotHATTwaffle.Modules.Json;

namespace BotHATTwaffle.Objects.Downloader
{
	public abstract class Downloader<TClient> : IDisposable where TClient : IDisposable
	{
		protected TClient Client;
		protected readonly DataServices DataSvc;
		protected readonly string DemoName;
		protected readonly string FtpPath;
		protected readonly string LocalPath;
		protected readonly string WorkshopId;

		protected Downloader(IReadOnlyList<string> testInfo, JsonServer server, DataServices dataSvc)
		{
			DataSvc = dataSvc;
			DateTime time = Convert.ToDateTime(testInfo[1]);
			string title = testInfo[2].Substring(0, testInfo[2].IndexOf(" "));

			DemoName = $"{time:MM_dd_yyyy}_{title}";
			FtpPath = server.FTPPath;
			LocalPath = $"{DataSvc.DemoPath}\\{time:yyyy}\\{time:MM} - " + $"{time:MMMM}\\{DemoName}";
			WorkshopId = Regex.Match(testInfo[6], @"\d+$").Value;
		}

		public abstract void Download();

		public void Dispose()
		{
			Client.Dispose();
		}
	}
}
