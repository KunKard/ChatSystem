using System;
using UnityEngine;

namespace ChatSystem.Data
{
    /// <summary>
    /// 头像获取接口。存在的意义是<b>把"头像从哪来"与调用方解耦</b>。
    /// </summary>
    /// <remarks>
    /// MVP 只实现本地 <see cref="ContactProfile.avatar"/> 直读，不实现网络加载 ——
    /// 网络头像会引入超时、失败占位、缓存失效一整条失败路径，对 Demo 属于过度设计。
    /// 保留此接口是为了让"以后换成网络头像"不必改动任何调用方。
    /// </remarks>
    public interface IAvatarProvider
    {
        /// <summary>异步获取头像。<paramref name="onLoaded"/> 可能在同步路径上被立即调用。</summary>
        void GetAvatar(string contactId, Action<Sprite> onLoaded);
    }
}
