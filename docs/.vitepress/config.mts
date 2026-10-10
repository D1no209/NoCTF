import { defineConfig, type DefaultTheme } from 'vitepress'

const groups: DefaultTheme.SidebarItem[] = [
  {
    "text": "开始阅读",
    "items": [
      {
        "text": "手册导读",
        "link": "/guide/"
      },
      {
        "text": "平台概念",
        "link": "/guide/concepts"
      },
      {
        "text": "角色与界面权限",
        "link": "/guide/roles"
      },
      {
        "text": "举办第一场比赛",
        "link": "/competition/quick-start"
      }
    ]
  },
  {
    "text": "安装与部署",
    "items": [
      {
        "text": "安装方式",
        "link": "/installation/"
      },
      {
        "text": "服务器与依赖",
        "link": "/installation/prerequisites"
      },
      {
        "text": "Action 镜像与配置包",
        "link": "/installation/images"
      },
      {
        "text": "Docker 安装",
        "link": "/installation/docker"
      },
      {
        "text": "HTTPS 与独立反代",
        "link": "/installation/reverse-proxy"
      },
      {
        "text": "Kubernetes 安装",
        "link": "/installation/kubernetes"
      },
      {
        "text": "部署配置与存储",
        "link": "/installation/configuration"
      },
      {
        "text": "首次启动与验收",
        "link": "/installation/verify"
      },
      {
        "text": "LiveSolo 可选媒体",
        "link": "/installation/live-solo-media"
      }
    ]
  },
  {
    "text": "选手使用",
    "items": [
      {
        "text": "账号",
        "link": "/player/account"
      },
      {
        "text": "导航、资料与外观",
        "link": "/player/profile-navigation"
      },
      {
        "text": "队伍与报名",
        "link": "/player/teams"
      },
      {
        "text": "题目与环境",
        "link": "/player/challenges"
      },
      {
        "text": "赛制参赛",
        "link": "/player/modes",
        "items": [
          {
            "text": "CTF",
            "link": "/player/modes/ctf"
          },
          {
            "text": "AWD",
            "link": "/player/modes/awd"
          },
          {
            "text": "AWDP",
            "link": "/player/modes/awdp"
          },
          {
            "text": "KoH",
            "link": "/player/modes/koh"
          },
          {
            "text": "LiveSolo",
            "link": "/player/modes/live-solo/",
            "items": [
              {
                "text": "名单与共享",
                "link": "/player/modes/live-solo/preparation"
              },
              {
                "text": "场内解题",
                "link": "/player/modes/live-solo/playing"
              },
              {
                "text": "延迟观赛",
                "link": "/player/modes/live-solo/program"
              },
              {
                "text": "赛后题解",
                "link": "/player/modes/live-solo/postgame"
              }
            ]
          }
        ]
      },
      {
        "text": "排行榜与练习",
        "link": "/player/leaderboard"
      },
      {
        "text": "消息、咨询与题解",
        "link": "/player/questions-writeups"
      }
    ]
  },
  {
    "text": "题库管理",
    "items": [
      {
        "text": "题库导读",
        "link": "/challenge-bank/"
      },
      {
        "text": "模板创建与分区保存",
        "link": "/challenge-bank/templates"
      },
      {
        "text": "附件与静态 Flag",
        "link": "/challenge-bank/attachments-flags"
      },
      {
        "text": "Runtime 与 Checker",
        "link": "/challenge-bank/runtime-checkers"
      },
      {
        "text": "模板测试",
        "link": "/challenge-bank/testing"
      },
      {
        "text": "权限、引用与删除",
        "link": "/challenge-bank/permissions-placement"
      }
    ]
  },
  {
    "text": "比赛管理",
    "items": [
      {
        "text": "入口、菜单与工作流",
        "link": "/competition/"
      },
      {
        "text": "筹备与规则",
        "items": [
          {
            "text": "创建与概览",
            "link": "/competition/settings/overview"
          },
          {
            "text": "基本资料与访问",
            "link": "/competition/settings/basic"
          },
          {
            "text": "方向目录",
            "link": "/competition/settings/directions"
          }
        ]
      },
      {
        "text": "赛制规则",
        "link": "/competition/settings/scoring",
        "items": [
          {
            "text": "CTF",
            "link": "/competition/settings/scoring#ctf-默认规则"
          },
          {
            "text": "AWD",
            "link": "/competition/settings/scoring#awd-默认规则"
          },
          {
            "text": "AWDP",
            "link": "/competition/settings/scoring#awdp-默认规则"
          },
          {
            "text": "KoH",
            "link": "/competition/settings/scoring#koh-默认规则"
          },
          {
            "text": "LiveSolo",
            "link": "/competition/modes/live-solo/",
            "items": [
              {
                "text": "设置与阶段",
                "link": "/competition/modes/live-solo/settings"
              },
              {
                "text": "题组与备用组",
                "link": "/competition/modes/live-solo/question-groups"
              },
              {
                "text": "种子与赛程",
                "link": "/competition/modes/live-solo/bracket"
              },
              {
                "text": "裁判操作",
                "link": "/competition/modes/live-solo/judging"
              },
              {
                "text": "结果纠正",
                "link": "/competition/modes/live-solo/corrections"
              },
              {
                "text": "录像管理",
                "link": "/competition/modes/live-solo/recordings"
              }
            ]
          }
        ]
      },
      {
        "text": "题目与闯关",
        "items": [
          {
            "text": "比赛题目实例",
            "link": "/competition/content/challenges"
          },
          {
            "text": "开题、计分和提交时间",
            "link": "/competition/content/timing"
          },
          {
            "text": "提示与 Flag",
            "link": "/competition/content/hints-flags"
          },
          {
            "text": "CTF 闯关与勋章",
            "link": "/competition/content/progression"
          }
        ]
      },
      {
        "text": "参赛组织与权限",
        "items": [
          {
            "text": "队伍查询与审核",
            "link": "/competition/participants/teams"
          },
          {
            "text": "赛道与准入",
            "link": "/competition/participants/tracks"
          },
          {
            "text": "协作者与所有权",
            "link": "/competition/participants/permissions"
          }
        ]
      },
      {
        "text": "裁判与审核",
        "items": [
          {
            "text": "评测、重判与证据",
            "link": "/competition/judging/submissions"
          },
          {
            "text": "人工调分",
            "link": "/competition/judging/adjustments"
          },
          {
            "text": "作弊、封禁与申诉",
            "link": "/competition/judging/cheating-appeals"
          },
          {
            "text": "题解审核",
            "link": "/competition/judging/writeups"
          }
        ]
      },
      {
        "text": "赛事运行",
        "items": [
          {
            "text": "发布与生命周期",
            "link": "/competition/operations/lifecycle"
          },
          {
            "text": "运行实例与强制处理",
            "link": "/competition/operations/runtimes"
          },
          {
            "text": "流量抓取",
            "link": "/competition/operations/traffic-captures"
          },
          {
            "text": "排行榜可见性",
            "link": "/competition/operations/leaderboard"
          },
          {
            "text": "动态与大屏",
            "link": "/competition/operations/events-live"
          },
          {
            "text": "删除、恢复与清理",
            "link": "/competition/operations/deletion"
          }
        ]
      },
      {
        "text": "沟通与集成",
        "items": [
          {
            "text": "公告发布与撤回",
            "link": "/competition/communication/announcements"
          },
          {
            "text": "咨询与私密沟通",
            "link": "/competition/communication/questions"
          },
          {
            "text": "Webhook 与自动化",
            "link": "/competition/integrations/webhooks"
          },
          {
            "text": "工作人员 Webhook",
            "link": "/competition/integrations/staff-webhooks"
          },
          {
            "text": "赛事导出",
            "link": "/competition/integrations/exports"
          }
        ]
      }
    ]
  },
  {
    "text": "平台管理",
    "items": [
      {
        "text": "管理菜单与初始化",
        "link": "/platform/"
      },
      {
        "text": "平台信息与品牌",
        "link": "/platform/branding"
      },
      {
        "text": "账号管理",
        "items": [
          {
            "text": "用户、角色与状态",
            "link": "/platform/users/accounts"
          },
          {
            "text": "Bot、令牌与模拟",
            "link": "/platform/users/tokens"
          },
          {
            "text": "删除与匿名化",
            "link": "/platform/users/deletion"
          }
        ]
      },
      {
        "text": "邮件与身份验证",
        "items": [
          {
            "text": "安全设置导读",
            "link": "/platform/security/"
          },
          {
            "text": "SMTP、验证与找回",
            "link": "/platform/security/email"
          },
          {
            "text": "人机验证独立策略",
            "link": "/platform/security/human-verification"
          },
          {
            "text": "SSO 配置与测试",
            "link": "/platform/security/authentication"
          },
          {
            "text": "双重验证",
            "link": "/platform/security/mfa"
          },
          {
            "text": "通行密钥",
            "link": "/platform/security/passkeys"
          }
        ]
      },
      {
        "text": "实验功能",
        "link": "/platform/experiments"
      },
      {
        "text": "平台维护",
        "items": [
          {
            "text": "跨比赛运行实例",
            "link": "/platform/maintenance/runtimes"
          },
          {
            "text": "日志与 JSONL 导出",
            "link": "/platform/maintenance/logs"
          },
          {
            "text": "审计与归档",
            "link": "/platform/maintenance/audit"
          }
        ]
      },
      {
        "text": "LiveSolo 视频资源策略",
        "link": "/platform/video-policy"
      }
    ]
  },
  {
    "text": "部署运维",
    "items": [
      {
        "text": "升级与回退",
        "link": "/operations/upgrade"
      },
      {
        "text": "一致备份与恢复",
        "link": "/operations/backup"
      },
      {
        "text": "监控与容量",
        "link": "/operations/monitoring"
      },
      {
        "text": "故障排查",
        "link": "/operations/troubleshooting"
      },
      {
        "text": "LiveSolo 启用与验收",
        "link": "/operations/live-solo-readiness"
      }
    ]
  },
  {
    "text": "参考与维护",
    "items": [
      {
        "text": "手册更新说明",
        "link": "/reference/updates"
      },
      {
        "text": "管理页面覆盖索引",
        "link": "/reference/management-map"
      },
      {
        "text": "常见问题",
        "link": "/reference/faq"
      },
      {
        "text": "术语表",
        "link": "/reference/glossary"
      },
      {
        "text": "文档站维护",
        "link": "/contributing"
      },
      {
        "text": "开发者源码构建",
        "link": "/development/local"
      }
    ]
  }
]

const legacyRedirects: Record<string, string> = {
  "live-solo/index.md": "competition/modes/live-solo/",
  "live-solo/settings.md": "competition/modes/live-solo/settings.html",
  "live-solo/question-groups.md": "competition/modes/live-solo/question-groups.html",
  "live-solo/bracket.md": "competition/modes/live-solo/bracket.html",
  "live-solo/judging.md": "competition/modes/live-solo/judging.html",
  "live-solo/corrections.md": "competition/modes/live-solo/corrections.html",
  "live-solo/recordings.md": "competition/modes/live-solo/recordings.html",
  "live-solo/preparation.md": "player/modes/live-solo/preparation.html",
  "live-solo/playing.md": "player/modes/live-solo/playing.html",
  "live-solo/program.md": "player/modes/live-solo/program.html",
  "live-solo/postgame.md": "player/modes/live-solo/postgame.html",
  "live-solo/media-deployment.md": "installation/live-solo-media.html",
  "live-solo/readiness.md": "operations/live-solo-readiness.html"
}

// Use /manual/ when publishing below a domain subdirectory.
const base = process.env.DOCS_BASE ?? '/'
if (!base.startsWith('/') || !base.endsWith('/') || base.includes('//')) {
  throw new Error('DOCS_BASE 必须是以 / 开始和结束的路径，例如 /manual/')
}

export default defineConfig({
  lang: 'zh-CN',
  title: 'NoCTF 使用手册',
  description: 'NoCTF 平台安装、参赛、赛事组织、平台管理与运维手册',
  base,
  transformHead({ pageData }) {
    const target = legacyRedirects[pageData.relativePath.replace(/\\/g, '/')]
    if (!target) return []
    const href = `${base}${target}`
    return [
      ['meta', { 'http-equiv': 'refresh', content: `0; url=${href}` }],
      ['link', { rel: 'canonical', href }],
      ['meta', { name: 'robots', content: 'noindex' }]
    ]
  },
  srcExclude: ['README.md'],
  lastUpdated: true,
  // Keep link validation enabled: missing pages must fail the build.
  ignoreDeadLinks: false,
  themeConfig: {
    siteTitle: 'NoCTF 使用手册',
    nav: [
      { text: '安装部署', link: '/installation/', activeMatch: '/installation/' },
      { text: '选手指南', link: '/player/account', activeMatch: '/player/' },
      { text: '题库管理', link: '/challenge-bank/', activeMatch: '/challenge-bank/' },
      { text: '比赛管理', link: '/competition/', activeMatch: '/competition/' },
      { text: '平台与运维', items: [
        { text: '平台管理', link: '/platform/' },
        { text: '运维与排障', link: '/operations/troubleshooting' }
      ] }
    ],
    sidebar: {
      '/installation/': [groups[0]!, groups[1]!],
      '/player/': [groups[0]!, groups[2]!],
      '/challenge-bank/': [groups[0]!, groups[3]!],
      '/competition/': [groups[0]!, groups[4]!],
      '/platform/': [groups[0]!, groups[5]!],
      '/operations/': [groups[0]!, groups[6]!],
      '/reference/': [groups[0]!, groups[7]!],
      '/development/': [groups[0]!, groups[7]!],
      '/': groups
    },
    outline: { level: [2, 3], label: '本页目录' },
    search: {
      provider: 'local',
      options: {
        miniSearch: {
          options: {
            tokenize: (text) => Array.from(
              new Intl.Segmenter('zh-CN', { granularity: 'word' }).segment(text)
            ).filter((part) => part.isWordLike).map((part) => part.segment)
          },
          searchOptions: { combineWith: 'AND' }
        },
        locales: {
          root: {
            translations: {
              button: { buttonText: '搜索手册', buttonAriaLabel: '搜索手册' },
              modal: {
                displayDetails: '显示详细结果', resetButtonTitle: '清空搜索',
                backButtonTitle: '关闭搜索', noResultsText: '没有找到相关内容',
                footer: { selectText: '选择', navigateText: '切换', closeText: '关闭' }
              }
            }
          }
        }
      }
    },
    docFooter: { prev: '上一页', next: '下一页' },
    lastUpdated: { text: '最后更新' },
    darkModeSwitchLabel: '外观', lightModeSwitchTitle: '切换为浅色模式',
    darkModeSwitchTitle: '切换为深色模式', sidebarMenuLabel: '目录',
    returnToTopLabel: '返回顶部',
    socialLinks: [{ icon: 'github', link: 'https://github.com/D1no209/NoCTF' }],
    footer: { message: 'NoCTF · CTF / AWD / AWDP / KoH / LiveSolo 平台使用手册' }
  }
})
