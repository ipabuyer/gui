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

## exe 可执行文件

1. `ipatool.exe` 位于 `Include` 目录，注意区分 `amd64` 和 `arm64`。
2. 针对 `ipatool` 输出的内容，需要在命令中加入 `--format text` 或 `--format json`。

## 数据库

1. `PurchasedAppDb.db` 文件存放已购买 App、购买 App 的邮箱地址、App 的状态（即“已购买”和“已拥有”）。
2. 数据库文件目录：
   1. 打包应用：`%AppData%\Local\Packages\IPAbuyer.IPAbuyer_kr1hdvrv6tpd0\LocalState\`
   2. 未打包应用：回退到 `%AppData%\Local\IPAbuyer\`
   3. 通过 Windows API 获取上述路径。

## UI 界面

1. 适配系统明暗模式并自动切换。
2. 使用侧边栏定位页面，侧边栏有 icon 并可折叠，折叠按钮位于标题栏上。
3. 共 3 个页面：主页、账户、设置。
4. 主窗口标题栏使用 WinUI `TitleBar` 控件，参考 WinUI Gallery 风格。
5. 主窗口标题栏图标使用 `Assets/Square44x44Logo.scale-200.png`，不要在 `TitleBar.IconSource` 中使用 `.ico`，避免运行时异常。
6. 不要给主窗口 `TitleBar` 设置 `x:Uid="MainWindow/TitleBar"`，避免与 `MainWindow/TitleBar/TitleText.Text` 资源键冲突。
7. 主窗口标题栏右侧使用 `TitleBar.RightHeader` 放置 `PersonPicture` 显示登录状态：已登录为绿色人头头像，未登录为红色人头头像。

## 主页界面

1. 主页包含搜索框、操作区、表格区。
2. 搜索框嵌入标题栏并居中。
3. 主页标题栏保留搜索框；非主页仅将搜索框设为禁用，不移除、不隐藏、不使用额外占位控件。
4. 操作区包含：“购买”按钮、“下载”按钮、“日志”按钮、“筛选”选框，形成一排；最右端有一个“终止下载”按钮，仅在下载开始后显示。
5. 表格要求：
   1. 表头为 App 名称、App ID、开发者、版本号、价格、购买状态。
   2. App 名称、App ID、开发者、版本号、价格来自搜索功能，购买状态来自数据库。
   3. 表头始终可见。
   4. App 带 icon。
   5. App 带复选框，配合操作区实现多 App 处理，也要配合主页右键实现多 App 处理。

### 购买状态

1. 分为“全部”、“未购买”、“已购买”和“已拥有”。
2. “已购买”指通过本软件进行购买的 App。
3. “已拥有”指用户购买过的 App，但不是通过本软件购买的。
4. “已购买”和“已拥有”需要写入数据库文件。
5. 当 `ipatool` 返回 `failed to purchase item with param 'STDQ'` 时，判断 App 为疑似已拥有，弹窗询问用户是否要标记为已拥有，并提供不再提示选框。

### 主页右键

1. 主页的表格区支持右键项目。
2. 右键分三个区：标记区、复制区、操作区。
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
   2. 如果是登录状态，则为操作区添加一个锁定浅灰色蒙版，只允许退出登录按钮和查询登录状态按钮。
   3. 如果是登录状态，则将 `ipatool` 返回的 `email` 即用户邮箱写入输入框。
4. 退出登录命令：`ipatool.exe auth revoke`
5. 为了测试用途，准备用户名 `test` 和密码 `test` 的账户，该账户购买或下载任何 App 都直接成功，用于界面测试。

### 账户加密密钥处理

1. 加密密钥即 `keychain-passphrase`，显示于输入框。
2. 账户登录时，需要生成 `keychain-passphrase` 用于加密 `ipatool` 的配置文件。
3. 加密密钥生成使用 UUID。
4. 在账户页面，若用户需要修改加密密钥，提示其退出登录并重新登录，在这个过程中输入新的加密密钥。
5. 只有 `ipatool auth` 处于登录状态时，才存在加密密钥。
6. 加密密钥用 `passphrase.txt` 存储。

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

1. 设置界面的配置写入 `settings.json` 文件，存放于数据库目录。
2. 修改和重置国家代码（默认为 `cn`）功能：
   1. 设置名称：`country`
   2. 需要提示用户：跨地区购买会导致标记为疑似已拥有。
3. 修改和重置下载目录功能，默认为当前用户的下载文件夹：
   1. 设置名称：`download_dir`
4. 详细日志选框，勾选后所有 `ipatool` 命令都显示在日志区，显示在软件日志输出前：
   1. 设置名称：`verbose`
   2. 需要提示用户：勾选后所有 `ipatool` 的命令和输出都显示在日志区。
5. 标记为已拥有前的提示，取消勾选后，标记为已拥有前要弹窗询问用户：
   1. 设置名称：`owned_check`
   2. 需要提示用户：勾选后，标记为已拥有将不再弹窗询问。
6. 关闭加密密钥轮换功能：
   1. 设置名称：`keychain_passphrase_rotation`
7. 开发者官方网站（按钮跳转 <https://ipa.blazesnow.com>）：
   1. 需要提示用户：打开开发者官方网站，查看 Q&A 及更多信息。
8. 反馈邮箱（按钮复制 <ipa@blazesnow.com>）：
   1. 需要提示用户：附带屏幕截图和复现步骤，有助于更快地修复问题。
9. 清空本地数据库按钮与介绍。
10. 清空 `ipatool` 数据按钮与介绍，`ipatool` 数据目录：`%USERNAME%/.ipatool/`
11. 导出 `ipatool.exe` 功能，默认输出目录为下载目录。

## 搜索功能

1. 通过 `https://itunes.apple.com/search?term=搜索名称&entity=software&limit=限制输出&country=国家代码` 向 Apple 服务器查询相关的软件列表。
2. 国家代码遵循 ISO 3166-1 Alpha-2。
3. 处理返回的 JSON 数据并展示在主页表格中。

## App 处理

1. 获取 App 列表，用户选中一些 App 后，可以进行购买或者下载。
2. 购买命令：`ipatool.exe purchase --keychain-passphrase 加密密钥 --bundle-identifier APPID`
3. 下载命令：`ipatool.exe download --keychain-passphrase 加密密钥 --output 输出位置 --bundle-identifier APPID`
4. 需要捕获 `ipatool` 的输出信息并进行处理。
