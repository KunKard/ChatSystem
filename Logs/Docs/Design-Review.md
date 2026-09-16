# 设计文档评审 —— 需改进与新增内容

> 评审对象：`Logs/Docs/Design.md`
> 评审日期：2026-09-16
> 评审依据：设计文档 + 工程实际状态（`Assets/` 全量扫描）

---

## 0. 评审结论

文档的**产品视角是完整的**（UI 布局、玩家侧交互、策划侧工具都覆盖到了），但作为**实现依据是不可用的**：

1. 缺少全部技术契约 —— 数据结构、存档 Schema、模块分层、验收标准一个都没有；
2. 与工程现状存在**明显落差** —— 文档描述的"ContentSizeFitter + VerticalLayoutGroup 自适应"在工程里根本不存在；
3. 存在一个**会直接导致 Demo 无法演示的阻塞问题** —— 中文字体未配置。

下面的内容按「🔴 阻塞 / 🟡 必须补 / 🟢 建议」分级。

---

## 1. 🔴 工程现状与文档的落差（最高优先级）

### 1.1 中文字体未配置 —— Demo 现在跑起来全是方块

这是**必须先解决的问题**，否则后面所有 UI 工作都无法验收。

证据链：

| 检查项 | 实际值 | 后果 |
|---|---|---|
| `m_defaultFontAsset` | `LiberationSans SDF` | 该字体**不含任何中文字形** |
| `m_fallbackFontAssets` | `[]`（空数组） | 没有兜底字体，缺字直接显示方块 |
| `Assets/TextMesh Pro/Fonts/` | 只有 `LiberationSans.ttf` | 工程内无中文字体文件 |
| ~~`m_leadingCharacters` / `m_followingCharacters`~~ | ~~英文标点集（`LineBreaking *.txt`）~~ | ~~中文标点在行首/行尾会错误断行~~ ← **此行结论错误，见下方勘误** |
| 设计文档全部示例文案 | 中文（"知更鸟""让我们把翅膀借给彼此"） | **全部渲染为 □□□□** |

> **勘误（Design v2.2）**：
>
> 1. **上表第 4 行是错的**。TMP 随包发布的 `LineBreaking Leading/Following Characters.txt`
>    **已包含完整且正确的中日文禁则**，不需要追加任何字符。且评审原文给出的映射方向**恰好颠倒**
>    （把闭标点写进了 `Leading`、开标点写进了 `Following`），照做会**制造**禁则错误。详见 `Design.md` 附录 A.2。
> 2. **字体问题已解决**（2026-09-16）。现已接入 `Assets/TextMesh Pro/Fonts/Genshin.ttf` →
>    `Genshin SDF.asset`，`m_defaultFontAsset` 已指向它，`m_fallbackFontAssets` 已挂 `LiberationSans SDF`。
>    上表第 1~3 行的现状描述**已失效**。
> 3. 但 `Genshin SDF.asset` 是**静态图集**（`m_AtlasPopulationMode: 0`，烘焙 3609 字形），
>    实测 `伽 （ ） 「 」 『 』 — · !` 等字符**不在图集内**，仍会显示方块。需改 Dynamic 模式。详见 `Design.md` 附录 A.1。

处理方案（Day 1 第一件事）：

1. **选字体**（注意授权，作品集会公开）：
   - ✅ 推荐：思源黑体 / Noto Sans CJK（SIL OFL，可商用可再分发）
   - ✅ 可用：阿里巴巴普惠体（免费商用）
   - ❌ 避免：微软雅黑（授权仅限 Windows 平台使用，不可随工程分发）
2. **生成 TMP Font Asset**：
   - 首选 **Dynamic SDF**（Atlas 1024×1024，不预烘字形，按需生成）——省事，覆盖全字符集；
   - 若遇到动态图集在 Build 后丢失字形的问题，退化为 **Static SDF**，字符集用「常用汉字 3500 + 全角标点 + 拉丁 + 数字」；
   - 注意勾选 `Clear Dynamic Data on Build` 的行为，避免打包后图集被清空。
3. **设为默认字体**（或加入 `m_fallbackFontAssets`），两者选一：
   - 设为默认：所有 TMP 组件直接用，最省事，但英文数字也会用中文字体渲染（字形略宽）；
   - 设为 fallback：保留 LiberationSans 渲染拉丁字符，中文走 fallback —— **推荐**，排版更精细。
4. ~~**补中文换行规则**：把 `，。！？；：、）》」』】…` 追加进 `LineBreaking Leading Characters.txt`（不可出现在行首），把 `（《「『【` 追加进 `Following Characters.txt`（不可出现在行尾）。~~
   → **此步作废，且原文的映射方向是反的**。TMP 自带的规则文件已完整覆盖中日文禁则，
   无需追加任何字符。详见 `Design.md` 附录 A.2。
5. **（新增，替代原第 4 步）图集模式改 Dynamic**：`Genshin SDF.asset` 当前是静态图集，
   未烘焙的字符仍会显示方块。Inspector 里 `Atlas Population Mode` 改 Static → Dynamic，
   并勾选 `Is Multi Atlas Texture Enabled`。详见 `Design.md` 附录 A.1。

### 1.2 布局组件完全不存在 —— 文档 3.2.2 描述的能力尚未搭建

全量扫描结果：

| 组件 | 场景 (`SampleScene.unity`) | 预制体 |
|---|---|---|
| `VerticalLayoutGroup` | **0 个** | **0 个** |
| `ContentSizeFitter` | **0 个** | **0 个** |
| `LayoutElement` | **0 个** | **0 个** |

也就是说，文档第 3.2.2 节写的「气泡随内容长短自动拉伸（ContentSizeFitter + VerticalLayoutGroup）」**目前一行都没实现**。现在所有 UI 元素都是**手工绝对定位**（硬编码 RectTransform 坐标），气泡尺寸是固定的。

含义：这部分不是"优化"，而是"从零搭建"。Day 1 需要真实投入，不能按"已有基础上微调"估工。

### 1.3 ScrollRect 水平滚动未关闭

场景里两处 `ScrollRect` 都是 `m_Horizontal: 1`。聊天流只需要垂直滚动，开着水平滚动会在文本宽度变化时产生横向抖动/误滚动。

**改为 `m_Horizontal: 0`。**

### 1.4 零 C# 代码

`Assets/` 下 `.cs` 文件数量为 **0**。项目实际进度是「UI 视觉骨架手工摆放完成，逻辑层 0%」。

已有资源可复用：3 个预制体结构基本合理，命名对应文档。
- `ChatBubble.prefab`：`BackGround / Avatar / Name / TextBubble > Content > Text / Sticker`
- `MyChatBubble.prefab`：同上（玩家侧）
- `ChatPartner.prefab`：`Avatar / Name / LastMessage / Reddot / Arrow`

✅ 好消息：左右气泡用了**两个独立预制体**，而非同一个预制体翻转对齐 —— 这是对的做法，简化了布局逻辑。

### 1.5 Canvas 配置（可保留）

`ScaleWithScreenSize`，参考分辨率 1920×1080，`MatchWidthOrHeight = 0`（匹配宽度），`ScreenSpaceOverlay`。

基本合理。注意点：match width 在超宽屏（21:9）下会让 UI 纵向过矮。建议 **match 改为 0.5**，或保持 0 但给聊天内容区设 `maxWidth` 上限，避免超宽屏上气泡被拉得过长影响阅读。

---

## 2. 🟡 必须新增的内容（文档缺失的技术契约）

### 2.1 架构分层 —— 文档完全没提

文档描述了"有什么功能"，但没描述"代码怎么组织"。建议明确四层，并在文档中画出依赖方向：

```
Data 层        ConversationAsset(SO) / ContactProfile(SO) / 存档 JSON
   ↓ 只被读取
Runtime 层     DialogueRunner(状态机) / ChatSession(运行时消息列表) / SaveService
   ↓ 事件通知
View 层        ContactListView / ChatWindowView / BubbleView / ReplyOptionsView / BubblePool
   ↓
Unity 引擎层   UGUI + TMP
```

**关键约束（建议写进文档作为架构原则）：Runtime 层禁止 `using UnityEngine.UI`，禁止继承 MonoBehaviour。**

理由，对作品集尤其重要：
- `DialogueRunner` 是纯 C# 状态机 → 可以用 Unity Test Framework（已在 `manifest.json` 中）写**不启动 Play 模式的单元测试**；
- 面试时"我的对话状态机有单测覆盖，存档序列化做了往返测试"是实打实的加分项，而"我用 ScrollRect 做了聊天框"不是。

### 2.2 对话数据结构 —— 文档只有自然语言描述，无 Schema

文档 4.1 说"消息节点""选项节点""连接对应的后续节点"，但没有定义节点长什么样、"连接"是索引还是 ID。

建议 Schema：

```csharp
public enum NodeKind { Message, Choice, Wait, End }

[Serializable]
public class DialogueNode {
    public string   id;              // 稳定唯一 ID，存档只存 ID 不存下标
    public NodeKind kind;
    public MessageData  message;     // kind == Message 时有效
    public List<ChoiceOption> options; // kind == Choice 时有效
    public float    delaySeconds;    // 发送延迟 / Wait 时长
    public string   nextId;          // 线性后继；Choice 节点为 null
}

[Serializable]
public class ChoiceOption {
    public string text;              // 按钮文案
    public string nextId;            // 跳转目标
}
```

**关于"用 ID 还是用数组下标"的决策**：必须用 **ID**。理由：策划在编辑器里插入/删除节点时，下标会整体位移，存档里存的旧下标会指向错误节点 —— 这是存档类系统的经典事故。建一个 `Dictionary<string, DialogueNode>` 做 O(1) 跳转。

### 2.3 消息模型 —— 文档列举了 4 种内容类型，但没抽象

文档 3.2.2 说气泡支持"纯文本、Emoji（图文混排）、表情包（图片）、图片"，但没有统一模型。

```csharp
public enum MessageKind { Text, Sticker, Image, TimeDivider }

[Serializable]
public class MessageData {
    public MessageKind kind;
    public string senderId;      // 空字符串 = 玩家自己
    public string text;          // Text 用；Emoji 直接内嵌 <sprite name="..."> 标签
    public string assetName;     // Sticker / Image 的资源名
}
```

> **后续修订（Design v2.1）**：时间戳**不放在消息模型上**，而是作为**策划配置**挂在对话节点上（`DialogueNode.timeLabel` + `timeValueUtc`），且**不写入存档**。本评审提出时尚未确定时间来源，详见 `Design.md` §4.2 / §5.2.4。

**关于 Emoji 的技术决策（建议写进文档）**：
- Emoji 不单独建一种 `MessageKind`，而是作为 **Text 内嵌的富文本标签**：`明天见 <sprite name="smile">`。
- 工程里已有现成的 TMP Sprite Asset（`Resources/Sprite Assets/EmojiOne.asset`），且 `TMP Settings` 中 `m_enableEmojiSupport: 1`，**开箱可用，不需要额外工作**。
- 这样"图文混排"由 TMP 原生处理，无需自己排版 —— 这是文档里应该说清楚但没说的实现路径。

**关于图片消息**：建议 **MVP 阶段直接砍掉**。理由是收益/成本比差：需要处理加载、缓存、宽高比适配、失败占位、点击预览。Demo 里放 1 张图证明能力即可，不要做完整系统。

### 2.4 存档 JSON Schema —— 文档只有一句"使用本地 JSON 存档"

建议 Schema：

```json
{
  "saveVersion": 1,
  "savedAtUtc": 1758000000,
  "conversations": [
    {
      "contactId": "robin",
      "currentNodeId": "n_012",
      "messages": [
        { "kind": 0, "senderId": "robin", "text": "原本是希望你能够享受…", "assetName": "" },
        { "kind": 3, "senderId": "",      "text": "昨天 21:30",           "assetName": "" }
      ]
    }
  ]
}
```

文档需要补充的**决策点**（这些不写清楚，实现时必然返工）：

1. **写入时机**：每次收到消息就写盘？还是退出时写？建议 **收到消息后延迟 1 秒合并写入**（防止连发消息时高频 IO），退出时立即 flush。
2. **原子写入**：先写 `save.json.tmp`，成功后 `File.Replace` 覆盖。否则写盘中途崩溃 = 存档永久损坏。**这个必须做**，是存档系统的基本要求。
3. **版本号**：`saveVersion` 字段预留，解析时校验，版本不匹配走默认值而非抛异常。
4. **损坏兜底**：`JsonUtility.FromJson` 包 try-catch，失败则重建空存档并备份损坏文件，**绝不允许因存档损坏导致游戏无法启动**。
5. **🔴 时间分割线的持久化问题（文档的隐藏 Bug）**：
   - 文档 3.2.2 说"超过 5 分钟间隔插入时间分割线"。
   - 但如果分割线是**运行时根据消息时间戳计算**的，从存档恢复重放历史消息时，计算结果会和当时一致（幂等）—— 这没问题；
   - 然而如果分割线是**作为独立消息项插入列表**的，它就必须一起持久化，否则恢复后分割线全部消失，或者位置错乱。
   - **建议**：把 `TimeDivider` 作为一种 `MessageKind` **持久化存储**，插入时计算一次。简单、无幂等性风险。文档需要明确这一点。

> **后续修订（Design v2.1）**：本项已采纳（持久化存储）。且**时间戳改为由策划配置、不读运行时时钟**（未配置时间的消息不显示时间、也不参与分割线判断）后，这条决策**更加必要**——因为时间不再来自运行时，存档必须带着已算好的分割线结果。详见 `Design.md` §5.2.4。

### 2.5 编辑器工具的形态 —— 文档只有"对话节点编辑器"六个字

文档 5.2 说"复杂的拖拽节点图编辑器初期搁置"，但**没有给出替代方案**。这里必须补：

建议 MVP 形态（**不要做 GraphView，至少 2 天，会吃掉整个计划**）：

| 功能 | 实现方式 | 优先级 |
|---|---|---|
| 节点列表增删改排序 | `[CustomEditor]` + `ReorderableList` | 🟡 必须 |
| 节点类型切换（Message/Choice/End） | 条件折叠绘制（`EditorGUILayout.Foldout`） | 🟡 必须 |
| 跳转目标选择 | 下拉框列出所有节点 ID（**不要手输字符串**） | 🟡 必须 |
| **数据校验** | 断链 / 空引用 / 不可达节点 → 红色警告框 | 🟡 必须 |
| **"从该节点预览"** | 把 `entryNodeId` 改为该节点，Play 时从此开始 | 🟢 强烈建议 |

**"数据校验"和"预览"是这套工具的展示价值所在**。策划工具的痛点从来不是"能不能编辑"，而是"配错了能不能立刻发现"。在作品集里，"我做了断链检测和节点级预览"比"我做了拖拽节点图"更能体现工程判断力 —— 前者解决真实问题，后者是炫技。

### 2.6 验收标准（DoD）—— 文档一条都没有

作品集项目尤其需要可判定的完成标准，否则"做完了"是个主观判断。建议为每条 MVP 功能补一行 DoD，例如：

| 功能 | 验收标准 |
|---|---|
| 气泡自适应 | 输入 200 字中文，气泡换行正确、不超面板宽度 70%、不截断 |
| 对象池 | 收发 500 条消息，Profiler 中 `Instantiate` 调用次数 ≤ 池容量 |
| 未读红点 | 收到消息红点出现；进入聊天后消失；退出重进状态正确 |
| 存档 | 收到消息 → 退出 → 重进，消息、当前节点、未读状态完全恢复 |
| 存档容错 | 手动把 JSON 改成非法字符，游戏能正常启动并重建存档 |

### 2.7 性能指标 —— 文档只有"防卡顿"，无量化目标

"使用对象池防卡顿"不是指标。建议补：

- 目标帧率：稳定 60 FPS（目标平台 PC）
- **单条消息渲染的 GC Alloc：池化后应为 0 B/条**（这是最能打的量化数据）
- 消息数上限：单会话 1000 条，滚动不卡顿
- 冷启动时间：≤ 3 秒进入可交互状态

**建议 Day 4 用 Profiler 抓"有池 vs 无池"的对比数据写进 README** —— 这是作品集里最有说服力的数字，因为它证明了优化确实有效，而不是"我按教程加了对象池"。

---

## 3. 🟡 需要改进/澄清的表述

| # | 位置 | 问题 | 修改建议 |
|---|---|---|---|
| 1 | 3.2.2 "气泡随内容长短自动拉伸" | **有经典实现坑，文档未提**。ScrollRect 下 `ContentSizeFitter` + `VerticalLayoutGroup` 会让长文本气泡**无限变宽**（TMP 的 `preferredWidth` 取的是不换行的整行宽度），因为父级不提供宽度约束 | 明确写：气泡 `ContentSizeFitter` **只勾 Vertical**，同时加 `LayoutElement.preferredWidth` 上限 clamp，或由代码在填充后设置 `maxWidth`。这是必须记录的决策 |
| 2 | 3.2.2 "双方方气泡" | 疑似笔误 | 改为"NPC 气泡（左对齐）/ 玩家气泡（右对齐）"，并注明用两个独立预制体实现 |
| 3 | 2. 整体 UI 布局 vs 3.1 | 右侧面板"浅灰白色背景"与左侧选中项"浅灰白色"**撞色** | 澄清配色层级：左侧深黑 + 选中态浅灰白；右侧用**更浅的近白色**区分。否则选中项和右侧面板视觉混淆 |
| 4 | 3.1 "头像支持网络/本地加载" | 对 Demo 是过度设计，且引入网络失败路径 | MVP 只做本地 `Sprite` 引用；保留 `IAvatarProvider` 接口但不实现网络版 |
| 5 | 3.2.1 "关闭按钮…退出游戏运行状态" | 未说明编辑器下的处理 | 写明需 `#if UNITY_EDITOR` 条件编译：`EditorApplication.isPlaying = false`，否则编辑器里点关闭没反应 |
| 6 | 3.2.3 底部选项区 | 未说明**选项数量不定**时的布局 | 补：选项按钮需动态生成 + 支持不定数量（**最终定为 1~3 个**），超出高度时整个区域可滚动 |
| 7 | 全文 | **完全没写多联系人切换的状态管理** | 必须补：切换联系人时——(a) 正在播放的延迟消息如何处理（建议挂起而非丢弃）、(b) 滚动位置是否记忆（建议记忆）、(c) 未读何时清零（建议进入即清零）、(d) 多个联系人同时有延迟消息时的调度顺序 |
| 8 | 4.1 "发送延迟" | 只配了延迟，**没定义延迟期间的 UI 表现** | 建议补"对方正在输入…"（typing indicator）气泡。成本极低但沉浸感提升明显，是这类系统的标配 |
| 9 | 全文 | 无错误处理与边界定义 | 补：空对话、选项节点无选项、断链、存档损坏、JSON 字段缺失 各类兜底行为 |
| 10 | 5.1 MVP 列表 | 缺"场景/UI 改造"这一项（见 1.2，实际是从零搭建） | 补充为独立任务项，避免低估工作量 |

---

## 4. 🟢 建议新增的功能（按性价比排序）

| 优先级 | 功能 | 成本 | 收益 |
|---|---|---|---|
| 🟢 高 | **Typing indicator（对方正在输入…）** | 2h | 沉浸感关键，配合已有的 delay 配置 |
| 🟢 高 | **自动滚底 + 玩家上滑时不打断** | 2h | 不做的话体验明显别扭 |
| 🟢 高 | **消息音效 + 红点出现动画** | 2h | 廉价的高级感，录屏效果提升明显 |
| 🟢 高 | **数据校验 + 节点预览**（编辑器） | 4h | 作品集差异化点，见 2.5 |
| 🟢 中 | 气泡淡入 / 逐条显示的动画 | 3h | 让录屏更"活" |
| 🟢 中 | 联系人列表按最后消息时间排序 + 未读优先 | 1h | 补上文档没写的排序规则 |
| 🟢 中 | 打字机效果（NPC 文本逐字显示） | 3h | 经典表现，但和聊天气泡不总是搭，可选 |
| ⚪ 低 | 图片消息完整支持 | 8h+ | **建议 MVP 砍掉**，放 1 张图证明即可 |
| ⚪ 低 | 网络头像加载 | 6h+ | **建议砍掉** |
| ⚪ 低 | 拖拽节点图（GraphView） | 16h+ | **明确砍掉**，4 天内做不完 |

---

## 5. 风险清单

| 等级 | 风险 | 影响 | 应对 |
|---|---|---|---|
| 🔴 高 | 中文字体未配置 | Demo 无法演示 | Day 1 第一件事解决，见 1.1 |
| 🔴 高 | 布局组件从零搭建，工期被低估 | 连锁延期 | Day 1 不排其他重活，见 1.2 |
| 🟡 中 | 气泡宽度自适应踩坑反复调 | 吃掉半天 | 直接采用 3.1 的既定方案，不要现场试 |
| 🟡 中 | 编辑器工具做成 GraphView | 吃掉 2 天 | 明确用 ReorderableList，见 2.5 |
| 🟡 中 | 存档写盘损坏 | Demo 翻车 | 原子写入 + try-catch 兜底，见 2.4 |
| 🟢 低 | URP 2D 模板与 UGUI 混用 | 一般无冲突 | 已确认场景可正常渲染，无需处理 |

---

## 6. 一句话总结

文档需要从「**功能描述**」升级为「**技术契约**」：补齐 **数据结构 / 存档 Schema / 分层约束 / 验收标准** 四类内容，同时修正与工程现状的三处落差（中文字体、布局组件、ScrollRect 配置）。
