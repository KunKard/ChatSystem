# 模拟手机聊天系统 —— 功能设计文档

| 项 | 值 |
|---|---|
| 文档版本 | v2.1 |
| 更新日期 | 2026-09-16 |
| 引擎 | Unity 2022.3.62f2c1 LTS |
| UI 系统 | UGUI + TextMeshPro 3.0.7 |
| 渲染管线 | URP 2D (14.0.12) |
| 目标平台 | PC (Windows) |
| 关联文档 | `Design-Review.md`（评审依据）、`Plan-4Days.md`（开发排期）、`Design.v1.bak.md`（v1 存档） |

> **v2.0 变更说明**：v1 为产品功能描述，缺少技术契约。v2 补齐了**架构分层、数据结构、存档 Schema、验收标准、性能指标**五类内容，修正了 10 处模糊或有坑的表述，并记录了工程现状与文档的落差。
>
> **v2.1 变更说明**：
>
> 1. **消息时间戳改由策划配置**，不再读取运行时时钟；未配置时间的消息不显示时间，也不参与分割线判断（影响 §4.1 / §4.2 / §5.2.4 / §7.1）。
> 2. **Typing Indicator 细化为两处联动**：顶部签名临时替换为"对方正在输入…" + 消息列表出现三点变色小气泡（影响 §5.2.5 / §6.1 / §12）。

---

## 1. 项目概述

- **项目定位**：复刻《崩坏：星穹铁道》《无限大》等游戏内手机聊天系统，包含玩家侧交互与策划侧数据编辑工具。
- **核心目标**：
  1. 实现沉浸式剧情对话体验；
  2. 提供策划无代码配置内容的可视化工作流；
  3. **作为作品集展示工程判断力**——量化性能数据、可测试的架构、可靠的数据校验工具。
- **非目标**（明确不做）：多人联网、账号系统、真实 IM 协议、拖拽式节点图编辑器。

---

## 2. 系统架构

### 2.1 分层设计

```text
┌─────────────────────────────────────────────────────────┐
│  Data 层（只被读取，不被修改）                            │
│  ConversationAsset (SO) │ ContactProfile (SO) │ 存档 JSON │
└────────────────────────┬────────────────────────────────┘
                         │ 加载 / 解析
┌────────────────────────▼────────────────────────────────┐
│  Runtime 层（纯 C#，无 MonoBehaviour，无 UnityEngine.UI） │
│  DialogueRunner（状态机） │ ChatSession（会话状态）        │
│  SaveService（序列化）    │ MessageFactory（分割线计算）   │
└────────────────────────┬────────────────────────────────┘
                         │ 事件通知（C# event / Action）
┌────────────────────────▼────────────────────────────────┐
│  View 层（MonoBehaviour，只做展示与输入）                 │
│  ContactListView │ ChatWindowView │ ReplyOptionsView      │
│  BubbleView │ BubblePool │ TypingIndicatorView            │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│  Unity 引擎层：UGUI + TMP + ScrollRect                   │
└─────────────────────────────────────────────────────────┘
```

### 2.2 架构约束（硬性）

| # | 约束 | 理由 |
|---|---|---|
| 1 | **Runtime 层禁止 `using UnityEngine.UI`** | UI 与逻辑解耦，逻辑可独立演进 |
| 2 | **Runtime 层禁止继承 `MonoBehaviour`** | 可在不进入 Play 模式下被单元测试（Unity Test Framework 已在 `manifest.json`） |
| 3 | Data 层是**只读**的，运行时状态一律存在 `ChatSession` 中 | 避免 Play 模式下污染 SO 资产 |
| 4 | View 层**不直接读写存档**，一律经由 Runtime 层 | 存档只有一个写入入口，降低损坏风险 |
| 5 | 事件用 `Action` 而非 `SendMessage` / 反射 | 可静态检查，无 GC |

> 约束 1、2 是这套架构的核心价值所在。它让"对话状态机有单测覆盖、存档序列化有往返测试"成为可能——这是作品集区别于教程作业的地方。

### 2.3 目录结构

```text
Assets/
├── Scripts/
│   ├── Data/             # ScriptableObject 定义
│   │   ├── ConversationAsset.cs
│   │   ├── ContactProfile.cs
│   │   └── Model/        # MessageData / DialogueNode / ChoiceOption
│   ├── Runtime/          # 纯 C#，无 MonoBehaviour
│   │   ├── DialogueRunner.cs
│   │   ├── ChatSession.cs
│   │   ├── SaveService.cs
│   │   └── MessageFactory.cs
│   ├── View/             # MonoBehaviour
│   │   ├── ContactListView.cs
│   │   ├── ChatWindowView.cs
│   │   ├── ReplyOptionsView.cs
│   │   ├── BubbleView.cs
│   │   └── BubblePool.cs
│   ├── Editor/           # 策划工具
│   │   ├── DialogueEditor.cs
│   │   └── DialogueValidator.cs
│   └── Tests/            # 单元测试
├── Prefabs/              # ChatBubble / MyChatBubble / ChatPartner
├── Fonts/                # 中文字体 + TMP Font Asset
├── Sprites/              # 头像 / 表情包 / UI 图集
├── ConversationData/     # ConversationAsset 实例
└── Scenes/SampleScene.unity
```

---

## 3. 整体 UI 布局

### 3.1 双面板布局

界面分为左右两个面板：

- **左侧面板（联系人列表，约占 30% 宽度）**：深黑色背景。
- **右侧面板（聊天窗口，约占 70% 宽度）**：近白色背景。

### 3.2 配色规范

| 用途 | 建议明度 | 说明 |
|---|---|---|
| 左侧面板背景 | 最深（`#1C1C1E`） | 深黑 |
| 左侧**选中项**背景 | 中深（`#3A3A3C`） | 在深黑底上形成可辨认的抬升 |
| 左侧普通项背景 | 同面板背景 | 仅 hover 时微亮 |
| 右侧面板背景 | 最浅（`#F2F2F7`） | 与左侧拉开最大对比 |
| NPC 气泡 | 白（`#FFFFFF`） | 在浅底上靠描边/阴影区分 |
| 玩家气泡 | 强调色（如 `#007AFF`） | 与 NPC 气泡形成左右+颜色双重区分 |

### 3.3 Canvas 配置

| 项 | 值 | 说明 |
|---|---|---|
| Render Mode | ScreenSpaceOverlay | 无需与 3D 物体交互 |
| Ui Scale Mode | ScaleWithScreenSize | |
| Reference Resolution | 1920 × 1080 | |
| Match Width Or Height | **0.5** | ⚠️ v1 隐含为 0（匹配宽度），超宽屏下 UI 会纵向过矮。改为 0.5 折中 |
| 聊天内容区最大宽度 | **900 px** | 超宽屏上限宽，避免单行过长影响阅读 |

---

## 4. 数据层设计

### 4.1 消息模型

```csharp
public enum MessageKind {
    Text,        // 纯文本，可内嵌 Emoji 富文本标签
    Sticker,     // 表情包（图片，固定尺寸）
    Image,       // 图片消息（保持宽高比，有最大宽高限制）
    TimeDivider  // 时间分割线（居中显示，text = 策划配置的显示文案）
}

[Serializable]
public class MessageData {
    public MessageKind kind;
    public string       senderId;      // 空字符串 = 玩家自己
    public string       text;          // Text / TimeDivider 用；Emoji 内嵌 <sprite name="...">
    public string       assetName;     // Sticker / Image 的资源名
}
```

> **🔴 v2.1：`MessageData` 不含时间字段。**
> 它是**运行时 + 存档模型**，只管"显示什么"。时间配置属于**策划配置**，挂在节点上（见 §4.2），完整规则见 §5.2.4。

**关键决策：Emoji 不单独建 `MessageKind`**

Emoji 作为 Text 内嵌的 TMP 富文本标签处理：

```text
明天见 <sprite name="smile"> <sprite name="heart">
```

理由：工程内已有 `Resources/Sprite Assets/EmojiOne.asset`，且 `TMP Settings` 中 `m_enableEmojiSupport: 1`，**开箱可用**。图文混排由 TMP 原生排版引擎处理，无需自行计算字符宽度与图片混排。

**关于图片消息**：完整支持（加载/缓存/比例适配/失败占位/点击预览）成本约 8h+，收益比差。**MVP 阶段仅保留 1 张图证明能力**，不做完整系统。

### 4.2 对话节点模型

```csharp
public enum NodeKind {
    Message,   // 发送一条消息
    Choice,    // 弹出玩家回复选项，等待选择
    Wait,      // 等待指定时长（用于制造节奏）
    End        // 对话结束
}

[Serializable]
public class DialogueNode {
    public string    id;                // 稳定唯一 ID，见下方决策
    public NodeKind  kind;
    public MessageData message;         // kind == Message 时有效
    public List<ChoiceOption> options;  // kind == Choice 时有效
    public float     delaySeconds;      // 发送延迟 / Wait 时长
    public string    nextId;            // 线性后继；Choice 节点为 null

    // ── 时间配置（策划填写，不写入存档）规则见 §5.2.4 ──
    public string    timeLabel;         // 留空 = 未配置时间：不显示，也不参与分割线判断
    public long      timeValueUtc;      // 仅用于比较相邻消息间隔；0 = 不参与比较
}

[Serializable]
public class ChoiceOption {
    public string text;                 // 按钮文案
    public string nextId;               // 跳转目标节点 ID
    public string timeLabel;            // 同 DialogueNode —— 玩家的回复消息也可带时间
    public long   timeValueUtc;
}
```

> **🔴 v2.1 决策：时间配置挂在节点上，不挂在 `MessageData` 上。**
> 因为时间的**来源是策划配置**，不是运行时快照。挂在节点上让"配置"与"运行时数据"界限清晰：节点是配置（只读），`MessageData` 是运行时产物（可存档）。
>
> **为什么需要 `timeLabel` + `timeValueUtc` 两个字段**：见 §5.2.4 —— 简单说，**文案无法比较大小，数值无法表达"昨天"**，两者缺一不可。

**🔴 关键决策：跳转必须用 ID，禁止用数组下标**

策划在编辑器里插入或删除节点时，数组下标会整体位移，而存档里记录的旧下标会**静默指向错误节点**——这是存档类系统的经典事故，且在测试中极难发现（只在"改了配置又读了旧档"时复现）。

因此：
- 节点用**全局唯一字符串 ID**；
- 运行时构建 `Dictionary<string, DialogueNode>` 做 O(1) 跳转；
- **存档只记录 `currentNodeId`，绝不记录下标**。

### 4.3 联系人与会话

```csharp
[CreateAssetMenu(menuName = "ChatSystem/Contact Profile")]
public class ContactProfile : ScriptableObject {
    public string id;             // 稳定 ID，存档键
    public string displayName;    // 备注名，如"知更鸟"
    public string signature;      // 个性签名
    public Sprite avatar;         // 本地引用（见下方决策）
    public string defaultPreview; // 无消息时的预览文案
}

[CreateAssetMenu(menuName = "ChatSystem/Conversation")]
public class ConversationAsset : ScriptableObject {
    public ContactProfile contact;
    public string entryNodeId;              // 入口节点
    public List<DialogueNode> nodes;        // 节点表

    // v2.2 修订：原来写成 [NonSerialized] public Dictionary<...> Lookup;
    // 改为私有缓存 + 访问器——公开可变字典会让调用方绕过 EnsureLookup()，
    // 拿着一个 null 或过期的表去查节点。
    [NonSerialized] private Dictionary<string, DialogueNode> _lookup;

    public int NodeCount { get; }
    public DialogueNode GetNode(string id);                       // 不存在返回 null
    public bool TryGetNode(string id, out DialogueNode node);
    private void EnsureLookup();                                  // 惰性构建，重复 ID 报错并保留首个
    private void OnValidate();                                    // 编辑器改动后置空缓存
}
```

**要点**：

- `_lookup` **惰性构建**，调用方不需要记得先初始化。
- 字典用 `StringComparer.Ordinal` —— 节点 ID 是程序标识符，不该受区域设置影响（土耳其语 `i/I` 大小写问题）。
- 重复 ID **报错并保留首个**，不静默覆盖：静默覆盖会让跳转指向"另一个"节点，是最难排查的一类 bug。
- `OnValidate()` 置空缓存，避免在 Inspector 里改完节点后仍查到旧表。

**决策：头像只做本地引用**

v1 写"支持网络/本地加载"。网络头像对 Demo 属于过度设计，且引入超时、失败占位、缓存失效等一整条失败路径。**MVP 只做本地 `Sprite` 引用**，但保留 `IAvatarProvider` 接口以便后续扩展：

```csharp
public interface IAvatarProvider {
    void GetAvatar(string contactId, Action<Sprite> onLoaded);
}
```

### 4.4 序列化方案选型

| 方案 | 优点 | 缺点 | 结论 |
|---|---|---|---|
| **`JsonUtility`** | Unity 内置、零依赖、快 | 不支持 `Dictionary`、不支持多态、不能序列化顶层数组 | ✅ **采用** |
| Newtonsoft.Json | 功能完整 | 需引包，增加体积 | ❌ MVP 不引入 |

采用 `JsonUtility` 的前提是 Schema 保持扁平 + 用 `List<>` 且**必须包在顶层对象里**（`JsonUtility` 无法序列化裸数组）。映射到 4.2 的设计：`List<DialogueNode>` 已包在 `ConversationAsset` 中，符合要求。

---

## 5. 玩家侧功能

### 5.1 左侧面板：联系人列表（Contact List）

- **标题栏**：顶部显示"新消息通知"。
- **列表项（`ScrollRect` + 对象池）**：
  - **头像**：圆形（`Image` + 圆形遮罩），本地加载。
  - **名字**：联系人备注名，如：知更鸟、吉尔伽美什。
  - **消息预览**：最近一条消息的摘要，**单行截断**（`TextMeshProUGUI.overflowMode = Ellipsis`）。表情包显示 `[表情]`，图片显示 `[图片]`。
  - **状态角标**：右侧小箭头（`>`）。
  - **未读红点**：头像右上角。**收到消息时带一次缩放动画**。
  - **选中状态**：当前正在聊天的联系人，背景色与普通项区分（见 §3.2）。
- **排序规则**（v1 缺失，v2 补充）：
  1. 有未读的优先；
  2. 同组内按最后一条消息时间倒序；
  3. 无消息的联系人排在最后，按配置顺序。

### 5.2 右侧面板：聊天窗口（Chat Window）

#### 5.2.1 顶部栏（Header）

- **联系人名字**：当前聊天的 NPC 名字。
- **签名 / 状态**：名字下方显示个性签名，如："让我们把翅膀借给彼此"。
- **关闭按钮**：右上角 `X`。

> ⚠️ **编辑器兼容**：v1 只写"退出游戏运行状态"。必须条件编译，否则在 Editor 里点关闭没反应：
> ```csharp
> #if UNITY_EDITOR
>     UnityEditor.EditorApplication.isPlaying = false;
> #else
>     Application.Quit();
> #endif
> ```

#### 5.2.2 聊天区域（Chat Content）

- **消息列表**：`ScrollRect`，**只开垂直滚动**（`m_Horizontal = 0`）。
- **两类气泡**（v1 原文"双方方气泡"为笔误）：
  - **NPC 气泡**：左对齐，`ChatBubble.prefab`
  - **玩家气泡**：右对齐，`MyChatBubble.prefab`
  - 用**两个独立预制体**实现，而非同一预制体翻转——布局逻辑更简单，已在工程中就位。
  - 结构：`Avatar` + `Name` + `TextBubble`（内含 `Text` / `Sticker`）。
- **内容类型**：纯文本、Emoji（图文混排）、表情包、图片。
- **对象池**：气泡实例全部走池，见 §9。

#### 5.2.3 气泡自适应实现方案（🔴 本项目最大的技术坑）

**问题**：`ContentSizeFitter` + 布局组的经典陷阱——在 `ScrollRect` 下，若气泡的 `ContentSizeFitter` 勾选了 **Horizontal = PreferredSize**，TMP 报告的 `preferredWidth` 是**文本不换行的整行宽度**，父级又不提供宽度约束，结果是长文本气泡会**无限变宽撑破面板，且永远不换行**。

**✅ 采用方案**：只让高度自适应，宽度用上限 clamp。

```text
ChatBubble (root)
├─ ContentSizeFitter: Vertical = PreferredSize, Horizontal = Unconstrained
├─ HorizontalLayoutGroup: childAlignment = MiddleLeft(NPC) / MiddleRight(玩家)
│                         childControlWidth = true, childControlHeight = true
│                         childForceExpandWidth = false
│
├─ Avatar
│    └─ LayoutElement: minWidth = maxWidth = 80, flexibleWidth = 0
│
└─ BubbleColumn
     ├─ VerticalLayoutGroup: childControlWidth = true, childForceExpandWidth = false
     ├─ Name (TMP)
     └─ TextBubble (Image，气泡底图)
          ├─ VerticalLayoutGroup + ContentSizeFitter: Vertical = PreferredSize
          ├─ ClampPreferredWidth: maxWidth = 面板宽度 × 70%
          │    （自定义 ILayoutElement，把 preferredWidth 钳到 maxWidth）
          └─ Text (TMP)
               └─ ContentSizeFitter: Vertical = PreferredSize, Horizontal = Unconstrained
```

**核心是 `ClampPreferredWidth` 这个小组件**——它实现 `ILayoutElement`，把子级的 `preferredWidth` 钳制在上限内。这是唯一能同时满足"短消息气泡窄、长消息气泡换行且不超宽"的声明式做法。

**备选方案**（不推荐但可行）：代码在填充文本后调用 `TMP_Text.GetPreferredValues()` 测量，再手动写 `LayoutElement.preferredWidth`。缺点是需要每帧/每次填充后触发重排，脆弱且易漏。

**其他约束**：
- 玩家手动上滑浏览历史时，`ScrollRect` 不应抖动。
- Emoji 与文字混排时，行高以最高元素为准（TMP 原生处理，无需干预）。

#### 5.2.4 时间线分割（v2.1 修订：时间由策划配置）

**🔴 时间来源决策（v2.1）**：消息时间戳**完全由策划配置**，**不读取运行时时钟**。

| 情况 | 行为 |
|---|---|
| 消息**配置了**时间（`timeLabel` 非空） | 参与分割线判断与显示 |
| 消息**未配置**时间（`timeLabel` 为空） | **不显示时间**，且**不参与**分割线判断 |

**分割线规则**（v2.2 补充首条与跳过的判定）：

- 与**最近一条配置了时间的消息**比较，`timeValueUtc` 间隔 **> 5 分钟**时插入居中分割线。
- 分割线文案取**后一条**（即当前这条）消息的 `timeLabel`。
- **未配置时间的消息自身不显示时间，也不参与比较** —— 它被完全跳过，
  比较跨越它、发生在前一条带时间的消息与当前消息之间。
- **会话中第一条带时间的消息也会插入分割线**（此时没有可比较的前者）。

> **为什么首条也要插**：气泡预制体（`ChatBubble` / `MyChatBubble`）里**没有时间文本节点**，
> 消息时间**只能通过分割线呈现**。若首条不插，策划为它配置的时间将永远不可见，
> "配置了就显示"这条规则也就落空了。

**判定实现**：抽成纯函数 `TimeDividerPolicy.ShouldInsert(lastValueUtc, currentValueUtc)`，
三种情况返回 `true` —— 首条带时间的消息（`lastValueUtc == 0`）、间隔严格大于 300 秒、
以及**时间倒退**（当前值小于前者；这是配置错误，插线让它在界面上暴露，好过静默吞掉）。

| 边界 | 行为 |
|---|---|
| 间隔恰好 300 秒 | **不插**（规则是"超过"，须严格大于） |
| `timeLabel` 有值但 `timeValueUtc` 为 0 | **不插**，并打 Warning。配置不完整，由 Day 3 的校验器另行报错 |
| 时间倒退 | **插**，暴露配置错误 |

**为什么需要 `timeLabel` + `timeValueUtc` 两个字段**：

| 字段 | 作用 | 为什么另一个替代不了 |
|---|---|---|
| `timeLabel`（文案） | 直接控制显示内容，如"昨天 21:30" | 数值无法表达"昨天"——本项目**没有运行时时钟作为参照系**，"昨天"相对谁而言无从推导 |
| `timeValueUtc`（数值） | 比较相邻消息间隔是否超阈值 | 纯文案无法比较大小，5 分钟规则需要可比较的数值 |

**存储方式**：

- 分割线作为一种 `MessageData`（`kind = TimeDivider`，`text` = 文案）**持久化存储**，在消息发出时计算一次。
- 理由：存档恢复时**无需重算**，也就无需重新读取策划配置的时间字段——**存档自包含**，不因策划后续改动配置而改变已产生的历史。

> 🔴 **v1 隐藏 Bug（v2.1 已解决）**：v1 未说明分割线的存储方式。若分割线仅由运行时按时间戳计算，则存档恢复重放历史时行为不一致。
> **v2 决策**：持久化存储，插入时计算一次。**v2.1 改为策划配置时间后这条决策依然成立且更必要**——因为时间不再来自运行时，存档必须带着已算好的结果。

#### 5.2.5 Typing Indicator（v2 新增，v2.1 细化为两处联动）

v1 的"发送延迟"只有配置项，没有定义延迟期间的 UI 表现。v2.1 明确定义为**两处联动**：

**① 顶部栏：签名临时替换**

- NPC 正在输入期间，名字下方的**个性签名临时替换为"对方正在输入…"**。
- 输入结束 → **恢复原签名**。
- 实现要点：原签名需缓存，且**按会话独立维护**（配合 §5.2.7 的多会话并行延迟）——切换到别的联系人再切回来时状态必须正确。

**② 消息列表：三点变色小气泡**

- 在消息列表底部（**NPC 侧、左对齐**）弹出一个小气泡，内容为 `...`。
- **三个点的颜色由浅变深循环变化**，表示消息正在加载中。
- 气泡尺寸固定且小，**不随内容拉伸**——与普通气泡的 `ContentSizeFitter` 逻辑区分开（它不需要 clamp 宽度，见 §5.2.3）。
- 输入结束 → 移除该气泡 → 插入真实消息。

**③ 联系人列表联动（低成本增强）**

非激活会话在输入时，左侧列表项的**消息预览也显示"对方正在输入…"**，与真实 IM 行为一致。复用同一份状态，成本几乎为零。

**⚠️ 动画实现（性能关键）**

- 三个点用**3 个独立 `Image` 组件**做颜色插值。
- **不要**用 TMP 文本每帧改写 `<color>` 富文本标签——那会每帧触发 TMP **网格重建**，是本项目最大的 CPU 尖峰与 GC 来源，直接违背 §10.2 的"单条消息 0 B GC"目标。
- 相位用 `Time.time` 驱动，**不用协程**（避免每帧分配 iterator，同时符合 §2.2 架构约束 2）。
- 气泡实例**复用气泡池**，不额外申请。

**状态持久化规则**

- typing 是**瞬时 UI 状态，不写入存档**。
- 玩家在 NPC 输入期间退出游戏时，**退出流程会 flush 待执行队列**（见 §7.2）——延迟中的消息直接落地并写入存档。重进时**不会看到卡住的"正在输入"**。

#### 5.2.6 自动滚底（v2 新增）

| 场景 | 行为 |
|---|---|
| 新消息到达，且玩家**已在底部** | 自动滚到底部 |
| 新消息到达，但玩家**正在上滑浏览历史** | **不打断**，改为在底部显示"N 条新消息"提示条 |
| 玩家手动滑回底部 | 提示条消失 |

判定方式：追加消息前检查 `scrollRect.verticalNormalizedPosition <= 0.05f`。滚动需 `Canvas.ForceUpdateCanvases()` 后再设置 `verticalNormalizedPosition = 0f`，否则布局尚未刷新，滚动位置会算错。

#### 5.2.7 多联系人状态管理（🔴 v1 完全缺失）

这是 v1 最严重的遗漏。补齐以下决策：

| 问题 | 决策 | 理由 |
|---|---|---|
| 切换联系人时，正在倒计时的延迟消息怎么办？ | **挂起而非丢弃**：每会话一个独立待执行队列，切回来继续倒计时 | 丢弃会导致剧情卡死 |
| 未读何时清零？ | **进入聊天即清零**（非"滚动到底"） | 符合主流 IM 直觉 |
| 滚动位置是否记忆？ | **按 `contactId` 记忆**，切回来恢复 | 多会话来回切换时体验流畅 |
| 多个联系人同时有延迟消息？ | **允许并行推进**，各自独立计时；但只有当前激活会话更新 UI，非激活会话静默入队并累加未读 | 符合真实 IM 的"后台收消息"直觉 |

### 5.3 底部栏：回复选项区（Reply Options）

- 当 `DialogueRunner` 进入 `Choice` 节点时，底部弹出选项面板。
- **预设选项按钮**：白色圆角按钮，例如："想要下次演出的票"、"我反复看演出录播"。
- **不定数量支持**（v1 未说明）：选项数 1~3 个均需布局正常；超出可用高度时整个区域可滚动。
- **点击反馈**：该内容作为玩家消息发送 → 面板消失 → 推进到目标节点 → 触发 NPC 后续对话。

---

## 6. 运行时逻辑

### 6.1 DialogueRunner 状态机

纯 C#，无 MonoBehaviour 依赖，可单测。

```csharp
public class DialogueRunner {
    // 事件
    public event Action<MessageData>  OnMessageEmitted;
    public event Action<IReadOnlyList<ChoiceOption>> OnChoicesPresented;
    public event Action<string, bool> OnTypingChanged;   // (contactId, 是否正在输入)
    public event Action               OnEnded;

    public void Start(string entryNodeId);
    public void Advance();          // 推进到 nextId
    public void Choose(int index);  // 选择选项
    public void Tick(float deltaTime); // 驱动 delaySeconds 倒计时

    public string CurrentNodeId { get; }
    public long   LastTimedValueUtc { get; }   // 时间比较基线，见下
    public void SeedTimeBaseline(long lastTimedValueUtc);  // 存档恢复用
    public void RestoreTo(string nodeId);                  // 存档恢复用
}
```

**实现说明（v2.2）**：

- `OnChoicesPresented` 传 **`IReadOnlyList`** 而非 `List`：这份数据属于 ScriptableObject，
  交给视图层一个可变列表，等于给了它改坏资产序列化数据的机会。
- `Tick` 的语义是**一帧只结算一个延迟**。一次传入很大的 `deltaTime` 不会把整段剧情瞬间播完，
  需要继续推进就再调一次。存档前的 flush 因此是一个循环，不是单次调用。
- `LastTimedValueUtc` 必须**随存档持久化**。分割线本身已经存进历史了，这个字段补的是
  "下一条新消息该和谁比时间" —— 没有它，重进游戏后第一条带时间的消息会被当成会话首条，
  多插一条分割线。详见 §7.1。

**为什么用 `Tick(deltaTime)` 而不是 `Coroutine`**：协程依赖 MonoBehaviour，会破坏架构约束 2，导致无法单测。用 `Tick` 让时间成为显式输入——测试时可以"快进 10 秒"而无需真实等待。

### 6.2 ChatSession

会话运行时状态，一个联系人一个实例：

```csharp
public class ChatSession {
    public readonly ConversationAsset Asset;
    public readonly DialogueRunner    Runner;      // 每会话一个，独立倒计时
    public readonly string            ContactId;
    public readonly List<MessageData> Messages;    // 已渲染的历史（含时间分割线）

    public int   UnreadCount;
    public float ScrollPosition;                   // 记忆的滚动位置
    public bool  IsActive;                         // 是否当前展示中的会话
    public bool  IsTyping { get; }

    public string CurrentNodeId      => Runner.CurrentNodeId;      // 转发，不另存一份
    public long   LastTimedValueUtc  => Runner.LastTimedValueUtc;  // 转发，存档次需写入
    public string PreviewText { get; }             // 联系人列表预览

    public void Tick(float deltaTime);
    public void Restore(IEnumerable<MessageData> history, string currentNodeId, long lastTimedValueUtc);
    public void MarkRead();
}
```

**实现说明（v2.2）**：

- **删掉了 `CurrentNodeId` 与 `PendingQueue` 两个字段。** v2.1 里 `CurrentNodeId` 在 `DialogueRunner`
  和 `ChatSession` 各存一份，两个来源必然漂移；`PendingQueue` 描述的延迟队列实际由 Runner 内部
  的 `_remainingDelay` 承载。现在会话只做转发，唯一事实来源在 Runner。
- **一个会话一个 `DialogueRunner`**，多联系人并行延迟（§5.2.7）就是各自 `Tick`、各自计时；
  切换联系人 = 停掉它的 UI 刷新，而不是停掉它的 Runner。
- `PreviewText` 的优先级：**对方正在输入 &gt; 最后一条消息 &gt; 联系人默认预览**。
  非激活会话也走同一份逻辑，所以后台输入时左侧列表项会同步显示"对方正在输入…"，
  成本几乎为零（§5.2.5 ③）。时间分割线会被跳过，预览不会显示成"21:30"。

---

## 7. 存档系统

### 7.1 JSON Schema

```json
{
  "saveVersion": 1,
  "savedAtUtc": 1758000000,
  "conversations": [
    {
      "contactId": "robin",
      "currentNodeId": "n_012",
      "lastTimedValueUtc": 1758000000,
      "unreadCount": 0,
      "scrollPosition": 0.0,
      "messages": [
        { "kind": 0, "senderId": "robin", "text": "原本是希望你能够享受…", "assetName": "" },
        { "kind": 3, "senderId": "",      "text": "昨天 21:30",           "assetName": "" },
        { "kind": 0, "senderId": "",      "text": "我反复看演出录播",      "assetName": "" }
      ]
    }
  ]
}
```

**路径**：`Application.persistentDataPath/save.json`

> **v2.1 变更**：
>
> - `messages` 中**不再包含 `timestampUtc`**。消息时间来自策划配置，不进入存档（见 §5.2.4）。
> - `kind = 3` 即 `TimeDivider`，作为**普通消息项持久化**，`text` 为策划配置的显示文案。
> - `savedAtUtc` 是**存档文件自身的元数据**（最后一次写盘时刻），与消息显示时间无关，勿混淆。
>
> **v2.2 变更**：
>
> - **新增 `lastTimedValueUtc`**（每个会话一个）。它是时间比较的基线：下一条新消息该和谁比。
>   分割线本身已存进 `messages`，这个字段补的是"接下去怎么判断"。缺了它，重进游戏后
>   第一条带时间的消息会被当成会话首条而**多插一条分割线**。
>   `0` 表示该会话尚无带时间的消息。
> - **修正玩家 `senderId`**：v2.1 的示例里写成了 `"player"`，与 §4.1 的约定
>   （**空字符串 = 玩家自己**）矛盾。以 §4.1 为准，示例已改为 `""`。
>   用一个魔法字符串当玩家标记还有个隐患：联系人 ID 若取作 `"player"` 就会撞车。

### 7.2 写入策略

| 项 | 决策 |
|---|---|
| 写入时机 | 收到消息后**延迟 1 秒合并写入**（防止连发消息时高频 IO）；退出时立即 flush |
| 退出时 flush 待执行队列 | 退出时把**延迟中的消息立即落地**并写入存档——避免重进后"正在输入"状态卡死或消息永久丢失（见 §5.2.5） |
| flush 的实现 | 循环调用 `DialogueRunner.Tick`（每次只结算一个延迟，见 §6.1），直到不再有延迟为止，再写入存档 |
| **原子写入** | 先写 `save.json.tmp` → 成功后替换 `save.json` |
| 版本号 | `saveVersion` 预留，解析时校验；版本不匹配走默认值而非抛异常 |

**⚠️ 原子写入的实现细节**：`File.Replace` 要求**目标文件必须已存在**，首次存档时会抛异常。正确写法：

```csharp
File.WriteAllText(tmpPath, json);
if (File.Exists(savePath))
    File.Replace(tmpPath, savePath, backupPath);  // 已有存档：替换 + 留备份
else
    File.Move(tmpPath, savePath);                 // 首次存档：直接移动
```

### 7.3 容错

**绝不允许因存档损坏导致游戏无法启动。**

```text
读取 save.json
  ├─ 文件不存在 ────────────→ 创建空存档，正常进入
  ├─ JSON 解析异常 ─────────→ 备份为 save.corrupt.{时间戳}.json
  │                           → 创建空存档 → 正常进入 → 提示玩家
  ├─ saveVersion 不匹配 ────→ 走默认值，忽略未知字段
  └─ 字段缺失 ──────────────→ JsonUtility 填默认值，不抛异常
```

---

## 8. 策划侧功能（编辑器工具）

> **形态决策**：v1 提到"复杂的拖拽节点图编辑器初期搁置"，但未给替代方案。**v2 明确：用 `ReorderableList`，不做 GraphView。**
> 理由：拖拽节点图至少 16h，会吃掉整个四天计划的一半；且其展示价值（"能拖拽"）低于同等时间的**数据校验 + 节点预览**（"配错了能立刻发现"）。

### 8.1 DialogueEditor（自定义 Inspector）

`[CustomEditor(typeof(ConversationAsset))]`，功能：

| 功能 | 实现 | 优先级 |
|---|---|---|
| 节点列表增删改排序 | `ReorderableList` | 必须 |
| 节点类型切换 | 条件折叠 (`Foldout`)，按 `NodeKind` 只显示相关字段 | 必须 |
| 跳转目标选择 | **下拉框列出所有节点 ID** | 必须 |
| 搜索/过滤节点 | 按 ID 或文案过滤 | 可选 |

> 🔴 **跳转目标禁止手输字符串**——手输字符串是断链的主要来源。用下拉框从节点表中选。

### 8.2 DialogueValidator（数据校验）

一键校验，问题以红色警告框列出，**明确指出是哪个节点**：

| 校验项 | 说明 |
|---|---|
| 断链 | `nextId` / `option.nextId` 指向不存在的节点 |
| 空引用 | `message` 为 null、`options` 为空、头像缺失 |
| 不可达节点 | 从 `entryNodeId` 出发无法到达的孤儿节点 |
| 无选项的 Choice | `kind == Choice` 但 `options.Count == 0`（会导致剧情卡死） |
| 入口缺失 | `entryNodeId` 为空或指向不存在的节点 |
| ID 重复 | 节点 ID 冲突 |

> **这是本工具的核心价值**。策划工具的痛点从来不是"能不能编辑"，而是"配错了能不能立刻发现"。

### 8.3 节点预览

编辑器内提供"**从该节点预览**"按钮：临时改写 `entryNodeId` 为该节点，进入 Play 后从此处开始，退出 Play 后恢复。让策划不必从头跑一遍剧情就能验证某个分支。

### 8.4 数据驱动机制

- 对话配置使用 `ScriptableObject` 资产化存储，**修改后无需改代码即可在游戏中预览**。
- 聊天记录使用**本地 JSON 存档**（与配置分离：配置只读，存档可写）。

---

## 9. 错误处理与边界（v1 完全缺失）

| 场景 | 期望行为 |
|---|---|
| 空对话（`nodes` 为空） | 显示空状态占位，不报错 |
| 断链 | 编辑器校验器红色报警；运行时降级为结束对话并打日志，**不崩溃** |
| Choice 无选项 | 同上，视为 `End` 处理 |
| 存档损坏 | 备份 + 重建，见 §7.3 |
| 资源缺失（头像/表情包） | 显示占位图，不抛异常 |
| 联系人列表为空 | 显示空状态提示 |
| 延迟期间玩家关闭界面 | 挂起队列，重开继续（见 §5.2.7） |

---

## 10. 性能设计

### 10.1 对象池

- **实现**：`UnityEngine.Pool.ObjectPool<T>`（Unity 2021+ 内置，无需第三方库）。
- **气泡池**：容量 30，按需扩容，**不缩容**（缩容会造成反复实例化的抖动）。
- **联系人项池**：容量 10。
- **复用流程**：`Spawn()` → 填充数据 → 入列；滚出视口 → `Release()` 回池并重置状态（文本、图片、激活态）。
- ⚠️ **回池必须重置**：未重置的复用实例会带着上一条消息的残留（文本、贴图），是对象池最常见的事故。

### 10.2 性能指标

| 指标 | 目标值 |
|---|---|
| 目标帧率 | 稳定 60 FPS |
| **单条消息渲染 GC Alloc** | **0 B**（池化后） |
| 单会话消息数上限 | 1000 条，滚动流畅 |
| 冷启动时间 | ≤ 3 秒进入可交互状态 |
| `Instantiate` 调用次数 | 收发 500 条消息后 ≤ 池容量 |

> **Day 4 用 Profiler 抓「有池 vs 无池」的 GC Alloc 对比数据写进 README。** 这是作品集里最有说服力的数字——它证明优化确实有效，而不是"我按教程加了对象池"。

---

## 11. MVP 范围与优先级

### 11.1 必须完成（四天冲刺目标）

| # | 功能 | 对应章节 |
|---|---|---|
| 1 | 双面板 UI 搭建 + 布局组件从零搭建 | §3 |
| 2 | **中文字体接入**（阻塞项） | 附录 A |
| 3 | 联系人列表：头像、名字、预览、未读红点、选中态、排序 | §5.1 |
| 4 | 聊天窗口：左右气泡自适应、Emoji/表情包展示 | §5.2 |
| 5 | 底部选项按钮：点击发送 + 触发 NPC 回复 | §5.3 |
| 6 | Typing indicator + 发送延迟 | §5.2.5 |
| 7 | 自动滚底 + 不打断上滑 | §5.2.6 |
| 8 | 多联系人状态管理 | §5.2.7 |
| 9 | 对象池 | §10.1 |
| 10 | 存档 + 原子写入 + 容错 | §7 |
| 11 | 编辑器工具 + 校验器 | §8 |
| 12 | 边界与错误处理 | §9 |

### 11.2 明确搁置

- 玩家手动输入文本框（完全依赖选项按钮推进剧情）
- 群聊系统、视频消息
- 拖拽节点图编辑器（GraphView）
- 网络头像加载
- 图片消息完整支持（仅保留 1 张证明）

### 11.3 🚫 不可砍（裁剪阶梯的最低线）

**存档容错、对象池、数据校验器、中文排版正确性**

理由：这四项是"工程判断力"的体现，也是作品集区别于教程作业的地方。功能少一个不致命，但存档会崩、中文排版错乱（缺字方块 / 标点顶行首）、没有校验工具，会直接暴露完成度问题。

> 注：v2.1 此处原为"中文换行规则"。v2.2 勘误后确认 TMP 自带禁则已正确（附录 A.2），
> 该项不再有工作量；但**排版正确性作为验收底线仍然不可砍**，风险点转移到了字体图集覆盖（附录 A.1）。

---

## 12. 验收标准（DoD）

> v1 无验收标准，导致"做完了"是主观判断。v2 为每条 MVP 功能补可判定的标准。

| 功能 | 验收标准 |
|---|---|
| 中文显示 | 中文**无方块**，中文标点换行位置正确 |
| 气泡自适应 | 输入 200 字中文，换行正确、宽度不超过面板 70%、不截断 |
| Emoji 混排 | Emoji 与文字在同一行内正确混排 |
| 滚动 | 滚动列表**无横向滚动** |
| 联系人列表 | 3 个联系人可自由切换，名字/签名/历史完全对应 |
| 未读红点 | 收到消息出现 → 进入聊天消失 → 切换再回来状态正确 |
| 选项交互 | 点击选项 → 玩家气泡立即出现 → typing 提示 → 延迟后 NPC 回复 |
| 选项布局 | 选项数 1 / 2 / 3 时布局均正常，不溢出 |
| Typing（顶部） | 输入期间个性签名变为"对方正在输入…"，结束后**恢复原签名**；切换联系人再切回时状态正确 |
| Typing（列表） | 输入期间 NPC 侧出现 `...` 小气泡，**三点颜色由浅变深循环变化**；结束后气泡消失、真实消息插入 |
| Typing（性能） | 三点动画运行期间 Profiler 中**无 GC Alloc**，TMP 无每帧网格重建 |
| 对象池 | 收发 500 条消息，Profiler 中 `Instantiate` ≤ 池容量 |
| 自动滚底 | 上滑浏览时新消息到达**不强制拉到底** |
| 存档 | 收到消息 → 退出 → 重进，消息、节点、未读完全恢复 |
| 时间分割线 | 配置了时间的消息按间隔 > 5 分钟插入分割线且文案正确；**未配置时间的消息不显示时间、也不触发分割线**；存档恢复后位置正确 |
| 存档容错 | 手动把 JSON 改成非法字符，游戏能正常启动并重建 |
| 编辑器校验 | 故意配一个断链，编辑器**红色警告明确指出节点** |
| 节点预览 | 点"从该节点预览" → Play 后从该节点开始 |
| 单元测试 | `DialogueRunner` 分支推进 + `SaveService` 序列化往返全绿 |
| 可复现性 | 他人按 README 能独立跑起来 |

---

## 13. 技术风险

| 等级 | 风险 | 影响 | 应对 |
|---|---|---|---|
| 🔴 高 | **中文字体未配置** | Demo 无法演示 | Day 1 第一件事，见附录 A |
| 🔴 高 | **布局组件从零搭建被低估** | 连锁延期 | 见附录 B；Day 1 不排其他重活 |
| 🟡 中 | 气泡宽度自适应反复调 | 吃掉半天 | 直接采用 §5.2.3 既定方案，不现场试验 |
| 🟡 中 | 编辑器工具做成 GraphView | 吃掉 2 天 | §8 已明令禁止 |
| 🟡 中 | 存档写盘损坏 | Demo 翻车 | §7.2 原子写入 + §7.3 兜底 |
| 🟢 低 | URP 2D 与 UGUI 冲突 | 一般无冲突 | 已确认场景可正常渲染 |

---

## 14. 开发排期

详见 `Plan-4Days.md`：

| 天数 | 主题 | 核心目标 |
|---|---|---|
| Day 1 | 地基与数据层 | 打通「数据 → 消息渲染」（含中文字体 + 布局改造） |
| Day 2 | 交互闭环 | 打通「选人 → 看历史 → 选回复 → 收回复」 |
| Day 3 | 持久化 + 策划工具 | 进度不丢 + 可视化配内容 |
| Day 4 | 打磨 + 作品集包装 | 可展示、可量化、可复现 |

**关键路径**：`字体接入 → 布局改造 → 气泡宽度 → 对象池 → 聊天窗口 → 多会话状态 → 存档恢复 → 性能数据/README`

---

## 附录 A：中文字体接入（✅ 已接入，剩 1 项待办）

**当前状态**：中文字体已接入并验证。

| 项 | 实际值 |
|---|---|
| 源字体 | `Assets/TextMesh Pro/Fonts/Genshin.ttf` |
| TMP 字体资源 | `Genshin SDF.asset`（guid `f8f38b0ba8bf4ef4d91831e7d8b27c62`） |
| `TMP Settings.m_defaultFontAsset` | `Genshin SDF` ✅ |
| `TMP Settings.m_fallbackFontAssets` | `[LiberationSans SDF]` ✅ |
| 图集 | 2048×2048，静态，已烘焙 **3609** 字形 |

### A.1 🔴 待办：图集模式改为 Dynamic

`Genshin SDF.asset` 当前 `m_AtlasPopulationMode: 0`（**Static**）且 `m_IsMultiAtlasTexturesEnabled: 0`。
静态图集只认得烘焙进去的 3609 个字形，**任何未烘焙的字符都会渲染成方块**。

实测**未**烘焙的字符（从 `m_CharacterTable` 直接读出）：

| 类别 | 缺失字符 |
|---|---|
| 汉字 | `伽`（"吉尔伽美什"） |
| 全角括号 | `（` `）` |
| 直角引号 | `「` `」` `『` `』` |
| 全角括号类 | `〈` `〉` `〔` `〕` |
| 全角其他 | `—` `·` `％` `＃` `＠` `‘` `’` |
| ASCII 部分 | `!` `$` `^` `_` `` ` `` |

**做法**（Unity 编辑器内，约 30 秒，无需重跑 Font Asset Creator）：

1. 选中 `Assets/TextMesh Pro/Fonts/Genshin SDF.asset`
2. Inspector 顶部 `Atlas Population Mode`：**Static → Dynamic**
3. 勾选 `Is Multi Atlas Texture Enabled`（运行时新增字形可溢出到新图集页，不会顶爆 2048×2048）
4. `Apply`

> ⚠️ `Clear Dynamic Data on Build` 保持**不勾** —— 勾了会在打包时清空运行时新增的字形。

改完后上表所有缺字都会在首次使用时按需生成，永久消除方块问题。

### A.2 ✅ 中文换行禁则：TMP 自带且已正确，无需改动

> **勘误（v2.2）**：v2.0 / v2.1 的本文档、`Design-Review.md` §1.1、`Plan-4Days.md` 任务 1.1
> 都要求"追加中文换行规则"并给出了映射 —— **那是错的，且两处都错**：TMP 随包发布的规则文件
> **已经包含完整且正确的中日文禁则**，而映射方向与原文所写**恰好相反**。

TMP 的文件命名极易读反，实际语义是：

| 文件 | 语义 | 已核对的实际内容 |
|---|---|---|
| `LineBreaking Leading Characters.txt` | 不可出现在**行尾** | `（ [ ｛ 〔 〈 《 「 『 【 〝 ‘ “ ｟ «` 等**开括号 / 前引号** |
| `LineBreaking Following Characters.txt` | 不可出现在**行首** | `） ] ｝ 〕 〉 》 」 』 】 〙 〗 〟 ’ ” ｠ »` 等**闭括号 / 后引号**，以及 `、 ％ , . : ; 。 ！ ？` |

即：中文的**行首禁则**（闭标点不能顶行首）由 `Following` 承担，**行尾禁则**（开标点不能挂行尾）由 `Leading` 承担。
两份文件均已齐备，**不需要追加任何字符**。

> 原文把闭标点 `，。！？；：、）》」』】` 写进了 `Leading`、把开标点 `（《「『【` 写进了 `Following`，
> 正好颠倒。若真按原文执行，会**制造**出禁则错误而不是修好它。

### A.3 ⚠️ 字体授权（作品集公开前需决策）

`Genshin.ttf` 是从《原神》客户端提取的**米哈游专有字体**，不在任何开源许可下。
本附录原定的取舍标准是"作品集会公开，注意授权"，并据此排除了微软雅黑（仅限 Windows 平台使用）。

**按同一标准，`Genshin.ttf` 属于同类风险且更明确** —— 它是他人产品的提取物，不是可再分发的字体。

替换成本极低：`Genshin SDF.asset` 由源 TTF 生成，换源后重新生成一次即可，**不影响任何代码与场景引用**
（场景引用的是 `Genshin SDF` 这个资源，而非 TTF）。视觉上最接近的合法替代是
**思源黑体 / Noto Sans CJK**（SIL OFL，可商用可再分发），字重与骨架高度接近。

---

## 附录 B：工程现状与改造项

> 记录文档与工程实际的落差，避免按"已有基础上微调"低估工作量。

| # | 项 | 现状 | 需做 |
|---|---|---|---|
| 1 | C# / asmdef | ✅ **17 个文件**（数据层 9 / 运行时 5 / 测试 3，含 3 个 asmdef） | Day 2 起写 View 层 |
| 2 | `VerticalLayoutGroup` | 场景 **0 个**、预制体 **0 个** | §5.2.3 方案从零搭建 |
| 3 | `ContentSizeFitter` | 场景 **0 个**、预制体 **0 个** | 同上 |
| 4 | `LayoutElement` | 场景 **0 个**、预制体 **0 个** | 同上 |
| 5 | `ScrollRect.m_Horizontal` | 两处均为 `1`（开启） | 改为 `0` |
| 6 | Canvas `MatchWidthOrHeight` | `0`（匹配宽度） | 改为 `0.5` |
| 7 | 中文字体 | ✅ 已接入 `Genshin SDF` | 图集改 Dynamic（附录 A.1） |
| 8 | 预制体结构 | `ChatBubble` / `MyChatBubble` / `ChatPartner` 命名与层级基本合理 | 可复用，需补布局组件 |
| 9 | 气泡内的时间节点 | **不存在** —— 两个气泡预制体只有 `Name` / `Text` 两个 TMP 节点 | 时间只能走分割线，见 §5.2.4 |
| 10 | 组件检测方法 | ⚠️ 必须先解析 GUID 再统计 | Unity 场景 YAML 用 `m_Script: {guid}` 引用组件，**按类型名 grep 永远返回 0** |

**结论**：当前进度 = **UI 视觉骨架手工摆放完成（绝对定位）；数据层与运行时状态机已完成并通过单测；布局系统 0%，View 层 0%**。

> 第 10 条是方法论提醒：早期评审时用"grep 组件名"统计场景组件，得到 0 个，
> 结论虽然碰巧正确，但**方法本身是无效的**。正确做法是从 UGUI 包缓存里解析出
> `VerticalLayoutGroup` 等组件的真实 GUID，再统计 GUID 出现次数。

---

## 附录 C：v1 → v2 修改对照

| # | v1 位置 | v1 问题 | v2 处理 |
|---|---|---|---|
| 1 | 3.2.2 | "ContentSizeFitter + VerticalLayoutGroup 自适应"，未提实现坑 | §5.2.3 给出完整方案 + 宽度 clamp |
| 2 | 3.2.2 | "双方方气泡"笔误 | §5.2.2 改为 NPC/玩家两类并说明对齐 |
| 3 | 2 vs 3.1 | 右侧面板背景与左侧选中态撞色 | §3.2 明确四级明度层级 |
| 4 | 3.1 | "头像支持网络/本地加载"过度设计 | §4.3 只做本地 + 预留接口 |
| 5 | 3.2.1 | 关闭按钮未说明编辑器处理 | §5.2.1 补条件编译 |
| 6 | 3.2.3 | 未说明选项数量不定的布局 | §5.3 补不定数量 + 溢出滚动（**v2.1 定为 1~3 个**） |
| 7 | 全文 | **无多联系人切换状态管理** | §5.2.7 补齐 4 项决策 |
| 8 | 4.1 | "发送延迟"无 UI 表现 | §5.2.5 补 Typing Indicator |
| 9 | 全文 | 无错误处理与边界 | §9 新增 |
| 10 | 3.2.2 | 时间分割线存储方式未定（隐藏 Bug） | §5.2.4 决策为持久化 |
| 11 | 4.1 | 跳转机制未定义 | §4.2 决策为 ID 而非下标 |
| 12 | 5.1 | 无验收标准 | §12 新增 |
| 13 | 5.1 | 无量化性能指标 | §10.2 新增 |
| 14 | 全文 | 无架构分层 | §2 新增 |
| 15 | 全文 | 无数据 Schema / 存档 Schema | §4 / §7 新增 |
| 16 | 3.1 | 无排序规则 | §5.1 新增 |
| 17 | 3.2.2 | 无自动滚底行为定义 | §5.2.6 新增 |
| 18 | 全文 | 未记录工程现状落差 | 附录 B 新增 |

### v2.0 → v2.1 修改对照

| # | 项 | v2.0 | v2.1 |
|---|---|---|---|
| 1 | 消息时间来源 | 运行时时钟 | **策划配置**，不读运行时时间 |
| 2 | 时间字段位置 | `MessageData.timestampUtc`（运行时模型） | `DialogueNode` / `ChoiceOption` 上的 `timeLabel` + `timeValueUtc`（策划配置，不进存档） |
| 3 | 未配置时间的消息 | 无定义 | **不显示时间，也不参与分割线判断** |
| 4 | 为什么两个时间字段 | 未说明 | §5.2.4 补充：文案无法比较大小，数值无法表达"昨天" |
| 5 | Typing Indicator | 单一"对方正在输入…"气泡 | **两处联动**：顶部签名临时替换 + 列表三点变色小气泡（+ 联系人列表预览联动） |
| 6 | Typing 动画实现 | 未指定 | 明确用 **3 个 `Image` 插值**，禁用 TMP 每帧富文本改写（性能） |
| 7 | 退出时行为 | 未定义 | **flush 待执行队列**，避免"正在输入"卡死 |
| 8 | 存档 Schema | `messages` 含 `timestampUtc` | 移除该字段；`TimeDivider` 作为普通消息项持久化 |
