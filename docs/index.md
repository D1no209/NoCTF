---
layout: home
hero:
  name: NoCTF
  text: 平台使用手册
  tagline: 从安装部署到举办比赛，为选手、赛事组织者和平台管理员提供完整操作指南。
  actions:
    - theme: brand
      text: 开始安装
      link: /installation/
    - theme: alt
      text: 比赛管理手册
      link: /competition/
    - theme: alt
      text: 选手指南
      link: /player/account
features:
  - title: 安装与部署
    details: 准备 Linux 服务器，配置 Docker 或 Kubernetes，接入 HTTPS、文件存储和题目网络，完成首次验收。
    link: /installation/docker
  - title: 参赛与解题
    details: 创建队伍、提交报名、阅读题目、使用运行环境、提交 Flag 或修复包，并查看排名和题解。
    link: /player/teams
  - title: 赛事组织
    details: 按管理菜单查阅概览、配置、题目、队伍、评测、实例、抓包、申诉、公告、权限与导出操作。
    link: /competition/
  - title: 平台管理与运维
    details: 管理账号、邮件、SSO 和人机验证，诊断 Runtime、评测与日志，执行升级和一致备份恢复。
    link: /platform/
  - title: LiveSolo 第五赛制
    details: 独立 Match/Round、名单与屏幕、受理顺序判胜、延迟节目、录像和结果纠正，附真实启用验收边界。
    link: /live-solo/
---

## 从你的任务开始

| 你要完成的任务 | 阅读入口 |
| --- | --- |
| 在一台 Linux 服务器上安装 NoCTF | [Docker 单机安装](./installation/docker.md) |
| 安装到已有 Kubernetes 集群 | [Kubernetes 安装](./installation/kubernetes.md) |
| 在 Windows 上验证完整平台 | [本地 kind 环境](./installation/kubernetes.md#本地-kind-验证环境) |
| 确认平台已经可以正式使用 | [首次启动与验收](./installation/verify.md) |
| 报名参加比赛 | [建队、邀请与报名](./player/teams.md) |
| 从零配置一场比赛 | [举办第一场比赛](./competition/quick-start.md) |
| 配置和参加 LiveSolo 对局 | [LiveSolo 操作手册](./live-solo/index.md) |
| 设置单题自动开放和提交截止 | [题目时间](./competition/content/timing.md) |
| 定位登录、题目环境或评测故障 | [常见故障排查](./operations/troubleshooting.md) |

本手册面向仓库当前版本。安装步骤以 `deploy/` 中的模板和脚本为依据，界面入口以当前 ClientApp 为依据；安装旧版本时，应读取对应 Git revision 的手册。开发者领域契约仍由仓库 `specs/` 维护。

最近内容核对：2026-10-11。新增及修正见 [更新说明](./reference/updates.md)；LiveSolo 的局部验证不代表完整生产投产验收。
