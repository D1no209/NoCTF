# NoCTF 文档站

本目录是独立的 VitePress 项目，使用 Bun 安装依赖并运行命令。不会依赖 ClientApp 的 node_modules。

```sh
cd docs
bun install --frozen-lockfile
bun run docs:dev
```

打开终端输出的地址，默认 http://127.0.0.1:5173。

```sh
bun run docs:build
bun run docs:preview
```

构建产物位于 `.vitepress/dist/`。依赖、缓存和构建产物已在本目录 `.gitignore` 中排除；`bun.lock` 必须提交。

目录规划、写作规范、Bun 安装方式、子路径配置与静态托管见 [维护与发布文档站](./contributing.md)。平台安装从 [安装方式](./installation/index.md) 开始。

管理手册入口：[比赛管理](./competition/index.md)、[题库管理](./challenge-bank/index.md)、[平台管理](./platform/index.md)。[覆盖索引](./reference/management-map.md)将当前所有管理路由对应到详细操作说明。

第五赛制见 [LiveSolo](./live-solo/index.md)。最近核对 2026-10-11，更新内容和能力边界见 [更新说明](./reference/updates.md)。

GitHub Pages 使用 `.github/workflows/docs-pages.yml`：main 文档变更自动发布，PR 仅验证，支持手工执行；生产 base 从 Pages 配置读取。首次需要 Settings → Pages → Source 选择 GitHub Actions，且目标仓库套餐支持 Pages。详细步骤和启用条件见 [发布说明](./contributing.md#github-pages-自动发布)。
