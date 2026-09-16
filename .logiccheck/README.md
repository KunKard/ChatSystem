# .logiccheck —— 在 Unity 之外验证运行时逻辑

这个目录**不属于 Unity 工程**：以点号开头，Unity 会整个忽略它（且它不在 `Assets/` 下）。

用途：`ChatSystem.Data` / `ChatSystem.Runtime` 两层是纯 C#，没必要为了跑一遍状态机而启动 Unity。
这里用一份最小的 `UnityEngine` 桩（`ScriptableObject` / `Debug` / 两个特性）把它们编译起来，
再跑一组断言。

```bash
cd .logiccheck
dotnet run
```

关键点是 `.logiccheck.csproj` 里用 `Compile Include` 直接引入
`../Assets/Scripts/Data/**` 与 `../Assets/Scripts/Runtime/**` 的**真实源码**，
而不是拷贝一份 —— 验的东西和跑的东西必须是同一份，否则这个检查没有意义。

## 它能做什么

- 不开 Unity 就能验证状态机行为（推进、延迟、选项、时间分割线、存档恢复、会话未读）
- 抓编译错误。已经抓到过一个：`out` 参数被 lambda 捕获（CS1628），
  当时 `Assets/Scripts/Tests/DialogueRunnerTests.cs` 里有一模一样的写法

## 它不能做什么

- 不替代 Unity Test Runner。正式的用例在 `Assets/Scripts/Tests/`，
  那里能验 `ScriptableObject` 生命周期、资源引用、以及 View 层。
- 桩不是 Unity。任何依赖真实序列化、生命周期回调、渲染的行为都验不了。

## 什么时候可以删

随时。它不参与构建，也不影响出包。删掉只是失去了"不开 Unity 查逻辑"这个便利。
