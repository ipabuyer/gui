# IPAbuyer AI 编程指导（简要版）

IPAbuyer：WinUI 3 桌面应用，调用 [ipatool](https://github.com/majd/ipatool) 浏览、购买（仅免费 App）并下载 App Store 的 App，发布至 Microsoft Store。

**本文件是基准文件，不允许修改。详细开发指南见 [DEVELOPMENT.md](./DEVELOPMENT.md)，该文件为详细开发内容，鼓励修改以同步最新开发进度。**

## 硬性约束

1. 本文件是基准文件，不允许修改；DEVELOPMENT.md 为详细开发内容，鼓励修改以同步最新开发进度。
2. 所有文件均以 UTF-8 格式存储、读取和修改。
3. 实际开发中需要注意终端的GBK和UTF-8的问题
4. 软件文本使用 `.resw`（`Strings/zh-Hans`、`Strings/en-US`）存储并引用，禁止硬编码。
5. UI 框架为 WinUI 3，控件尽可能用 CommunityToolkit.WinUI（如 `SettingsCard`），避免自绘。
6. 所有 ipatool 操作通过内置 `ipatool.exe`（`Include/` 目录，区分 amd64/arm64）或用户自定义路径执行，命令需加 `--format json`。
7. 仅支持 packaged 模式运行与测试（Visual Studio packaged），不做未打包回退逻辑。
8. 发布配置集中在 `IPAbuyer.csproj`（不使用 `.pubxml`）；trim 依赖 `ILLink.Descriptors.xml`。
9. 更新内置 ipatool 版本、发布配置、发布包版本或准备发布前，提示用户确认上游正式版是否变化。

## 页面与核心服务

- 4 个导航页：主页（`MainPage`）、账户（`LoginPage`）、ipatool（`IpatoolPage`）、设置（`Settings`）；另有全局日志窗口 `LogViewerWindow`。
- 数据库：`PurchasedAppDb.db`（LocalState 目录），记录已购买 App 与邮箱；"已拥有"已合并为"已购买"，账户级同步时间存于 `SyncState` 表。
- 已购买列表同步由 `PurchaseSyncService` 管理：调用 `list-purchases` 全量同步（每页 100，消耗大），仅在设置页由用户手动触发。
- 下载队列由 `DownloadQueueService` 管理，主页只有“终止下载”入口；主页不做批量操作。
- 设置写入 `ApplicationData.Current.LocalSettings`；加密密钥存于 Windows PasswordVault。

## ipatool 命令速查

| 用途         | 命令                                                                                                                                        |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------- |
| 登录         | `ipatool.exe auth login --auth-code 双重验证码 --email 邮箱 --password 密码 --keychain-passphrase 加密密钥`                                 |
| 查询登录状态 | `ipatool.exe auth info --keychain-passphrase 加密密钥`                                                                                      |
| 退出登录     | `ipatool.exe auth revoke`                                                                                                                   |
| 列出已拥有   | `ipatool.exe list-purchases --max-results 每页数量(≤100) --page 页码 --keychain-passphrase 加密密钥 --format json --non-interactive --verbose` |
| 购买         | `ipatool.exe purchase --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose`                   |
| 下载         | `ipatool.exe download --output 输出位置 --bundle-identifier APPID --keychain-passphrase 加密密钥 --format json --non-interactive --verbose` |

测试账户：用户名 `test`、密码 `test`，购买/下载一律直接成功，不执行 list-purchases 同步。

## LocalSettings 键速查

`CountryCode`（默认 `cn`）、`DownloadDirectory`、`KeychainPassphraseRotationEnabled`、`IpatoolFlavor`（`Main`/`Custom`）、`CustomIpatoolPath`、`DetailedIpatoolLogEnabled`。
