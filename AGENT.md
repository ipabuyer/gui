# IPAbuyer AI 编程指导文件

## 通用约束

1. 禁止修改本文件，除非用户明确授权。
2. 所有文件均以 UTF-8 格式存储、读取和修改。
3. 软件文本使用 `.resw` 存储并引用，禁止硬编码。
4. 不需要主动编译；如果编译出错，用户会提供错误日志。

## 软件框架与发布

1. 使用 WinUI 3 作为软件框架。
2. 软件 UI 界面尽可能多采用 CommunityToolkit.WinUI 包内容，避免自绘。
3. 调用 `ipatool.exe` 执行命令。
4. 最终发布至 Microsoft Store。
5. 每次更新发布配置、发布包版本或准备发布前，需要提示用户确认 `ipatool` 的 Git 提交值是否发生变化。
6. 发布配置集中在 `IPAbuyer.csproj` 中维护，不使用独立 `.pubxml` 作为主要发布配置来源。
7. 本地测试使用 Visual Studio packaged 模式；本项目不考虑未打包运行状态。

## exe 可执行文件

1. `ipatool.exe` 位于 `Include` 目录，注意区分 `amd64` 和 `arm64`。
2. 当前主线 `ipatool` 来源为自行拉取上游仓库 `main` 分支构建，不是正式 release 版；设置页需要展示短 Git 提交值，并通过 tooltip 保留完整提交值。
3. `ipatool-main-windows-*.exe` 打包后映射为 `ipatool.exe`，用于搜索以外的购买、下载等主流程。
4. `ipatool-2.3.0-windows-*.exe` 打包后映射为 `ipatool-legacy.exe`，用于认证登录等需要 legacy 行为的流程。
5. 针对 `ipatool` 输出的内容，需要在命令中加入 `--format json`；详细日志开启时记录命令和输出。

## 数据库

1. `PurchasedAppDb.db` 文件存放已购买 App、购买 App 的邮箱地址、App 的状态（即“已购买”和“已拥有”）。
2. 数据库文件目录：
   1. 使用 packaged 应用 LocalState 路径：`%AppData%\Local\Packages\IPAbuyer.IPAbuyer_kr1hdvrv6tpd0\LocalState\`
   2. 通过 Windows API 获取上述路径。
   3. 不需要实现或保留未打包运行状态的本地目录回退逻辑。

## UI 界面

1. 适配系统明暗模式并自动切换。
2. 使用侧边栏定位页面，侧边栏有 icon 并可折叠，折叠按钮位于标题栏上。
3. 共 3 个导航页面：主页、账户、设置。
4. 主窗口标题栏使用 WinUI `TitleBar` 控件，参考 WinUI Gallery 风格。
5. 主窗口标题栏图标使用 `Assets/Square44x44Logo.scale-200.png`，不要在 `TitleBar.IconSource` 中使用 `.ico`，避免运行时异常。
6. 不要给主窗口 `TitleBar` 设置 `x:Uid="MainWindow/TitleBar"`，避免与 `MainWindow/TitleBar/TitleText.Text` 资源键冲突。
7. 主窗口标题栏右侧使用 `TitleBar.RightHeader` 放置 `PersonPicture` 显示登录状态：已登录为绿色人头头像，未登录为红色人头头像。

## 主页界面

1. 主页包含标题栏搜索框、筛选/日志操作区、搜索结果卡片列表和底部状态提示。
2. 搜索框嵌入标题栏并居中。
3. 主页标题栏保留搜索框；非主页仅将搜索框设为禁用，不移除、不隐藏、不使用额外占位控件。
4. 操作区左侧使用筛选 ToggleButton：“全部”、“未购买”、“已购买”、“已拥有”；右侧包含“日志”按钮和仅在下载队列运行时显示的“终止下载”按钮。
5. 搜索结果使用 `ListView` + CommunityToolkit `SettingsCard` 卡片列表，不再使用带表头的传统表格，也不使用批量复选框。
6. 搜索结果卡片要求：
   1. Header 显示 App 名称，Description 显示开发者。
   2. HeaderIcon 显示 App 图标。
   3. 卡片右侧显示版本号、购买状态、单项操作按钮和“三个点”菜单。
   4. 购买状态来自数据库；App 名称、App ID、开发者、版本号、价格、图标来自搜索结果。
   5. 购买状态文字需要区分颜色：已购买/已拥有为绿色，无法购买为红色。
   6. 单项操作按钮根据状态切换：未购买时为购买；已购买/已拥有时为下载；无法购买时禁用。
7. 搜索结果列表为空时显示空状态提示；搜索中显示居中的 `ProgressRing`。
8. 主页使用 `InfoBar` 展示操作状态，详细日志通过全局日志窗口查看。

### 购买状态

1. 分为“全部”、“未购买”、“已购买”和“已拥有”。
2. “已购买”指通过本软件进行购买的 App。
3. “已拥有”指用户购买过的 App，但不是通过本软件购买的。
4. 另有“无法购买”状态，用于非免费或当前不可购买的 App；该状态不应作为可执行购买状态处理。
5. “已购买”和“已拥有”需要写入数据库文件。
6. 当 `ipatool` 返回 `failed to purchase item with param 'STDQ'` 时，判断 App 为疑似已拥有，按设置决定是否弹窗询问用户是否要标记为已拥有，并提供不再提示选框。

### 主页右键

1. 主页搜索结果卡片使用“三个点”按钮打开菜单，不再依赖传统表格行右键。
2. 菜单分三个区：标记区、复制区、操作区。
3. 标记区：标记为未购买、已购买、已拥有。
4. 复制区：复制 App 名称、ID。
5. 操作区：打开 App Store 中该软件详情页。
6. 主页卡片的“三个点”弹出菜单项需要带 icon，复制类菜单项统一使用复制 icon。

## 账户界面

1. 含四个输入框和按钮。
2. 输入框：账户、密码、双重验证码、加密密钥，分别对应 `email`、`password`、`auth-code`、`keychain-passphrase`。
3. 按钮：“查询登录状态”、“登录”、“退出登录”、“打开苹果账户官网”、“日志”，按钮大小保持统一。

### 登录账户

1. 对 App 进行购买和下载，需要用户登录苹果账户。
2. 登录命令：`ipatool.exe auth login --auth-code 双重验证码 --email 邮箱 --password 密码 --keychain-passphrase 加密密钥`
   1. 可以先通过只传入邮箱、密码和 `000000`（双重验证码）的方式，让苹果向用户发送双重验证码，然后再让用户输入真实的双重验证码。
   2. 对于收不到双重验证码的情况，提示用户打开 <https://account.apple.com/>，即苹果账户官网，输入用户名和密码后获取双重验证码，然后填入本软件。
   3. 需要提示用户：新创建的苹果账号不能直接用于购买和下载，必须先在苹果设备上登录过一次 App Store 并完成一次 App 购买。
3. 查询登录状态命令：`ipatool.exe auth info --keychain-passphrase 加密密钥`
   1. 打开软件时，自动静默执行查询登录状态命令。
   2. 如果是登录状态，则为输入区添加一个锁定浅灰色蒙版，只允许退出登录按钮和查询登录状态按钮。
   3. 如果是登录状态，则将 `ipatool` 返回的 `email` 即用户邮箱写入输入框。
4. 退出登录命令：`ipatool.exe auth revoke`
5. 为了测试用途，准备用户名 `test` 和密码 `test` 的账户，该账户购买或下载任何 App 都直接成功，用于界面测试。

### 账户加密密钥处理

1. 加密密钥即 `keychain-passphrase`，显示于账户页输入框，供用户查看和复制。
2. 账户页初始化时读取当前保存的加密密钥；如果不存在则自动生成一个 UUID 格式的新密钥并写入输入框。
3. 加密密钥主要存储在 Windows PasswordVault 中，资源名为 `IPAbuyer.ipatool.passphrase`，用户键为 `__default__`。
4. `passphrase.txt` 仅作为 legacy 迁移或 PasswordVault 不可用时的兜底文件；当成功迁移或成功写入 PasswordVault 后，需要删除 legacy `passphrase.txt`。
5. 登录成功后保存当前输入框中的加密密钥；查询登录状态时优先使用输入框中的值，输入为空时再使用已保存的值。
6. 购买和下载等需要 `--keychain-passphrase` 的命令不从账户输入框直接读取，而是通过 `KeychainConfig.GetPassphrase(null)` 获取当前保存的值。
7. 如果用户需要修改加密密钥，提示其退出登录，编辑输入框中的新密钥后重新登录。
8. 退出登录成功后，如果设置项 `KeychainPassphraseRotationEnabled` 为 `true`，自动生成新的 UUID 密钥并更新输入框；如果为 `false`，保留当前密钥。

## 日志弹窗

1. 日志展示格式：“[日期时间] [INFO] 具体日志”。
2. 日志根据等级不同使用不同的颜色。
3. 如果日志是 `ipatool` 输出而不是本项目输出，中间的 `[INFO]` 修订为 `[ipatool]`。
4. 执行操作时自动弹出日志窗口：“购买”、“登录”、“查询登录状态”、“下载”、“终止下载”。
5. 日志窗口使用独立 WinUI `Window`，布局采用 XAML 和 code-behind。
6. 日志窗口标题栏使用 WinUI `TitleBar` 控件，并启用 Mica 背景。
7. 主页面和账户页面共用同一套全局日志系统，不再分别维护独立日志列表。
8. 日志窗口全局只保留一个实例，重复点击“日志”按钮时聚焦已有窗口。
9. 日志正文背景为深色时，INFO 日志颜色使用浅灰色，不能跟随浅色主题变成深色。

## 设置界面

1. 设置界面的配置写入 packaged 应用的 `ApplicationData.Current.LocalSettings`。
2. 如果旧版 `settings.json` 存在，需要先迁移到 LocalSettings；迁移成功后将原文件改名为 `settings.json.migrated`。
3. 修改和重置国家代码（默认为 `cn`）功能：
   1. LocalSettings 名称：`CountryCode`
   2. 需要提示用户：跨地区购买会导致标记为疑似已拥有。
4. 修改和重置下载目录功能，默认为当前用户的下载文件夹：
   1. LocalSettings 名称：`DownloadDirectory`
5. 详细日志选框，勾选后所有 `ipatool` 命令都显示在日志区，显示在软件日志输出前：
   1. LocalSettings 名称：`DetailedIpatoolLogEnabled`
   2. 需要提示用户：勾选后所有 `ipatool` 的命令和输出都显示在日志区。
6. 标记为已拥有前的提示：
   1. LocalSettings 名称：`OwnedCheckEnabled`
   2. `OwnedCheckEnabled` 为 `true` 时，标记为已拥有不再弹窗询问；为 `false` 时，标记前需要弹窗确认。
7. 关闭加密密钥轮换功能：
   1. LocalSettings 名称：`KeychainPassphraseRotationEnabled`
8. 开发者官方网站（按钮跳转 <https://ipa.blazesnow.com>）：
   1. 需要提示用户：打开开发者官方网站，查看 Q&A 及更多信息。
9. 反馈邮箱（按钮复制 <ipa@blazesnow.com>）：
   1. 需要提示用户：附带屏幕截图和复现步骤，有助于更快地修复问题。
10. 清空本地数据库按钮与介绍。
11. 清空 `ipatool` 数据按钮与介绍，`ipatool` 数据目录：`%USERNAME%/.ipatool/`
12. 导出 `ipatool.exe` 功能，默认输出目录为下载目录。
13. 导出 `ipatool.exe` 卡片需要展示当前主线 `ipatool` 的短 Git 提交值，例如 `main@dcddce4`，完整提交值通过 tooltip 展示。
14. 设置页最底部显示软件版本卡片，仅显示版本号，不需要 Description。

## 搜索功能

1. 通过 `https://itunes.apple.com/search?term=搜索名称&entity=software&limit=限制输出&country=国家代码` 向 Apple 服务器查询相关的软件列表。
2. 国家代码遵循 ISO 3166-1 Alpha-2。
3. 搜索默认限制为 200 条结果。
4. 处理返回的 JSON 数据并展示在主页搜索结果卡片列表中。

## App 处理

1. 获取 App 列表后，用户通过单个卡片的操作按钮购买或加入下载队列。
2. 当前主页不做批量选择、批量购买或批量下载。
3. 购买命令基于：`ipatool.exe purchase --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose`
4. 下载命令基于：`ipatool.exe download --output 输出位置 --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose`
5. 已购买或已拥有的 App 点击操作按钮后加入全局下载队列，并在队列未运行时启动下载队列。
6. 下载队列需要支持待下载、下载中、成功、失败、已取消等状态；主页仅显示“终止下载”入口，队列细节由 `DownloadQueueService` 管理。
7. 需要捕获 `ipatool` 的输出信息并进行处理；详细日志关闭时避免刷屏，详细日志开启时显示命令、输出和下载进度片段。
