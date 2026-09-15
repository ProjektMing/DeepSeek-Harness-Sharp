# DeepSeek-Harness-Sharp

一个用 C# 重写的 DeepSeek Harness 实现,初衷是解决原 DeepSeek Harness 在高并发下性能减退和崩溃的问题(TUI 单进程内存占用约 20MB)。

## 运行

1. 配置文件:`~/.dsh/settings.yaml`(Linux)/`%USERPROFILE%\.dsh\settings.yaml`(Windows);首次运行会生成带注释的模板,插件开关与参数写在 `plugins:` 段。
2. 构建与运行:
   ```bash
   dotnet build
   # 默认入口: 交互式 TUI
   dotnet run --project DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj 
   dotnet run --project DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj --gpu # 使用GPU加速渲染
   # 无头单次任务
   dotnet run --project DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj -- headless "跑一遍测试"
   # TUI 的会话管理(PTY 守护进程按需自动拉起,无需手动启动)
   dotnet run --project DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj -- tui list   # 列出所有会话id
   dotnet run --project DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj -- tui attach <id>  #进入某个会话
   # Web 观察页(启动后打印 http://127.0.0.1:<port>)与 LSP 服务(stdio)
   dotnet run --project DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj -- web
   dotnet run --project DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj -- lsp
   ```
3. 插件运维:TUI 内用 `/plugins list|add <包|dll 路径>|remove <包> [--force]|disable <包>|enable <包>`;改动写回 `settings.yaml` 的 `plugins:` 段并保留注释。
4. 日志:`<home>/logs/dsh-YYYYMMDD.log`(默认开启);`logging:` 段可配置级别、内存缓冲、是否输出到控制台(TUI 下强制关闭)。
5. 后台会话:TUI 内 `/detach [命令]`(Ctrl+X D)把命令交给 PTY 守护进程托管;`tui list`/`tui attach` 会自动拉起守护进程。
6. 插件目录:把插件放进 `<安装目录>/plugins/`,启动时自动加载并启用(可用 `/plugins disable <包>` 关闭)。托管插件是构建时引入 `Dsh.Plugins.Generator` 的 dll(生成清单提供入口),仅在默认 JIT 发布档可用;原生插件是 `.so`/`.dylib`/`.dll` 共享库(实现 `IDshNativePlugin` 声明包名与工具,导出握手与 ABI 胶水由生成器产出),JIT 与 AOT 发布档均可用,示例见 `tests/Dsh.NativePluginSample`。

## 运行构建产物

`dotnet build` 的产物在 `DeepSeek-Harness-Sharp/bin/<Debug|Release>/net10.0/`,`dotnet publish` 的产物在其 `publish/` 子目录。

```bash
# Linux/macOS:直接运行构建产物
DeepSeek-Harness-Sharp/bin/Debug/net10.0/DeepSeek-Harness-Sharp              # 交互式 TUI
DeepSeek-Harness-Sharp/bin/Debug/net10.0/DeepSeek-Harness-Sharp --gpu       # GPU 渲染
DeepSeek-Harness-Sharp/bin/Debug/net10.0/DeepSeek-Harness-Sharp headless "跑一遍测试"
DeepSeek-Harness-Sharp/bin/Debug/net10.0/DeepSeek-Harness-Sharp tui list

# 或将 DLL 交给本机 dotnet 运行
dotnet DeepSeek-Harness-Sharp/bin/Debug/net10.0/DeepSeek-Harness-Sharp.dll tui

# Windows
DeepSeek-Harness-Sharp\bin\Debug\net10.0\DeepSeek-Harness-Sharp.exe tui
```

用 `--home <目录>` 或 `DSH_HOME` 可以指定独立的配置/会话目录,便于用不同配置试跑构建产物。

## 发布形态

- 默认 Release:JIT + 裁剪 + 自包含。
  ```bash
  dotnet publish DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj -c Release -r linux-x64
  ```
  支持运行时插件装载:`<安装目录>/plugins/` 启动加载,以及 `/plugins add <dll>` 热装载(可回收 AssemblyLoadContext,移除后校验回收)。
- NativeAOT(可选档):
  ```bash
  dotnet publish DeepSeek-Harness-Sharp/DeepSeek-Harness-Sharp.csproj -c Release -r linux-x64 -p:PublishAot=true
  ```
  - 启动更快、体积更小,覆盖镜像内的全部插件与 LLM 适配器;
  - 原生插件可在启动时从 `<安装目录>/plugins/` 加载并注册工具(ABI v1:日志与工具;插件需自带 NativeAOT 编译的共享库);
  - NativeAOT 不支持在运行期装载托管 dll:`plugins/` 中的托管插件启动时逐文件 WARN 并跳过(被跳过的插件不会出现在 `/plugins list`),其余插件照常启用、程序正常启动;要使用托管插件就把它编译进镜像;
  - 会话持久化(JSONL + Zstd)在 JIT 与 AOT 下均可用。

## settings.yaml 结构

配置文件为 `~/.dsh/settings.yaml`;首次运行按 `src/Dsh.Boot/Profiles/Templates/settings.yaml` 生成带注释的模板(可用 `--home <path>` 或 `$DSH_HOME` 指定其它 harness home 做隔离测试)。
`/plugins` 命令写回 `plugins:` 段时采用文本级修改(保留注释)。

```yaml
# 全局默认模型 / 压缩模型 / 子代理默认模型,格式均为 provider/model
global_default_model: deepseek-official/deepseek-v4-flash
compaction_model: deepseek-official/deepseek-v4-flash
subagent:
  default_model: deepseek-official/deepseek-v4-flash

# LLM 提供方:options 为连接参数,models 为该提供方可用模型
# type 缺省即 openai-compatible;DeepSeek 专属行为(缓存 token 统计等)需显式 type: deepseek
# 适配器由插件提供(dsh-llm-openai / dsh-llm-anthropic / dsh-llm-deepseek),可禁用或换用第三方插件;
# 未配置 key 的 provider 会被跳过并记 WARN;完全不配 provider 也能启动,首次发起请求时才报错
providers:
  deepseek-official:
    type: openai-compatible
    options:
      baseUrl: https://api.deepseek.com
      apiKeyEnv: DEEPSEEK_API_KEY
    models:
      deepseek-v4-flash: { name: DeepSeek V4 Flash, reasoning: true, tool_call: true }
      deepseek-v4-pro:   { name: DeepSeek V4 Pro,   reasoning: true, tool_call: true }

skills:                 # 技能目录与 URL
  paths: []
  urls: []
rules: []               # 全局规则
plugins: {}             # 插件开关,见下
mcp: {}                 # MCP 服务器:transport: stdio|sse|streamable-http,配 command/args/url/enabled
memory:                 # 项目记忆(/memory on|off;file 或 mongo 后端)
  enabled: false
  # backend: mongo      # 可选:mongo 时用数据库存整段 markdown
  # mongo: { connectionString: mongodb://localhost:27017, database: dsh_memory, collection: memory, key: project }
compaction:             # 自动压缩
  auto: true
  prune: true
checkpoints:            # shadow git 恢复点
  enabled: false
  max_points: 256
  keep_days: 15
logging:                # 日志:trace|debug|info|warn|error|off
  level: info
  buffer_size: 1000
  console: false
  file: true
  file_max_mb: 32
  keep_days: 15
safety:
  autoApprove: false
  blacklist: []
```

- `plugins:` 支持两种写法:`"@deepseek-ai/dsh-tool-todo": false`(禁用),或带参数的 `"@deepseek-ai/dsh-tool-todo": { enabled: true, ... }`(保存插件参数);插件被发现即启用,这里的禁用项与 `/plugins` 命令是仅有的两个开关来源。
- 运行期数据:`<home>/logs/dsh-YYYYMMDD.log`(按天 + `file_max_mb` 切分、`keep_days` 清理),`<home>/sessions/<工作目录转写>/<会话 id>/session.jsonl.zstd`,`<home>/telemetry.jsonl`(agent 生命周期与错误事件,可用 `plugins: {"@deepseek-ai/dsh-telemetry": false}` 关闭)。
- 会话检索:TUI/CLI 内 `/sessions <关键词>` 搜索历史会话(命中用 `/session <id>` 打开);模型侧对应 `session_search` 工具。检索覆盖内存中的活会话与 `sessions/` 下的历史日志。
