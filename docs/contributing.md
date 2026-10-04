# 维护与发布文档站

本目录是独立的 VitePress 项目，使用 Bun 管理依赖和运行脚本，当前固定 VitePress 1.6.4。配置使用 ESM TypeScript，默认主题提供中文导航、侧边栏、全文搜索、深浅色和移动端目录。

中文搜索通过 `Intl.Segmenter` 分词，构建与浏览器使用同一 tokenizer；多词查询采用 AND，避免任一常见字词匹配导致结果泛滥。

## 安装文档开发依赖

准备 Bun 和受支持的 Node.js LTS；本项目使用 Bun 1.3.14 生成锁文件，可用 Node.js 22/24 运行 VitePress 工具链。未安装 Bun 时按 [官方安装文档](https://bun.sh/docs/installation)操作：

```bash
# Linux / macOS；执行官方安装器后重新打开终端。
curl -fsSL https://bun.sh/install | bash
```

```powershell
# Windows PowerShell。
powershell -c "irm bun.sh/install.ps1 | iex"
```

从仓库根进入文档目录：

```sh
cd docs
bun --version
node --version
bun install --frozen-lockfile
bun run docs:dev
```

打开终端输出的本机地址，默认 `http://127.0.0.1:5173`。已占用时 Vite 会选择其他端口。源码变动自动热更新，文档站无需 PostgreSQL、Redis、.NET 或 ClientApp dev server。

## 目录规划

```text
docs/
  .vitepress/config.mts   # 导航、侧边栏、中文搜索、base
  .gitignore              # 文档依赖、缓存和产物
  package.json            # 独立 Bun 脚本与固定依赖
  bun.lock                # 可复现依赖，提交到 Git
  index.md                # 手册首页
  guide/                  # 导读、概念、权限
  installation/           # 环境、Docker/K8s、代理、配置、验收
  player/                 # 账号、队伍、题目、模式、榜单、题解
  challenge-bank/         # 可复用模板、附件/Flag、定义、测试、权限与引用
  competition/            # 单场比赛管理，按任务子目录组织
    settings/             # 创建概览、基本设置、计分、方向
    content/              # 比赛题目、提示/Flag、闯关与勋章
    participants/         # 队伍、赛道、协作者与所有权
    judging/              # 评测、调分、作弊/申诉、题解
    operations/           # 生命周期、Runtime、抓包、榜单、大屏、删除
    communication/        # 公告、咨询
    integrations/         # Webhook、赛事导出
  platform/               # 全平台管理，和比赛工作区分开
    users/                # 账号、Bot/Token/模拟、删除/匿名化
    security/             # 邮件、人机验证、SSO
    maintenance/          # 跨比赛 Runtime、日志、审计
  operations/             # 升级、恢复、观测、排障
  reference/              # FAQ、术语
  development/            # 仅开发者：本地开发与源码构建
  contributing.md         # 本页
  README.md               # 仓库阅读入口，不生成站点路由
```

页面文件名使用简短英文 kebab-case，标题和正文使用中文。目录 `index.md` 是对应章节入口。新增章节同步修改 `.vitepress/config.mts` 的 sidebar/nav，并为上下文添加站内链接。

## 写作要求

- 先说明目标、适用角色、前置条件和执行位置，再给操作和预期结果。
- 生产和开发命令分开标注，替换项明确列出，不放真实密钥或历史生产数据。
- 安装步骤依据当前 `deploy` 脚本；产品流程核对当前页面、Application 和领域规则。
- 讲清异步受理与最终结果，提供可执行排障入口。
- 修改契约时同步相关用户页和安装/运维页，不从旧 specs 拷贝已删除能力。
- 保持与 `specs` 分工：此站面向操作，specs 维护领域和实现契约。
- 变更说明包括适用 revision，历史上线报告不作为通用安装教程。

站内链接使用相对 `.md`，例如 `[Docker 安装](./installation/docker.md)`；导航使用根路由，例如 `/installation/docker`。图片使用本地受控资产，不包含敏感截图。缺失链接应修复，不将 `ignoreDeadLinks` 改为 true。

## 构建与本机预览

```sh
cd docs
bun install --frozen-lockfile
bun run docs:build
bun run docs:preview
```

构建产物在 `.vitepress/dist`，预览默认 `http://127.0.0.1:4173`。检查首页、每个目录、长表格、代码块、中文搜索和手机宽度。构建会检查 Markdown 页面链接，但锚点和 sidebar 路由仍需实际核对。

`.gitignore` 排除 `node_modules`、cache、dist 和临时输出；提交 Markdown、配置、package.json 和 bun.lock，不提交构建产物。升级依赖时用 Bun 更新并重建，不混入其他包管理器锁文件。

## 子路径部署

默认发布在域名根 `/`。放在 `/manual/` 时构建阶段设置 `DOCS_BASE`：

```bash
DOCS_BASE=/manual/ bun run docs:build
DOCS_BASE=/manual/ bun run docs:preview
```

```powershell
$env:DOCS_BASE = '/manual/'
bun run docs:build
bun run docs:preview
```

路径必须首尾都有 `/`。预览与构建使用相同 base，后续回根部署时清除环境变量或设 `/`。不按 base 编译会导致资源、搜索索引或导航请求错误路径。

## 静态托管

把 `.vitepress/dist` 的内容部署到 Nginx、对象静态托管或其他静态服务。仅需静态文件，不在生产运行 `docs:dev`，也不依赖 NoCTF Host 进程。

根路径 Nginx 配置示例：

```nginx
server {
    listen 80;
    server_name docs.example.com;
    root /srv/www/noctf-docs;
    index index.html;

    location / {
        try_files $uri $uri.html $uri/ =404;
    }
}
```

证书与 HTTPS 按运维流程配置。`/manual/` 部署时把产物放到 `/srv/www/noctf-docs/manual/`，用 `/manual/` location 并保持对应 root，验证直接打开深层 `.html` 和章节目录地址。404 不回退到首页来掩盖链接错误。

## GitHub Pages 自动发布

仓库的 `.github/workflows/docs-pages.yml` 使用独立的 **Documentation Pages** 工作流。Bun 1.3.14 按锁文件安装依赖，构建 VitePress，仅将 `docs/.vitepress/dist` 上传为 Pages artifact，不发布应用后端、安装配置或运行数据。

### 首次启用

1. 确认仓库及拥有者套餐支持 Pages。私有仓库需要支持该功能的套餐；不支持时可使用独立公开文档仓库，保留应用仓库私有。
2. 将 `docs/` 和 Pages workflow 提交到目标仓库的 main。
3. 在 Settings → Pages → Build and deployment 将 Source 设为 **GitHub Actions**。
4. 在 Actions 手动运行 Documentation Pages，或提交文档变更触发。
5. 查看 deploy job 的 `github-pages` environment URL，验证首页、深层文档、导航和中文搜索。

如果首次启用 API 返回“Your current plan does not support GitHub Pages for this repository”，说明拥有者套餐不支持当前仓库 Pages；升级套餐或选择可用目标后再发布。工作流文件存在不代表站点已上线，也不需要为解决该限制公开整个应用仓库。

### 触发与权限

- main 上的 `docs/**` 或本工作流变更触发构建和部署。
- 对 main 的文档 Pull Request 只构建、检查链接，不部署，避免 PR 覆盖正式站点。
- workflow_dispatch 支持手工执行；只有 main 的执行会部署。
- build 读取内容与 Pages metadata；deploy 仅有 `pages: write` 和 `id-token: write`，使用 github-pages environment。
- 发布按分支串行执行，运行中的正式部署不会被新提交直接取消。

所有 Action 固定完整 commit SHA。Node.js 24 为 VitePress CLI 提供运行环境，依赖管理与文档命令使用 Bun；无需平台数据库、Docker 或 .NET。

### Pages 子路径与域名

生产构建从 `actions/configure-pages` 的 `base_path` 设置 `DOCS_BASE`，默认项目站点使用 `/NoCTF/`，用户站点或配置了独立域名的根站点使用 `/`。路径保留仓库名称大小写，不硬编码用户名或地址。

PR 验证按目标仓库名推导项目路径。配置自定义域名时先在 Pages 设置 DNS/TLS，再手工运行 Documentation Pages 重新构建，不直接将旧 `/NoCTF/` 产物搬到根目录。

使用独立公开文档仓库时，只需要复制 `docs/` 与该工作流，保留目录层级和 bun.lock，启用目标仓库 Pages。访问 URL 以实际 deploy 输出为准，不将尚未部署的默认地址写成已上线链接。

GitHub Pages 是静态站点，普通项目 Pages 不提供平台账号认证；公开托管前检查文档内容。`.nojekyll` 随产物输出，使用静态 artifact 部署，不经过 Jekyll 转换。

官方流程见 [GitHub Pages 自定义工作流](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)。

配置依据 [VitePress 1.x 入门](https://vuejs.github.io/vitepress/v1/guide/getting-started)、[本地搜索](https://vuejs.github.io/vitepress/v1/reference/default-theme-search)与 [部署指南](https://vuejs.github.io/vitepress/v1/guide/deploy)，并通过 context7 检索核对。
