using System.Linq;
using Dm.util;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using WeChatAuto.Utils;

namespace WeChatAuto.Extentions
{
    public static class AutomationElementExtensions
    {
        /// <summary>
        /// 使用 TreeWalker 获取指定偏移量的同级元素。
        /// offset = +1 表示下一个同级；-1 表示上一个同级。
        /// </summary>
        public static AutomationElement GetSibling(this AutomationElement element, int offset)
        {
            if (element == null) return null;

            var automation = element.Automation;
            var walker = automation.TreeWalkerFactory.GetControlViewWalker();

            if (offset == 0)
                return element;

            var sibling = element;

            if (offset > 0)
            {
                for (int i = 0; i < offset; i++)
                {
                    sibling = walker.GetNextSibling(sibling);
                    if (sibling == null) return null;
                }
            }
            else
            {
                for (int i = 0; i < -offset; i++)
                {
                    sibling = walker.GetPreviousSibling(sibling);
                    if (sibling == null) return null;
                }
            }

            return sibling;
        }

        /// <summary>
        /// 获取父元素。
        /// </summary>
        public static AutomationElement GetParent(this AutomationElement element)
        {
            if (element == null) return null;
            var walker = element.Automation.TreeWalkerFactory.GetControlViewWalker();
            return walker.GetParent(element);
        }
        /// <summary>
        /// 检查element是否还在UI Tree上
        /// </summary>
        /// <param name="element">待检查的元素</param>
        /// <param name="parent">元素的父对象</param>
        /// <returns></returns>
        public static bool IsElementInTree(this AutomationElement element, AutomationElement parent)
        {
            if (element == null || !element.IsAvailable || parent == null)
                return false;
            var uniqueString = element.Properties.RuntimeId.ToUniqueString() + "|" + element.Name;
            return parent.FindAllChildren().Any(u => (u.Properties.RuntimeId.ToUniqueString() + "|" + u.Name).Equals(uniqueString));
        }
    }
}
