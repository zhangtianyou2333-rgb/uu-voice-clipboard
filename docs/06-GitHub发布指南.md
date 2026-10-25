# GitHub 发布指南（给第一次使用的人）

## 推荐放法

建议仓库名称 `uu-voice-clipboard`。仓库主页放 README、源码、教程和验收清单；Release 附件放 Windows 可运行 ZIP 和必要的参考包。不要上传整个原始工作目录，也不要把本会员整包 ZIP 当作源码文件上传。

GitHub 网页向仓库上传单文件上限为 25 MiB，而 Windows 自包含包约几十 MiB，应放到 Releases。参见[官方文件限制](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)和[Releases 说明](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)。

## 分享范围

- **面向所有人复刻**：公开仓库，会员直接打开、下载或 Fork，操作最少。
- **仅会员可看**：私有仓库，邀请会员的 GitHub 账号作为协作者。私有仓库链接本身不是访问权限；大量成员逐个管理会更费事，也可以先在会员资料区发整理好的 ZIP。

本次作者已经选择公开仓库。

发布前由作者选定项目授权。若准备允许公众自由改造，需选好正式的开源 LICENSE；不要把代码公开等同于已经确定复刻/再分发条款，也不要把第三方组件 LICENSE 当作项目自己的许可证。参见[GitHub 官方许可说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。本整理包没有替作者选定许可证。

## 最容易上手的上传步骤

1. 登录 GitHub，新建名为 `uu-voice-clipboard` 的仓库，按上面选择 Public / Private。
2. 解压源码 ZIP，在 `uu-voice-clipboard` 文件夹里把 `.gitignore.template` 重命名为 `.gitignore`。
3. 使用网页 Add file → Upload files，上传**这个文件夹里面**的 README、AGENTS、docs、源码目录、LICENSES 等内容，让 README 出现在仓库根部。确认隐藏的 `.gitignore` 也在；不要只上传一个 ZIP。若本地已经构建，跳过 bin、obj、.build、publish、dist 等生成目录；网页上传不会替你应用本地忽略规则。
4. 提交后打开主页，点教程链接核对能正常阅读。确认不存在自己的路径、账户配置、日志、截图或历史测试输出。
5. 当源码验证并完成对应实机验收后，用标签发布 Release；当前本地包只能作为预览，勾选 pre-release。源码应先提交并与该标签对应，再挂同版本 Windows ZIP、校验文件及版本说明。不要给预览包标正式稳定版。
6. 把仓库链接发给会员，并说明“现有发送端 + Mac 接收参考，Windows 接收端待按教程开发”。

本仓库为作者选择公开分享的未加固复刻版。运行附件在 Releases。项目开源许可证尚待作者选定；第三方组件许可证仅适用于各组件。
