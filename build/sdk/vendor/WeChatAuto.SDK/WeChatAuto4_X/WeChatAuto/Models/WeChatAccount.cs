using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using SqlSugar;

namespace WeChatAuto.Models
{
    /// <summary>
    /// 微信账号
    /// </summary>
    [SugarTable("wechat_account")]
    public class WeChatAccount
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>
        /// SDK 内部定义的客户端名称，例如 default、wx1、wx2
        /// </summary>
        [SugarColumn(Length = 100, IsNullable = false)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 微信 wxid
        /// </summary>
        [SugarColumn(Length = 100, IsNullable = true)]
        public string WxId { get; set; } = string.Empty;

        [SugarColumn(Length = 100)]
        public string NickName { get; set; } = string.Empty;

        [SugarColumn(Length = 100)]
        public string WeChatId { get; set; } = string.Empty;

        [SugarColumn(Length = 500)]
        public string AvatarUrl { get; set; } = string.Empty;
    }
}