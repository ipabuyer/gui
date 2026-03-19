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

1. 主页含搜索框、操作区、表格区、日志区
2. 搜索框：嵌入自定义标题栏并居中，取代系统标题栏
3. 操作区：购买按钮、添加到下载队列按钮、筛选多选框，形成一排
4. 表格区：
   1. 表头为app名称、appid、开发者、版本号、价格、购买状态
   2. app名称、appid、开发者、版本号、价格从搜索功能来，购买状态从数据库里取
   3. 表头要求始终可见
   4. app带icon
   5. app带选框，配合操作区实现多app处理，也要配合主页右键实现多app处理

### 购买状态

1. 分“全部”、“未购买”、“已购买”和“已拥有”
2. “已购买”和“已拥有”的区别：“已购买”指通过本软件进行购买的app；“已拥有”指用户购买过的app，但不是通过本软件购买的
3. “已购买”和“已拥有”需要写入数据库文件

### 主页右键

1. 主页的表格区支持右键项目
2. 右键分三个区：操作区、标记区、复制区
3. 操作区：购买app、添加到下载队列
4. 标记区：标记为未购买、已购买、已拥有
5. 复制区：复制app名称、id、版本号

## 账户界面

1. 账户界面分操作区、日志区
2. 操作区含四个输入框和按钮
3. 输入框有：账户输入框、密码输入框、双重验证码输入框、加密密钥输入框，分别对应email、password、auth-code、keychain-passphrase
4. 按钮有“查询登录状态”、“登录”、“退出登录”、“打开苹果账户官网”，按钮大小保持统一
5. 日志区同主页日志区，用于展示ipatool的输出内容

### 登录账户

1. 对app进行购买和下载，需要用户登录苹果账户
2. 登录命令为：`ipatool.exe auth login --auth-code 双重验证码 --email 邮箱 --password 密码 --keychain-passphrase 加密密钥`
   1. 可以先通过只传入邮箱、密码和000000作为双重验证码的方式，让苹果向用户发送双重验证码，然后再让用户输入双重验证码
   2. 对于收不到双重验证码的情况，提示用户打开<https://account.apple.com/>即苹果账户官网，输入用户名和密码后获取双重验证码，然后填入本软件
   3. 需要提示用户，新创建的苹果账号不能直接用于进行购买和下载，必须要在苹果设备上登陆过一次AppStore并完成一次app的购买
3. 查询登录状态的命令：`ipatool.exe auth info --keychain-passphrase 加密密钥`
   1. 进入账户界面时，自动执行查询登录状态命令
   2. 如果是登录状态，则为操作区添加一个锁定浅灰色蒙版，只允许退出登录按钮和查询登录状态按钮
4. 退出登录命令为：`ipatool.exe auth revoke`
5. 为了测试用途，在数据库中准备用户名test和密码test的账户，该账户购买或下载任何app都直接成功，该账户用于界面测试

### 账户加密密钥处理

1. 加密密钥即keychain-passphrase，默认为12345678，显示于输入框
2. 账户登录时，需要让用户输入加密密钥即keychain-passphrase用于加密ipatool的配置文件
3. 在账户页面，若用户需要修改加密密钥，提示其退出登录并重新登录，在这个过程中输入新的加密密钥
4. 注意：只有ipatool auth处于登录状态时，才存在加密密钥
5. 加密密钥存储在数据库路径里，用passphrase.txt储存即可

## 下载界面

1. 下载界面分操作区、表格区、日志区
2. 操作区有以下按钮：开始下载队列、移出下载队列、移除下载成功项、打开下载目录、终止所有下载
3. 操作区下面是表格区和日志区，表格区和日志区竖直比例为7比3
4. 表格区表头同主页界面表格区，但是购买状态修改为下载状态
5. 日志区有以下按钮：复制、清空、终止当前app下载

## 日志区

1. 所有界面的日志区均遵守以下规则
2. 日志展示：“[日期时间] [INFO] 具体日志”
3. 日志根据等级不同使用不同的颜色
4. 如果日志是ipatool输出而不是本项目输出，中间的[INFO]修订为[ipatool]

## 设置界面

1. 设置界面的配置写入`settings.json`文件，存放于数据库目录
2. 修改和重置国家代码（默认为cn）功能
   1. 设置名称：country
   2. 需要提示用户：跨地区购买会导致标记为疑似已拥有
3. 修改和重置下载目录功能，默认为当前用户的下载文件夹
   1. 设置名称：download_dir
4. 详细日志选框，勾选后所有ipatool命令都显示在日志区，显示在软件日志输出前
   1. 设置名称：verbose
5. 开发者官方网站（按钮跳转<https://ipa.blazesnow.com>）
6. 反馈邮箱（按钮复制<ipa@blazesnow.com>）
7. 清空本地数据库按钮与介绍
8. 清空ipatool数据按钮与介绍，ipatool数据目录：`%USERNAME%/.ipatool/`
9. 导出ipatool.exe功能，默认输出目录为下载目录

## 搜索功能

1. 通过`https://itunes.apple.com/search?term=搜索名称&entity=software&limit=限制输出&country=国家代码`向apple服务器查询相关的软件列表
2. 国家代码遵循ISO 3166-1 Alpha-2
3. 处理返回的json数据并展示在主页表格中

## app处理

1. 获取app列表，用户选中一些app后，可以进行购买或者下载
2. 使用`ipatool.exe purchase --keychain-passphrase 加密密钥 --bundle-identifier APPID`进行购买
3. 使用`ipatool.exe download --keychain-passphrase 加密密钥 --output 输出位置 --bundle-identifier APPID`进行下载
4. 需要捕获ipatool的输出信息并进行处理

## 变量命名

遵循驼峰命名规则

## 发布MSIX

1. Name="IPAbuyer.IPAbuyer"
2. Publisher="CN=68F867E4-B304-4B5D-9818-31B1910E0771"
3. Version="2026.3.17.0"
4. Language="zh-CN"

## exe可执行文件

1. ipatool.exe位于Include目录，注意区分amd64和arm64
2. 针对ipatool输出的内容，需要在命令中加入`--format text`或者`--format json`

## 数据库文件目录

1. DEBUG时存放于`%AppData%\Local\IPAbuyer\`
2. RELEASE时存放于`%AppData%\Local\Packages\IPAbuyer.IPAbuyer_kr1hdvrv6tpd0\LocalState\`
3. 推荐通过winui 3的Windows api获取上述路径
