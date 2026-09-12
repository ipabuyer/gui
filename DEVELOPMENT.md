# IPAbuyer 开发指南

本文是 IPAbuyer 的详细开发指南。[AGENTS.md](./AGENTS.md) 是基准文件，不允许修改；本文为详细开发内容，鼓励修改以同步最新开发进度。

## 目录

1. [项目概述](#1-项目概述)
2. [通用约束](#2-通用约束)
3. [技术栈与解决方案结构](#3-技术栈与解决方案结构)
4. [构建与调试](#4-构建与调试)
5. [发布与版本管理](#5-发布与版本管理)
6. [内置 ipatool 可执行文件](#6-内置-ipatool-可执行文件)
7. [ipatool 命令参考](#7-ipatool-命令参考)
8. [账户页与登录流程](#8-账户页与登录流程)
9. [加密密钥（keychain-passphrase）处理](#9-加密密钥keychain-passphrase处理)
10. [数据库](#10-数据库)
11. [UI 总体规范](#11-ui-总体规范)
12. [主页（MainPage）](#12-主页mainpage)
13. [购买状态](#13-购买状态)
14. [ipatool 页（IpatoolPage）](#14-ipatool-页ipatoolpage)
15. [日志系统](#15-日志系统)
16. [设置页（Settings）](#16-设置页settings)
17. [搜索功能](#17-搜索功能)
18. [App 处理与下载队列](#18-app-处理与下载队列)
19. [本地化](#19-本地化)
20. [测试](#20-测试)
21. [参考链接](#21-参考链接)

## 1. 项目概述

IPAbuyer 是一款 WinUI 3 桌面应用，帮助用户浏览、购买（仅限免费 App）并下载 App Store 中的 App，最终发布至 Microsoft Store。

- 底层工具：[majd/ipatool](https://github.com/majd/ipatool)，本项目通过调用 `ipatool.exe` 完成认证、购买与下载。
- 代码仓库：<https://github.com/ipabuyer/ipabuyer>
- 开发者网站：<https://www.blazesnow.com/ipa/>
- 更新日志：[CHANGELOG.md](./CHANGELOG.md)

## 2. 通用约束

1. 所有文件均以 UTF-8 格式存储、读取和修改。
2. 软件文本使用 `.resw` 存储并引用，禁止硬编码；资源文件见[本地化](#19-本地化)。
3. 允许并鼓励自行编译与运行单元测试验证改动（见[测试](#20-测试)）；完整 packaged 运行验证由用户在 Visual Studio 中进行。
4. 在终端执行命令时注意 GBK 与 UTF-8 编码差异（PowerShell/cmd 默认 GBK 代码页），避免中文输出与脚本执行乱码。
5. [AGENTS.md](./AGENTS.md) 是基准文件，不允许修改；本文鼓励修改，应随开发进度及时更新以保持同步。

## 3. 技术栈与解决方案结构

- UI 框架：WinUI 3（Windows App SDK）。
- UI 控件尽可能采用 CommunityToolkit.WinUI 包内容（如 `SettingsCard`），避免自绘。
- 解决方案文件：`IPAbuyer.slnx`。
- 目标框架：`net10.0-windows10.0.26100.0`，最低支持 Windows 10 17763；平台为 `x64` 与 `ARM64`。
- 应用以 MSIX 打包（`WindowsPackageType=MSIX`），包标识为 `IPAbuyer.IPAbuyer_kr1hdvrv6tpd0`。

| 项目 | 职责 |
| --- | --- |
| `IPAbuyer`（`IPAbuyer.csproj`） | 主应用入口（`App.xaml`），打包与发布配置集中于此；全部 UI 同在本工程（源码位于 `IPAbuyer.Pages/` 目录，保留 `IPAbuyer.Pages` 命名空间）：`MainWindow`、`MainPage`（主页）、`LoginPage`（账户）、`IpatoolPage`、`Settings`（设置）、`LogViewerWindow`（日志窗口） |
| `IPAbuyer.Core` | 业务门面：配置、状态、日志存储与对 Rust core 的 FFI 调用封装（数据库、ipatool 集成、搜索、购买、同步、下载队列） |
| `IPAbuyer.Tests` | xUnit 单元测试，仅引用 `IPAbuyer.Core` |

关键目录：

| 目录 | 内容 |
| --- | --- |
| `Assets/` | 应用图标与商店素材 |
| `Include/` | 内置 `ipatool.exe`、Rust core DLL（`ipabuyer-core-windows-*.dll`）与 `get-ipatool-release.ps1`、`get-core-release.ps1` |
| `Strings/` | `zh-Hans` 与 `en-US` 的 `Resources.resw` |
| `Scripts/` | `Verify-LocalizationResources.ps1` 本地化校验脚本 |
| `ILLink.Descriptors.xml` | 发布 trim 的根描述文件 |

### Rust Core（ipabuyer_core.dll）

1. 核心业务（数据库、ipatool 进程编排与命令解析、搜索、购买、已购买同步、下载队列）由独立 Rust 仓库 [IPAbuyer.Core](https://github.com/ipabuyer/IPAbuyer.Core) 实现，编译为 C ABI 动态库 `ipabuyer_core.dll`；导出清单与 JSON 契约见该仓库 DEVELOPMENT.md 第 5 节。
2. C# 侧封装位于 `IPAbuyer.Core/Native/`：`CoreNative`（P/Invoke + 状态码/last_error 约定）、`CoreDtos`（JSON 契约 DTO 与源生成序列化上下文，发布 full trim 下不使用反射序列化）、`CoreMessages`（键名/原文消息渲染）。
3. 服务类（`PurchasedAppDb`、`LoginService`、`AppCatalogService`、`PurchaseService`、`PurchaseSyncService`、`DownloadQueueService`、`IpatoolClient`）保留原有公共 API 作为门面，UI 层不直接触达 FFI。
4. 长任务（同步、下载队列）采用 Core 侧轮询句柄：C# 侧 `Task.Run` 包装阻塞调用并以 200ms 间隔轮询 `*_status`，取消经 `*_cancel`（队列取消为队列级：终止当前下载并结束本轮）。
5. DLL 不入 git：本地测试版构建后复制到 `Include/ipabuyer-core-windows-<arch>.dll`（已被 `.gitignore` 排除）；正式版由 Core 仓库打 `v*` 标签经 GitHub Actions 发布，运行 `Include/get-core-release.ps1`（可带 `-Version x.y.z`）下载并校验后写入 `Include/`，文件名不含版本号，避免每次更新改 csproj；文件缺失时对应平台的打包产物会缺少该 DLL。

## 4. 构建与调试

1. 本地测试使用 Visual Studio packaged 模式；本项目不考虑未打包运行状态，也不实现、不保留未打包回退逻辑。
2. 单元测试可通过 `dotnet test --project IPAbuyer.Tests/IPAbuyer.Tests.csproj -p:Platform=x64` 自行构建运行（测试项目仅引用 `IPAbuyer.Core`，无需打包）；`global.json` 已声明 `Microsoft.Testing.Platform` 测试运行器，适配 .NET 10 SDK 的 dotnet test MTP 模式。
3. Rust core 的构建、测试与发版流程见 Core 仓库（`cargo test` / `tag.ps1`）；主工程编译要求 `Include/ipabuyer-core-windows-*.dll` 已就位（对应平台缺失时打包会缺少该 DLL）。

## 5. 发布与版本管理

1. 最终发布至 Microsoft Store。
2. 发布配置集中在 `IPAbuyer.csproj` 中维护，不使用独立 `.pubxml` 作为主要发布配置来源。
3. 每次更新内置 `ipatool` 版本、发布配置、发布包版本或准备发布前，需要提示用户确认上游正式版是否发生变化。
4. 发布 trim 使用 `ILLink.Descriptors.xml`，并在 `IPAbuyer.csproj` 中通过 `TrimmerRootDescriptor` 引用；当前发布选项为 `PublishTrimmed=true`、`TrimMode=full`、`SelfContained=true`。
5. 发布包为 x64 + ARM64 的 Appx bundle（`AppxBundlePlatforms=x64|arm64`）。
6. 发布包版本维护在 `Package.appxmanifest` 的 `Identity/@Version`（当前 `2026.8.1.0`），发版流程：
   1. 更新版本号并补充 [CHANGELOG.md](./CHANGELOG.md)。
   2. 运行 `tag.ps1`，确认后按清单版本创建并推送 `v*` 标签，触发 GitHub Release 工作流（`.github/workflows/version.yml`）。

### 更新内置 ipatool 版本

1. 运行 `Include/get-ipatool-release.ps1`（可带 `-Version x.y.z`）获取并校验上游最新正式版：脚本会下载 `amd64` 与 `arm64` 两个 tar.gz、校验 SHA-256、验证 PE 文件头后写入 `Include/`。
2. 更新后需同步三处：
   1. `IPAbuyer.csproj` 中两个 `Content Include="Include\ipatool-*.exe"` 的文件名。
   2. `IPAbuyer.Core/Integration/Ipatool/IpatoolPathResolver.cs` 中的内置文件名与 ipatool 页展示版本。
   3. 本文档中记录的版本号。

## 6. 内置 ipatool 可执行文件

1. 内置 `ipatool.exe` 位于 `Include` 目录，注意区分 `amd64` 和 `arm64`。
2. 当前内置版本为上游正式版 `2.5.0`，文件名为 `ipatool-2.5.0-windows-*.exe`，打包后映射为 `ipatool.exe`（`TargetPath`），用于所有内置 `ipatool` 命令。
3. `Include/get-ipatool-release.ps1` 可获取并校验上游最新正式版，流程见[上文](#更新内置-ipatool-版本)。
4. 用户可通过设置页（ipatool 页）配置并选择自定义 `ipatool.exe`；未选择或路径失效时使用内置版本。
5. 针对 `ipatool` 输出的内容，需要在命令中加入 `--format json`；详细日志开启时记录命令和输出。
6. 所有 ipatool 命令的组装与执行在 Rust core（`ipabuyer_core_*` 导出）；`IpatoolPathResolver` 只负责解析可执行文件路径（优先当前选择的来源：内置或自定义，自定义路径失效时回退到内置正式版），并经 FFI 传给 Core；`IpatoolClient` 保留认证查询/登出与详细日志事件门面。

## 7. ipatool 命令参考

| 用途 | 命令模板 |
| --- | --- |
| 登录 | `ipatool.exe auth login --auth-code 双重验证码 --email 邮箱 --password 密码 --keychain-passphrase 加密密钥` |
| 查询登录状态 | `ipatool.exe auth info --keychain-passphrase 加密密钥` |
| 退出登录 | `ipatool.exe auth revoke` |
| 列出已拥有 | `ipatool.exe list-purchases --max-results 每页数量(≤100) --page 页码 --keychain-passphrase 加密密钥 --format json --non-interactive --verbose` |
| 购买 | `ipatool.exe purchase --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose` |
| 下载 | `ipatool.exe download --output 输出位置 --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose` |

## 8. 账户页与登录流程

### 界面

1. 含四个输入框和按钮。
2. 输入框：账户、密码、双重验证码、加密密钥，分别对应 `email`、`password`、`auth-code`、`keychain-passphrase`。
3. 按钮：“查询登录状态”、“登录”、“退出登录”、“打开苹果账户官网”、“日志”，按钮大小保持统一。

### 登录

1. 对 App 进行购买和下载，需要用户登录苹果账户。
2. 登录命令见[命令参考](#7-ipatool-命令参考)：
   1. 可以先通过只传入邮箱、密码和 `000000`（双重验证码）的方式，让苹果向用户发送双重验证码，然后再让用户输入真实的双重验证码。
   2. 对于收不到双重验证码的情况，提示用户打开 <https://account.apple.com/>，即苹果账户官网，输入用户名和密码后获取双重验证码，然后填入本软件。
   3. 需要提示用户：新创建的苹果账号不能直接用于购买和下载，必须先在苹果设备上登录过一次 App Store 并完成一次 App 购买。
3. 查询登录状态：
   1. 打开软件时，自动静默执行查询登录状态命令。
   2. 如果是登录状态，则为输入区添加一个锁定浅灰色蒙版，只允许退出登录按钮和查询登录状态按钮。
   3. 如果是登录状态，则将 `ipatool` 返回的 `email` 即用户邮箱写入输入框。
4. 退出登录命令：`ipatool.exe auth revoke`。
5. 为了测试用途，准备用户名 `test` 和密码 `test` 的账户，该账户购买或下载任何 App 都直接成功，用于界面测试。

## 9. 加密密钥（keychain-passphrase）处理

1. 加密密钥即 `keychain-passphrase`，显示于账户页输入框，供用户查看和复制。
2. 账户页初始化时读取当前保存的加密密钥；如果不存在则自动生成一个 UUID 格式的新密钥并写入输入框。
3. 加密密钥主要存储在 Windows PasswordVault 中，资源名为 `IPAbuyer.ipatool.passphrase`，用户键为 `__default__`（实现位于 `IPAbuyer.Core/Configuration/PassphraseStore.cs`）。
4. `passphrase.txt` 仅作为 legacy 迁移或 PasswordVault 不可用时的兜底文件；当成功迁移或成功写入 PasswordVault 后，需要删除 legacy `passphrase.txt`。
5. 登录成功后保存当前输入框中的加密密钥；查询登录状态时优先使用输入框中的值，输入为空时再使用已保存的值。
6. 购买和下载等需要 `--keychain-passphrase` 的命令不从账户输入框直接读取，而是通过 `KeychainConfig.GetPassphrase(null)` 获取当前保存的值。
7. 如果用户需要修改加密密钥，提示其退出登录，编辑输入框中的新密钥后重新登录。
8. 退出登录成功后，如果设置项 `KeychainPassphraseRotationEnabled` 为 `true`，自动生成新的 UUID 密钥并更新输入框；如果为 `false`，保留当前密钥。

## 10. 数据库

1. `PurchasedAppDb.db` 文件存放已购买 App 与购买 App 的邮箱地址，状态统一为“已购买”（原“已拥有”已合并，数据库迁移 `user_version` 2 归并旧数据）。
2. 另有 `SyncState` 表记录账户级同步状态（`LastSuccessSyncUtc`、`LastAttemptSyncUtc`），供 `PurchaseSyncService` 判定自动同步阈值。
3. 数据库文件目录：
   1. 使用 packaged 应用 LocalState 路径：`%AppData%\Local\Packages\IPAbuyer.IPAbuyer_kr1hdvrv6tpd0\LocalState\`。
   2. 通过 Windows API（`ApplicationData.Current.LocalFolder`）获取上述路径。
   3. 不需要实现或保留未打包运行状态的本地目录回退逻辑。
4. 实现位于 `IPAbuyer.Core/Data/PurchasedApps/`（`Database.cs` 解析路径、`PurchasedAppDb.cs` 门面）；存储与迁移由 Rust core（rusqlite）承担，C# 侧以全局锁串行化句柄访问。

## 11. UI 总体规范

1. 适配系统明暗模式并自动切换。
2. 使用侧边栏定位页面，侧边栏有 icon 并可折叠，折叠按钮位于标题栏上。
3. 共 4 个导航页面：主页、账户、ipatool、设置（对应 `MainPage`、`LoginPage`、`IpatoolPage`、`Settings`）。
4. 主窗口标题栏使用 WinUI `TitleBar` 控件，参考 WinUI Gallery 风格。
5. 主窗口标题栏图标使用 `Assets/Square44x44Logo.scale-200.png`，不要在 `TitleBar.IconSource` 中使用 `.ico`，避免运行时异常。
6. 主窗口标题栏的 `Title` 与 `Subtitle` 通过 `x:Uid="MainWindow/TitleBar"` 与键 `MainWindow/TitleBar.Title`、`MainWindow/TitleBar.Subtitle` 设置，`Window.Title` 在代码中取自 `AppTitleBar.Title`；不要保留 `MainWindow/TitleBar/TitleText.Text` 之类无法映射到属性的键，会导致 x:Uid 应用异常。
7. 主窗口标题栏右侧使用 `TitleBar.RightHeader` 放置 `PersonPicture` 显示登录状态：已登录为绿色人头头像，未登录为红色人头头像。

## 12. 主页（MainPage）

### 布局与控件

1. 主页包含标题栏搜索框、筛选/日志操作区、搜索结果卡片列表和底部状态提示。
2. 搜索框嵌入标题栏并居中。
3. 主页标题栏保留搜索框；非主页仅将搜索框设为禁用，不移除、不隐藏、不使用额外占位控件。
4. 操作区左侧使用筛选 ToggleButton：“全部”、“未购买”、“已购买”；右侧包含“日志”按钮和仅在下载队列运行时显示的“终止下载”按钮。
5. 搜索结果使用 `ListView` + CommunityToolkit `SettingsCard` 卡片列表，不再使用带表头的传统表格，也不使用批量复选框。
6. 搜索结果卡片要求：
   1. Header 显示 App 名称，Description 显示开发者。
   2. HeaderIcon 显示 App 图标。
   3. 卡片右侧显示版本号、购买状态、单项操作按钮和“三个点”菜单。
   4. 购买状态来自数据库；App 名称、App ID、开发者、版本号、价格、图标来自搜索结果。
   5. 购买状态文字需要区分颜色：已购买为绿色，无法购买为红色。
   6. 单项操作按钮根据状态切换：未购买时为购买；已购买时为下载；无法购买时禁用。
   7. 无法购买状态旁需要按账户界面问号 tooltip 的方式展示原因。
7. 搜索结果列表为空时显示空状态提示；搜索中显示居中的 `ProgressRing`。
8. 主页使用 `InfoBar` 展示操作状态，详细日志通过全局日志窗口查看。

### 卡片“三个点”菜单

1. 主页搜索结果卡片使用“三个点”按钮打开菜单，不再依赖传统表格行右键。
2. 菜单分三个区：标记区、复制区、操作区。
3. 标记区：标记为未购买、已购买。
4. 复制区：复制 App 名称、ID。
5. 操作区：打开 App Store 中该软件详情页。
6. 主页卡片的“三个点”弹出菜单项需要带 icon，复制类菜单项统一使用复制 icon。

## 13. 购买状态

1. 分为“全部”、“未购买”和“已购买”。
2. “已购买”指用户拥有许可的 App：通过本软件购买的、以及账户中已拥有但非通过本软件购买的（原“已拥有”状态已合并入“已购买”，由数据库迁移 `user_version` 2 归并旧数据）。
3. 另有“无法购买”状态，用于非免费或当前不可购买的 App；该状态不入库，由价格推导，不应作为可执行购买状态处理。
4. 当 `ipatool` 返回 `alreadyOwned` 或 `failed to purchase item with param 'STDQ'` 时，直接在本地标记为已购买，不弹窗确认。
5. 已购买列表同步由 `PurchaseSyncService`（`IPAbuyer.Core/Services/Purchases/PurchaseSyncService.cs`）负责：
   1. 通过 `list-purchases` 分页拉取全量（每页 100，为 ipatool 单页上限），逐页写入数据库并统一标记为已购买，进度逐页写入日志；分页拉取、页面解析与写入在 Rust core（`ipabuyer_core_sync_*` 轮询句柄，同步线程内自建数据库连接），C# 门面以 200ms 轮询转交进度与日志。
   2. 触发时机：仅由用户在设置页“刷新已购买列表”手动触发，不做任何自动同步。
   3. `list-purchases` 消耗较大，自动同步按 `SyncState` 表中上次成功时间判定阈值，失败不推进成功时间；同步进行中忽略新的同步请求。
   4. 测试账户（`test`/`test`）不执行同步。

## 14. ipatool 页（IpatoolPage）

1. 使用 CommunityToolkit `SettingsCard` 展示内置 `ipatool` 与可选的自定义 `ipatool.exe`。
2. 内置版本展示为 `release@2.5.0`（随内置版本同步更新）。
3. 显示版本要求卡片：内置与自定义 `ipatool` 均要求版本 ≥ `2.5.0`（已购买功能依赖 2.5.0 引入的新逻辑，含破坏性更改）。
4. 内置与自定义来源选择写入 LocalSettings 名称：`IpatoolFlavor`，内部值为 `Main`（内置正式版）和 `Custom`；默认值为 `Main`。
5. 自定义 `ipatool.exe` 路径写入 LocalSettings 名称：`CustomIpatoolPath`；自定义文件只要求扩展名为 `.exe`。
6. 认证登录、查询登录状态、退出登录、购买、下载等所有 `ipatool` 命令使用当前选择的来源；自定义路径失效时回退到内置正式版。
7. 内置版本卡片右端使用三点菜单提供导出功能，导出内置版本到下载目录，目标文件名为 `ipatool.exe`。
8. 自定义 `ipatool.exe` 卡片通过主按钮选择或更换文件，提供“使用”按钮切换来源，右端三点菜单只提供删除插槽功能；删除插槽只移除 LocalSettings 路径，不删除原文件。
9. 显示详细日志开关写入 LocalSettings 名称：`DetailedIpatoolLogEnabled`，勾选后所有 `ipatool` 的命令和输出都显示在日志区。
10. 页面底部显示清空 `ipatool` 数据卡片，`ipatool` 数据目录：`%USERNAME%/.ipatool/`。
11. 页面底部显示 `majd/ipatool` 仓库卡片，Description 写完整网址 <https://github.com/majd/ipatool>，按钮打开该网址。

## 15. 日志系统

1. 日志展示格式：“[日期时间] [INFO] 具体日志”。
2. 日志根据等级不同使用不同的颜色。
3. 如果日志是 `ipatool` 输出而不是本项目输出，中间的 `[INFO]` 修订为 `[ipatool]`。
4. 执行操作时自动弹出日志窗口：“购买”、“登录”、“查询登录状态”、“下载”、“终止下载”、“刷新已购买列表”。
5. 日志窗口使用独立 WinUI `Window`（`LogViewerWindow`），布局采用 XAML 和 code-behind。
6. 日志窗口标题栏使用 WinUI `TitleBar` 控件，并启用 Mica 背景。
7. 主页面和账户页面共用同一套全局日志系统（`IPAbuyer.Core/Logging/UiLogStore.cs`），不再分别维护独立日志列表。
8. 日志窗口全局只保留一个实例，重复点击“日志”按钮时聚焦已有窗口。
9. 日志正文背景为深色时，INFO 日志颜色使用浅灰色，不能跟随浅色主题变成深色。

## 16. 设置页（Settings）

1. 设置界面的配置写入 packaged 应用的 `ApplicationData.Current.LocalSettings`。
2. 如果旧版 `settings.json` 存在，需要先迁移到 LocalSettings；迁移成功后将原文件改名为 `settings.json.migrated`。
3. 修改和重置国家代码（默认为 `cn`）功能：
   1. LocalSettings 名称：`CountryCode`
   2. 需要提示用户：跨地区购买会导致已拥有 App 不在同步列表中。
4. 修改和重置下载目录功能，默认为当前用户的下载文件夹：
   1. LocalSettings 名称：`DownloadDirectory`
5. 刷新已购买列表卡片：显示上次同步时间，按钮手动触发 `PurchaseSyncService` 全量同步，点击后自动打开日志窗口并在日志中显示逐页进度；若已有同步在进行，仅打开日志窗口并提示进行中；未登录或测试账户时弹窗提示。
6. 关闭加密密钥轮换功能：
   1. LocalSettings 名称：`KeychainPassphraseRotationEnabled`
7. 开发者官方网站（按钮跳转 <https://www.blazesnow.com/ipa/>）：
   1. 需要提示用户：打开开发者官方网站，查看 Q&A 及更多信息。
8. 项目仓库（按钮打开 <https://github.com/ipabuyer/ipabuyer>）：
   1. 可在 GitHub 项目仓库反馈问题或参与贡献。
9. 清空本地数据库按钮与介绍。
10. 设置页最底部显示软件版本卡片，仅显示版本号，不需要 Description。

### LocalSettings 键一览

| 键 | 含义 | 默认值 |
| --- | --- | --- |
| `CountryCode` | App Store 国家/地区代码（ISO 3166-1 Alpha-2） | `cn`（首次启动从 Windows 区域初始化） |
| `DownloadDirectory` | 下载目录 | 当前用户下载文件夹 |
| `KeychainPassphraseRotationEnabled` | 退出登录后是否自动轮换加密密钥 | — |
| `IpatoolFlavor` | ipatool 来源：`Main` 内置 / `Custom` 自定义 | `Main` |
| `CustomIpatoolPath` | 自定义 `ipatool.exe` 路径 | — |
| `DetailedIpatoolLogEnabled` | 是否显示 ipatool 详细日志 | — |

## 17. 搜索功能

1. 通过 `https://itunes.apple.com/search?term=搜索名称&entity=software&limit=限制输出&country=国家代码` 向 Apple 服务器查询相关的软件列表。
2. 国家代码遵循 ISO 3166-1 Alpha-2。
3. 搜索默认限制为 200 条结果。
4. 处理返回的 JSON 数据并展示在主页搜索结果卡片列表中。
5. 搜索请求、响应解析与已购买状态合成在 Rust core（`ipabuyer_core_catalog_search`）；`IPAbuyer.Core/Services/AppCatalog/` 保留 `AppCatalogService` 门面（规范值到本地化显示串的映射）与 `DeveloperFilter`（开发者筛选）。

## 18. App 处理与下载队列

1. 获取 App 列表后，用户通过单个卡片的操作按钮购买或加入下载队列。
2. 当前主页不做批量选择、批量购买或批量下载。
3. 购买命令基于：`ipatool.exe purchase --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose`
4. 下载命令基于：`ipatool.exe download --output 输出位置 --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose`
5. 已购买或已拥有的 App 点击操作按钮后加入全局下载队列，并在队列未运行时启动下载队列。
6. 下载队列需要支持待下载、下载中、成功、失败、已取消等状态；主页仅显示“终止下载”入口。队列状态机、输出解析与进程编排在 Rust core（`ipabuyer_core_queue_*` 轮询句柄）；`DownloadQueueService`（`IPAbuyer.Core/Services/Downloads/DownloadQueueService.cs`）作为门面维护 `ObservableCollection` 镜像并以 200ms 轮询同步条目状态与日志。“终止下载”与关停取消为队列级：终止当前下载并结束本轮队列，剩余条目保留原状态，可再次启动继续。
7. 需要捕获 `ipatool` 的输出信息并进行处理；详细日志关闭时避免刷屏，详细日志开启时显示命令、输出和下载进度片段。
8. 下载命令不设置固定超时；长时间下载依靠用户“终止下载”或应用关闭时终止进程。登录命令超时 60 秒，查询登录状态与购买命令为 2 分钟超时。所有子进程的标准输入在启动后立即关闭，交互式提示会因 EOF 立即结束而不是挂起等待。

## 19. 本地化

1. 资源文件：`Strings/zh-Hans/Resources.resw`（默认语言）与 `Strings/en-US/Resources.resw`，通过 `x:Uid` 引用，禁止在代码或 XAML 中硬编码 UI 文本。
2. 两个 resw 由各 csproj 以 `PRIResource` 链接进 PRI 资源。
3. 修改资源后运行 `Scripts/Verify-LocalizationResources.ps1`，校验两个语言文件的键一致、无重复键且格式化占位符匹配。

## 20. 测试

1. 单元测试位于 `IPAbuyer.Tests`，使用 xUnit v3，仅引用 `IPAbuyer.Core`（不依赖 UI）。
2. 覆盖范围包括配置、ipatool payload 判定、购买状态判定、日志格式化、序列化等宿主侧纯逻辑；ipatool 命令构建/响应解析/下载输出解析等已迁入 Rust core，对应测试位于 Core 仓库（含假 `.cmd` 脚本的 FFI 端到端测试）。
3. 新增或修改 `IPAbuyer.Core` 中的可测逻辑时，应补充对应测试。

## 21. 参考链接

- ipatool 仓库：<https://github.com/majd/ipatool>
- 苹果账户官网（获取双重验证码）：<https://account.apple.com/>
- iTunes 搜索 API：`https://itunes.apple.com/search?term=...&entity=software&limit=...&country=...`
- WinUI Gallery（`TitleBar` 等控件参考）
- CommunityToolkit.WinUI（`SettingsCard` 等控件）
