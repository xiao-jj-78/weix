using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Microsoft.Extensions.DependencyInjection;
using WeAutoCommon.Models;
using WeAutoCommon.Utils;
using WeChatAuto.Extentions;
using WeChatAuto.Utils;
using WeAutoCommon.Enums;
using System.Threading.Tasks;
using FlaUI.UIA3;
using WeAutoCommon.Extentions;
using System.IO;
using FlaUI.Core;
using MessagePack;
using System.Net.Http;
using System.Drawing;
using WeChatAuto.Options;
using WeChatAuto.Models;
using Emgu.CV.Dai;
using System.Windows.Controls;
using Emgu.CV.Structure;
using WeChatAuto.Services;
using Emgu.CV;
using System.Windows.Media.Imaging;
using System.ComponentModel.DataAnnotations;


namespace WeChatAuto.Components
{
	/// <summary>
	/// 朋友圈操作
	/// </summary>
	public partial class Moments
	{
		private readonly IServiceProvider _serviceProvider;
		private UIThreadInvoker _uiMainThreadInvoker;
		private AutoLogger<Moments> _logger;
		private WeChatClient _Client;



		internal Moments(WeChatClient client, UIThreadInvoker uiThreadInvoker, IServiceProvider serviceProvider)
		{
			_logger = serviceProvider.GetRequiredService<AutoLogger<Moments>>();
			_uiMainThreadInvoker = uiThreadInvoker;
			_Client = client;
			_serviceProvider = serviceProvider;
		}
	}
}