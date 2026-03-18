# IPAbuyer AI 编程指导文件

禁止对此文件进行编辑

## 软件框架

1. winui 3作为软件框架
2. 调用ipatool.exe执行命令
3. 最终发布至Microsoft Store

## 数据库

1. PurchasedAppDb.db文件存放已购买app、购买app的邮箱地址、app的状态（即“已购买”和“已拥有”）

## UI界面

1. 适配系统明暗模式并自动切换
2. 系统为明亮模式时，软件为明亮模式，系统为暗色模式时，软件为暗色模式
3. 使用侧边栏定位页面，侧边栏有icon并可折叠，折叠按钮位于自定义标题栏上
4. 使用自定义标题栏
5. 共4个页面：主页、下载、账户、设置
6. 统一所有输入框、按钮的高度，宽度适配文字

## 主页界面

1. 搜索框、筛选、表格，
2. 搜索框嵌入自定义标题栏并居中，取代系统标题栏
3. 购买、添加到下载队列、筛选为一排
4. 表格的表头为app名称（图标）、appid、开发者、版本号、价格
5. 禁止加入未提及的内容

### 主页右键

1. 主页的表格区支持右键项目
2. 右键分三个区：操作区、标记区、复制区
3. 操作区：购买app、添加到下载队列
4. 标记区：标记为未购买、已购买、已拥有
5. 复制区：复制app名称、id、版本号

## 下载界面

1. 下载界面有以下按钮：开始下载队列、移出下载队列、打开下载目录、终止所有下载
2. 按钮下为表格区和日志区，二者竖直比例为七比三
3. 表格区表头为app名称、appid、下载状态
4. 日志区有以下按钮：复制、清空、终止当前app下载
5. 禁止加入未提及的内容

## 设置界面

1. 修改和重置国家代码（默认为cn）功能
2. 修改和重置下载目录功能，默认为当前用户的下载文件夹
3. 开发者官方网站（按钮跳转<https://ipa.blazesnow.com>）
4. 反馈邮箱（按钮复制<ipa@blazesnow.com>）
5. 清空本地数据库按钮与介绍
6. 禁止加入未提及的内容

## 搜索功能

1. 通过`https://itunes.apple.com/search?term=搜索名称&entity=software&limit=限制输出&country=国家代码`向apple服务器查询相关的软件列表
2. 国家代码遵循ISO 3166-1 Alpha-2
3. 处理返回的json数据并展示在主页表格中

## 主页表格

1. 主页表格不允许翻页
2. 主页表格允许鼠标滚轮翻动列表
3. 允许选中一些app并处理
4. 允许右键app进行处理
5. 加入筛选功能，分“全部”、“未购买”、“已购买”和“已拥有”
6. “已购买”和“已拥有”的区别：“已购买”指通过本软件进行购买的app；“已拥有”指用户购买过的app，但不是通过本软件购买的
7. “已购买”和“已拥有”需要写入PurchasedAppDb.db

## app处理

1. 获取app列表，用户选中一些app后，可以进行购买或者下载
2. 使用`ipatool.exe purchase --keychain-passphrase 加密密钥 --bundle-identifier APPID`进行购买
3. 使用`ipatool.exe download --keychain-passphrase 加密密钥 --output 输出位置 --bundle-identifier APPID`进行下载
4. 需要捕获ipatool的输出信息并进行处理

## 登录苹果账户

1. 对app进行购买和下载，需要用户登录苹果账户
2. 登录命令为：`ipatool.exe auth login --auth-code 双重验证码 --email 邮箱 --password 密码 --keychain-passphrase 加密密钥`；可以先通过只传入邮箱和密码的方式，让苹果向用户发送双重验证码，然后再让用户输入双重验证码；如果用户仍然收不到双重验证码，则提示用户打开<https://account.apple.com/>，输入用户名和密码后获取双重验证码，然后填入本软件
3. 需要提示用户，新创建的苹果账号不能直接用于进行购买和下载，必须要在苹果设备上登陆过一次AppStore并完成一次app的购买
4. 查询登录状态的命令：`ipatool.exe auth info --keychain-passphrase 加密密钥`
5. 登出命令为：`ipatool.exe auth revoke`
6. 为了测试用途，在数据库中准备用户名test和密码test的账户，该账户购买或下载任何app都直接成功，该账户用于界面测试

## 账户加密密钥处理

1. 账户登录时，需要让用户输入加密密钥即keychain passphrase用于加密ipatool的配置文件
2. 在账户页面，若用户需要修改加密密钥，提示其退出登录并重新登录，在这个过程中输入新的加密密钥
3. 注意：只有ipatool auth处于登录状态时，才存在加密密钥
4. 加密密钥存储在数据库路径里，用passphrase.txt储存即可

## 变量命名

遵循驼峰命名规则

## 发布MSIX

1. Name="IPAbuyer.IPAbuyer"
2. Publisher="CN=68F867E4-B304-4B5D-9818-31B1910E0771"
3. Version="2026.3.17.0"
4. Language="zh-CN"

## exe可执行文件

1. ipatool.exe位于include目录，注意区分amd64和arm64
2. 针对ipatool输出的内容，需要在命令中加入`--format text`或者`--format json`

## 数据库文件目录

1. DEBUG时存放于`AppData\Local\IPAbuyer\`
2. RELEASE时存放于`AppData\Local\Packages\IPAbuyer.IPAbuyer_kr1hdvrv6tpd0\LocalState\`
