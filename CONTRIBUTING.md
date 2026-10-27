# 参与开发

欢迎通过 Issue 反馈问题或提出改进建议。提交代码前，请先阅读 [README](README.md) 和 [资源来源与权利说明](THIRD_PARTY_NOTICES.md)。

## 开发环境

- Windows x64
- .NET 8 SDK
- 支持 C# 和 WPF 的编辑器（Visual Studio 2022 或命令行均可）

```powershell
dotnet restore .\YueXinCat.csproj
dotnet build .\YueXinCat.csproj -c Release
dotnet run --project .\YueXinCat.csproj
```

## 提交前验证

1. 确认 Release 构建通过，普通模式 GIF 正常播放。
2. 涉及计薪时，检查工作时段、午休、休息日、跨午夜和加班场景；使用虚构余额和月薪。
3. 涉及键盘时，检查按下、松开、快速输入，以及切换普通模式后的行为。
4. 确认托盘退出和设置保存仍可使用。
5. 不要提交真实 `state.json`、构建产物、密码、令牌、证书或本机私有路径。

Pull Request 请写清楚问题、改动和验证结果。当前没有完整的自动化测试套件；CI 只检查 Windows 构建和发布，不替代上述桌面交互验证。
