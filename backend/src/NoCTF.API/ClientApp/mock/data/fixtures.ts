import { date, id, model, now, type Data } from '../schema'
import { mockRuntimeEndpoint } from '../runtime'

// Fictional seed data only. No production database, credentials, or external URLs.
export const accounts = [
  { login: 'admin', password: 'Mock123!', role: 'Administrator', userId: id(1), userName: 'Mock Admin' },
  { login: 'organizer', password: 'Mock123!', role: 'Organizer', userId: id(1, 2), userName: 'Mock Organizer' },
  { login: 'player', password: 'Mock123!', role: 'User', userId: id(1, 3), userName: 'Mock Player' },
]

export function createFixtures() {
  const users = accounts.map(user => model('AuthenticationCurrentUserResponse', {
    ...user, kind: 'Human', email: `${user.login}@mock.invalid`, emailVerified: true,
    description: 'Local Mock account / 本地演示账号', wallpaperRevision: null, wallpaperEnabled: false,
  }))
  const competitions = ['Ctf', 'Awd', 'Awdp', 'Koh'].map((mode, index) => model('CompetitionsCompetitionResponse', {
    tracksEnabled: true,
    id: id(2, index + 1), title: ['NoCTF 春季挑战赛 · MOCK', '攻防训练场 · MOCK', 'AWDP 修复演练 · MOCK', 'KoH 占领演练 · MOCK'][index],
    posterUrl: `/api/v1/competitions/${id(2, index + 1)}/poster?revision=${id(12, index + 1).replaceAll('-', '')}`,
    description: '## 本地演示环境 / Local demo\n\n所有数据均为虚构，操作只影响内存。\n\n- 演示 Flag：`flag{mock_success}`\n- 支持浏览题目、队伍、排行榜与管理页面\n- 重启 Mock 服务即可重置数据',
    mode, status: index === 2 ? 'Published' : 'Running', startTime: date(index === 2 ? 24 : -4), endTime: date(index === 2 ? 72 : 48),
    ownerId: id(1), administrationRole: 'Owner', leaderboardVisibility: 'Normal', maxTeamMembers: 5,
    maxConcurrentRuntimeInstancesPerTeam: 3, maxActiveQuestionsPerTeam: 5, maxParticipantMessagesBeforeHandlerReply: 5,
    teamRegistrationAutoApprove: true, allowTeamRegistrationWhileRunning: true, practiceModeEnabled: true,
  }))
  competitions.push(model('CompetitionsCompetitionResponse', {
    ...competitions[0], id: id(2, 5), title: '秋季公开赛 · MOCK', status: 'Visible', startTime: date(120), endTime: date(144),
    posterUrl: `/api/v1/competitions/${id(2, 5)}/poster?revision=${id(12, 5).replaceAll('-', '')}`,
  }))
  competitions.push(model('CompetitionsCompetitionResponse', {
    ...competitions[0], id: id(2, 6), title: '夏季邀请赛 · MOCK', status: 'Finished', startTime: date(-120), endTime: date(-72),
    posterUrl: `/api/v1/competitions/${id(2, 6)}/poster?revision=${id(12, 6).replaceAll('-', '')}`,
  }))
  const challengeCatalog = [
    { direction: 'Misc', title: 'Welcome to NoCTF', slug: 'misc' },
    { direction: 'Web', title: 'Orbiting Headers', slug: 'web' },
    { direction: 'Crypto', title: 'Cipher Vault', slug: 'crypto' },
    { direction: 'Pwn', title: 'Memory Garden', slug: 'pwn' },
    { direction: 'Reverse', title: 'Signal Rewind', slug: 'reverse' },
    { direction: 'Penetration', title: 'Surface Mapping', slug: 'penetration' },
    { direction: 'Forensics', title: 'Fragmented Evidence', slug: 'forensics' },
    { direction: 'OSINT', title: 'Open Trace', slug: 'osint' },
    { direction: 'AI', title: 'Prompt Boundary', slug: 'ai' },
    { direction: 'Mobile', title: 'Pocket Vault', slug: 'mobile' },
    { direction: 'IoT', title: 'Beacon Relay', slug: 'iot' },
    { direction: 'Hardware', title: 'Logic Pulse', slug: 'hardware' },
    { direction: 'Cloud', title: 'Ephemeral Bucket', slug: 'cloud' },
    { direction: 'Blockchain', title: 'Ledger Drift', slug: 'blockchain' },
  ] as const
  const templates = competitions.slice(0, 4).flatMap((competition, ci) => challengeCatalog.map(({ title, direction }, i) => model('ChallengeBankChallengeTemplateResponse', {
    id: id(3, ci * 100 + i + 1), ownerId: id(1), managerIds: [], mode: competition.mode, visibility: 'Shared',
    title, direction, description: `## ${title}\n\n这是 ${competition.mode} 模式的 ${direction} 方向本地演示题目。\n\n输入 \`flag{mock_success}\` 可体验正确提交；其他内容将产生错误结果。\n\n此环境不会运行真实容器或处理真实 Flag。`,
    definitionJson: JSON.stringify({ schemaVersion: { Ctf: 2, Awd: 4, Awdp: 4, Koh: 1 }[competition.mode as string], runtime: null }),
    activeCompetitionReferenceCount: 1, createdAt: date(-168), updatedAt: date(-3),
  })))
  const attachments = templates.map((template, index) => {
    const item = challengeCatalog[index % challengeCatalog.length]!
    const mockContent = [
      'NoCTF isolated Mock attachment',
      `Direction: ${item.direction}`,
      `Challenge: ${template.title}`,
      'Demo Flag: flag{mock_success}',
      'This fictional file never comes from the production backend.',
    ].join('\n') + '\n'
    return model('ChallengeBankChallengeAttachmentResponse', {
      id: id(11, index + 1), challengeId: template.id, fileName: `${item.slug}-brief.txt`,
      contentType: 'text/plain; charset=utf-8', byteLength: new TextEncoder().encode(mockContent).byteLength,
      sha256: (index + 1).toString(16).padStart(64, '0'), exactFlag: null, deletedAt: null,
      createdAt: date(-2), mockContent,
    })
  })
  const challenges = competitions.flatMap((competition, ci) => templates.filter(template => template.mode === competition.mode).map((template, index) => model('ChallengesChallengeResponse', {
    id: id(4, ci * 100 + index + 1), competitionId: competition.id, challengeId: template.id,
    title: template.title, direction: template.direction, description: template.description, order: index,
    isPublished: true, hasRuntime: true, usesDynamicFlag: false, urls: [],
    createdAt: date(-24), updatedAt: date(-3), leaderboardVisibility: 'Normal', dataScope: 'Live',
    maximumFlagAttempts: 20,
    acceptedFlagAttempts: index === challengeCatalog.length - 1 ? 16 : 0,
    remainingFlagAttempts: index === challengeCatalog.length - 1 ? 4 : 20,
    hints: [model('ChallengesParticipantChallengeHintResponse', { id: id(7, ci * 10 + index + 1), order: 0, content: 'Try flag{mock_success}', cost: 0, isUnlocked: true, isPublished: true })],
  })))
  const teams = competitions.flatMap((competition, ci) => ['Aurora', 'BlueShift', 'ByteGarden', 'NullPointer', 'RedPanda', 'StackTrace', 'ZeroDay', 'Moonlight'].map((name, index) => model('TeamsTeamResponse', {
    id: id(5, ci * 10 + index + 1), competitionId: competition.id, trackKey: 'open', trackName: '公开赛道 / Open', name,
    captainId: index === 0 ? id(1, 3) : id(10, index), memberIds: index === 0 ? users.map(user => user.userId) : [id(10, index)],
    registrationStatus: index === 7 ? 'Pending' : 'Approved', registeredAt: date(-48), isBanned: false,
  })))
  const runtimes = challenges.map((challenge, index) => {
    const competitionIndex = competitions.findIndex(competition => competition.id === challenge.competitionId)
    return model('RuntimeRuntimeResponse', {
      id: id(12, index + 1), competitionId: challenge.competitionId, competitionChallengeId: challenge.id,
      teamId: id(5, competitionIndex * 10 + 1), runtimeKind: 'Container', provider: 'Docker', state: 'Running',
      failureCode: null, ...mockRuntimeEndpoint(index),
      createdAt: date(-1), runningAt: date(-0.9), expiresAt: date(24), stoppedAt: null,
    })
  })
  const notifications = competitions.map((competition, i) => model('NotificationsNotificationResponse', {
    id: id(6, i + 1), sourceType: 2, sourceId: competition.id, sourceDisplayName: competition.title,
    targetType: 2, targetId: competition.id, kind: 'CompetitionAnnouncement',
    content: { title: '欢迎参加演示赛 / Welcome', body: 'This is a local Mock site. 演示 Flag：flag{mock_success}', competitionId: competition.id }, sentAt: date(-1),
  }))
  const facts = competitions.flatMap((competition, ci) => Array.from({ length: 3 }, (_, i) => {
    const factId = id(8, ci * 10 + i + 1)
    const seededCorrect = competition.mode === 'Ctf' && ci === 0 && i === 0 || i > 0
    return model('GameplayFactsGameplayFactListItemResponse', {
      id: factId, gameplayFactId: factId, competitionId: competition.id, competitionChallengeId: id(4, ci * 100 + i + 1),
      teamId: id(5, ci * 10 + i + 1), actorUserId: i === 0 ? id(1, 3) : id(10, i),
      kind: competition.mode === 'Awdp' ? 'BreakAttempt' : 'FlagAttempt', state: 'Completed', result: seededCorrect ? 'Correct' : 'Wrong',
      value: seededCorrect ? 'flag{mock_success}' : 'flag{demo_wrong}', occurredAt: date(-2 + i / 4), updatedAt: date(-2 + i / 4),
    })
  }))
  const questions = competitions.map((competition, ci) => model('QuestionsCompetitionQuestionResponse', {
    threadRootId: id(9, ci + 1), competitionId: competition.id, teamId: id(5, ci * 10 + 1), askedByUserId: id(1, 3),
    askerDisplayName: 'Mock Player', teamDisplayName: 'Aurora', title: '如何验证演示 Flag？', body: '请问演示题目应当提交什么 Flag？',
    subject: 'Challenge', competitionChallengeId: id(4, ci * 100 + 1), challengeTitle: 'Welcome to NoCTF',
    status: 'Pending', createdAt: date(-1), updatedAt: date(-0.8), maxParticipantMessagesBeforeHandlerReply: 5,
    lastActorDisplayName: 'Mock Player', lastActorRole: 'Asker',
    entries: [
      model('QuestionsCompetitionQuestionEntryResponse', { id: id(9, ci + 1), kind: 'Message', actorRole: 'Asker', actorUserId: id(1, 3), actorDisplayName: 'Mock Player', body: '请问演示题目应当提交什么 Flag？', createdAt: date(-1) }),
      model('QuestionsCompetitionQuestionEntryResponse', { id: id(19, ci * 10 + 1), kind: 'Message', actorRole: 'PlatformAdministrator', actorUserId: id(1), actorDisplayName: 'Mock Admin', body: '输入 flag{mock_success} 即可体验提交与榜单更新。\n这是独立 Mock 环境，不会发送到生产服务。', createdAt: date(-0.9) }),
      model('QuestionsCompetitionQuestionEntryResponse', { id: id(19, ci * 10 + 2), kind: 'Message', actorRole: 'Asker', actorUserId: id(1, 3), actorDisplayName: 'Mock Player', body: '收到，谢谢！还有问题可以在这里继续沟通吗？', createdAt: date(-0.8) }),
    ],
  }))
  return {
    users, competitions, templates, challenges, attachments, teams, notifications,
    facts, runtimes, questions,
    settings: new Map<string, Data>(),
    platform: { name: 'NoCTF · MOCK', description: '本地演示站 · Fictional data · No production backend', logoUrl: null,
      imageUploadLimits: { maximumAvatarBytes: 12 * 1024 * 1024, maximumWallpaperBytes: 16 * 1024 * 1024 },
      humanVerification: { provider: 'None', siteKey: null, apiEndpoint: null, runtimeRequired: false }, updatedAt: now() },
  }
}

export type MockState = ReturnType<typeof createFixtures>
